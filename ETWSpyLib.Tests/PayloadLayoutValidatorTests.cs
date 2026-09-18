using static ETWSpyLib.PayloadLayoutValidator;

namespace ETWSpyLib.Tests;

public class PayloadLayoutValidatorTests
{
    // TDH_IN_TYPE values
    private const int UnicodeString = 1;
    private const int AnsiString = 2;
    private const int UInt8 = 4;
    private const int UInt16 = 6;
    private const int UInt32 = 8;
    private const int UInt64 = 10;
    private const int Boolean = 13;
    private const int Binary = 14;
    private const int Guid = 15;

    private static byte[] Unicode(string value)
    {
        var bytes = new List<byte>();
        foreach (char c in value)
        {
            bytes.Add((byte)(c & 0xFF));
            bytes.Add((byte)(c >> 8));
        }
        bytes.Add(0);
        bytes.Add(0);
        return bytes.ToArray();
    }

    [Fact]
    public void Validate_MatchingFixedSizeSchema_ReturnsMatch()
    {
        var payload = new byte[12]; // UInt64 + UInt32

        var result = Validate(new[] { UInt64, UInt32 }, payload, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(12, consumed);
    }

    [Fact]
    public void Validate_SchemaShorterThanPayload_ReturnsMismatch()
    {
        var payload = new byte[12];

        var result = Validate(new[] { UInt32 }, payload, out int consumed);

        Assert.Equal(LayoutMatch.Mismatch, result);
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void Validate_SchemaLongerThanPayload_ReturnsMismatch()
    {
        var payload = new byte[4];

        var result = Validate(new[] { UInt64, UInt64 }, payload, out _);

        Assert.Equal(LayoutMatch.Mismatch, result);
    }

    /// <summary>
    /// Reproduces the reported AppLaunch_UserClick defect: the event carries
    /// PartA_PrivTags (UInt64) followed by entryPoint (UInt32) and appId (string),
    /// but krabsetw decodes it with the variant schema that omits PartA_PrivTags.
    /// </summary>
    [Fact]
    public void Validate_AppLaunchUserClickWrongVariant_ReturnsMismatch()
    {
        var payload = new List<byte>();
        payload.AddRange(BitConverter.GetBytes(50331648UL)); // PartA_PrivTags
        payload.AddRange(BitConverter.GetBytes(23u));        // entryPoint
        payload.AddRange(Unicode("MSEdge"));                 // appId
        payload.AddRange(BitConverter.GetBytes(1u));         // isDirectLaunch
        var data = payload.ToArray();

        // Correct schema accounts for the payload exactly
        var correct = Validate(new[] { UInt64, UInt32, UnicodeString, UInt32 }, data, out int correctConsumed);
        Assert.Equal(LayoutMatch.Match, correct);
        Assert.Equal(data.Length, correctConsumed);

        // The cached variant without PartA_PrivTags does not
        var wrong = Validate(new[] { UInt32, UnicodeString }, data, out _);
        Assert.Equal(LayoutMatch.Mismatch, wrong);
    }

    [Fact]
    public void Validate_UnicodeStringSchema_ReturnsMatch()
    {
        var data = Unicode("Test");

        var result = Validate(new[] { UnicodeString }, data, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(10, consumed); // 4 chars * 2 + 2 terminator
    }

    [Fact]
    public void Validate_AnsiStringSchema_ReturnsMatch()
    {
        var data = new byte[] { (byte)'a', (byte)'b', 0 };

        var result = Validate(new[] { AnsiString }, data, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(3, consumed);
    }

    [Fact]
    public void Validate_BooleanIsFourBytes()
    {
        var payload = new byte[4];

        var result = Validate(new[] { Boolean }, payload, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void Validate_GuidIsSixteenBytes()
    {
        var payload = new byte[16];

        var result = Validate(new[] { Guid }, payload, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(16, consumed);
    }

    [Fact]
    public void Validate_MixedIntegerWidths_ReturnsMatch()
    {
        var payload = new byte[1 + 2 + 4 + 8];

        var result = Validate(new[] { UInt8, UInt16, UInt32, UInt64 }, payload, out int consumed);

        Assert.Equal(LayoutMatch.Match, result);
        Assert.Equal(15, consumed);
    }

    [Fact]
    public void Validate_BinaryField_ReturnsIndeterminate()
    {
        // Binary carries no intrinsic length, so no judgement can be made
        var payload = new byte[16];

        var result = Validate(new[] { Binary }, payload, out _);

        Assert.Equal(LayoutMatch.Indeterminate, result);
    }

    [Fact]
    public void Validate_UnterminatedString_ReturnsIndeterminate()
    {
        var data = new byte[] { (byte)'a', (byte)'b' };

        var result = Validate(new[] { AnsiString }, data, out _);

        Assert.Equal(LayoutMatch.Indeterminate, result);
    }

    [Fact]
    public void Validate_NoPropertiesAndNoPayload_ReturnsMatch()
    {
        var result = Validate(Array.Empty<int>(), Array.Empty<byte>(), out _);

        Assert.Equal(LayoutMatch.Match, result);
    }

    [Fact]
    public void Validate_NoPropertiesButPayloadPresent_ReturnsMismatch()
    {
        var result = Validate(Array.Empty<int>(), new byte[4], out _);

        Assert.Equal(LayoutMatch.Mismatch, result);
    }

    [Fact]
    public void Validate_NullInputs_ReturnsIndeterminate()
    {
        Assert.Equal(LayoutMatch.Indeterminate, Validate(null!, new byte[4], out _));
        Assert.Equal(LayoutMatch.Indeterminate, Validate(new[] { UInt32 }, null!, out _));
    }

    [Fact]
    public void FormatHex_IncludesLengthAndBytes()
    {
        var result = PayloadLayoutValidator.FormatHex(new byte[] { 0xDE, 0xAD });

        Assert.Contains("[2 bytes]", result);
        Assert.Contains("DE-AD", result);
    }

    [Fact]
    public void FormatHex_TruncatesLongPayloads()
    {
        var result = PayloadLayoutValidator.FormatHex(new byte[512], maxBytes: 16);

        Assert.Contains("[512 bytes]", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void FormatHex_EmptyPayload_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PayloadLayoutValidator.FormatHex(Array.Empty<byte>()));
    }
}
