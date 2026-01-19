using System;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser.DataSources {
    /// <summary>
    /// Abstraction for data sources (FPGA, simulation, network, etc.)
    /// This allows easy switching between real hardware and test data.
    /// </summary>
    public interface IDataSource : IDisposable {
        /// <summary>
        /// Connect to the data source.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Disconnect from the data source.
        /// </summary>
        void Disconnect();
        
        /// <summary>
        /// Read next data frame.
        /// </summary>
        /// <returns>Raw byte data, or null if no data available</returns>
        Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// True if connected and ready to read.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Human-readable source name for logging.
        /// </summary>
        string SourceName { get; }
        
        /// <summary>
        /// Source type for diagnostics.
        /// </summary>
        DataSourceType SourceType { get; }
        
        /// <summary>
        /// Statistics and diagnostics.
        /// </summary>
        DataSourceStats GetStats();
        
        /// <summary>
        /// Events for monitoring.
        /// </summary>
        event Action<string> OnLog;
        event Action OnConnected;
        event Action OnDisconnected;
        event Action<Exception> OnError;
    }
    
    /// <summary>
    /// Data source types.
    /// </summary>
    public enum DataSourceType {
        Simulation,
        Udp,
        Serial,
        TcpClient,
        TcpServer,
        SharedMemory,
        FpgaDirect,
        FilePlayback,
        Pcap
    }
    
    /// <summary>
    /// Statistics for data source.
    /// </summary>
    public struct DataSourceStats {
        public long FramesRead;
        public long BytesRead;
        public long Errors;
        public TimeSpan Uptime;
        public double FramesPerSecond;
        
        public override string ToString() {
            return $"Frames: {FramesRead:N0} | {FramesPerSecond:F1} FPS | Bytes: {BytesRead:N0} | Errors: {Errors}";
        }
    }
}
