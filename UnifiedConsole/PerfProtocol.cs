using System;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnifiedConsole {
    /// <summary>
    /// Binary protocol for high-speed port statistics communication.
    /// Optimized for 40ms update rate (25 FPS).
    /// </summary>
    public static class PerfProtocol {
        public const string PIPE_NAME = "EthernetPerfPipe";
        public const int PORT_COUNT = 16;
        
        // Message types
        public const byte MSG_PORT_STATS = 0x01;
        public const byte MSG_TEST_START = 0x02;
        public const byte MSG_TEST_STOP = 0x03;
        public const byte MSG_CONFIG = 0x04;

        /// <summary>
        /// Per-port statistics. 16 bytes each.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
        public struct PortStats {
            public uint RxCount;       // Receive count
            public uint TxCount;       // Transmit count
            public uint ErrorCount;    // Mismatch errors
            public ushort VlanId;      // VLAN ID for this port
            public byte PortNumber;    // 1-16
            public byte Status;        // 0=OK, 1=Error, 2=Inactive
        }

        /// <summary>
        /// Full frame header. 16 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
        public struct FrameHeader {
            public byte MessageType;
            public byte PortCount;
            public ushort Reserved;
            public uint SequenceNumber;
            public uint Timestamp;
            public uint TotalErrors;
        }

        public const int HEADER_SIZE = 16;
        public const int PORT_STATS_SIZE = 16;
        public const int FULL_FRAME_SIZE = HEADER_SIZE + (PORT_COUNT * PORT_STATS_SIZE);  // 272 bytes

        /// <summary>
        /// Serialize port stats to buffer using unsafe code for speed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SerializeFrame(PortStats[] ports, uint sequence, byte[] buffer) {
            if (buffer.Length < FULL_FRAME_SIZE) return 0;

            fixed (byte* pBuffer = buffer) {
                FrameHeader* header = (FrameHeader*)pBuffer;
                header->MessageType = MSG_PORT_STATS;
                header->PortCount = PORT_COUNT;
                header->Reserved = 0;
                header->SequenceNumber = sequence;
                header->Timestamp = (uint)Environment.TickCount;
                
                uint totalErrors = 0;
                PortStats* pStats = (PortStats*)(pBuffer + HEADER_SIZE);
                for (int i = 0; i < PORT_COUNT && i < ports.Length; i++) {
                    pStats[i] = ports[i];
                    totalErrors += ports[i].ErrorCount;
                }
                header->TotalErrors = totalErrors;
            }

            return FULL_FRAME_SIZE;
        }

        /// <summary>
        /// Deserialize port stats from buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe FrameHeader DeserializeFrame(byte[] buffer, PortStats[] output) {
            fixed (byte* pBuffer = buffer)
            fixed (PortStats* pOutput = output) {
                FrameHeader header = *(FrameHeader*)pBuffer;
                
                PortStats* pStats = (PortStats*)(pBuffer + HEADER_SIZE);
                int count = Math.Min(header.PortCount, output.Length);
                
                for (int i = 0; i < count; i++) {
                    pOutput[i] = pStats[i];
                }
                
                return header;
            }
        }
    }

    /// <summary>
    /// High-speed pipe server for performance data.
    /// Uses double buffering for lock-free operation at 40ms.
    /// </summary>
    public sealed class PerfPipeServer : IDisposable {
        private NamedPipeServerStream _pipe;
        private CancellationTokenSource _cts;
        private Task _connectionTask;
        private volatile bool _isConnected;
        
        // Double buffer for lock-free writes
        private readonly byte[] _buffer1;
        private readonly byte[] _buffer2;
        private volatile byte[] _activeBuffer;
        private volatile int _pendingLength;
        private readonly object _writeLock = new object();

        public event Action<string> OnLog;
        public event Action OnClientConnected;
        public event Action OnClientDisconnected;
        public bool IsConnected => _isConnected;

        public long FramesSent { get; private set; }
        public long FramesDropped { get; private set; }

        public PerfPipeServer() {
            _buffer1 = new byte[PerfProtocol.FULL_FRAME_SIZE];
            _buffer2 = new byte[PerfProtocol.FULL_FRAME_SIZE];
            _activeBuffer = _buffer1;
        }

        public void Start() {
            if (_connectionTask != null) return;
            _cts = new CancellationTokenSource();
            _connectionTask = Task.Run(() => ConnectionLoop());
            Log("Server started");
        }

        public void Stop() {
            _cts?.Cancel();
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _isConnected = false;
            Log("Server stopped");
        }

        private async Task ConnectionLoop() {
            while (!_cts.Token.IsCancellationRequested) {
                try {
                    _pipe = new NamedPipeServerStream(
                        PerfProtocol.PIPE_NAME,
                        PipeDirection.Out,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        PerfProtocol.FULL_FRAME_SIZE * 4,
                        PerfProtocol.FULL_FRAME_SIZE * 4);

                    Log("Waiting for UI panel connection...");
                    await Task.Factory.FromAsync(_pipe.BeginWaitForConnection, _pipe.EndWaitForConnection, null);

                    if (_cts.Token.IsCancellationRequested) break;

                    _isConnected = true;
                    Log("UI panel connected!");
                    OnClientConnected?.Invoke();

                    // Flush loop
                    while (_isConnected && _pipe.IsConnected && !_cts.Token.IsCancellationRequested) {
                        if (_pendingLength > 0) {
                            FlushPending();
                        }
                        await Task.Delay(1);
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    Log($"Error: {ex.Message}");
                    await Task.Delay(1000);
                } finally {
                    _isConnected = false;
                    OnClientDisconnected?.Invoke();
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                }
            }
        }

        /// <summary>
        /// Queue frame for sending. Latest-wins if previous not sent.
        /// </summary>
        public bool SendFrame(PerfProtocol.PortStats[] ports, uint sequence) {
            if (!_isConnected) return false;

            // Write to inactive buffer
            byte[] target = ReferenceEquals(_activeBuffer, _buffer1) ? _buffer2 : _buffer1;
            int len = PerfProtocol.SerializeFrame(ports, sequence, target);
            
            // Swap
            _activeBuffer = target;
            if (_pendingLength > 0) FramesDropped++;
            _pendingLength = len;
            
            return true;
        }

        private void FlushPending() {
            int len = _pendingLength;
            if (len <= 0) return;
            
            byte[] buf = _activeBuffer;
            _pendingLength = 0;

            try {
                lock (_writeLock) {
                    if (_pipe != null && _pipe.IsConnected) {
                        _pipe.Write(buf, 0, len);
                        FramesSent++;
                    }
                }
            } catch {
                _isConnected = false;
            }
        }

        private void Log(string msg) => OnLog?.Invoke($"[Server] {msg}");

        public void Dispose() {
            Stop();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// High-speed pipe client for UI panel.
    /// </summary>
    public sealed class PerfPipeClient : IDisposable {
        private NamedPipeClientStream _pipe;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private volatile bool _isConnected;

        private readonly byte[] _receiveBuffer;
        private readonly PerfProtocol.PortStats[] _portStats;

        public event Action<PerfProtocol.FrameHeader, PerfProtocol.PortStats[]> OnDataReceived;
        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _isConnected;
        public long FramesReceived { get; private set; }

        public PerfPipeClient() {
            _receiveBuffer = new byte[PerfProtocol.FULL_FRAME_SIZE];
            _portStats = new PerfProtocol.PortStats[PerfProtocol.PORT_COUNT];
        }

        public async Task ConnectAsync(int timeoutMs = 5000) {
            _pipe = new NamedPipeClientStream(".", PerfProtocol.PIPE_NAME, PipeDirection.In, PipeOptions.Asynchronous);
            
            Log("Connecting...");
            await Task.Run(() => _pipe.Connect(timeoutMs));

            _isConnected = true;
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop());

            Log("Connected!");
            OnConnected?.Invoke();
        }

        public void Disconnect() {
            _cts?.Cancel();
            _isConnected = false;
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            OnDisconnected?.Invoke();
        }

        private async Task ReceiveLoop() {
            try {
                while (!_cts.Token.IsCancellationRequested && _pipe.IsConnected) {
                    int read = await ReadExactAsync(_receiveBuffer, 0, PerfProtocol.FULL_FRAME_SIZE);
                    if (read < PerfProtocol.FULL_FRAME_SIZE) break;

                    var header = PerfProtocol.DeserializeFrame(_receiveBuffer, _portStats);
                    FramesReceived++;
                    OnDataReceived?.Invoke(header, _portStats);
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Log($"Error: {ex.Message}");
            } finally {
                _isConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        private async Task<int> ReadExactAsync(byte[] buffer, int offset, int count) {
            int total = 0;
            while (total < count) {
                int read = await Task.Factory.FromAsync(
                    (cb, st) => _pipe.BeginRead(buffer, offset + total, count - total, cb, st),
                    _pipe.EndRead, null);
                if (read == 0) break;
                total += read;
            }
            return total;
        }

        private void Log(string msg) => OnLog?.Invoke($"[Client] {msg}");

        public void Dispose() {
            Disconnect();
            _cts?.Dispose();
        }
    }
}
