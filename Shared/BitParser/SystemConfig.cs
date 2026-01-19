using System;
using System.IO;
using System.Web.Script.Serialization;

namespace BitParser {
    /// <summary>
    /// Centralized configuration for the entire FPGA test system.
    /// All settings in one place - no more scattered hardcoded values!
    /// </summary>
    public class SystemConfig {
        // File Paths
        public string SchemaPath { get; set; } = "Schema\\CbitSchema.xml";
        public string LogDirectory { get; set; } = "logs";
        
        // Named Pipes
        public string DataPipeName { get; set; } = "BitStatusPipe";
        public string PerfPipeName { get; set; } = "EthernetPerfPipe";
        public int PipeBufferSize { get; set; } = 65536;
        public int PipeConnectionTimeoutMs { get; set; } = 5000;
        
        // Performance
        public int UIUpdateRateFps { get; set; } = 30;
        public int SimulationIntervalMs { get; set; } = 50;
        public int PerfTestIntervalMs { get; set; } = 40;
        
        // Reconnection & Recovery
        public bool AutoReconnect { get; set; } = true;
        public int ReconnectDelayMs { get; set; } = 2000;
        public int MaxReconnectAttempts { get; set; } = -1;  // -1 = infinite
        
        // Validation
        public bool StrictValidation { get; set; } = true;
        public bool LogOutOfRangeValues { get; set; } = true;
        public bool LogFaultConditions { get; set; } = true;
        
        // Logging
        public bool EnableFileLogging { get; set; } = true;
        public bool EnableCsvLogging { get; set; } = true;
        public int CsvLogIntervalMs { get; set; } = 1000;
        public int MaxLogFileSizeMB { get; set; } = 50;
        public bool EnableDebugLogging { get; set; } = false;
        
        // Memory Management
        public bool EnableMemoryMonitoring { get; set; } = true;
        public long MaxMemoryUsageBytes { get; set; } = 500_000_000;  // 500 MB
        public int MemoryCheckIntervalMs { get; set; } = 60000;  // 1 minute
        
        // UI
        public bool AutoLaunchApps { get; set; } = true;
        public bool MinimizeLauncherOnStart { get; set; } = false;
        
        // Data Source
        public string DataSourceType { get; set; } = "Simulation";  // Simulation, UDP, Serial
        public int UdpPort { get; set; } = 5000;
        public string SerialPort { get; set; } = "COM1";
        public int SerialBaudRate { get; set; } = 115200;
        public string PcapDeviceName { get; set; } = "\\Device\\NPF_Loopback"; // Default to loopback or empty
        public string PcapFilter { get; set; } = "udp port 5000";
        
        // Simulation
        public bool EnableSimulation { get; set; } = true;
        public double SimulationErrorRate { get; set; } = 0.01;  // 1% error rate
        
        /// <summary>
        /// Load configuration from JSON file.
        /// </summary>
        public static SystemConfig Load(string path) {
            try {
                if (File.Exists(path)) {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var config = serializer.Deserialize<SystemConfig>(json);
                    return config ?? CreateDefault();
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }
            
            return CreateDefault();
        }
        
        /// <summary>
        /// Save configuration to JSON file.
        /// </summary>
        public void Save(string path) {
            try {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                
                // Pretty print (manual formatting for .NET Framework)
                json = FormatJson(json);
                
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllText(path, json);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Create default configuration.
        /// </summary>
        public static SystemConfig CreateDefault() {
            return new SystemConfig();
        }
        
        /// <summary>
        /// Find configuration file in common locations.
        /// </summary>
        public static string FindConfigFile() {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            
            var paths = new[] {
                Path.Combine(basePath, "system.config.json"),
                Path.Combine(basePath, "config.json"),
                Path.Combine(basePath, "..", "..", "..", "system.config.json"),
                Path.Combine(basePath, "..", "..", "..", "config.json")
            };
            
            foreach (var path in paths) {
                if (File.Exists(path)) {
                    return Path.GetFullPath(path);
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Simple JSON pretty printer (manual indentation).
        /// </summary>
        private string FormatJson(string json) {
            var result = new System.Text.StringBuilder();
            int indent = 0;
            bool inString = false;
            
            for (int i = 0; i < json.Length; i++) {
                char c = json[i];
                
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) {
                    inString = !inString;
                }
                
                if (!inString) {
                    if (c == '{' || c == '[') {
                        result.Append(c);
                        result.Append("\r\n");
                        indent++;
                        result.Append(new string(' ', indent * 2));
                    } else if (c == '}' || c == ']') {
                        result.Append("\r\n");
                        indent--;
                        result.Append(new string(' ', indent * 2));
                        result.Append(c);
                    } else if (c == ',') {
                        result.Append(c);
                        result.Append("\r\n");
                        result.Append(new string(' ', indent * 2));
                    } else if (c == ':') {
                        result.Append(c);
                        result.Append(' ');
                    } else if (c != ' ') {
                        result.Append(c);
                    }
                } else {
                    result.Append(c);
                }
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Validate configuration values.
        /// </summary>
        public bool Validate(out string error) {
            if (UIUpdateRateFps <= 0 || UIUpdateRateFps > 120) {
                error = "UIUpdateRateFps must be between 1 and 120";
                return false;
            }
            
            if (SimulationIntervalMs < 10) {
                error = "SimulationIntervalMs must be at least 10ms";
                return false;
            }
            
            if (PipeBufferSize < 4096) {
                error = "PipeBufferSize must be at least 4096 bytes";
                return false;
            }
            
            if (MaxMemoryUsageBytes < 100_000_000) {
                error = "MaxMemoryUsageBytes must be at least 100 MB";
                return false;
            }
            
            error = null;
            return true;
        }
    }
}
