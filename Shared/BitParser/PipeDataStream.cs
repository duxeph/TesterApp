using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser {
    /// <summary>
    /// Optimized pipe server with buffer pooling and lock-free latest-wins.
    /// </summary>
    public sealed class PipeDataServer : IDisposable {
        private readonly string _pipeName;
        private readonly int _bufferSize;
        private NamedPipeServerStream _pipeServer;
        private CancellationTokenSource _cts;
        private Task _connectionTask;
        private volatile bool _isConnected;
        private readonly object _writeLock = new object();

        // Double buffer for lock-free latest-wins
        private readonly byte[] _buffer1;
        private readonly byte[] _buffer2;
        private byte[] _activeBuffer;
        private volatile int _pendingLength;
        private volatile int _bufferSwitch;

        // Pre-allocated send buffer
        private readonly byte[] _sendBuffer;
        private readonly BufferPool _bufferPool;

        // Statistics
        public long TotalBytesSent { get; private set; }
        public long FramesSent { get; private set; }
        public long FramesDropped { get; private set; }
        public bool IsConnected => _isConnected;

        public event Action<string> OnLog;
        public event Action OnClientConnected;
        public event Action OnClientDisconnected;

        public PipeDataServer(string pipeName = null, int bufferSize = 65536) {
            _pipeName = pipeName ?? PipeProtocol.PIPE_NAME;
            _bufferSize = bufferSize;
            
            // Pre-allocate buffers
            _buffer1 = new byte[bufferSize];
            _buffer2 = new byte[bufferSize];
            _sendBuffer = new byte[bufferSize];
            _activeBuffer = _buffer1;
            _bufferPool = new BufferPool(4, bufferSize);
        }

        public void Start() {
            if (_pipeServer != null) return;

            _cts = new CancellationTokenSource();
            _connectionTask = Task.Run(() => ConnectionLoop());
            Log($"Pipe server started on: \\\\.\\pipe\\{_pipeName}");
        }
        
        public PipeStats GetStats() {
            return new PipeStats {
                BytesWritten = TotalBytesSent,
                FramesWritten = FramesSent,
                FramesDropped = FramesDropped,
                ConnectedClients = _isConnected ? 1 : 0
            };
        }

        public void Stop() {
            _cts?.Cancel();
            try { _pipeServer?.Dispose(); } catch { }
            _pipeServer = null;
            _isConnected = false;
            Log("Pipe server stopped");
        }

        private async Task ConnectionLoop() {
            while (!_cts.Token.IsCancellationRequested) {
                try {
                    _pipeServer = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.Out,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        _bufferSize,
                        _bufferSize);

                    Log("Waiting for client...");
                    await Task.Factory.FromAsync(
                        _pipeServer.BeginWaitForConnection,
                        _pipeServer.EndWaitForConnection,
                        null);

                    if (_cts.Token.IsCancellationRequested) break;

                    _isConnected = true;
                    Log("Client connected!");
                    OnClientConnected?.Invoke();

                    // Continuous flush loop
                    while (_isConnected && _pipeServer.IsConnected && !_cts.Token.IsCancellationRequested) {
                        if (_pendingLength > 0) {
                            FlushPendingData();
                        }
                        await Task.Delay(1);  // Minimal delay, high throughput
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    Log($"Error: {ex.Message}");
                    await Task.Delay(1000);
                } finally {
                    _isConnected = false;
                    OnClientDisconnected?.Invoke();
                    try { _pipeServer?.Dispose(); } catch { }
                    _pipeServer = null;
                }
            }
        }

        /// <summary>
        /// Queue data using double buffering (lock-free latest-wins).
        /// </summary>
        public bool QueueData(byte[] data, int length) {
            if (!_isConnected) return false;

            // Swap to inactive buffer and copy
            int currentSwitch = _bufferSwitch;
            byte[] targetBuffer = currentSwitch == 0 ? _buffer2 : _buffer1;
            
            int copyLength = Math.Min(length, targetBuffer.Length);
            Buffer.BlockCopy(data, 0, targetBuffer, 0, copyLength);
            
            // Atomic swap
            Interlocked.Exchange(ref _bufferSwitch, currentSwitch == 0 ? 1 : 0);
            _activeBuffer = targetBuffer;
            
            if (_pendingLength > 0) {
                FramesDropped++;
            }
            _pendingLength = copyLength;

            return true;
        }

        /// <summary>
        /// Send pre-serialized delta frame.
        /// </summary>
        public bool SendDeltaFrame(ParseResult parseResult, uint sequenceNumber) {
            if (!_isConnected || parseResult.Count == 0) return false;

            int length = PipeProtocol.SerializeDeltaFrame(parseResult, sequenceNumber, _sendBuffer);
            if (length > 0) {
                return QueueData(_sendBuffer, length);
            }
            return false;
        }

        /// <summary>
        /// Send raw data.
        /// </summary>
        public bool SendRawData(byte[] data, int length, uint sequenceNumber) {
            if (!_isConnected) return false;

            int packetLength = PipeProtocol.SerializeDataFrame(data, length, sequenceNumber, _sendBuffer);
            if (packetLength > 0) {
                return QueueData(_sendBuffer, packetLength);
            }
            return false;
        }

        private void FlushPendingData() {
            int length = _pendingLength;
            if (length <= 0) return;

            byte[] bufferToSend = _activeBuffer;
            _pendingLength = 0;

            try {
                lock (_writeLock) {
                    if (_pipeServer != null && _pipeServer.IsConnected) {
                        _pipeServer.Write(bufferToSend, 0, length);
                        TotalBytesSent += length;
                        FramesSent++;
                    }
                }
            } catch (Exception ex) {
                Log($"Write error: {ex.Message}");
                _isConnected = false;
            }
        }

        private void Log(string message) => OnLog?.Invoke($"[Server] {message}");

        public void Dispose() {
            Stop();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// Statistics for pipe connection.
    /// </summary>
    public struct PipeStats {
        public long BytesWritten;
        public long FramesWritten;
        public long FramesDropped;
        public int ConnectedClients;
    }

    /// <summary>
    /// Optimized pipe client with pre-allocated buffers.
    /// </summary>
    public sealed class PipeDataClient : IDisposable {
        private readonly string _pipeName;
        private readonly int _bufferSize;
        private NamedPipeClientStream _pipeClient;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private volatile bool _isConnected;
        private volatile bool _shouldReconnect;  // Auto-reconnect flag
        private Task _reconnectTask;

        // Pre-allocated buffers
        private readonly byte[] _receiveBuffer;
        private readonly byte[] _headerBuffer;
        private readonly ParseResult _parseResult;

        public event Action<PipeProtocol.MessageHeader, byte[]> OnDataReceived;
        public event Action<ParseResult> OnParsedDataReceived;  // New: direct ParseResult
        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _isConnected;
        public bool AutoReconnect { get; set; } = true;
        public int ReconnectDelayMs { get; set; } = 2000;
        public int MaxReconnectAttempts { get; set; } = -1;  // -1 = infinite

        public PipeDataClient(string pipeName = null, int bufferSize = 65536) {
            _pipeName = pipeName ?? PipeProtocol.PIPE_NAME;
            _bufferSize = bufferSize;
            _receiveBuffer = new byte[bufferSize];
            _headerBuffer = new byte[PipeProtocol.HEADER_SIZE];
            _parseResult = new ParseResult(1024);
        }

        /// <summary>
        /// Connect to pipe with optional auto-reconnection.
        /// </summary>
        public async Task ConnectAsync(int timeoutMs = 5000) {
            _shouldReconnect = AutoReconnect;
            await ConnectAsyncInternal(timeoutMs);
        }
        
        /// <summary>
        /// Connect with retry logic.
        /// </summary>
        public async Task ConnectWithRetry(int timeoutMs = 5000, int maxAttempts = -1) {
            int attempt = 0;
            
            while (maxAttempts < 0 || attempt < maxAttempts) {
                try {
                    await ConnectAsyncInternal(timeoutMs);
                    return;  // Success
                } catch (Exception ex) {
                    attempt++;
                    Log($"Connection attempt {attempt} failed: {ex.Message}");
                    
                    if (maxAttempts >= 0 && attempt >= maxAttempts) {
                        throw;  // Give up
                    }
                    
                    Log($"Retrying in {ReconnectDelayMs}ms...");
                    await Task.Delay(ReconnectDelayMs);
                }
            }
        }
        
        private async Task ConnectAsyncInternal(int timeoutMs) {
            _pipeClient = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous);

            Log($"Connecting to {_pipeName}...");
            await Task.Run(() => _pipeClient.Connect(timeoutMs));

            _isConnected = true;
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop());

            Log("Connected!");
            OnConnected?.Invoke();
        }

        public void Disconnect() {
            _shouldReconnect = false;  // Stop auto-reconnection
            _cts?.Cancel();
            _isConnected = false;
            try { _pipeClient?.Dispose(); } catch { }
            _pipeClient = null;
            OnDisconnected?.Invoke();
        }

        private async Task ReceiveLoop() {
            try {
                while (!_cts.Token.IsCancellationRequested && _pipeClient.IsConnected) {
                    // Read header
                    int headerRead = await ReadExactAsync(_receiveBuffer, 0, PipeProtocol.HEADER_SIZE);
                    if (headerRead < PipeProtocol.HEADER_SIZE) break;

                    var header = PipeProtocol.DeserializeHeader(_receiveBuffer);

                    // Calculate and read payload
                    int payloadSize = header.MessageType == PipeProtocol.MSG_DELTA_FRAME
                        ? header.ValueCount * PipeProtocol.VALUE_ENTRY_SIZE
                        : header.ValueCount;

                    if (payloadSize > 0) {
                        int payloadRead = await ReadExactAsync(_receiveBuffer, PipeProtocol.HEADER_SIZE, payloadSize);
                        if (payloadRead < payloadSize) break;
                    }

                    // Fast path: direct to ParseResult
                    if (header.MessageType == PipeProtocol.MSG_DELTA_FRAME && OnParsedDataReceived != null) {
                        PipeProtocol.DeserializeToParseResult(_receiveBuffer, header.ValueCount, _parseResult);
                        OnParsedDataReceived.Invoke(_parseResult);
                    }
                    
                    // Also fire raw event for compatibility
                    int totalSize = PipeProtocol.HEADER_SIZE + payloadSize;
                    byte[] messageCopy = new byte[totalSize];
                    Buffer.BlockCopy(_receiveBuffer, 0, messageCopy, 0, totalSize);
                    OnDataReceived?.Invoke(header, messageCopy);
                }
        } catch (OperationCanceledException) {
                // Purposeful cancellation
            } catch (Exception ex) {
                Log($"Receive error: {ex.Message}");
            } finally {
                _isConnected = false;
                OnDisconnected?.Invoke();
                
                // Auto-reconnect if enabled
                if (_shouldReconnect && !_cts.Token.IsCancellationRequested) {
                    Log($"Connection lost. Auto-reconnecting in {ReconnectDelayMs}ms...");
                    _reconnectTask = Task.Run(async () => {
                        await Task.Delay(ReconnectDelayMs);
                        
                        int attempt = 0;
                        while (_shouldReconnect && (MaxReconnectAttempts < 0 || attempt < MaxReconnectAttempts)) {
                            try {
                                await ConnectAsyncInternal(5000);
                                Log("Auto-reconnection successful!");
                                return;
                            } catch {
                                attempt++;
                                if (MaxReconnectAttempts >= 0 && attempt >= MaxReconnectAttempts) {
                                    Log($"Auto-reconnection failed after {attempt} attempts.");
                                    return;
                                }
                                await Task.Delay(ReconnectDelayMs);
                            }
                        }
                    });
                }
            }
        }

        private async Task<int> ReadExactAsync(byte[] buffer, int offset, int count) {
            int totalRead = 0;
            while (totalRead < count) {
                int bytesRead = await Task.Factory.FromAsync(
                    (callback, state) => _pipeClient.BeginRead(buffer, offset + totalRead, count - totalRead, callback, state),
                    _pipeClient.EndRead,
                    null);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        private void Log(string message) => OnLog?.Invoke($"[Client] {message}");

        public void Dispose() {
            Disconnect();
            _cts?.Dispose();
        }
    }
}
