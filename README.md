# PipedStatusProject - Complete Documentation

## Overview

This project provides a high-performance, real-time data parsing and display system for embedded/FPGA systems. It uses a **piped architecture** to separate data acquisition from UI rendering, ensuring the interface never freezes even under high data rates (1536 bytes @ 50ms = 30KB/s).

**Target Framework:** .NET Framework 4.7.2  
**Architecture:** Producer-Consumer via Named Pipes  
**Max Throughput:** 20+ packets/second with zero UI freezing

---

## Solution Structure

```
PipedStatusProject/
├── PipedStatusProject.sln              # Visual Studio Solution
├── README.md                           # This file
├── ALGORITHMS.md                       # Detailed algorithm documentation
│
├── Schema/                             # XML schema definitions
│   ├── BitSchema.xml                   # Original format (bit_0, bit_1, ...)
│   └── CbitSchema.xml                  # New format (<cbit><sub>...)
│
├── Shared/BitParser/                   # Core parsing library (shared DLL)
│   ├── CompiledSchema.cs               # XML parser
│   ├── FastBitParser.cs                # High-speed byte parser
│   ├── PipeProtocol.cs                 # Binary protocol
│   ├── PipeDataStream.cs               # Named pipe server/client
│   ├── ThrottledUIUpdater.cs           # UI update throttling
│   └── StatusLogger.cs                 # CSV logging & monitoring
│
├── PipeListener/MainApp/               # Data producer application
│   └── Form1.cs                        # Simulates data, serves via pipe
│
├── BitStatusPanel/BitStatusPanel/      # Data consumer/UI application
│   └── Form1.cs                        # TreeListView display with validation
│
├── EthernetPerfTester/                 # Ethernet performance testing
│   ├── Form1.cs                        # Dual-mode (server/client)
│   ├── PerfProtocol.cs                 # Port stats protocol
│   └── PortStatsGrid.cs                # Ultra-fast 16-port grid
│
└── TestHarness/                        # Comprehensive test suite
    ├── Form1.cs                        # Test runner UI
    ├── TestFramework.cs                # ITest interface, TestRunner
    └── SampleTests.cs                  # Config, Power, Ping, Timing, etc.
```

---

## Projects

### 1. BitParser (Shared Library)

**Path:** `Shared/BitParser/BitParser.csproj`  
**Output:** `BitParser.dll`  
**Purpose:** Core parsing engine shared by all applications

#### Files:

| File | Purpose |
|------|---------|
| `CompiledSchema.cs` | Parses XML schema **once at startup** into optimized runtime structures |
| `FastBitParser.cs` | High-speed byte array parser with delta detection and validation |
| `PipeProtocol.cs` | Binary message format for pipe communication (unsafe, zero-alloc) |
| `PipeDataStream.cs` | Named pipe server/client with double-buffering |
| `ThrottledUIUpdater.cs` | Limits UI updates to sustainable frame rate (30 FPS) |
| `StatusLogger.cs` | CSV logging at 1-second intervals with file rotation |

#### Key Classes:

##### `CompiledSchema`
- Parses both XML formats (BitSchema and CbitSchema)
- Pre-computes masks, shifts, and validation rules
- Loaded once, never re-parsed during operation

##### `FastBitParser`
- Zero allocations in hot path after initialization
- Pre-compiled `CompiledField[]` array for cache-friendly access
- Integer-based error tracking (no string allocations)
- Delta detection: only reports changed values

##### `PipeProtocol`
- Uses `unsafe` pointer operations for serialization
- Fixed 16-byte structs for predictable performance
- Buffer pooling to avoid GC pressure

##### `StatusLogger`
- 1-second interval CSV logging
- Automatic file rotation (daily or 100MB)
- Field names as headers
- Values with status (OK/RANGE/FAULT)

---

### 2. MainApp (Data Producer)

**Path:** `PipeListener/MainApp/MainApp.csproj`  
**Output:** `MainApp.exe`  
**Purpose:** Simulates data acquisition and serves parsed data via named pipe

#### Features:
- Loads XML schema
- Generates simulated device data
- Parses data using FastBitParser
- Sends only changed values (delta compression)
- Serves data via named pipe to BitStatusPanel

