using ETWSpyLib;
using Microsoft.O365.Security.ETW;

Console.WriteLine("ETWSpy - Event Tracing for Windows Monitor");
Console.WriteLine("==========================================\n");

// Check if running as administrator (required for ETW)
if (!IsAdministrator())
{
    Console.WriteLine("ERROR: This application requires administrator privileges.");
    Console.WriteLine("Please run as administrator to capture ETW events.\n");
    return 1;
}

// Display menu
Console.WriteLine("Select monitoring mode:");
Console.WriteLine("1. Monitor DNS Client events (User-mode)");
Console.WriteLine("2. Monitor Process events (Kernel-mode)");
Console.WriteLine("3. Monitor File I/O events (Kernel-mode)");
Console.WriteLine("4. Monitor Network TCP/IP events (Kernel-mode)");
Console.WriteLine("5. Custom provider by GUID");
Console.WriteLine("6. Monitor Edge events (3A5F2396-5C8F-4F1F-9B67-6CCA6C990E61)");
Console.Write("\nEnter choice (1-6): ");

var choice = Console.ReadLine();
var cts = new CancellationTokenSource();

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n\nStopping trace session...");
    cts.Cancel();
};

try
{
    switch (choice)
    {
        case "1":
            await MonitorDnsEventsAsync(cts.Token);
            break;
        case "2":
            await MonitorProcessEventsAsync(cts.Token);
            break;
        case "3":
            await MonitorFileIOEventsAsync(cts.Token);
            break;
        case "4":
            await MonitorNetworkEventsAsync(cts.Token);
            break;
        case "5":
            await MonitorCustomProviderAsync(cts.Token);
            break;
        case "6":
            await MonitorEdgeProviderAsync(cts.Token);
            break;
        default:
            Console.WriteLine("Invalid choice.");
            return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    return 1;
}

Console.WriteLine("\nTrace session stopped. Press any key to exit...");
Console.ReadKey();
return 0;

// ==================== Monitoring Functions ====================

static async Task MonitorDnsEventsAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("\n=== Monitoring DNS Client Events ===");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateUserSession("ETWSpy-DNS-Session");

    // Create DNS Client provider
    var dnsProvider = new EtwProviderWrapper("Microsoft-Windows-DNS-Client");
    dnsProvider.EnableAllEvents();
    dnsProvider.SetTraceLevel(TraceLevel.Information);

    // Add a filter for DNS query events (Event ID 3008)
    var queryFilter = new EtwEventFilter(3008);
    queryFilter.OnEvent(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            var queryName = record.GetUnicodeString("QueryName", "");
            var queryType = record.GetUInt16("QueryType", 0);
            
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] DNS Query: {queryName} (Type: {queryType})");
        }
        catch
        {
            Console.WriteLine($"[{record.Timestamp.ToLocalTime():HH:mm:ss.fff}] DNS Event ID: {record.Id}");
        }
    });

    dnsProvider.AddFilter(queryFilter);
    session.EnableProvider(dnsProvider);

    await session.StartAsync(cancellationToken);
}

static async Task MonitorProcessEventsAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("\n=== Monitoring Process Events ===");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateKernelSession("ETWSpy-Process-Session");

    // Create kernel provider for process events
    var processProvider = EtwKernelProviderWrapper.ForProcessEvents();

    // Add filter for process start events
    var startFilter = new EtwEventFilter(1); // Process Start
    startFilter.OnEvent(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            var processId = record.GetUInt32("ProcessId", 0);
            var imageName = record.GetAnsiString("ImageFileName", "");
            
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] Process Started: PID={processId}, Name={imageName}");
        }
        catch
        {
            Console.WriteLine($"[{record.Timestamp.ToLocalTime():HH:mm:ss.fff}] Process Event ID: {record.Id}");
        }
    });

    // Add filter for process stop events
    var stopFilter = new EtwEventFilter(2); // Process Stop
    stopFilter.OnEvent(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            var processId = record.GetUInt32("ProcessId", 0);
            var imageName = record.GetAnsiString("ImageFileName", "");
            
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] Process Stopped: PID={processId}, Name={imageName}");
        }
        catch
        {
            Console.WriteLine($"[{record.Timestamp.ToLocalTime():HH:mm:ss.fff}] Process Event ID: {record.Id}");
        }
    });

    processProvider.AddFilter(startFilter);
    processProvider.AddFilter(stopFilter);
    session.EnableKernelProvider(processProvider);

    await session.StartAsync(cancellationToken);
}

