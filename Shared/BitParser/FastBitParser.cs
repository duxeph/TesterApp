using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BitParser {
    /// <summary>
    /// Represents a single parsed value with validation.
    /// Optimized: 24 bytes, cache-line friendly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ParsedValue {
        public ushort WordIndex;        // 2 bytes
        public short FieldIndex;        // 2 bytes (-1 for word-level)
        public float Value;             // 4 bytes (float instead of double for speed)
        public uint RawValue;           // 4 bytes
        public byte StatusByte;         // 1 byte (ValidationStatus)
        public byte Flags;              // 1 byte (HasChanged in bit 0)
        public ushort ErrorCount;       // 2 bytes
        public int FieldId;             // 4 bytes (unique ID for fast lookup)
        // Total: 20 bytes, padded to 24

        public bool HasChanged {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Flags & 1) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Flags = value ? (byte)(Flags | 1) : (byte)(Flags & ~1);
        }

        public ValidationStatus Status {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ValidationStatus)StatusByte;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => StatusByte = (byte)value;
        }

        public bool IsError {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => StatusByte == (byte)ValidationStatus.FaultCondition || 
                   StatusByte == (byte)ValidationStatus.OutOfRange;
        }
    }

    /// <summary>
    /// Zero-allocation result container using pre-allocated array.
    /// </summary>
    public sealed class ParseResult {
        public readonly ParsedValue[] Values;
        public int Count;
        public int TotalErrors;
        public int NewErrors;
        public readonly int Capacity;

        public ParseResult(int capacity) {
            Capacity = capacity;
            Values = new ParsedValue[capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            Count = 0;
            TotalErrors = 0;
            NewErrors = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ParsedValue value) {
            if (Count < Capacity) {
                Values[Count++] = value;
            }
        }
    }

    /// <summary>
    /// Optimized error tracker using integer IDs instead of string keys.
    /// Zero string allocations in hot path.
    /// </summary>
    public sealed class FastErrorTracker {
        private readonly ushort[] _errorCounts;      // Error count per field ID
        private readonly byte[] _previousErrorState; // Was in error state last frame
        private readonly int _capacity;

        public FastErrorTracker(int maxFieldId) {
            _capacity = maxFieldId + 1;
            _errorCounts = new ushort[_capacity];
            _previousErrorState = new byte[_capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetErrorCount(int fieldId) {
            return fieldId >= 0 && fieldId < _capacity ? _errorCounts[fieldId] : (ushort)0;
        }

        /// <summary>
        /// Returns true if this is a NEW error (transition from OK to error).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool UpdateAndCheckNewError(int fieldId, bool isError) {
            if (fieldId < 0 || fieldId >= _capacity) return false;

            byte wasError = _previousErrorState[fieldId];
            _previousErrorState[fieldId] = isError ? (byte)1 : (byte)0;

            if (isError && wasError == 0) {
                // New error - increment
                if (_errorCounts[fieldId] < ushort.MaxValue) {
                    _errorCounts[fieldId]++;
                }
                return true;
            }
            return false;
        }

        public void Reset() {
            Array.Clear(_errorCounts, 0, _capacity);
            Array.Clear(_previousErrorState, 0, _capacity);
        }

        public int TotalErrorCount {
            get {
                int total = 0;
                for (int i = 0; i < _capacity; i++) {
                    total += _errorCounts[i];
                }
                return total;
            }
        }
    }

    /// <summary>
    /// Pre-compiled field info for zero-overhead field access.
    /// All validation parameters pre-computed.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CompiledField {
        public int FieldId;           // Unique ID for error tracking
        public int DataOffset;        // Absolute byte offset in packet
        public uint Mask;             // Bit mask
        public int ShiftAmount;       // Right shift after masking
        public float Resolution;      // Multiply factor
        public float Bias;            // Add after resolution
        public float Min;             // Min valid value (float.MinValue if none)
        public float Max;             // Max valid value (float.MaxValue if none)
        public float FaultValue;      // Fault value (float.NaN if none)
        public byte HasMinMax;        // 1 if has min/max validation
        public byte HasFault;         // 1 if has fault validation
        public byte IsBool;           // 1 if single bit
        public byte IsVisible;        // 1 if should be displayed

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeValue(uint rawWord) {
            uint masked = rawWord & Mask;
            uint shifted = masked >> ShiftAmount;
            return shifted * Resolution + Bias;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExtractRaw(uint rawWord) {
            return (rawWord & Mask) >> ShiftAmount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValidationStatus Validate(float computedValue) {
            // Check fault first
            if (HasFault != 0) {
                float diff = computedValue - FaultValue;
                if (diff > -0.0001f && diff < 0.0001f) {
                    return ValidationStatus.FaultCondition;
                }
            }

            // Check range
            if (HasMinMax != 0) {
                if (computedValue < Min || computedValue > Max) {
                    return ValidationStatus.OutOfRange;
                }
                return ValidationStatus.Valid;
            }

            return HasFault != 0 ? ValidationStatus.Valid : ValidationStatus.NoValidation;
        }
    }

    /// <summary>
    /// Ultra-fast bit parser with zero allocations after initialization.
    /// Uses pre-compiled field structures and integer-based tracking.
    /// </summary>
    public sealed class FastBitParser : IDisposable {
        private readonly CompiledField[] _fields;
        private readonly int _fieldCount;
        private readonly uint[] _previousRawValues;
        private readonly FastErrorTracker _errorTracker;
        private readonly ParseResult _result;
        private readonly CompiledSchema _schema;
        private bool _firstFrame = true;

        // Statistics
        public long TotalFramesParsed { get; private set; }
        public long TotalBytesProcessed { get; private set; }
        public int TotalErrorCount => _errorTracker.TotalErrorCount;

        public FastBitParser(CompiledSchema schema) {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));

            // Pre-compile all fields into flat array for cache efficiency
            var fieldList = new List<CompiledField>();
            int fieldId = 0;

            foreach (var word in schema.Words) {
                if (!word.IsVisible) continue;

                foreach (var field in word.Fields) {
                    if (field.IsReserved || !field.IsVisible) continue;

                    var cf = new CompiledField {
                        FieldId = fieldId++,
                        DataOffset = word.Offset + field.SubOffset,
                        Mask = field.Mask,
                        ShiftAmount = field.ShiftAmount,
                        Resolution = (float)field.Resolution,
                        Bias = (float)field.Bias,
                        Min = field.Min.HasValue ? (float)field.Min.Value : float.MinValue,
                        Max = field.Max.HasValue ? (float)field.Max.Value : float.MaxValue,
                        FaultValue = field.FaultValue.HasValue ? (float)field.FaultValue.Value : float.NaN,
                        HasMinMax = (byte)(field.Min.HasValue || field.Max.HasValue ? 1 : 0),
                        HasFault = (byte)(field.FaultValue.HasValue ? 1 : 0),
                        IsBool = (byte)(field.BitCount == 1 ? 1 : 0),
                        IsVisible = 1
                    };
                    fieldList.Add(cf);
                }
            }

            _fields = fieldList.ToArray();
            _fieldCount = _fields.Length;
            _previousRawValues = new uint[_fieldCount];
            _errorTracker = new FastErrorTracker(_fieldCount);
            _result = new ParseResult(_fieldCount + 64);  // Extra space for safety
        }

        /// <summary>
        /// Ultra-fast parse with zero allocations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ParseResult Parse(byte[] data, int dataLength, bool changedOnly = true) {
            _result.Clear();

            if (data == null || dataLength == 0) {
                return _result;
            }

            for (int i = 0; i < _fieldCount; i++) {
                ref CompiledField field = ref _fields[i];

                // Bounds check
                if (field.DataOffset + 4 > dataLength) continue;

                // Fast little-endian read (unrolled)
                uint rawWord = (uint)(
                    data[field.DataOffset] |
                    (data[field.DataOffset + 1] << 8) |
                    (data[field.DataOffset + 2] << 16) |
                    (data[field.DataOffset + 3] << 24));

                uint rawExtracted = field.ExtractRaw(rawWord);
                
                // Delta detection
                bool hasChanged = _firstFrame || rawExtracted != _previousRawValues[i];
                _previousRawValues[i] = rawExtracted;

                if (!changedOnly || hasChanged) {
                    float computedValue = field.ComputeValue(rawWord);
                    var status = field.Validate(computedValue);

                    bool isError = status == ValidationStatus.FaultCondition || 
                                   status == ValidationStatus.OutOfRange;
                    
                    if (_errorTracker.UpdateAndCheckNewError(field.FieldId, isError)) {
                        _result.NewErrors++;
                    }
                    if (isError) {
                        _result.TotalErrors++;
                    }

                    var pv = new ParsedValue {
                        WordIndex = (ushort)GetWordIndex(field.DataOffset),
                        FieldIndex = (short)i,
                        Value = computedValue,
                        RawValue = rawExtracted,
                        StatusByte = (byte)status,
                        Flags = hasChanged ? (byte)1 : (byte)0,
                        ErrorCount = _errorTracker.GetErrorCount(field.FieldId),
                        FieldId = field.FieldId
                    };
                    _result.Add(pv);
                }
            }

            _firstFrame = false;
            TotalFramesParsed++;
            TotalBytesProcessed += dataLength;

            return _result;
        }

        /// <summary>
        /// Convenience overload.
        /// </summary>
        public ParseResult Parse(byte[] data, bool changedOnly = true) {
            return Parse(data, data?.Length ?? 0, changedOnly);
        }

        public ParseResult ParseFull(byte[] data) {
            return Parse(data, data?.Length ?? 0, changedOnly: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetWordIndex(int offset) {
            // Fast lookup - could be optimized further with a lookup table
            for (int i = 0; i < _schema.Words.Count; i++) {
                if (_schema.Words[i].Offset == offset) return i;
            }
            return 0;
        }

        public void ResetDeltaState() {
            _firstFrame = true;
            Array.Clear(_previousRawValues, 0, _previousRawValues.Length);
        }

        public void ResetErrorCounts() {
            _errorTracker.Reset();
        }

        public CompiledSchema Schema => _schema;
        public int FieldCount => _fieldCount;

        public void Dispose() { }
    }

    /// <summary>
    /// Thread-safe parser pool.
    /// </summary>
    public static class FastBitParserPool {
        private static readonly object _lock = new object();
        private static readonly Stack<FastBitParser> _pool = new Stack<FastBitParser>();
        private static CompiledSchema _schema;

        public static void Initialize(CompiledSchema schema) {
            lock (_lock) {
                _schema = schema;
                _pool.Clear();
            }
        }

        public static FastBitParser Rent() {
            lock (_lock) {
                return _pool.Count > 0 ? _pool.Pop() : new FastBitParser(_schema);
            }
        }

        public static void Return(FastBitParser parser) {
            if (parser == null) return;
            parser.ResetDeltaState();
            lock (_lock) {
                _pool.Push(parser);
            }
        }
    }
}