#### UI:
- "Load Schema..." button
- "Start Pipe Server" button  
- "Start Simulation" button
- Statistics display

---

### 3. BitStatusPanel (Data Consumer/UI)

**Path:** `BitStatusPanel/BitStatusPanel/BitStatusPanel.csproj`  
**Output:** `BitStatusPanel.exe`  
**Purpose:** Real-time display of parsed data with validation and error tracking

#### Features:
- Connects to MainApp via named pipe
- Displays data in hierarchical TreeListView
- Colorizes rows based on validation status
- Tracks cumulative error counts per field
- CSV logging at 1-second intervals
- Stability monitoring for long-running operation

#### UI Columns:
| Column | Description |
|--------|-------------|
| Name | Field name from schema |
| Value | Computed value (resolution × raw + bias) |
| Raw | Raw hex value after masking |
| Unit | Unit of measurement (V, °C, etc.) |
| Status | OK, RANGE, or FAULT |
| Errors | Cumulative error count |
| Min | Minimum valid value |
| Max | Maximum valid value |

#### Colorization:
| Color | Meaning |
|-------|---------|
| 🟢 Light Green | Value is valid (within min/max, not fault) |
| 🔴 Light Red | Error (out of range or fault condition) |
| 🟡 Light Yellow | Value changed (no validation rules) |
| ⬜ White | No validation defined |

---

### 4. EthernetPerfTester (Performance Testing)

**Path:** `EthernetPerfTester/EthernetPerfTester.csproj`  
**Output:** `EthernetPerfTester.exe`  
**Purpose:** Ethernet forwarding table performance testing at 40ms intervals

#### Dual Mode Operation:

##### Server Mode (Test Runner)
- Generates packet forwarding simulation
- Port 1-8 → Port 9-16 mapping
- VLAN ID comparison for validation
- Serves stats via named pipe

##### Client Mode (UI Panel)
- Connects to server via named pipe
- Displays 16-port statistics grid
- 25 FPS (40ms) update rate

#### Grid Layout:
| Column | Description |
|--------|-------------|
| Port | Port number (1-8 blue, 9-16 green) |
| RX Count | Packets received |
| TX Count | Packets transmitted |
| Errors | Mismatch count |

---

## XML Schema Formats

### Format 1: BitSchema (Original)

```xml
<BitSchema version="1.0" totalBytes="1536">
  <cbit 
    Name="SystemStatus" 
    Offset="4" 
    Size="4"
    Mask="0xFFFFFFFF"
    Resolution="1"
    Visible="true"
    bit_0="PowerGood" 
    bit_1="ClockLocked"
    ...
    bit_31="Reserved">
    
    <!-- Optional structured fields -->
    <field Name="ErrorCode" StartBit="0" EndBit="15" 
           Mask="0x0000FFFF" Resolution="1" 
           min="0" max="100" fault="0" Unit=""/>
  </cbit>
</BitSchema>
```

### Format 2: CbitSchema (New)

```xml
<root totalBytes="256">
  <cbit Name="Power" visible="1" offset="8" length="8">
    <sub Name="Primary" 
         sub_offset="0" 
         mask="0x0000FFFF" 
         length="4" 
         resolution="0.001" 
         bias="0" 
         max="5.5" 
         min="4.5" 
         unit="V"/>
  </cbit>
</root>
```

### Schema Attributes:

| Attribute | Description | Example |
|-----------|-------------|---------|
| `Name` | Field display name | "Primary Voltage" |
| `offset` | Byte offset in packet | 8 |
| `length` | Byte length | 4 |
| `mask` | Bit mask (hex) | 0x0000FFFF |
| `resolution` | Multiplier for raw value | 0.001 |
| `bias` | Added after resolution | -40 |
| `min` | Minimum valid value | 3.1 |
| `max` | Maximum valid value | 3.5 |
| `fault` | Value indicating fault | 0 |
| `unit` | Display unit | "V", "°C" |
| `visible` | Show in UI (1/0) | 1 |

### Value Calculation:
```
computed_value = (raw_value × resolution) + bias
```