static async Task MonitorFileIOEventsAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("\n=== Monitoring File I/O Events ===");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateKernelSession("ETWSpy-FileIO-Session");

    var fileIOProvider = EtwKernelProviderWrapper.ForFileIOEvents();

    // Filter for file create/open events
    var createFilter = new EtwEventFilter(64); // FileIO Create
    createFilter.OnEvent(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            var fileName = record.GetUnicodeString("FileName", "");
            
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] File Opened: {fileName}");
            }
        }
        catch
        {
            // Silently skip errors
        }
    });

    fileIOProvider.AddFilter(createFilter);
    session.EnableKernelProvider(fileIOProvider);

    await session.StartAsync(cancellationToken);
}

static async Task MonitorNetworkEventsAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("\n=== Monitoring Network TCP/IP Events ===");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateKernelSession("ETWSpy-Network-Session");

    var networkProvider = EtwKernelProviderWrapper.ForNetworkEvents();

    // TCP connection filter
    var tcpFilter = new EtwEventFilter(10); // TCP Connect
    tcpFilter.OnEvent(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            var processId = record.GetUInt32("PID", 0);
            
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] TCP Connection: PID={processId}");
        }
        catch
        {
            Console.WriteLine($"[{record.Timestamp.ToLocalTime():HH:mm:ss.fff}] Network Event ID: {record.Id}");
        }
    });

    networkProvider.AddFilter(tcpFilter);
    session.EnableKernelProvider(networkProvider);

    await session.StartAsync(cancellationToken);
}

static async Task MonitorCustomProviderAsync(CancellationToken cancellationToken)
{
    Console.Write("\nEnter provider GUID (e.g., 22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716): ");
    var guidInput = Console.ReadLine();

    if (!Guid.TryParse(guidInput, out var providerGuid))
    {
        Console.WriteLine("Invalid GUID format.");
        return;
    }

    Console.Write("Enter event ID to filter (0 for all events): ");
    var eventIdInput = Console.ReadLine();

    Console.WriteLine($"\n=== Monitoring Provider {providerGuid} ===");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateUserSession("ETWSpy-Custom-Session");

    var provider = new EtwProviderWrapper(providerGuid);
    provider.EnableAllEvents();
    provider.SetTraceLevel(TraceLevel.Verbose);

    if (ushort.TryParse(eventIdInput, out var eventId) && eventId > 0)
    {
        var filter = new EtwEventFilter(eventId);
        filter.OnEvent(record =>
        {
            var timestamp = record.Timestamp.ToLocalTime();
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] Event ID: {record.Id}, Provider: {record.ProviderId}");
        });
        provider.AddFilter(filter);
    }
    else
    {
        // No specific filter - all events will be captured
        Console.WriteLine("Monitoring all events from provider...");
    }

    session.EnableProvider(provider);
    await session.StartAsync(cancellationToken);
}

static async Task MonitorEdgeProviderAsync(CancellationToken cancellationToken)
{
    var providerGuid = new Guid("3A5F2396-5C8F-4F1F-9B67-6CCA6C990E61");
    
    Console.WriteLine($"\n=== Monitoring Edge Provider ({providerGuid}) ===");
    Console.WriteLine("Dumping all events with full payload data...");
    Console.WriteLine("Press Ctrl+C to stop...\n");

    using var session = EtwTraceSession.CreateUserSession("ETWSpy-Edge-Session");

    var provider = new EtwProviderWrapper(providerGuid);
    provider.EnableAllEvents();
    provider.SetTraceLevel(TraceLevel.Verbose);

    // Register callback to receive ALL events and dump their payloads
    provider.OnAllEvents((IEventRecordDelegate)(record =>
    {
        try
        {
            var timestamp = record.Timestamp.ToLocalTime();
            
            Console.WriteLine($"\n============================================================");
            Console.WriteLine($"[{timestamp:HH:mm:ss.fff}] Event ID: {record.Id}");
            Console.WriteLine($"  Event Name: {record.Name ?? "N/A"}");
            Console.WriteLine($"  Task Name: {record.TaskName ?? "N/A"}");
            Console.WriteLine($"  Provider: {record.ProviderId}");
            Console.WriteLine($"  Provider Name: {record.ProviderName ?? "N/A"}");
            Console.WriteLine($"  Process ID: {record.ProcessId}");
            Console.WriteLine($"  Thread ID: {record.ThreadId}");
            
            // Dump all event payload properties
            Console.WriteLine("  Payload Properties:");
            DumpEventPayload(record);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{record.Timestamp.ToLocalTime():HH:mm:ss.fff}] Event ID: {record.Id} (Error reading payload: {ex.Message})");
        }
    }));

    session.EnableProvider(provider);
    await session.StartAsync(cancellationToken);
}

static void DumpEventPayload(IEventRecord record)
{
    var properties = EtwPropertyFormatter.GetFormattedProperties(record);
    
    if (properties.Count == 0)
    {
        Console.WriteLine("    (No properties found in payload)");
        return;
    }

    foreach (var kvp in properties)
    {
        Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
    }
}

static bool IsAdministrator()
{
    try
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}
