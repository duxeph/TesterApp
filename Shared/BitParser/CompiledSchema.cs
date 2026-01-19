using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml;

namespace BitParser {
    /// <summary>
    /// Validation result for a parsed value.
    /// </summary>
    public enum ValidationStatus {
        Valid,          // Within limits, no fault
        OutOfRange,     // Below min or above max
        FaultCondition, // Equals fault value
        NoValidation    // No validation rules defined
    }

    /// <summary>
    /// Represents a sub-field within a cbit block.
    /// Supports resolution, bias, min/max, and fault validation.
    /// </summary>
    public sealed class SubFieldDefinition {
        public string Name { get; set; }
        public int SubOffset { get; set; }      // Offset within cbit block
        public uint Mask { get; set; }
        public int Length { get; set; }         // Byte length
        public double Resolution { get; set; } = 1.0;
        public double Bias { get; set; } = 0.0;
        public double? Min { get; set; }        // Nullable - no limit if null
        public double? Max { get; set; }        // Nullable - no limit if null
        public double? FaultValue { get; set; } // Value that indicates fault
        public string Unit { get; set; } = "";
        public bool IsVisible { get; set; } = true;

        // Pre-computed values
        public int ShiftAmount { get; set; }
        public int BitCount { get; set; }
        public bool IsBooleanFlag { get; set; } // True if mask has only 1 bit set

        /// <summary>
        /// Compute the final value after resolution and bias.
        /// Formula: computed_value = (raw_value * resolution) + bias
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ComputeValue(uint rawWord) {
            uint masked = rawWord & Mask;
            uint shifted = masked >> ShiftAmount;
            return (shifted * Resolution) + Bias;
        }

        /// <summary>
        /// Extract raw shifted value (without resolution/bias).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExtractRaw(uint rawWord) {
            return (rawWord & Mask) >> ShiftAmount;
        }