### Validation Logic:
```
if (computed_value == fault) → FAULT
else if (computed_value < min) → OUT_OF_RANGE  
else if (computed_value > max) → OUT_OF_RANGE
else → VALID
```

---

## Performance Optimizations

### Zero-Allocation Hot Path

| Component | Technique |
|-----------|-----------|
| Parsing | Pre-allocated `ParsedValue[]` array |
| Error tracking | Integer array indexed by field ID |
| Serialization | Unsafe pointer writes |
| UI updates | Double buffering with atomic swap |

### Struct Layouts

```csharp
// 20 bytes, cache-aligned
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ParsedValue {
    public ushort WordIndex;    // 2
    public short FieldIndex;    // 2
    public float Value;         // 4
    public uint RawValue;       // 4
    public byte StatusByte;     // 1
    public byte Flags;          // 1
    public ushort ErrorCount;   // 2
    public int FieldId;         // 4
}
```

### Memory Profile

| Metric | Target |
|--------|--------|
| Steady-state memory | 40-60 MB |
| GC frequency | ~1 per minute (Gen0 only) |
| Allocation rate | Near zero during operation |

---

## Named Pipe Communication

### Pipe Names:
- **BitStatusPipe** - For BitStatusPanel
- **EthernetPerfPipe** - For EthernetPerfTester

### Protocol:

```
┌────────────────────────────────────────────┐
│ MessageHeader (16 bytes)                   │
├────────────────────────────────────────────┤
│ MessageType    │ 1 byte                    │
│ Reserved       │ 1 byte                    │
│ ValueCount     │ 2 bytes                   │
│ SequenceNumber │ 4 bytes                   │
│ TotalErrors    │ 2 bytes                   │
│ NewErrors      │ 2 bytes                   │
│ Timestamp      │ 4 bytes                   │
├────────────────────────────────────────────┤
│ Values (16 bytes each × ValueCount)        │
└────────────────────────────────────────────┘
```

### Flow Control:
- **Latest-wins policy**: If UI is slow, old frames are dropped
- **No buffer growth**: Fixed-size buffers prevent memory issues
- **Sequence numbers**: Detect and count dropped frames

---

## CSV Logging

### Location:
```
{app_directory}/logs/status_log_{timestamp}.csv
```

### Format:
```csv
Timestamp,TotalErrors,ActiveErrors,Power.Primary,Power.Primary_Status,...
2026-01-17 09:30:01.234,0,0,5.0120,OK,...
2026-01-17 09:30:02.235,1,1,5.5200,RANGE,...
```

### Features:
- 1-second interval logging
- Automatic file rotation (daily or 100MB)
- Field names as headers
- Value + Status for each field

---

## Long-Running Stability

### Memory Management:
- Fixed-size buffers everywhere
- Log TextBox auto-trimmed at 50KB
- No growing collections

### Monitoring:
- Uptime counter
- Memory usage tracking
- GC collection counting
- Warning alerts for high memory/GC

### Error Recovery:
- Automatic pipe reconnection
- Graceful degradation on errors
- Proper resource disposal

---

## Building and Running

### Build:
```bash
cd c:\Users\kagan\Desktop\PipedStatusProject
dotnet build PipedStatusProject.sln --configuration Release
```

### Run Bit Status Display:
1. Start `MainApp.exe`
2. Click "Start Pipe Server"
3. Click "Load Schema..." → Select XML
4. Click "Start Simulation"
5. Start `BitStatusPanel.exe`
6. Click "Load Schema..." → Same XML
7. Click "Connect to Pipe"

### Run Ethernet Performance Test:
1. Start `EthernetPerfTester.exe` (Server mode)
2. Click "Start"
3. Start another `EthernetPerfTester.exe` (Client mode)
4. Click "Start"

---

## Dependencies

- .NET Framework 4.7.2
- ObjectListView (BrightIdeasSoftware) - For TreeListView
- System.IO.Pipes - For named pipe communication

---

## Future Enhancements

- [ ] Network streaming (TCP/UDP)
- [ ] Database storage (SQLite)
- [ ] Email alerts on errors
- [ ] Grafana integration
- [ ] Hardware integration APIs

---

*Document generated: 2026-01-17*
