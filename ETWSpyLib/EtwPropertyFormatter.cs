using Microsoft.O365.Security.ETW;
using System.Text;

namespace ETWSpyLib
{
    /// <summary>
    /// Represents a formatted ETW property with name, type, and value.
    /// </summary>
    public class FormattedProperty
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Provides utilities for formatting ETW event properties for display.
    /// </summary>
    public static class EtwPropertyFormatter
    {
        /// <summary>
        /// Gets a human-readable type name from a TDH_IN_TYPE value.
        /// </summary>
        /// <param name="type">The TDH_IN_TYPE value.</param>
        /// <returns>Human-readable type name.</returns>
        public static string GetTypeName(int type)
        {
            return type switch
            {
                1 => "UnicodeString",
                2 => "AnsiString",
                3 => "Int8",
                4 => "UInt8",
                5 => "Int16",
                6 => "UInt16",
                7 => "Int32",
                8 => "UInt32",
                9 => "Int64",
                10 => "UInt64",
                11 => "Float",
                12 => "Double",
                13 => "Boolean",
                14 => "Binary",
                15 => "GUID",
                16 => "Pointer",
                17 => "FileTime",
                18 => "SystemTime",
                19 => "SID",
                20 => "HexInt32",
                21 => "HexInt64",
                22 => "CountedString",
                23 => "CountedAnsiString",
                24 => "ReversedCountedString",
                25 => "ReversedCountedAnsiString",
                26 => "NonNullTerminatedString",
                27 => "NonNullTerminatedAnsiString",
                28 => "UnicodeChar",
                29 => "AnsiChar",
                30 => "SizeT",
                31 => "HexDump",
                32 => "WbemSID",
                _ => $"Unknown({type})"
            };
        }

        /// <summary>
        /// Formats all properties of an event record into a list of FormattedProperty objects
        /// that include name, type, and value.
        /// </summary>
        /// <param name="record">The ETW event record.</param>
        /// <param name="interner">Optional string interner for deduplicating strings. If null, no interning is performed.</param>
        /// <returns>List of formatted properties with type information.</returns>
        public static List<FormattedProperty> GetFormattedPropertiesWithTypes(IEventRecord record, StringInterner? interner = null)
        {
            var result = new List<FormattedProperty>();

            try
            {
                foreach (var property in record.Properties)
                {
                    var name = property.Name;
                    var typeName = GetTypeName(property.Type);
                    var value = FormatPropertyValue(record, property);

                    if (!string.IsNullOrEmpty(value))
                    {
                        result.Add(new FormattedProperty
                        {
                            // Intern name and typeName (highly repetitive), but not value (often unique)
                            Name = interner?.Intern(name) ?? name,
                            TypeName = interner?.Intern(typeName) ?? typeName,
                            Value = value
                        });
                    }
                }
            }
            catch
            {
                // If enumeration fails, return what we have
            }

            return result;
        }
        /// <summary>
        /// Formats all properties of an event record into a dictionary of name/value pairs.
        /// </summary>
        /// <param name="record">The ETW event record.</param>
        /// <returns>Dictionary of property names to their formatted string values.</returns>
        public static Dictionary<string, string> GetFormattedProperties(IEventRecord record)
        {
            var result = new Dictionary<string, string>();

            try
            {
                foreach (var property in record.Properties)
                {
                    var name = property.Name;
                    var value = FormatPropertyValue(record, property);

                    if (!string.IsNullOrEmpty(value))
                    {
                        result[name] = value;
                    }
                }
            }
            catch
            {
                // If enumeration fails, return what we have
            }

            return result;
        }

