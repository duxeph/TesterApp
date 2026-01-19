using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser.DataSources {
    /// <summary>
    /// Serial port data source (COM port / RS232 / USB Serial).
    /// Common for embedded systems and development boards.
    /// </summary>
    public class SerialDataSource : IDataSource {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly int _frameSize;
        
        private SerialPort _serialPort;
        private bool _isConnected;
        private long _frameCount;
        private long _byteCount;
        private long _errorCount;
        private DateTime _startTime;
        private Timer _statsTimer;
        private double _currentFps;
        
        private byte[] _readBuffer;
        private int _bufferPosition;
        
        public bool IsConnected => _isConnected;
        public string SourceName => $"Serial {_portName}";
        public DataSourceType SourceType => DataSourceType.Serial;
        
        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<Exception> OnError;
        
        public SerialDataSource(string portName = "COM1", int baudRate = 115200, int frameSize = 1536) {
            _portName = portName;
            _baudRate = baudRate;
            _frameSize = frameSize;
            _readBuffer = new byte[frameSize * 2];  // Double buffer
        }
        
        public Task ConnectAsync(CancellationToken cancellationToken = default) {
            if (_isConnected) return Task.CompletedTask;
            
            try {
                _serialPort = new SerialPort(_portName, _baudRate) {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    ReadBufferSize = 65536
                };
                
                _serialPort.Open();
                
                _isConnected = true;
                _startTime = DateTime.Now;
                _frameCount = 0;
                _bufferPosition = 0;
                
                _statsTimer = new Timer(UpdateStats, null, 1000, 1000);
                
                Log($"Opened {_portName} at {_baudRate} baud");
                OnConnected?.Invoke();
                
                return Task.CompletedTask;
                
            } catch (Exception ex) {
                Log($"Failed to open serial port: {ex.Message}");
                OnError?.Invoke(ex);
                throw;
            }
        }
        
        public void Disconnect() {
            if (!_isConnected) return;
            
            _isConnected = false;
            _statsTimer?.Dispose();
            _statsTimer = null;
            
            try {
                _serialPort?.Close();
                _serialPort?.Dispose();
            } catch { }
            _serialPort = null;
            
            Log("Serial port closed");
            OnDisconnected?.Invoke();
        }
        
        public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken = default) {
            if (!_isConnected || _serialPort == null || !_serialPort.IsOpen) {
                return null;
            }
            
            try {
                // Read until we have a full frame
                while (_bufferPosition < _frameSize) {
                    if (cancellationToken.IsCancellationRequested) {
                        return null;
                    }
                    
                    int bytesAvailable = _serialPort.BytesToRead;
                    if (bytesAvailable == 0) {
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }
                    
                    int toRead = Math.Min(bytesAvailable, _frameSize - _bufferPosition);
                    int bytesRead = _serialPort.Read(_readBuffer, _bufferPosition, toRead);
                    _bufferPosition += bytesRead;
                }
                
                // Got a full frame
                byte[] frame = new byte[_frameSize];
                Buffer.BlockCopy(_readBuffer, 0, frame, 0, _frameSize);
                
                // Shift remaining data
                if (_bufferPosition > _frameSize) {
                    Buffer.BlockCopy(_readBuffer, _frameSize, _readBuffer, 0, _bufferPosition - _frameSize);
                    _bufferPosition -= _frameSize;
                } else {
                    _bufferPosition = 0;
                }
                
                _frameCount++;
                _byteCount += _frameSize;
                
                return frame;
                
            } catch (TimeoutException) {
                // Serial read timeout is normal
                return null;
            } catch (Exception ex) {
                _errorCount++;
                Log($"Read error: {ex.Message}");
                OnError?.Invoke(ex);
                return null;
            }
        }
        
        private long _lastFrameCount;
        
        private void UpdateStats(object state) {
            long currentFrames = _frameCount;
            _currentFps = currentFrames - _lastFrameCount;
            _lastFrameCount = currentFrames;
        }
        
        public DataSourceStats GetStats() {
            return new DataSourceStats {
                FramesRead = _frameCount,
                BytesRead = _byteCount,
                Errors = _errorCount,
                Uptime = _isConnected ? DateTime.Now - _startTime : TimeSpan.Zero,
                FramesPerSecond = _currentFps
            };
        }
        
        private void Log(string message) {
            OnLog?.Invoke($"[Serial] {message}");
        }
        
        public void Dispose() {
            Disconnect();
        }
    }
}
