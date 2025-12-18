namespace ETWSpyLib.Tests;

public class EtwPropertyFormatterTests
{
    [Theory]
    [InlineData(1, "UnicodeString")]
    [InlineData(2, "AnsiString")]
    [InlineData(3, "Int8")]
    [InlineData(4, "UInt8")]
    [InlineData(5, "Int16")]
    [InlineData(6, "UInt16")]
    [InlineData(7, "Int32")]
    [InlineData(8, "UInt32")]
    [InlineData(9, "Int64")]
    [InlineData(10, "UInt64")]
    [InlineData(11, "Float")]
    [InlineData(12, "Double")]
    [InlineData(13, "Boolean")]
    [InlineData(14, "Binary")]
    [InlineData(15, "GUID")]
    [InlineData(16, "Pointer")]
    [InlineData(17, "FileTime")]
    [InlineData(18, "SystemTime")]
    [InlineData(19, "SID")]
    [InlineData(20, "HexInt32")]
    [InlineData(21, "HexInt64")]
    [InlineData(22, "CountedString")]
    [InlineData(23, "CountedAnsiString")]
    [InlineData(24, "ReversedCountedString")]
    [InlineData(25, "ReversedCountedAnsiString")]
    [InlineData(26, "NonNullTerminatedString")]
    [InlineData(27, "NonNullTerminatedAnsiString")]
    [InlineData(28, "UnicodeChar")]
    [InlineData(29, "AnsiChar")]
    [InlineData(30, "SizeT")]
    [InlineData(31, "HexDump")]
    [InlineData(32, "WbemSID")]
    public void GetTypeName_ReturnsCorrectTypeName(int type, string expectedName)
    {
        var result = EtwPropertyFormatter.GetTypeName(type);

        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(100)]
    [InlineData(-1)]
    public void GetTypeName_ReturnsUnknownForInvalidType(int type)
    {
        var result = EtwPropertyFormatter.GetTypeName(type);

        Assert.StartsWith("Unknown(", result);
        Assert.EndsWith(")", result);
        Assert.Contains(type.ToString(), result);
    }

    [Fact]
    public void FormattedProperty_DefaultValues()
    {
        var property = new FormattedProperty();

        Assert.Equal(string.Empty, property.Name);
        Assert.Equal(string.Empty, property.TypeName);
        Assert.Equal(string.Empty, property.Value);
    }

    [Fact]
    public void FormattedProperty_SetProperties()
    {
        var property = new FormattedProperty
        {
            Name = "TestProperty",
            TypeName = "UInt32",
            Value = "42"
        };

        Assert.Equal("TestProperty", property.Name);
        Assert.Equal("UInt32", property.TypeName);
        Assert.Equal("42", property.Value);
    }
}