        /// <summary>
        /// Validate the computed value against min/max/fault rules.
        /// </summary>
        public ValidationStatus Validate(double computedValue) {
            // Check fault condition first
            if (FaultValue.HasValue) {
                if (IsBooleanFlag) {
                    // For boolean flags, compare raw value
                    if (Math.Abs(computedValue - FaultValue.Value) < 0.0001) {
                        return ValidationStatus.FaultCondition;
                    }
                } else {
                    // For numeric values, also compare with small epsilon
                    if (Math.Abs(computedValue - FaultValue.Value) < 0.0001) {
                        return ValidationStatus.FaultCondition;
                    }
                }
            }

            // Check range limits
            if (Min.HasValue && computedValue < Min.Value) {
                return ValidationStatus.OutOfRange;
            }
            if (Max.HasValue && computedValue > Max.Value) {
                return ValidationStatus.OutOfRange;
            }

            // If we have any validation rules and passed, it's valid
            if (Min.HasValue || Max.HasValue || FaultValue.HasValue) {
                return ValidationStatus.Valid;
            }

            return ValidationStatus.NoValidation;
        }
    }

    /// <summary>
    /// Represents a cbit block containing multiple sub-fields.
    /// </summary>
    public sealed class CbitDefinition {
        public string Name { get; set; }
        public int Offset { get; set; }         // Byte offset in data packet
        public int Length { get; set; }         // Total byte length
        public bool IsVisible { get; set; } = true;
        public List<SubFieldDefinition> SubFields { get; } = new List<SubFieldDefinition>();
    }

    /// <summary>
    /// Compiled schema supporting both old (cbit with bit_N attributes) 
    /// and new (cbit with sub elements) XML formats.
    /// </summary>
    public sealed class CompiledSchema {
        public int TotalBytes { get; private set; }
        public List<WordDefinition> Words { get; } = new List<WordDefinition>();
        public List<CbitDefinition> Cbits { get; } = new List<CbitDefinition>();
        public SchemaFormat Format { get; private set; }

        private Dictionary<string, WordDefinition> _wordsByName = new Dictionary<string, WordDefinition>();
        private Dictionary<string, CbitDefinition> _cbitsByName = new Dictionary<string, CbitDefinition>();

        public enum SchemaFormat {
            BitSchema,  // Old format with bit_0, bit_1, etc.
            CbitSchema  // New format with <sub> elements
        }

        public static CompiledSchema LoadFromXml(string xmlPath) {
            var schema = new CompiledSchema();
            var doc = new XmlDocument();
            doc.Load(xmlPath);

            var root = doc.DocumentElement;
            
            // Determine format based on root element
            if (root.Name == "root") {
                schema.Format = SchemaFormat.CbitSchema;
                schema.ParseCbitFormat(root);
            } else if (root.Name == "BitSchema") {
                schema.Format = SchemaFormat.BitSchema;
                schema.ParseBitSchemaFormat(root);
            } else {
                // Try to auto-detect
                if (root.SelectNodes("cbit/sub").Count > 0) {
                    schema.Format = SchemaFormat.CbitSchema;
                    schema.ParseCbitFormat(root);
                } else {
                    schema.Format = SchemaFormat.BitSchema;
                    schema.ParseBitSchemaFormat(root);
                }
            }

            return schema;
        }

        private void ParseCbitFormat(XmlElement root) {
            if (root.HasAttribute("totalBytes")) {
                TotalBytes = int.Parse(root.GetAttribute("totalBytes"));
            }

            foreach (XmlNode node in root.SelectNodes("cbit")) {
                var cbit = ParseCbitDefinition(node);
                Cbits.Add(cbit);
                _cbitsByName[cbit.Name] = cbit;

                // Also create WordDefinition for compatibility with existing code
                var word = ConvertCbitToWord(cbit);
                Words.Add(word);
                _wordsByName[word.Name] = word;
            }
        }

        private void ParseBitSchemaFormat(XmlElement root) {
            if (root.HasAttribute("totalBytes")) {
                TotalBytes = int.Parse(root.GetAttribute("totalBytes"));
            }

            foreach (XmlNode node in root.SelectNodes("cbit")) {
                var word = ParseWordDefinition(node);
                Words.Add(word);
                _wordsByName[word.Name] = word;
            }
        }

        private CbitDefinition ParseCbitDefinition(XmlNode node) {
            var cbit = new CbitDefinition {
                Name = GetAttribute(node, "Name", "Unknown"),
                Offset = GetIntAttribute(node, "offset", 0),
                Length = GetIntAttribute(node, "length", 4),
                IsVisible = GetIntAttribute(node, "visible", 1) == 1
            };

            foreach (XmlNode subNode in node.SelectNodes("sub")) {
                var sub = ParseSubFieldDefinition(subNode);
                cbit.SubFields.Add(sub);
            }

            return cbit;
        }

        private SubFieldDefinition ParseSubFieldDefinition(XmlNode node) {
            var sub = new SubFieldDefinition {
                Name = GetAttribute(node, "Name", "Unknown"),
                SubOffset = GetIntAttribute(node, "sub_offset", 0),
                Length = GetIntAttribute(node, "length", 4),
                Resolution = GetDoubleAttribute(node, "resolution", 1.0),
                Bias = GetDoubleAttribute(node, "bias", 0.0),
                Unit = GetAttribute(node, "unit", ""),
                IsVisible = true
            };

            // Parse mask (support both formats)
            string maskStr = GetAttribute(node, "mask", null);
            if (!string.IsNullOrEmpty(maskStr)) {
                sub.Mask = ParseUInt32(maskStr);
            } else {
                sub.Mask = 0xFFFFFFFF;
            }

            // Parse optional min/max/fault
            string minStr = GetAttribute(node, "min", null);
            if (!string.IsNullOrEmpty(minStr)) {
                sub.Min = double.Parse(minStr);
            }

            string maxStr = GetAttribute(node, "max", null);
            if (!string.IsNullOrEmpty(maxStr)) {
                sub.Max = double.Parse(maxStr);
            }

            string faultStr = GetAttribute(node, "fault", null);
            if (!string.IsNullOrEmpty(faultStr)) {
                sub.FaultValue = double.Parse(faultStr);
            }

            // Compute shift amount from mask
            sub.ShiftAmount = GetShiftFromMask(sub.Mask);
            sub.BitCount = CountBitsInMask(sub.Mask);
            sub.IsBooleanFlag = sub.BitCount == 1;

            return sub;
        }

        /// <summary>
        /// Convert CbitDefinition to WordDefinition for backward compatibility.
        /// </summary>
        private WordDefinition ConvertCbitToWord(CbitDefinition cbit) {
            var word = new WordDefinition {
                Name = cbit.Name,
                Offset = cbit.Offset,
                Size = cbit.Length,
                Mask = 0xFFFFFFFF,
                Resolution = 1.0,
                IsVisible = cbit.IsVisible
            };

            foreach (var sub in cbit.SubFields) {
                var field = new BitFieldDefinition {
                    Name = sub.Name,
                    StartBit = sub.ShiftAmount,
                    EndBit = sub.ShiftAmount + sub.BitCount - 1,
                    Mask = sub.Mask,
                    Resolution = sub.Resolution,
                    Bias = sub.Bias,
                    Min = sub.Min,
                    Max = sub.Max,
                    FaultValue = sub.FaultValue,
                    Unit = sub.Unit,
                    IsVisible = sub.IsVisible,
                    IsReserved = false,
                    ShiftAmount = sub.ShiftAmount,
                    BitCount = sub.BitCount,
                    SubOffset = sub.SubOffset
                };
                word.Fields.Add(field);
            }

            return word;
        }

        private static WordDefinition ParseWordDefinition(XmlNode node) {
            var word = new WordDefinition {
                Name = GetAttribute(node, "Name", "Unknown"),
                Offset = GetIntAttribute(node, "Offset", 0),
                Size = GetIntAttribute(node, "Size", 4),
                Resolution = GetDoubleAttribute(node, "Resolution", 1.0),
                IsVisible = GetBoolAttribute(node, "Visible", true)
            };

            // Parse mask
            string maskStr = GetAttribute(node, "Mask", null);
            string maskXStr = GetAttribute(node, "MaskX", null);
            if (!string.IsNullOrEmpty(maskStr)) {
                word.Mask = ParseUInt32(maskStr);
            } else if (!string.IsNullOrEmpty(maskXStr)) {
                word.Mask = Convert.ToUInt32(maskXStr, 16);
            } else {
                word.Mask = 0xFFFFFFFF;
            }

            // Parse individual bit names
            uint reservedMask = 0;
            for (int i = 0; i < 32; i++) {
                string bitName = GetAttribute(node, $"bit_{i}", null);
                word.BitNames[i] = bitName;
                if (string.IsNullOrEmpty(bitName) || 
                    bitName.Equals("Reserved", StringComparison.OrdinalIgnoreCase)) {
                    reservedMask |= (1u << i);
                }
            }
            word.ReservedBitMask = reservedMask;

            // Parse structured fields
            foreach (XmlNode fieldNode in node.SelectNodes("field")) {
                var field = new BitFieldDefinition {
                    Name = GetAttribute(fieldNode, "Name", "Unknown"),
                    StartBit = GetIntAttribute(fieldNode, "StartBit", 0),
                    EndBit = GetIntAttribute(fieldNode, "EndBit", 0),
                    Resolution = GetDoubleAttribute(fieldNode, "Resolution", 1.0),
                    Bias = GetDoubleAttribute(fieldNode, "Bias", 0.0),
                    Unit = GetAttribute(fieldNode, "Unit", ""),
                    IsVisible = true,
                    IsReserved = false
                };

                // Parse field mask
                string fieldMaskStr = GetAttribute(fieldNode, "Mask", null);
                if (!string.IsNullOrEmpty(fieldMaskStr)) {
                    field.Mask = ParseUInt32(fieldMaskStr);
                } else {
                    field.Mask = ComputeMask(field.StartBit, field.EndBit);
                }

                // Parse min/max/fault
                string minStr = GetAttribute(fieldNode, "min", null);
                if (!string.IsNullOrEmpty(minStr)) field.Min = double.Parse(minStr);

                string maxStr = GetAttribute(fieldNode, "max", null);
                if (!string.IsNullOrEmpty(maxStr)) field.Max = double.Parse(maxStr);

                string faultStr = GetAttribute(fieldNode, "fault", null);
                if (!string.IsNullOrEmpty(faultStr)) field.FaultValue = double.Parse(faultStr);

                field.ShiftAmount = field.StartBit;
                field.BitCount = field.EndBit - field.StartBit + 1;

                word.Fields.Add(field);
            }

            return word;
        }

        private static int GetShiftFromMask(uint mask) {
            if (mask == 0) return 0;
            int shift = 0;
            while ((mask & 1) == 0) {
                mask >>= 1;
                shift++;
            }
            return shift;
        }

        private static int CountBitsInMask(uint mask) {
            int count = 0;
            while (mask != 0) {
                count += (int)(mask & 1);
                mask >>= 1;
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeMask(int startBit, int endBit) {
            int bitCount = endBit - startBit + 1;
            uint mask = (1u << bitCount) - 1;
            return mask << startBit;
        }

        private static uint ParseUInt32(string value) {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                return Convert.ToUInt32(value.Substring(2), 16);
            }
            return uint.Parse(value);
        }

        private static string GetAttribute(XmlNode node, string name, string defaultValue) {
            var attr = node.Attributes?[name];
            return attr?.Value ?? defaultValue;
        }

        private static int GetIntAttribute(XmlNode node, string name, int defaultValue) {
            var attr = node.Attributes?[name];
            if (attr == null) return defaultValue;
            return int.TryParse(attr.Value, out int result) ? result : defaultValue;
        }

        private static double GetDoubleAttribute(XmlNode node, string name, double defaultValue) {
            var attr = node.Attributes?[name];
            if (attr == null) return defaultValue;
            return double.TryParse(attr.Value, out double result) ? result : defaultValue;
        }

        private static bool GetBoolAttribute(XmlNode node, string name, bool defaultValue) {
            var attr = node.Attributes?[name];
            if (attr == null) return defaultValue;
            return bool.TryParse(attr.Value, out bool result) ? result : defaultValue;
        }

        public WordDefinition GetWordByName(string name) {
            return _wordsByName.TryGetValue(name, out var word) ? word : null;
        }

        public CbitDefinition GetCbitByName(string name) {
            return _cbitsByName.TryGetValue(name, out var cbit) ? cbit : null;
        }
    }

    /// <summary>
    /// Represents a single bit field definition with validation support.
    /// </summary>
    public sealed class BitFieldDefinition {
        public string Name { get; set; }
        public int StartBit { get; set; }
        public int EndBit { get; set; }
        public uint Mask { get; set; }
        public double Resolution { get; set; } = 1.0;
        public double Bias { get; set; } = 0.0;
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? FaultValue { get; set; }
        public string Unit { get; set; }
        public bool IsReserved { get; set; }
        public bool IsVisible { get; set; } = true;
        public int SubOffset { get; set; }  // For cbit format

        // Pre-computed values
        public int ShiftAmount { get; set; }
        public int BitCount { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ExtractValue(uint rawWord) {
            uint masked = rawWord & Mask;
            uint shifted = masked >> ShiftAmount;
            return (shifted * Resolution) + Bias;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExtractRaw(uint rawWord) {
            return (rawWord & Mask) >> ShiftAmount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ExtractBit(uint rawWord) {
            return (rawWord & Mask) != 0;
        }

        public ValidationStatus Validate(double computedValue) {
            // Check fault condition
            if (FaultValue.HasValue) {
                double rawForFault = (computedValue - Bias) / (Resolution != 0 ? Resolution : 1);
                if (Math.Abs(rawForFault - FaultValue.Value) < 0.0001) {
                    return ValidationStatus.FaultCondition;
                }
            }

            // Check range
            if (Min.HasValue && computedValue < Min.Value) {
                return ValidationStatus.OutOfRange;
            }
            if (Max.HasValue && computedValue > Max.Value) {
                return ValidationStatus.OutOfRange;
            }

            if (Min.HasValue || Max.HasValue || FaultValue.HasValue) {
                return ValidationStatus.Valid;
            }

            return ValidationStatus.NoValidation;
        }
    }

    /// <summary>
    /// Represents a 32-bit word (cbit) definition with all its bit fields.
    /// </summary>
    public sealed class WordDefinition {
        public string Name { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }
        public uint Mask { get; set; }
        public double Resolution { get; set; }
        public bool IsVisible { get; set; }

        // Individual bit names (bit_0 through bit_31)
        public string[] BitNames { get; } = new string[32];

        // Structured sub-fields
        public List<BitFieldDefinition> Fields { get; } = new List<BitFieldDefinition>();

        // Pre-computed: which bits are reserved
        public uint ReservedBitMask { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExtractRawValue(byte[] data) {
            if (Offset + Size > data.Length) return 0;

            // Little-endian read
            uint value = 0;
            for (int i = 0; i < Size && i < 4; i++) {
                value |= (uint)data[Offset + i] << (i * 8);
            }
            return value & Mask;
        }

        /// <summary>
        /// Extract value from data at a specific sub-offset within this word.
        /// Used for cbit format where sub-fields have their own offset.
        /// </summary>
        public uint ExtractRawValueAtSubOffset(byte[] data, int subOffset, int length) {
            int actualOffset = Offset + subOffset;
            if (actualOffset + length > data.Length) return 0;

            uint value = 0;
            for (int i = 0; i < length && i < 4; i++) {
                value |= (uint)data[actualOffset + i] << (i * 8);
            }
            return value;
        }
    }
}
