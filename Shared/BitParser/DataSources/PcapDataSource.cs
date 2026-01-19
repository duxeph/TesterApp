using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace BitParser.DataSources {
    public class PcapDataSource : IDataSource {
        private PacketCommunicator _communicator;
        private Thread _captureThread;
        private readonly ConcurrentQueue<byte[]> _packetQueue;
        private readonly SystemConfig _config;
        private bool _isConnected;
        private volatile bool _stopCapture;
        
        // Stats
        private long _framesRead;
        private long _bytesRead;
        private DateTime _startTime;
        private long _lastFrameCount;
        private double _currentFps;
        private Timer _statsTimer;

        public bool IsConnected => _isConnected;
        public string SourceName => "Pcap Ethernet";
        public DataSourceType SourceType => DataSourceType.Pcap;

        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<Exception> OnError;

        public PcapDataSource(SystemConfig config) {
            _config = config;
            _packetQueue = new ConcurrentQueue<byte[]>();
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) {
            if (_isConnected) return Task.CompletedTask;

            try {
                // 1. Find Device
                IList<LivePacketDevice> allDevices = LivePacketDevice.AllLocalMachine;
                if (allDevices.Count == 0) {
                    throw new Exception("No interfaces found! Make sure WinPcap/Npcap is installed.");
                }

                LivePacketDevice device = null;
                
                // Try to match by name or description
                string target = _config.PcapDeviceName;
                if (!string.IsNullOrWhiteSpace(target)) {
                    device = allDevices.FirstOrDefault(d => 
                        d.Name.Contains(target) || 
                        (d.Description != null && d.Description.Contains(target)));
                }

                // Fallback to first if not found or empty
                if (device == null) {
                    Log($"Device '{target}' not found. Using first available: {allDevices[0].Description}");
                    device = allDevices[0];
                } else {
                    Log($"Selected device: {device.Description} ({device.Name})");
                }

                // 2. Open Communicator
                // 65536 len, Promiscuous, 1000ms read timeout
                _communicator = device.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000);

                // 3. Set Filter
                if (!string.IsNullOrWhiteSpace(_config.PcapFilter)) {
                    Log($"Applying filter: {_config.PcapFilter}");
                    using (BerkeleyPacketFilter filter = _communicator.CreateFilter(_config.PcapFilter)) {
                        _communicator.SetFilter(filter);
                    }
                }

                // 4. Start Capture Thread
                _stopCapture = false;
                _captureThread = new Thread(CaptureLoop) {
                    IsBackground = true,
                    Name = "PcapCaptureThread"
                };
                _captureThread.Start();

                _isConnected = true;
                _startTime = DateTime.Now;
                _statsTimer = new Timer(UpdateStats, null, 1000, 1000);
                
                OnConnected?.Invoke();
            } catch (Exception ex) {
                OnError?.Invoke(ex);
                throw;
            }

            return Task.CompletedTask;
        }

        private void CaptureLoop() {
            try {
                while (!_stopCapture) {
                    Packet packet;
                    PacketCommunicatorReceiveResult result = _communicator.ReceivePacket(out packet);

                    switch (result) {
                        case PacketCommunicatorReceiveResult.Timeout:
                            // Continue...
                            continue;
                        case PacketCommunicatorReceiveResult.Ok:
                            ProcessPacket(packet);
                            break;
                        case PacketCommunicatorReceiveResult.Eof:
                            _stopCapture = true;
                            break;
                        default:
                            continue;
                    }
                }
            } catch (Exception ex) {
                if (!_stopCapture) {
                    Log($"Capture error: {ex.Message}");
                    OnError?.Invoke(ex);
                }
            }
        }

        private void ProcessPacket(Packet packet) {
            // Extract UDP payload
            // Assuming the filter ensures it's UDP, but good to check.
            IpV4Datagram ip = packet.Ethernet.IpV4;
            UdpDatagram udp = ip.Udp;

            if (udp != null && udp.Payload != null && udp.Payload.Length > 0) {
                byte[] data = udp.Payload.ToMemoryStream().ToArray();
                _packetQueue.Enqueue(data);
                
                // Drop if queue too full to prevent OOM
                if (_packetQueue.Count > 1000) {
                    _packetQueue.TryDequeue(out _);
                }
            }
        }

        public void Disconnect() {
            if (!_isConnected) return;

            _stopCapture = true;
            _captureThread?.Join(500);
            _communicator?.Dispose();
            _communicator = null;
            
            _statsTimer?.Dispose();
            _isConnected = false;
            OnDisconnected?.Invoke();
        }

        public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken = default) {
            if (!_isConnected) return null;

            // Spin/Wait for data
            while (!_stopCapture && !cancellationToken.IsCancellationRequested) {
                if (_packetQueue.TryDequeue(out byte[] data)) {
                    Interlocked.Increment(ref _framesRead);
                    Interlocked.Add(ref _bytesRead, data.Length);
                    return data;
                }
                await Task.Delay(1, cancellationToken); // Yield
            }
            return null;
        }

        private void UpdateStats(object state) {
            long currentFrames = Interlocked.Read(ref _framesRead);
            _currentFps = currentFrames - _lastFrameCount;
            _lastFrameCount = currentFrames;
        }

        public DataSourceStats GetStats() {
            return new DataSourceStats {
                FramesRead = Interlocked.Read(ref _framesRead),
                BytesRead = Interlocked.Read(ref _bytesRead),
                Errors = 0,
                Uptime = _isConnected ? DateTime.Now - _startTime : TimeSpan.Zero,
                FramesPerSecond = _currentFps
            };
        }

        private void Log(string msg) => OnLog?.Invoke($"[Pcap] {msg}");

        public void Dispose() {
            Disconnect();
        }
    }
}
