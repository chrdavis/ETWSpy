namespace ETWSpyLib
{
    /// <summary>
    /// Detects when a decoded event schema does not match the event's actual payload.
    /// </summary>
    /// <remarks>
    /// krabsetw caches TDH schemas keyed on provider, event name, id, version, opcode, level
    /// and keyword. TraceLogging events always report an id of 0 and are identified by name,
    /// and the same event name can be emitted from multiple call sites with different field
    /// sets. Those variants produce an identical cache key, so whichever variant is decoded
    /// first is reused for the rest - silently shifting every field.
    /// See https://github.com/microsoft/krabsetw/issues/193.
    ///
    /// The schema cannot be corrected from managed code, because krabsetw does not expose the
    /// raw EVENT_RECORD or its extended data (where TraceLogging schemas live). What can be
    /// done is to notice the mismatch: walking the schema over the payload yields a byte count
    /// that disagrees with the real payload length when the wrong schema was applied.
    /// </remarks>
    public static class PayloadLayoutValidator
    {
        /// <summary>
        /// Result of comparing a schema against a payload.
        /// </summary>
        public enum LayoutMatch
        {
            /// <summary>The schema accounts for exactly the payload bytes.</summary>
            Match,

            /// <summary>The schema does not account for the payload; values are unreliable.</summary>
            Mismatch,

            /// <summary>The payload contains a type whose length cannot be determined.</summary>
            Indeterminate
        }

        /// <summary>
        /// Compares a schema's field layout against the raw payload.
        /// </summary>
        /// <param name="propertyTypes">TDH_IN_TYPE of each field, in schema order.</param>
        /// <param name="userData">The raw event payload.</param>
        /// <param name="consumedBytes">Bytes accounted for by the schema.</param>
        /// <returns>Whether the schema matches, conflicts with, or cannot be checked against the payload.</returns>
        public static LayoutMatch Validate(IReadOnlyList<int> propertyTypes, byte[] userData, out int consumedBytes)
        {
            consumedBytes = 0;

            if (propertyTypes == null || userData == null)
            {
                return LayoutMatch.Indeterminate;
            }

            // An event with no fields and no payload trivially matches
            if (propertyTypes.Count == 0)
            {
                return userData.Length == 0 ? LayoutMatch.Match : LayoutMatch.Mismatch;
            }

            int offset = 0;

            foreach (var type in propertyTypes)
            {
                if (offset > userData.Length)
                {
                    consumedBytes = offset;
                    return LayoutMatch.Mismatch;
                }

                int size = GetFieldSize(type, userData, offset);

                if (size < 0)
                {
                    // Length depends on another field or an unsupported type
                    consumedBytes = offset;
                    return LayoutMatch.Indeterminate;
                }

                offset += size;
            }

            consumedBytes = offset;
            return offset == userData.Length ? LayoutMatch.Match : LayoutMatch.Mismatch;
        }

        /// <summary>
        /// Gets the byte length of a single field.
        /// </summary>
        /// <returns>The field length, or -1 when it cannot be determined.</returns>
        private static int GetFieldSize(int type, byte[] data, int offset)
        {
            // TDH_IN_TYPE values from tdh.h
            switch (type)
            {
                case 3:  // INT8
                case 4:  // UINT8
                case 29: // ANSICHAR
                    return 1;
                case 5:  // INT16
                case 6:  // UINT16
                case 28: // UNICODECHAR
                    return 2;
                case 7:  // INT32
                case 8:  // UINT32
                case 11: // FLOAT
                case 13: // BOOLEAN - 4 bytes, not 1
                case 20: // HEXINT32
                    return 4;
                case 9:  // INT64
                case 10: // UINT64
                case 12: // DOUBLE
                case 17: // FILETIME
                case 21: // HEXINT64
                case 16: // POINTER (x64)
                case 30: // SIZET (x64)
                    return 8;
                case 15: // GUID
                case 18: // SYSTEMTIME
                    return 16;
                case 1:  // UNICODESTRING - null terminated
                    return MeasureUnicodeString(data, offset);
                case 2:  // ANSISTRING - null terminated
                    return MeasureAnsiString(data, offset);
                case 22: // COUNTEDSTRING - 2 byte length prefix, value in bytes
                case 23: // COUNTEDANSISTRING
                    return MeasureCountedString(data, offset);
                default:
                    // BINARY, SID, HEXDUMP and friends carry no intrinsic length
                    return -1;
            }
        }

        private static int MeasureUnicodeString(byte[] data, int offset)
        {
            for (int i = offset; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    return i - offset + 2;
                }
            }

            // Unterminated: the schema cannot be reconciled with this payload
            return -1;
        }

        private static int MeasureAnsiString(byte[] data, int offset)
        {
            for (int i = offset; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    return i - offset + 1;
                }
            }

            return -1;
        }

        private static int MeasureCountedString(byte[] data, int offset)
        {
            if (offset + 2 > data.Length)
            {
                return -1;
            }

            int length = data[offset] | (data[offset + 1] << 8);
            return 2 + length;
        }

        /// <summary>
        /// Formats a payload as a hex string for display.
        /// </summary>
        /// <param name="userData">The raw event payload.</param>
        /// <param name="maxBytes">Maximum bytes to render before truncating.</param>
        public static string FormatHex(byte[] userData, int maxBytes = 256)
        {
            if (userData == null || userData.Length == 0)
            {
                return string.Empty;
            }

            int count = Math.Min(userData.Length, maxBytes);
            string hex = BitConverter.ToString(userData, 0, count);

            return count < userData.Length
                ? $"[{userData.Length} bytes] {hex}..."
                : $"[{userData.Length} bytes] {hex}";
        }
    }
}
