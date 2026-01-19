using System;
using System.Threading;
using System.Threading.Tasks;
using BitParser.DataSources;

namespace BitParser {
    /// <summary>
    /// The "Easy Mode" integrator.
    /// Connects a Pcap Source directly to a Named Pipe Server.
    /// Use this to add Pipe capabilities to your existing application.
    /// </summary>
    public class PipeBridge : IDisposable {
        private IDataSource _source;
        private PipeDataServer _pipe;
        private SharedMemoryServer _shmem;
        private Task _runTask;
        private CancellationTokenSource _cts;

        /// <summary>
        /// Starts the Bridge: Pcap (Device) -> Pipe/ShMem
        /// </summary>
        public void StartPcapServer(string pipeName, string pcapFilter, string deviceName = "", bool useSharedMemory = false) {
            Stop(); 

            var config = new SystemConfig {
                DataSourceType = "Pcap",
                PcapDeviceName = deviceName,
                PcapFilter = pcapFilter
            };
            _source = new PcapDataSource(config);

            InitTransport(pipeName, useSharedMemory);

            _cts = new CancellationTokenSource();
            _runTask = RunLoopAsync(_cts.Token);
        }

        public void StartSerialServer(string pipeName, string portName, int baudRate, int frameSize = 1536, bool useSharedMemory = false) {
            Stop();

            _source = new SerialDataSource(portName, baudRate, frameSize);
            InitTransport(pipeName, useSharedMemory);

            _cts = new CancellationTokenSource();
            _runTask = RunLoopAsync(_cts.Token);
        }

        public void StartUdpServer(string pipeName, int port, int frameSize = 1536, bool useSharedMemory = false) {
            Stop();

            _source = new UdpDataSource(port, frameSize);
            InitTransport(pipeName, useSharedMemory);

            _cts = new CancellationTokenSource();
            _runTask = RunLoopAsync(_cts.Token);
        }

        private void InitTransport(string name, bool useSharedMemory) {
            if (useSharedMemory) {
                _shmem = new SharedMemoryServer(name);
            } else {
                _pipe = new PipeDataServer(name);
            }
        }

        private uint _sequenceNumber = 0;

        private async Task RunLoopAsync(CancellationToken token) {
            try {
                await _source.ConnectAsync(token);
                
                while (!token.IsCancellationRequested) {
                    byte[] data = await _source.ReadFrameAsync(token);
                    if (data != null) {
                        if (_pipe != null) {
                            _pipe.SendRawData(data, data.Length, _sequenceNumber++);
                        } else if (_shmem != null) {
                            _shmem.SendRawData(data, data.Length, _sequenceNumber++);
                        }
                    } else {
                        await Task.Delay(1, token);
                    }
                }
            } catch (OperationCanceledException) { 
                // Normal stop
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[Bridge] Error: {ex.Message}");
            }
        }

        public void Stop() {
            _cts?.Cancel();
            try { _runTask?.Wait(1000); } catch { }
            
            _source?.Disconnect();
            _source?.Dispose();
            
            _pipe?.Dispose();
            _shmem?.Dispose();
            
            _source = null;
            _pipe = null;
            _shmem = null;
        }

        public void Dispose() {
            Stop();
        }
    }
}
