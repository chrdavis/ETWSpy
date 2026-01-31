using System.Text;
using System.Text.Json;

namespace ETWSpyLib.Tests;

public class ProviderJsonReaderTests
{
    [Fact]
    public void ReadFromStream_ParsesValidJsonFormat()
    {
        var jsonContent = """
            [
                {"Name": "TestProvider", "Guid": "{12345678-1234-1234-1234-123456789abc}"},
                {"Name": "AnotherProvider", "Guid": "{87654321-4321-4321-4321-cba987654321}"}
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

        var entries = ProviderJsonReader.ReadFromStream(stream);

        Assert.Equal(2, entries.Count);
        Assert.Equal("TestProvider", entries[0].Name);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), entries[0].Guid);
        Assert.Equal("AnotherProvider", entries[1].Name);
        Assert.Equal(Guid.Parse("87654321-4321-4321-4321-cba987654321"), entries[1].Guid);
    }

    [Fact]
    public void ReadFromStream_ParsesGuidsWithoutBraces()
    {
        var jsonContent = """
            [
                {"Name": "TestProvider", "Guid": "12345678-1234-1234-1234-123456789abc"}
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

        var entries = ProviderJsonReader.ReadFromStream(stream);

        Assert.Single(entries);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), entries[0].Guid);
    }

    [Fact]
    public void ReadFromStream_ReturnsEmptyListForEmptyArray()
    {
        var jsonContent = "[]";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

        var entries = ProviderJsonReader.ReadFromStream(stream);

        Assert.Empty(entries);
    }

    [Fact]
    public void WriteToFile_WritesCorrectFormat()
    {
        var entries = new List<ProviderEntry>
        {
            new("Provider1", Guid.Parse("12345678-1234-1234-1234-123456789abc")),
            new("Provider2", Guid.Parse("87654321-4321-4321-4321-cba987654321"))
        };
        
        var tempFile = Path.GetTempFileName();
        try
        {
            ProviderJsonReader.WriteToFile(tempFile, entries);
            var content = File.ReadAllText(tempFile);
            
            Assert.Contains("Provider1", content);
            Assert.Contains("Provider2", content);
            Assert.Contains("12345678-1234-1234-1234-123456789abc", content);
            Assert.Contains("87654321-4321-4321-4321-cba987654321", content);
            
            // Verify it's valid JSON
            var doc = JsonDocument.Parse(content);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ReadFromFile_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => 
            ProviderJsonReader.ReadFromFile("nonexistent_file.json"));
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var originalEntries = new List<ProviderEntry>
        {
            new("Test.Provider.One", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new("Test.Provider.Two", Guid.Parse("22222222-2222-2222-2222-222222222222"))
        };
        
        var tempFile = Path.GetTempFileName();
        try
        {
            ProviderJsonReader.WriteToFile(tempFile, originalEntries);
            var loadedEntries = ProviderJsonReader.ReadFromFile(tempFile);

            Assert.Equal(originalEntries.Count, loadedEntries.Count);
            for (int i = 0; i < originalEntries.Count; i++)
            {
                Assert.Equal(originalEntries[i].Name, loadedEntries[i].Name);
                Assert.Equal(originalEntries[i].Guid, loadedEntries[i].Guid);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ProviderEntry_GuidProperty_ParsesAndFormatsCorrectly()
    {
        var entry = new ProviderEntry();
        var testGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        
        entry.Guid = testGuid;
        
        Assert.Equal(testGuid, entry.Guid);
        Assert.Equal("{12345678-1234-1234-1234-123456789abc}", entry.GuidString);
    }

    [Fact]
    public void ProviderEntry_Constructor_SetsPropertiesCorrectly()
    {
        var testGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        
        var entry = new ProviderEntry("TestProvider", testGuid);
        
        Assert.Equal("TestProvider", entry.Name);
        Assert.Equal(testGuid, entry.Guid);
    }

    [Fact]
    public void HasEmbeddedResource_ReturnsTrue()
    {
        // This test verifies the embedded resource is correctly included
        Assert.True(ProviderJsonReader.HasEmbeddedResource());
    }

    [Fact]
    public void ReadFromEmbeddedResource_ReturnsProviders()
    {
        var entries = ProviderJsonReader.ReadFromEmbeddedResource();
        
        Assert.NotEmpty(entries);
        // Verify we got real provider data
        Assert.All(entries, e => Assert.False(string.IsNullOrEmpty(e.Name)));
        Assert.All(entries, e => Assert.NotEqual(Guid.Empty, e.Guid));
    }
}
