using System;

namespace BitParser.DataSources {
    /// <summary>
    /// Factory for creating data sources from configuration.
    /// </summary>
    public static class DataSourceFactory {
        /// <summary>
        /// Create data source from configuration.
        /// </summary>
        public static IDataSource CreateFromConfig(SystemConfig config, CompiledSchema schema) {
            if (config == null) throw new ArgumentNullException(nameof(config));
            
            var sourceType = config.DataSourceType?.ToLowerInvariant() ?? "simulation";
            
            switch (sourceType) {
                case "simulation":
                case "sim":
                    return new SimulatedDataSource(
                        schema,
                        config.SimulationIntervalMs,
                        config.SimulationErrorRate);
                
                case "udp":
                    return new UdpDataSource(
                        config.UdpPort,
                        schema?.TotalBytes ?? 1536);
                
                case "serial":
                case "com":
                    return new SerialDataSource(
                        config.SerialPort,
                        config.SerialBaudRate,
                        schema?.TotalBytes ?? 1536);
                
                case "pcap":
                    return new PcapDataSource(config);

                default:
                    throw new NotSupportedException($"Data source type '{sourceType}' is not supported. Use: simulation, udp, serial, pcap");
            }
        }
        
        /// <summary>
        /// Create data source with explicit type.
        /// </summary>
        public static IDataSource Create(DataSourceType type, CompiledSchema schema, object config = null) {
            switch (type) {
                case DataSourceType.Simulation:
                    return new SimulatedDataSource(schema);
                
                case DataSourceType.Udp:
                    int udpPort = config as int? ?? 5000;
                    return new UdpDataSource(udpPort, schema?.TotalBytes ?? 1536);
                
                case DataSourceType.Serial:
                    string port = config as string ?? "COM1";
                    return new SerialDataSource(port, 115200, schema?.TotalBytes ?? 1536);
                
                default:
                    throw new NotSupportedException($"Data source type {type} is not yet implemented");
            }
        }
    }
}