        /// <summary>
        /// Formats all properties of an event record as a single string.
        /// </summary>
        /// <param name="record">The ETW event record.</param>
        /// <param name="separator">The separator between properties (default is ", ").</param>
        /// <returns>Formatted string of all properties.</returns>
        public static string FormatAllProperties(IEventRecord record, string separator = ", ")
        {
            var properties = GetFormattedProperties(record);
            if (properties.Count == 0)
            {
                return "(No properties)";
            }

            var sb = new StringBuilder();
            bool first = true;
            foreach (var kvp in properties)
            {
                if (!first)
                {
                    sb.Append(separator);
                }
                sb.Append($"{kvp.Key}: {kvp.Value}");
                first = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a single property value based on its TDH_IN_TYPE.
        /// </summary>
        /// <param name="record">The ETW event record.</param>
        /// <param name="property">The property to format.</param>
        /// <returns>Formatted string value.</returns>
        public static string FormatPropertyValue(IEventRecord record, Property property)
        {
            try
            {
                var name = property.Name;
                var type = property.Type;

                // TDH_IN_TYPE values from Windows SDK (tdh.h)
                return type switch
                {
                    // TDH_INTYPE_UNICODESTRING = 1
                    1 => $"\"{record.GetUnicodeString(name, "")}\"",
                    // TDH_INTYPE_ANSISTRING = 2
                    2 => $"\"{record.GetAnsiString(name, "")}\"",
                    // TDH_INTYPE_INT8 = 3
                    3 => record.GetInt8(name, 0).ToString(),
                    // TDH_INTYPE_UINT8 = 4
                    4 => record.GetUInt8(name, 0).ToString(),
                    // TDH_INTYPE_INT16 = 5
                    5 => record.GetInt16(name, 0).ToString(),
                    // TDH_INTYPE_UINT16 = 6
                    6 => record.GetUInt16(name, 0).ToString(),
                    // TDH_INTYPE_INT32 = 7
                    7 => record.GetInt32(name, 0).ToString(),
                    // TDH_INTYPE_UINT32 = 8
                    8 => record.GetUInt32(name, 0).ToString(),
                    // TDH_INTYPE_INT64 = 9
                    9 => record.GetInt64(name, 0).ToString(),
                    // TDH_INTYPE_UINT64 = 10
                    10 => record.GetUInt64(name, 0).ToString(),
                    // TDH_INTYPE_FLOAT = 11
                    11 => BitConverter.ToSingle(BitConverter.GetBytes(record.GetUInt32(name, 0)), 0).ToString("G"),
                    // TDH_INTYPE_DOUBLE = 12
                    12 => BitConverter.ToDouble(BitConverter.GetBytes(record.GetUInt64(name, 0)), 0).ToString("G"),
                    // TDH_INTYPE_BOOLEAN = 13
                    13 => record.GetUInt32(name, 0) != 0 ? "true" : "false",
                    // TDH_INTYPE_BINARY = 14
                    14 => FormatBinary(record, name),
                    // TDH_INTYPE_GUID = 15
                    15 => FormatGuid(record, name),
                    // TDH_INTYPE_POINTER = 16
                    16 => $"0x{record.GetUInt64(name, 0):X}",
                    // TDH_INTYPE_FILETIME = 17
                    17 => FormatFileTime(record, name),
                    // TDH_INTYPE_SYSTEMTIME = 18
                    18 => FormatSystemTime(record, name),
                    // TDH_INTYPE_SID = 19
                    19 => FormatSid(record, name),
                    // TDH_INTYPE_HEXINT32 = 20
                    20 => $"0x{record.GetUInt32(name, 0):X8}",
                    // TDH_INTYPE_HEXINT64 = 21
                    21 => $"0x{record.GetUInt64(name, 0):X16}",
                    // TDH_INTYPE_COUNTEDSTRING = 22
                    22 => $"\"{record.GetUnicodeString(name, "")}\"",
                    // TDH_INTYPE_COUNTEDANSISTRING = 23
                    23 => $"\"{record.GetAnsiString(name, "")}\"",
                    // TDH_INTYPE_REVERSEDCOUNTEDSTRING = 24
                    24 => $"\"{record.GetUnicodeString(name, "")}\"",
                    // TDH_INTYPE_REVERSEDCOUNTEDANSISTRING = 25
                    25 => $"\"{record.GetAnsiString(name, "")}\"",
                    // TDH_INTYPE_NONNULLTERMINATEDSTRING = 26
                    26 => $"\"{record.GetUnicodeString(name, "")}\"",
                    // TDH_INTYPE_NONNULLTERMINATEDANSISTRING = 27
                    27 => $"\"{record.GetAnsiString(name, "")}\"",
                    // TDH_INTYPE_UNICODECHAR = 28
                    28 => $"'{(char)record.GetUInt16(name, 0)}'",
                    // TDH_INTYPE_ANSICHAR = 29
                    29 => $"'{(char)record.GetUInt8(name, 0)}'",
                    // TDH_INTYPE_SIZET = 30
                    30 => record.GetUInt64(name, 0).ToString(),
                    // TDH_INTYPE_HEXDUMP = 31
                    31 => FormatBinary(record, name),
                    // TDH_INTYPE_WBEMSID = 32
                    32 => FormatSid(record, name),
                    // Default - try to read as binary and show type
                    _ => FormatBinaryFallback(record, name, type)
                };
            }
            catch (Exception ex)
            {
                return $"(error: {ex.Message})";
            }
        }

        /// <summary>
        /// Formats a GUID property.
        /// </summary>
        public static string FormatGuid(IEventRecord record, string name)
        {
            try
            {
                var bytes = record.GetBinary(name);
                if (bytes != null && bytes.Length == 16)
                {
                    return new Guid(bytes).ToString();
                }
                return "(invalid GUID)";
            }
            catch
            {
                return "(error reading GUID)";
            }
        }

        /// <summary>
        /// Formats a FILETIME property.
        /// </summary>
        public static string FormatFileTime(IEventRecord record, string name)
        {
            try
            {
                var fileTime = record.GetInt64(name, 0);
                if (fileTime > 0)
                {
                    var dateTime = DateTime.FromFileTime(fileTime);
                    return dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
                }
                return "0";
            }
            catch
            {
                return "(error reading FILETIME)";
            }
        }

        /// <summary>
        /// Formats a SYSTEMTIME property.
        /// </summary>
        public static string FormatSystemTime(IEventRecord record, string name)
        {
            try
            {
                var bytes = record.GetBinary(name);
                if (bytes != null && bytes.Length >= 16)
                {
                    // SYSTEMTIME structure: year(2), month(2), dayOfWeek(2), day(2), hour(2), minute(2), second(2), milliseconds(2)
                    int year = BitConverter.ToInt16(bytes, 0);
                    int month = BitConverter.ToInt16(bytes, 2);
                    int day = BitConverter.ToInt16(bytes, 6);
                    int hour = BitConverter.ToInt16(bytes, 8);
                    int minute = BitConverter.ToInt16(bytes, 10);
                    int second = BitConverter.ToInt16(bytes, 12);
                    int ms = BitConverter.ToInt16(bytes, 14);
                    return $"{year:D4}-{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{second:D2}.{ms:D3}";
                }
                return "(invalid SYSTEMTIME)";
            }
            catch
            {
                return "(error reading SYSTEMTIME)";
            }
        }

        /// <summary>
        /// Formats a SID (Security Identifier) property.
        /// </summary>
        public static string FormatSid(IEventRecord record, string name)
        {
            try
            {
                var bytes = record.GetBinary(name);
                if (bytes != null && bytes.Length > 0)
                {
                    var sid = new System.Security.Principal.SecurityIdentifier(bytes, 0);
                    return sid.ToString();
                }
                return "(empty SID)";
            }
            catch
            {
                return "(error reading SID)";
            }
        }

        /// <summary>
        /// Formats a binary property.
        /// </summary>
        /// <param name="record">The ETW event record.</param>
        /// <param name="name">The property name.</param>
        /// <param name="maxDisplayBytes">Maximum bytes to display before truncating (default 64).</param>
        public static string FormatBinary(IEventRecord record, string name, int maxDisplayBytes = 64)
        {
            try
            {
                var bytes = record.GetBinary(name);
                if (bytes != null && bytes.Length > 0)
                {
                    if (bytes.Length <= maxDisplayBytes)
                        return $"[{bytes.Length} bytes] {BitConverter.ToString(bytes)}";
                    else
                        return $"[{bytes.Length} bytes] {BitConverter.ToString(bytes, 0, maxDisplayBytes)}...";
                }
                return "(empty)";
            }
            catch
            {
                return "(error reading binary)";
            }
        }

        /// <summary>
        /// Formats a property with unknown type as binary with type information.
        /// </summary>
        public static string FormatBinaryFallback(IEventRecord record, string name, int type)
        {
            try
            {
                var bytes = record.GetBinary(name);
                if (bytes != null && bytes.Length > 0)
                {
                    if (bytes.Length <= 32)
                        return $"[Type:{type}, {bytes.Length} bytes] {BitConverter.ToString(bytes)}";
                    else
                        return $"[Type:{type}, {bytes.Length} bytes] {BitConverter.ToString(bytes, 0, 32)}...";
                }
                return $"(Type:{type}, empty)";
            }
            catch
            {
                return $"(Type:{type}, error reading)";
            }
        }
    }
}
