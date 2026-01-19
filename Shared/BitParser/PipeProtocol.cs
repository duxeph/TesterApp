using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BitParser {
    /// <summary>
    /// Ultra-fast binary protocol using unsafe pointer operations.
    /// No Marshal.AllocHGlobal, no boxing, minimal allocations.
    /// </summary>
    public static class PipeProtocol {
        public const byte MSG_DATA_FRAME = 0x01;
        public const byte MSG_DELTA_FRAME = 0x02;
        public const byte MSG_SCHEMA_PATH = 0x03;
        public const byte MSG_RESET = 0x04;
        public const byte MSG_HEARTBEAT = 0x05;
        public const byte MSG_ERROR_SUMMARY = 0x06;

        public const string PIPE_NAME = "BitStatusPipe";

        // Fixed sizes for fast calculation
        public const int HEADER_SIZE = 16;
        public const int VALUE_ENTRY_SIZE = 16;

        /// <summary>
        /// Message header - 16 bytes fixed.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
        public struct MessageHeader {
            public byte MessageType;
            public byte Reserved1;
            public ushort ValueCount;
            public uint SequenceNumber;
            public ushort TotalErrors;
            public ushort NewErrors;
            public uint Timestamp;
        }

        /// <summary>
        /// Value entry - 16 bytes fixed.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
        public struct ValueEntry {
            public ushort WordIndex;
            public short FieldIndex;
            public float Value;
            public uint RawValue;
            public byte Status;
            public byte Flags;
            public ushort ErrorCount;
        }

        public static int HeaderSize => HEADER_SIZE;
        public static int ValueEntrySize => VALUE_ENTRY_SIZE;

        /// <summary>
        /// Fast serialization using fixed pointers - no allocations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SerializeDeltaFrame(
            ParseResult parseResult,
            uint sequenceNumber,
            byte[] buffer) {
            
            if (buffer == null || buffer.Length < HEADER_SIZE) {
                return 0;
            }

            fixed (byte* pBuffer = buffer) {
                // Write header
                MessageHeader* header = (MessageHeader*)pBuffer;
                header->MessageType = MSG_DELTA_FRAME;
                header->Reserved1 = 0;
                header->ValueCount = (ushort)Math.Min(parseResult.Count, ushort.MaxValue);
                header->SequenceNumber = sequenceNumber;
                header->TotalErrors = (ushort)Math.Min(parseResult.TotalErrors, ushort.MaxValue);
                header->NewErrors = (ushort)Math.Min(parseResult.NewErrors, ushort.MaxValue);
                header->Timestamp = (uint)(Environment.TickCount);

                // Write values
                int maxValues = Math.Min(parseResult.Count, 
                    (buffer.Length - HEADER_SIZE) / VALUE_ENTRY_SIZE);
                
                ValueEntry* pValues = (ValueEntry*)(pBuffer + HEADER_SIZE);
                
                for (int i = 0; i < maxValues; i++) {
                    ref ParsedValue v = ref parseResult.Values[i];
                    pValues[i].WordIndex = v.WordIndex;
                    pValues[i].FieldIndex = v.FieldIndex;
                    pValues[i].Value = v.Value;
                    pValues[i].RawValue = v.RawValue;
                    pValues[i].Status = v.StatusByte;
                    pValues[i].Flags = v.Flags;
                    pValues[i].ErrorCount = v.ErrorCount;
                }

                return HEADER_SIZE + maxValues * VALUE_ENTRY_SIZE;
            }
        }

        /// <summary>
        /// Fast serialization for raw data frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SerializeDataFrame(
            byte[] data,
            int dataLength,
            uint sequenceNumber,
            byte[] buffer) {
            
            int requiredSize = HEADER_SIZE + dataLength;
            if (buffer == null || buffer.Length < requiredSize) {
                return 0;
            }

            fixed (byte* pBuffer = buffer) {
                MessageHeader* header = (MessageHeader*)pBuffer;
                header->MessageType = MSG_DATA_FRAME;
                header->Reserved1 = 0;
                header->ValueCount = (ushort)dataLength;
                header->SequenceNumber = sequenceNumber;
                header->TotalErrors = 0;
                header->NewErrors = 0;
                header->Timestamp = (uint)(Environment.TickCount);
            }

            Buffer.BlockCopy(data, 0, buffer, HEADER_SIZE, dataLength);
            return requiredSize;
        }

        /// <summary>
        /// Fast deserialization of header.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe MessageHeader DeserializeHeader(byte[] buffer) {
            fixed (byte* pBuffer = buffer) {
                return *(MessageHeader*)pBuffer;
            }
        }

        /// <summary>
        /// Fast deserialization of values - returns span-like view.
        /// Note: Values are copied to output array for safety.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void DeserializeValues(byte[] buffer, ValueEntry[] output, int count) {
            fixed (byte* pBuffer = buffer)
            fixed (ValueEntry* pOutput = output) {
                ValueEntry* pValues = (ValueEntry*)(pBuffer + HEADER_SIZE);
                int copyCount = Math.Min(count, output.Length);
                
                // Use memcpy equivalent for bulk copy
                int copyBytes = copyCount * VALUE_ENTRY_SIZE;
                Buffer.MemoryCopy(pValues, pOutput, copyBytes, copyBytes);
            }
        }

        /// <summary>
        /// Allocate-free value deserialization using pre-allocated array.
        /// </summary>
        public static unsafe ValueEntry[] DeserializeValues(byte[] buffer, int valueCount) {
            var values = new ValueEntry[valueCount];
            DeserializeValues(buffer, values, valueCount);
            return values;
        }

        /// <summary>
        /// Get values directly as ParsedValue array (for UI).
        /// </summary>
        public static unsafe void DeserializeToParseResult(byte[] buffer, int valueCount, ParseResult result) {
            result.Clear();
            
            fixed (byte* pBuffer = buffer) {
                MessageHeader* header = (MessageHeader*)pBuffer;
                result.TotalErrors = header->TotalErrors;
                result.NewErrors = header->NewErrors;

                ValueEntry* pValues = (ValueEntry*)(pBuffer + HEADER_SIZE);
                int copyCount = Math.Min(valueCount, result.Capacity);

                for (int i = 0; i < copyCount; i++) {
                    result.Values[i] = new ParsedValue {
                        WordIndex = pValues[i].WordIndex,
                        FieldIndex = pValues[i].FieldIndex,
                        Value = pValues[i].Value,
                        RawValue = pValues[i].RawValue,
                        StatusByte = pValues[i].Status,
                        Flags = (byte)(pValues[i].Flags | 1), // Mark as changed
                        ErrorCount = pValues[i].ErrorCount,
                        FieldId = i
                    };
                }
                result.Count = copyCount;
            }
        }

        public static byte[] DeserializeDataFrame(byte[] buffer, int byteCount) {
            var data = new byte[byteCount];
            Buffer.BlockCopy(buffer, HEADER_SIZE, data, 0, byteCount);
            return data;
        }

        /// <summary>
        /// Convert single ValueEntry to ParsedValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParsedValue ToParseValue(ValueEntry entry) {
            return new ParsedValue {
                WordIndex = entry.WordIndex,
                FieldIndex = entry.FieldIndex,
                Value = entry.Value,
                RawValue = entry.RawValue,
                StatusByte = entry.Status,
                Flags = (byte)(entry.Flags | 1),
                ErrorCount = entry.ErrorCount,
                FieldId = 0
            };
        }
    }

    /// <summary>
    /// Pre-allocated buffer pool to avoid allocations during operation.
    /// </summary>
    public sealed class BufferPool {
        private readonly byte[][] _buffers;
        private readonly int _bufferSize;
        private readonly bool[] _inUse;
        private readonly object _lock = new object();

        public BufferPool(int bufferCount, int bufferSize) {
            _bufferSize = bufferSize;
            _buffers = new byte[bufferCount][];
            _inUse = new bool[bufferCount];
            
            for (int i = 0; i < bufferCount; i++) {
                _buffers[i] = new byte[bufferSize];
            }
        }

        public byte[] Rent() {
            lock (_lock) {
                for (int i = 0; i < _buffers.Length; i++) {
                    if (!_inUse[i]) {
                        _inUse[i] = true;
                        return _buffers[i];
                    }
                }
            }
            // All buffers in use - allocate new (rare case)
            return new byte[_bufferSize];
        }

        public void Return(byte[] buffer) {
            lock (_lock) {
                for (int i = 0; i < _buffers.Length; i++) {
                    if (ReferenceEquals(_buffers[i], buffer)) {
                        _inUse[i] = false;
                        return;
                    }
                }
            }
            // Buffer was allocated dynamically - let GC handle it
        }
    }
}
