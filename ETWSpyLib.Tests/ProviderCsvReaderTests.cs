using System.Text;
using Xunit.Abstractions;

namespace ETWSpyLib.Tests;

public class ProviderCsvReaderTests
{
    private readonly ITestOutputHelper _output;

    public ProviderCsvReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ReadFromStream_ParsesValidCsvFormat()
    {
        var csvContent = "TestProvider,{12345678-1234-1234-1234-123456789abc}\nAnotherProvider,{87654321-4321-4321-4321-cba987654321}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        var entries = ProviderCsvReader.ReadFromStream(stream);

        Assert.Equal(2, entries.Count);
        Assert.Equal("TestProvider", entries[0].Name);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), entries[0].Guid);
        Assert.Equal("AnotherProvider", entries[1].Name);
        Assert.Equal(Guid.Parse("87654321-4321-4321-4321-cba987654321"), entries[1].Guid);
    }

    [Fact]
    public void ReadFromStream_SkipsEmptyLines()
    {
        var csvContent = "Provider1,{12345678-1234-1234-1234-123456789abc}\n\n\nProvider2,{87654321-4321-4321-4321-cba987654321}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        var entries = ProviderCsvReader.ReadFromStream(stream);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ReadFromStream_SkipsInvalidLines()
    {
        var csvContent = "ValidProvider,{12345678-1234-1234-1234-123456789abc}\nInvalidLine\nAlsoValid,{87654321-4321-4321-4321-cba987654321}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        var entries = ProviderCsvReader.ReadFromStream(stream);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void WriteToStream_WritesCorrectFormat()
    {
        var entries = new List<CsvProviderEntry>
        {
            new("Provider1", Guid.Parse("12345678-1234-1234-1234-123456789abc")),
            new("Provider2", Guid.Parse("87654321-4321-4321-4321-cba987654321"))
        };
        using var stream = new MemoryStream();

        ProviderCsvReader.WriteToStream(stream, entries);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        
        Assert.Contains("Provider1,{12345678-1234-1234-1234-123456789abc}", content);
        Assert.Contains("Provider2,{87654321-4321-4321-4321-cba987654321}", content);
    }

    [Fact]
    public void ReadFromFile_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => 
            ProviderCsvReader.ReadFromFile("nonexistent_file.csv"));
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var originalEntries = new List<CsvProviderEntry>
        {
            new("Test.Provider.One", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new("Test.Provider.Two", Guid.Parse("22222222-2222-2222-2222-222222222222"))
        };
        
        var tempFile = Path.GetTempFileName();
        try
        {
            ProviderCsvReader.WriteToFile(tempFile, originalEntries);
            var loadedEntries = ProviderCsvReader.ReadFromFile(tempFile);

            Assert.Equal(originalEntries.Count, loadedEntries.Count);
            for (int i = 0; i < originalEntries.Count; i++)
            {
                Assert.Equal(originalEntries[i].Name, loadedEntries[i].Name);
                Assert.Equal(originalEntries[i].Guid, loadedEntries[i].Guid);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Attempts to load each provider from the CSV file by GUID and reports failures.
    /// This test validates which providers in the CSV are actually registered on the system.
    /// </summary>
    [Fact]
    public void LoadProvidersFromCsv_ReportsFailedProviders()
    {
        var csvPath = ProviderCsvReader.GetDefaultCsvPath();
        
        // If the CSV doesn't exist at the default path, try the source location
        if (!File.Exists(csvPath))
        {
            csvPath = Path.Combine(AppContext.BaseDirectory, "ProviderNameGuid.csv");
        }
        
        if (!File.Exists(csvPath))
        {
            _output.WriteLine($"CSV file not found at: {csvPath}");
            _output.WriteLine("Skipping provider load test.");
            return;
        }

        var entries = ProviderCsvReader.ReadFromFile(csvPath);
        var registeredProviders = new List<CsvProviderEntry>();
        var unregisteredProviders = new List<CsvProviderEntry>();

        _output.WriteLine($"Testing {entries.Count} providers from CSV...");
        _output.WriteLine(new string('-', 80));

        foreach (var entry in entries)
        {
            // Check if the provider GUID is registered on this machine
            if (EtwProviderValidator.IsProviderRegistered(entry.Guid))
            {
                registeredProviders.Add(entry);
            }
            else
            {
                unregisteredProviders.Add(entry);
            }
        }

        _output.WriteLine($"\nResults:");
        _output.WriteLine($"  Total providers: {entries.Count}");
        _output.WriteLine($"  Registered on this machine: {registeredProviders.Count}");
        _output.WriteLine($"  Not registered: {unregisteredProviders.Count}");
        _output.WriteLine(new string('-', 80));

        // Log registered providers
        _output.WriteLine($"\n\nREGISTERED PROVIDERS ({registeredProviders.Count}):");
        _output.WriteLine(new string('=', 80));
        foreach (var entry in registeredProviders)
        {
            _output.WriteLine($"  - {entry.Name} ({entry.Guid})");
        }

        // Log unregistered providers
        if (unregisteredProviders.Count > 0)
        {
            _output.WriteLine($"\n\nUNREGISTERED PROVIDERS ({unregisteredProviders.Count}):");
            _output.WriteLine(new string('=', 80));
            foreach (var entry in unregisteredProviders)
            {
                _output.WriteLine($"  - {entry.Name} ({entry.Guid})");
            }
        }

        // This test is informational - we don't fail it based on provider availability
        // since available providers vary by system configuration
        Assert.True(true, "Provider loading test completed. Check output for details.");
    }
}
