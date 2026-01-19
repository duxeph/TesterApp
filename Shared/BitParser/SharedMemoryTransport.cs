using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser {
    /// <summary>
    /// High-performance shared memory writer (Server).
    /// Uses optimistic concurrency (Versioning) for lock-free reading.
    /// </summary>
    public class SharedMemoryServer : IDisposable {
        private readonly string _mapName;
        private readonly int _bufferSize;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private EventWaitHandle _signal;
        
        // Minimum buffer size to ensure consistency
        private const int MIN_BUFFER_SIZE = 65536;
        
        // Stats
        public long FramesWritten { get; private set; }
        public long BytesWritten { get; private set; }

        public SharedMemoryServer(string baseName, int bufferSize = 65536) {
            _mapName = baseName + "_Map";
            // Always use at least 64KB to match client expectations
            _bufferSize = Math.Max(bufferSize, MIN_BUFFER_SIZE);
            
            // Create Memory Mapped File with header space
            _mmf = MemoryMappedFile.CreateOrOpen(_mapName, _bufferSize + 16);
            _accessor = _mmf.CreateViewAccessor();
            
            // Initialize header to zero (prevents stale data issues)
            _accessor.Write(0, 0);   // Version = 0 (even = stable)
            _accessor.Write(4, 0);   // Length = 0
            _accessor.Write(8, 0);   // Sequence = 0
            _accessor.Write(12, 0);  // Reserved
            
            // Signal for waking up sleeping clients
            _signal = new EventWaitHandle(false, EventResetMode.ManualReset, baseName + "_Signal");
        }

        public void SendRawData(byte[] data, int length, uint sequenceNumber) {
            if (length > _bufferSize || length <= 0) return; // Too big or invalid

            // Protocol:
            // [0-3] Version (ODD = Writing, EVEN = Stable)
            // [4-7] Data Length
            // [8-11] Sequence Number
            // [16...] Data

            // 1. Mark as Writing (Version += 1 -> Odd)
            int currentVer = _accessor.ReadInt32(0);
            _accessor.Write(0, currentVer + 1);

            // 2. Write Metadata
            _accessor.Write(4, length);
            _accessor.Write(8, (int)sequenceNumber);

            // 3. Write Payload
            _accessor.WriteArray(16, data, 0, length);

            // 4. Mark as Stable (Version += 1 -> Even)
            _accessor.Write(0, currentVer + 2);
            
            // 5. Signal clients
            _signal.Set();
            _signal.Reset();

            FramesWritten++;
            BytesWritten += length;
        }

        public void Dispose() {
            try { _accessor?.Dispose(); } catch { }
            try { _mmf?.Dispose(); } catch { }
            try { _signal?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// High-performance shared memory reader (Client).
    /// </summary>
    public class SharedMemoryClient : IDisposable {
        private readonly string _mapName;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private EventWaitHandle _signal;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private byte[] _readBuffer;
        private volatile bool _isConnected;
        private volatile bool _isDisposing;

        public event Action<PipeProtocol.MessageHeader, byte[]> OnDataReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnLog;

        public bool IsConnected => _isConnected;

        public SharedMemoryClient(string baseName, int bufferSize = 65536) {
            _mapName = baseName;
            // Use at least 64KB buffer to handle typical data frames
            _readBuffer = new byte[Math.Max(bufferSize, 65536)];
        }

        public Task ConnectAsync() {
            try {
                string mapName = _mapName + "_Map";
                _mmf = MemoryMappedFile.OpenExisting(mapName);
                _accessor = _mmf.CreateViewAccessor();
                _signal = EventWaitHandle.OpenExisting(_mapName + "_Signal");

                _isConnected = true;
                _isDisposing = false;
                _cts = new CancellationTokenSource();
                _receiveTask = Task.Run(ReceiveLoop);
                
                OnConnected?.Invoke();
                return Task.CompletedTask;
            } catch (Exception ex) {
                _isConnected = false;
                throw new Exception($"Could not connect to Shared Memory '{_mapName}'. Is server running?", ex);
            }
        }

        public void Disconnect() {
            if (_isDisposing) return;
            _isDisposing = true;
            _isConnected = false;
            
            _cts?.Cancel();
            
            // Wait for receive loop to exit
            try { _receiveTask?.Wait(1000); } catch { }
            
            // Dispose resources safely
            try { _accessor?.Dispose(); } catch { }
            try { _mmf?.Dispose(); } catch { }
            try { _signal?.Dispose(); } catch { }
            
            _accessor = null;
            _mmf = null;
            _signal = null;
            
            try { OnDisconnected?.Invoke(); } catch { }
        }

        private void ReceiveLoop() {
            int lastSeq = -1;
            
            while (!_isDisposing && _cts != null && !_cts.Token.IsCancellationRequested) {
                try {
                    // Check if we're still valid
                    var signal = _signal;
                    var accessor = _accessor;
                    if (signal == null || accessor == null || _isDisposing) break;

                    // Wait for signal (timeout 100ms to allow check cancellation)
                    try {
                        if (!signal.WaitOne(100)) continue;
                    } catch (ObjectDisposedException) {
                        break;
                    }

                    if (_isDisposing) break;

                    // Optimistic Read Loop
                    int attempts = 0;
                    while (attempts < 5 && !_isDisposing) {
                        accessor = _accessor;
                        if (accessor == null) break;

                        // 1. Read Version
                        int verStart;
                        try {
                            verStart = accessor.ReadInt32(0);
                        } catch {
                            break;
                        }
                        
                        // If Odd, writer is writing. Spin.
                        if (verStart % 2 != 0) {
                            Thread.SpinWait(100);
                            attempts++;
                            continue;
                        }

                        // 2. Read Metadata
                        int length = accessor.ReadInt32(4);
                        int seq = accessor.ReadInt32(8);

                        if (seq == lastSeq) break; // Already processed

                        // Sanity check - skip if data too large
                        if (length <= 0 || length > _readBuffer.Length) {
                            OnLog?.Invoke($"[ShMem] Skipping frame: size {length} exceeds buffer {_readBuffer.Length}");
                            break;
                        }

                        // 3. Read Payload
                        accessor.ReadArray(16, _readBuffer, 0, length);

                        // 4. Verify Version
                        int verEnd = accessor.ReadInt32(0);

                        if (verStart == verEnd) {
                            // Success! Consistent frame.
                            lastSeq = seq;
                            
                            // Create header info
                            var header = new PipeProtocol.MessageHeader {
                                MessageType = PipeProtocol.MSG_DATA_FRAME,
                                SequenceNumber = (uint)seq,
                                ValueCount = (ushort)Math.Min(length, ushort.MaxValue),
                                TotalErrors = 0,
                                NewErrors = 0
                            };

                            // IMPORTANT: DeserializeDataFrame expects header + data format
                            // So we must prepend a 16-byte header to match the Pipe format
                            byte[] framedData = new byte[PipeProtocol.HEADER_SIZE + length];
                            
                            // Write header bytes (matching PipeProtocol layout)
                            framedData[0] = PipeProtocol.MSG_DATA_FRAME;
                            framedData[1] = 0;  // Reserved
                            framedData[2] = (byte)(length & 0xFF);
                            framedData[3] = (byte)((length >> 8) & 0xFF);
                            framedData[4] = (byte)(seq & 0xFF);
                            framedData[5] = (byte)((seq >> 8) & 0xFF);
                            framedData[6] = (byte)((seq >> 16) & 0xFF);
                            framedData[7] = (byte)((seq >> 24) & 0xFF);
                            // Rest of header (errors, timestamp) = 0
                            
                            // Copy payload after header
                            Array.Copy(_readBuffer, 0, framedData, PipeProtocol.HEADER_SIZE, length);
                            
                            try {
                                OnDataReceived?.Invoke(header, framedData);
                            } catch { }
                            break; 
                        }
                        
                        // Version mismatch, retry.
                        attempts++;
                    }
                } catch (ObjectDisposedException) {
                    break;
                } catch (Exception ex) {
                    if (!_isDisposing) {
                        OnLog?.Invoke($"[ShMem] Error: {ex.Message}");
                        Thread.Sleep(500);
                    }
                }
            }
        }

        public void Dispose() {
            Disconnect();
        }
    }
}
