using Xunit.Abstractions;

namespace ETWSpyLib.Tests;

public class EtwProviderValidatorTests
{
    private readonly ITestOutputHelper _output;

    public EtwProviderValidatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Prints all registered ETW provider information from GetRegisteredProviderInfo().
    /// </summary>
    [Fact]
    public void GetRegisteredProviderInfo_PrintsAllProviders()
    {
        var providers = EtwProviderValidator.GetRegisteredProviderInfo();

        _output.WriteLine($"Total Registered Providers: {providers.Count}");
        _output.WriteLine(new string('=', 100));
        _output.WriteLine($"{"Name",-60} {"GUID",-38} {"Schema"}");
        _output.WriteLine(new string('-', 100));

        foreach (var provider in providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"{TruncateName(provider.Name, 60),-60} {provider.Guid,-38} {provider.SchemaSource}");
        }

        _output.WriteLine(new string('=', 100));
        _output.WriteLine($"Total: {providers.Count} providers");

        Assert.True(providers.Count > 0, "Expected to find registered providers on the system");
    }

    [Fact]
    public void GetRegisteredProviderInfo_PrintsProvidersCSV()
    {
        var providers = EtwProviderValidator.GetRegisteredProviderInfo();

        foreach (var provider in providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"{provider.Name},{{{provider.Guid}}}");
        }

        Assert.True(providers.Count > 0, "Expected to find registered providers on the system");
    }

    /// <summary>
    /// Prints a summary of providers grouped by SchemaSource.
    /// </summary>
    [Fact]
    public void GetRegisteredProviderInfo_PrintsSchemaSourceSummary()
    {
        var providers = EtwProviderValidator.GetRegisteredProviderInfo();

        var grouped = providers
            .GroupBy(p => p.SchemaSource)
            .OrderBy(g => g.Key);

        _output.WriteLine("Providers by Schema Source:");
        _output.WriteLine(new string('=', 80));

        foreach (var group in grouped)
        {
            _output.WriteLine($"\nSchemaSource {group.Key} ({GetSchemaSourceDescription(group.Key)}): {group.Count()} providers");
            _output.WriteLine(new string('-', 40));
            
            foreach (var provider in group.OrderBy(p => p.Name).Take(10))
            {
                _output.WriteLine($"  - {provider.Name}");
            }
            
            if (group.Count() > 10)
            {
                _output.WriteLine($"  ... and {group.Count() - 10} more");
            }
        }
    }

    private static string GetSchemaSourceDescription(int schemaSource)
    {
        return schemaSource switch
        {
            0 => "XML manifest",
            1 => "WMI MOF",
            2 => "TraceLogging self-describing",
            _ => "Unknown"
        };
    }

    private static string TruncateName(string name, int maxLength)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        
        return name.Length <= maxLength ? name : name[..(maxLength - 3)] + "...";
    }
}
