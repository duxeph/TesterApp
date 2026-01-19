# Algorithms Documentation

This document provides detailed explanations of all algorithms used in the PipedStatusProject, including pseudo-code, complexity analysis, and implementation rationale.

---

## Table of Contents

1. [XML Schema Compilation](#1-xml-schema-compilation)
2. [Bit Field Extraction](#2-bit-field-extraction)
3. [Value Computation (Resolution + Bias)](#3-value-computation-resolution--bias)
4. [Delta Detection (Change Tracking)](#4-delta-detection-change-tracking)
5. [Validation Algorithm](#5-validation-algorithm)
6. [Error Tracking](#6-error-tracking)
7. [Double Buffering (Lock-Free Latest-Wins)](#7-double-buffering-lock-free-latest-wins)
8. [UI Throttling](#8-ui-throttling)
9. [Binary Serialization (Unsafe)](#9-binary-serialization-unsafe)
10. [Pipe Communication Protocol](#10-pipe-communication-protocol)
11. [CSV Logging with File Rotation](#11-csv-logging-with-file-rotation)
12. [Owner-Drawn Grid Rendering](#12-owner-drawn-grid-rendering)

---

## 1. XML Schema Compilation

### Purpose
Parse XML schema once at startup and create optimized runtime structures, eliminating repeated XML parsing during operation.

### Algorithm

```
FUNCTION CompileSchema(xmlPath):
    doc = LoadXmlDocument(xmlPath)
    root = doc.DocumentElement
    
    // Detect format
    IF root.Name == "root" OR HasSubElements(root):
        format = CbitSchema
    ELSE:
        format = BitSchema
    
    schema = new CompiledSchema()
    fieldId = 0
    
    FOR EACH cbitNode IN root.SelectNodes("cbit"):
        word = new WordDefinition()
        word.Name = cbitNode.GetAttribute("Name")
        word.Offset = ParseInt(cbitNode.GetAttribute("offset"))
        word.Size = ParseInt(cbitNode.GetAttribute("length"))
        
        // Pre-compute mask parameters
        maskStr = cbitNode.GetAttribute("mask")
        word.Mask = ParseHex(maskStr)
        word.ShiftAmount = CountTrailingZeros(word.Mask)
        
        // Parse sub-fields
        FOR EACH subNode IN cbitNode.SelectNodes("sub"):
            field = new FieldDefinition()
            field.FieldId = fieldId++
            field.Name = subNode.GetAttribute("Name")
            field.Mask = ParseHex(subNode.GetAttribute("mask"))
            field.ShiftAmount = CountTrailingZeros(field.Mask)
            field.BitCount = CountBits(field.Mask)
            field.Resolution = ParseDouble(subNode.GetAttribute("resolution"), 1.0)
            field.Bias = ParseDouble(subNode.GetAttribute("bias"), 0.0)
            field.Min = ParseNullableDouble(subNode.GetAttribute("min"))
            field.Max = ParseNullableDouble(subNode.GetAttribute("max"))
            field.FaultValue = ParseNullableDouble(subNode.GetAttribute("fault"))
            
            word.Fields.Add(field)
        
        schema.Words.Add(word)
    
    RETURN schema

FUNCTION CountTrailingZeros(mask):
    IF mask == 0: RETURN 0
    count = 0
    WHILE (mask & 1) == 0:
        mask = mask >> 1
        count++
    RETURN count

FUNCTION CountBits(mask):
    count = 0
    WHILE mask != 0:
        count += (mask & 1)
        mask = mask >> 1
    RETURN count
```

### Complexity
- **Time:** O(n) where n = number of fields
- **Space:** O(n) for compiled structures
- **Executed:** Once at startup

### Output Structure
```
CompiledField {
    FieldId: int           // Unique identifier
    DataOffset: int        // Absolute byte offset
    Mask: uint             // Bit mask
    ShiftAmount: int       // Pre-computed shift
    Resolution: float      // Multiplier
    Bias: float            // Offset
    Min, Max: float        // Validation limits
    FaultValue: float      // Fault condition
    HasMinMax: bool        // Has range validation
    HasFault: bool         // Has fault validation
    IsBool: bool           // Single bit flag
}
```

---

## 2. Bit Field Extraction

### Purpose
Extract a specific field's value from a raw byte array using pre-computed mask and shift.

### Algorithm

```
FUNCTION ExtractField(data[], offset, mask, shiftAmount):
    // Read 4 bytes as little-endian uint32
    rawWord = data[offset] |
              (data[offset+1] << 8) |
              (data[offset+2] << 16) |
              (data[offset+3] << 24)
    
    // Apply mask and shift
    masked = rawWord & mask
    extracted = masked >> shiftAmount
    
    RETURN extracted
```

### Example

```
Data: [0x12, 0x34, 0x56, 0x78] at offset 0
Mask: 0x0000FF00
Shift: 8 (calculated from mask)

rawWord = 0x78563412  (little-endian)
masked  = 0x78563412 & 0x0000FF00 = 0x00003400
extracted = 0x00003400 >> 8 = 0x34

Result: 0x34 (decimal 52)
```

### Complexity
- **Time:** O(1) - constant time operations
- **Memory:** No allocations

---

## 3. Value Computation (Resolution + Bias)

### Purpose
Convert raw extracted value to engineering units using resolution and bias.

### Formula
```
computed_value = (raw_value × resolution) + bias
```

### Algorithm

```
FUNCTION ComputeValue(rawValue, resolution, bias):
    RETURN (rawValue * resolution) + bias
```

### Example: Temperature Sensor

```
Raw Value: 8500 (from ADC)
Resolution: 0.01
Bias: -40  (sensor has -40°C offset)

computed = (8500 × 0.01) + (-40)
         = 85.00 - 40
         = 45.00°C
```

### Example: Voltage Measurement

```
Raw Value: 3300 (12-bit ADC, 3.3V reference)
Resolution: 0.001 (1mV per count)
Bias: 0

computed = (3300 × 0.001) + 0
         = 3.300V
```

---

## 4. Delta Detection (Change Tracking)

### Purpose
Only report values that have changed since the last frame, reducing data volume by ~90%.

### Algorithm

```
CLASS DeltaDetector:
    previousValues: uint[]  // One entry per field
    firstFrame: bool = true
    
    FUNCTION DetectChanges(currentValues[]):
        changedList = []
        
        FOR i = 0 TO currentValues.Length - 1:
            IF firstFrame OR currentValues[i] != previousValues[i]:
                changedList.Add(i, currentValues[i])
                previousValues[i] = currentValues[i]
        
        firstFrame = false
        RETURN changedList
```

### Optimization: Integer Comparison
```
// Fast path: compare raw uint values, not computed floats
hasChanged = (currentRaw != previousRaw)

// Store previous as raw, not computed
previousValues[fieldId] = currentRaw
```

### Statistics Example
```
1536-byte packet, 100 fields
Typical change rate: 5-10 fields per frame
Data reduction: 90-95%
```

---

## 5. Validation Algorithm

### Purpose
Check if computed value is within acceptable limits or matches a fault condition.

### Algorithm

```
FUNCTION Validate(computedValue, field):
    // Check fault condition first (highest priority)
    IF field.HasFault:
        IF ABS(computedValue - field.FaultValue) < 0.0001:
            RETURN ValidationStatus.FaultCondition
    
    // Check range limits
    IF field.HasMinMax:
        IF computedValue < field.Min:
            RETURN ValidationStatus.OutOfRange
        IF computedValue > field.Max:
            RETURN ValidationStatus.OutOfRange
        RETURN ValidationStatus.Valid
    
    // No validation rules defined
    IF field.HasFault:
        RETURN ValidationStatus.Valid  // Passed fault check
    
    RETURN ValidationStatus.NoValidation
```

### Decision Tree
```
                    ┌──────────────┐
                    │ Has Fault?   │
                    └──────┬───────┘
                           │
              ┌────────────┴────────────┐
              ▼ Yes                     ▼ No
        ┌──────────────┐          ┌──────────────┐
        │ Value==Fault?│          │ Has Min/Max? │
        └──────┬───────┘          └──────┬───────┘
               │                         │
    ┌──────────┴──────────┐   ┌──────────┴──────────┐
    ▼ Yes                 ▼ No   ▼ Yes              ▼ No
  FAULT              Continue   Check Range    NoValidation
                         │           │
                   ┌─────┴─────┐     │
                   ▼           ▼     ▼
              Has Min/Max?  Valid   In Range?
                   │                 │
              (repeat)        ┌──────┴──────┐
                              ▼ Yes         ▼ No
                            Valid      OutOfRange
```

### Boolean Flag Validation
```
For single-bit flags (e.g., PowerGood, ClockLocked):
- fault="0" means: fault if bit is 0 (not good)
- fault="1" means: fault if bit is 1 (error flag set)

Example:
  <sub Name="PowerGood" mask="0x01" fault="0"/>
  
  If bit=0 → FAULT (power not good)
  If bit=1 → VALID (power is good)
```

---

## 6. Error Tracking

### Purpose
Maintain cumulative error counts per field, incrementing only on new errors (not sustained errors).

### Algorithm

```
CLASS ErrorTracker:
    errorCounts: ushort[]       // Cumulative count per field
    previousErrorState: bool[]  // Was in error last frame?
    
    FUNCTION UpdateAndCheckNewError(fieldId, isCurrentlyError):
        wasError = previousErrorState[fieldId]
        previousErrorState[fieldId] = isCurrentlyError
        
        // Only count on TRANSITION from OK to ERROR
        IF isCurrentlyError AND NOT wasError:
            errorCounts[fieldId]++
            RETURN true  // New error
        
        RETURN false  // Sustained or cleared
    
    FUNCTION GetErrorCount(fieldId):
        RETURN errorCounts[fieldId]
```

### State Transition Diagram
```
         isError=false      isError=true
              │                  │
    ┌─────────▼─────────┐  ┌─────▼─────┐
    │    OK State       │  │Error State│
    │  (wasError=false) │  │(wasError= │
    │                   │  │   true)   │
    └─────────┬─────────┘  └─────┬─────┘
              │                  │
              │   isError=true   │
              ├──────────────────┤
              │ INCREMENT COUNT  │
              │ (new error)      │
              └──────────────────┘
              
    Sustained errors (ERROR→ERROR) do NOT increment count.
    Cleared errors (ERROR→OK) do NOT increment count.
    Only new errors (OK→ERROR) increment the count.
```

### Why Integer Keys Instead of Strings?
```
// SLOW: String-based lookup (allocates, computes hash)
Dictionary<string, int> errorCounts;
string key = $"W{wordIndex}_F{fieldIndex}";  // Allocates!
errorCounts[key]++;  // Hash computation

// FAST: Integer array lookup (no allocation, O(1))
ushort[] errorCounts = new ushort[fieldCount];
errorCounts[fieldId]++;  // Direct array access
```

---

## 7. Double Buffering (Lock-Free Latest-Wins)

### Purpose
Allow producer to write new data without blocking consumer, discarding old unread data.

### Algorithm

```
CLASS DoubleBuffer:
    buffer1: byte[]
    buffer2: byte[]
    activeBuffer: int = 0  // 0 or 1
    pendingLength: int = 0
    
    FUNCTION Write(data[], length):
        // Write to INACTIVE buffer
        target = (activeBuffer == 0) ? buffer2 : buffer1
        Copy(data, target, length)
        
        // Atomic swap
        IF pendingLength > 0:
            droppedFrames++  // Previous frame wasn't read
        
        activeBuffer = 1 - activeBuffer  // Flip 0↔1
        pendingLength = length
    
    FUNCTION Read():
        IF pendingLength == 0:
            RETURN null
        
        source = (activeBuffer == 0) ? buffer1 : buffer2
        length = pendingLength
        pendingLength = 0
        
        RETURN Copy(source, length)
```

### Diagram
```
Time →  T1        T2        T3        T4        T5
        ├─────────┼─────────┼─────────┼─────────┼─────────
Writer: Write(A)  Write(B)  Write(C)  ·         Write(D)
        buffer1   buffer2   buffer1   ·         buffer2
        │         │         │         │         │
Reader: ·         Read()    ·         Read()    ·
        ·         gets B    ·         gets C    ·
        ·         (A dropped)         (D pending)

Result: A is dropped, B and C are read, D pending
```

### Thread Safety
```
volatile int activeBuffer;  // Visibility across threads
                           // No lock needed for single producer/consumer
```

---

## 8. UI Throttling

### Purpose
Limit UI updates to sustainable frame rate (30 FPS), preventing GUI freeze.

### Algorithm

```
CLASS ThrottledUIUpdater:
    targetIntervalMs: int = 33  // ~30 FPS
    latestData: ParsedValue[]
    hasNewData: bool = false
    timer: Timer
    
    FUNCTION SubmitData(data[]):
        // Fast path: just store reference
        latestData = data
        IF hasNewData:
            framesSkipped++
        hasNewData = true
    
    FUNCTION TimerTick():  // Called every 33ms
        IF NOT hasNewData:
            RETURN
        
        hasNewData = false
        UpdateUI(latestData)  // Actual UI update
```

### Timeline Example
```
Data arrives:  |----|----|----|----|----|----|----|----|
               0ms  10ms 20ms 30ms 40ms 50ms 60ms 70ms

Timer fires:        |                   |
                   33ms                66ms

UI sees:           Frame 3            Frame 6
                   (1,2 skipped)      (4,5 skipped)
```

### Why 30 FPS?
```
Human perception: ~60 FPS maximum useful
WinForms overhead: ~10-20ms per update
Safety margin: 33ms gives comfortable headroom
CPU usage: <5% vs 30%+ at uncapped rate
```

---

## 9. Binary Serialization (Unsafe)

### Purpose
Serialize/deserialize structs at maximum speed using direct memory access.

### Algorithm

```
UNSAFE FUNCTION SerializeStruct<T>(buffer[], offset, value):
    // Pin buffer in memory
    FIXED (byte* pBuffer = buffer):
        // Cast to struct pointer and write
        *(T*)(pBuffer + offset) = value

UNSAFE FUNCTION DeserializeStruct<T>(buffer[], offset):
    FIXED (byte* pBuffer = buffer):
        RETURN *(T*)(pBuffer + offset)
```

### C# Implementation

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe int SerializeDeltaFrame(
    ParseResult parseResult,
    uint sequenceNumber,
    byte[] buffer) {
    
    fixed (byte* pBuffer = buffer) {
        // Write header (16 bytes)
        MessageHeader* header = (MessageHeader*)pBuffer;
        header->MessageType = MSG_DELTA_FRAME;
        header->ValueCount = (ushort)parseResult.Count;
        header->SequenceNumber = sequenceNumber;
        header->Timestamp = (uint)Environment.TickCount;

        // Write values (16 bytes each)
        ValueEntry* pValues = (ValueEntry*)(pBuffer + HEADER_SIZE);
        for (int i = 0; i < parseResult.Count; i++) {
            pValues[i].WordIndex = parseResult.Values[i].WordIndex;
            pValues[i].Value = parseResult.Values[i].Value;
            // ... etc
        }
    }
    return HEADER_SIZE + parseResult.Count * VALUE_ENTRY_SIZE;
}
```

### Performance Comparison
```
Method              Time per struct
─────────────────────────────────────
Marshal.StructureToPtr    ~500 ns
BinaryWriter              ~200 ns
Unsafe pointer            ~10 ns  ← 50x faster
```

### Memory Layout
```
┌────────────────────────────────────────────────────────┐
│ Buffer (byte[])                                        │
├─────────────────┬─────────────────┬───────────────────┤
│ Header (16B)    │ Value 0 (16B)   │ Value 1 (16B) ... │
├─────────────────┼─────────────────┼───────────────────┤
│ byte 0-15       │ byte 16-31      │ byte 32-47    ... │
└─────────────────┴─────────────────┴───────────────────┘
```

---

## 10. Pipe Communication Protocol

### Purpose
Efficient inter-process communication with minimal overhead.

### Message Format

```
┌─────────────────────────────────────────────────────────┐
│ MESSAGE HEADER (16 bytes)                               │
├──────────────┬──────────────────────────────────────────┤
│ Byte 0       │ MessageType (1=Data, 2=Delta, 3=Schema)  │
│ Byte 1       │ Reserved                                 │
│ Bytes 2-3    │ ValueCount (little-endian uint16)        │
│ Bytes 4-7    │ SequenceNumber (uint32)                  │
│ Bytes 8-9    │ TotalErrors (uint16)                     │
│ Bytes 10-11  │ NewErrors (uint16)                       │
│ Bytes 12-15  │ Timestamp (uint32)                       │
├──────────────┴──────────────────────────────────────────┤
│ VALUES (16 bytes each × ValueCount)                     │
├──────────────┬──────────────────────────────────────────┤
│ Bytes 0-1    │ WordIndex (uint16)                       │
│ Bytes 2-3    │ FieldIndex (int16, -1 for word-level)    │
│ Bytes 4-7    │ Value (float32)                          │
│ Bytes 8-11   │ RawValue (uint32)                        │
│ Byte 12      │ Status (0=None, 1=Valid, 2=Range, 3=Fault│
│ Byte 13      │ Flags (bit 0 = HasChanged)               │
│ Bytes 14-15  │ ErrorCount (uint16)                      │
└──────────────┴──────────────────────────────────────────┘
```

### Sequence Number Algorithm
```
SENDER:
    sequenceNumber = 0
    FOR EACH frame:
        sequenceNumber++
        Send(frame, sequenceNumber)

RECEIVER:
    lastSequence = 0
    FOR EACH received(frame, seq):
        IF seq > lastSequence + 1:
            droppedCount += (seq - lastSequence - 1)
        lastSequence = seq
```

### Calculating Dropped Frames
```
Sent:     1  2  3  4  5  6  7  8  9  10
Received: 1     3     5  6        9  10
                ↑     ↑           ↑
           Gaps: 2-3=1, 5-3=1, 9-6=2

Total dropped = 1 + 1 + 2 = 4 frames
```

---

## 11. CSV Logging with File Rotation

### Purpose
Record status history for analysis, with automatic file management.

### Algorithm

```
CLASS StatusLogger:
    currentFile: StreamWriter
    currentFileDate: DateTime
    currentFileSize: long
    maxFileSizeMB: int = 100
    
    FUNCTION WriteLogLine():
        CheckRotation()
        
        line = FormatTimestamp(DateTime.Now)
        line += "," + totalErrors
        line += "," + activeErrors
        
        FOR EACH field IN schema:
            line += "," + field.Value
            line += "," + field.StatusText
        
        currentFile.WriteLine(line)
        currentFileSize += line.Length
        
        // Periodic flush (every 10 lines)
        IF linesWritten % 10 == 0:
            currentFile.Flush()
    
    FUNCTION CheckRotation():
        needRotation = false
        
        // Daily rotation
        IF DateTime.Now.Date != currentFileDate.Date:
            needRotation = true
        
        // Size-based rotation
        IF currentFileSize > maxFileSizeMB * 1024 * 1024:
            needRotation = true
        
        IF needRotation:
            currentFile.Close()
            currentFile = OpenNewFile()
            WriteHeader()
```

### File Naming
```
status_log_20260117_093012.csv   // First file
status_log_20260118_000000.csv   // Next day
status_log_20260118_154532.csv   // Size rotation
```

### CSV Format
```csv
Timestamp,TotalErrors,ActiveErrors,Power.Primary,Power.Primary_Status,Temp.Board,Temp.Board_Status
2026-01-17 09:30:01.234,0,0,5.0120,OK,45.32,OK
2026-01-17 09:30:02.235,1,1,5.5200,RANGE,45.35,OK
2026-01-17 09:30:03.236,1,0,5.0100,OK,45.31,OK
```

---

## 12. Owner-Drawn Grid Rendering

### Purpose
Render 16-port statistics grid at 25 FPS (40ms) with minimal overhead.

### Algorithm

```
FUNCTION OnPaint(graphics):
    // Use high-speed settings
    graphics.SmoothingMode = HighSpeed
    graphics.TextRenderingHint = ClearTypeGridFit
    
    y = 0
    
    // Draw header (fixed)
    FillRectangle(headerBg, 0, 0, Width, HEADER_HEIGHT)
    FOR col = 0 TO columns.Length - 1:
        DrawString(columns[col].Header, headerFont, x, 5)
        x += columns[col].Width
    
    y = HEADER_HEIGHT
    
    // Draw rows
    FOR row = 0 TO PORT_COUNT - 1:
        data = rows[row]
        
        // Row background (conditional coloring)
        IF data.HasError:
            rowBg = ERROR_COLOR  // Red
        ELSE IF row % 2 == 0:
            rowBg = EVEN_COLOR   // Light blue
        ELSE:
            rowBg = ODD_COLOR    // White
        
        FillRectangle(rowBg, 0, y, Width, ROW_HEIGHT)
        
        // Draw cells
        x = 0
        DrawText(data.Port, portFont, x, y)
        x += COL_WIDTH[0]
        DrawText(data.RxCount, dataFont, x, y)
        x += COL_WIDTH[1]
        DrawText(data.TxCount, dataFont, x, y)
        x += COL_WIDTH[2]
        DrawText(data.ErrorCount, dataFont, x, y, 
                 data.ErrorCount > 0 ? RED : GREEN)
        
        // Grid lines
        DrawLine(0, y + ROW_HEIGHT, Width, y + ROW_HEIGHT)
        
        y += ROW_HEIGHT
```

### Optimization Techniques

```
1. CACHED RESOURCES (no allocation during paint):
   - Fonts created once in constructor
   - Brushes reused across paints
   - Pens cached
   
2. MINIMAL DRAW CALLS:
   - Single FillRectangle per row (not per cell)
   - Combined grid line drawing
   
3. CONTROL STYLES:
   SetStyle(
     AllPaintingInWmPaint |    // Handle WM_PAINT only
     UserPaint |              // We do all drawing
     OptimizedDoubleBuffer |  // Built-in double buffer
     ResizeRedraw             // Redraw on resize
   );
   
4. FAST TEXT RENDERING:
   - ClearTypeGridFit: fast with good quality
   - Avoid MeasureString: fixed column widths
```

### Performance
```
16 rows × 4 columns = 64 cells
Time per paint: ~2-3 ms
Max sustainable FPS: ~300
Actual update rate: 25 FPS (40ms)
CPU headroom: 90%+ idle
```

---

## Summary: Algorithm Complexity

| Algorithm | Time Complexity | Space Complexity | Executed |
|-----------|-----------------|------------------|----------|
| Schema Compilation | O(n) | O(n) | Once at startup |
| Bit Extraction | O(1) | O(1) | Per field per frame |
| Value Computation | O(1) | O(1) | Per field per frame |
| Delta Detection | O(n) | O(n) | Per frame |
| Validation | O(1) | O(1) | Per field per frame |
| Error Tracking | O(1) | O(n) | Per field per frame |
| Serialization | O(n) | O(1) | Per frame |
| UI Update | O(n) | O(1) | 30× per second |
| CSV Logging | O(n) | O(1) | 1× per second |
| Grid Rendering | O(rows) | O(1) | 25× per second |

Where n = number of fields in schema (~50-200 typical)

---

*Algorithm documentation generated: 2026-01-17*
