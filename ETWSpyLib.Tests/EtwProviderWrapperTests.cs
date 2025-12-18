using Microsoft.O365.Security.ETW;

namespace ETWSpyLib.Tests;

public class EtwProviderWrapperTests
{
    // Use a GUID instead of a name to avoid SEHException when provider doesn't exist
    private static readonly Guid TestProviderGuid = Guid.Parse("3A5F2396-5C8F-4F1F-9B67-6CCA6C990E61");

    [Fact]
    public void Constructor_WithName_SetsName()
    {
        var providerName = "Microsoft-Windows-DNS-Client";

        using var wrapper = new EtwProviderWrapper(providerName);

        Assert.Equal(providerName, wrapper.Name);
        Assert.Null(wrapper.Guid);
    }

    [Fact]
    public void Constructor_WithName_CreatesProvider()
    {
        // Use a known Windows provider name
        using var wrapper = new EtwProviderWrapper("Microsoft-Windows-DNS-Client");

        Assert.NotNull(wrapper.Provider);
    }

    [Fact]
    public void Constructor_WithGuid_SetsGuid()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        Assert.Equal(TestProviderGuid, wrapper.Guid);
        Assert.Null(wrapper.Name);
    }

    [Fact]
    public void Constructor_WithGuid_CreatesProvider()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        Assert.NotNull(wrapper.Provider);
    }

    [Fact]
    public void AddFilter_ThrowsOnNull()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        Assert.Throws<ArgumentNullException>(() => wrapper.AddFilter(null!));
    }

    [Fact]
    public void AddFilter_AddsToFilters()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        var filter = new EtwEventFilter(100);

        wrapper.AddFilter(filter);

        Assert.Equal(1, wrapper.FilterCount);
        Assert.Contains(filter, wrapper.Filters);
    }

    [Fact]
    public void AddFilter_CanAddMultiple()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        var filter1 = new EtwEventFilter(100);
        var filter2 = new EtwEventFilter(200);

        wrapper.AddFilter(filter1);
        wrapper.AddFilter(filter2);

        Assert.Equal(2, wrapper.FilterCount);
    }

    [Fact]
    public void RemoveFilter_ThrowsOnNull()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        Assert.Throws<ArgumentNullException>(() => wrapper.RemoveFilter(null!));
    }

    [Fact]
    public void RemoveFilter_RemovesFilter()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        var filter = new EtwEventFilter(100);
        wrapper.AddFilter(filter);

        var result = wrapper.RemoveFilter(filter);

        Assert.True(result);
        Assert.Equal(0, wrapper.FilterCount);
    }

    [Fact]
    public void RemoveFilter_ReturnsFalseIfNotFound()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        var filter = new EtwEventFilter(100);

        var result = wrapper.RemoveFilter(filter);

        Assert.False(result);
    }

    [Fact]
    public void ClearFilters_RemovesAllFilters()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        wrapper.AddFilter(new EtwEventFilter(100));
        wrapper.AddFilter(new EtwEventFilter(200));

        wrapper.ClearFilters();

        Assert.Equal(0, wrapper.FilterCount);
    }

    [Fact]
    public void AddEventFilter_CreatesAndAddsFilter()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        var filter = wrapper.AddEventFilter(100);

        Assert.NotNull(filter);
        Assert.Equal(100, filter.EventId);
        Assert.Equal(1, wrapper.FilterCount);
    }

    [Fact]
    public void AddEventFilter_WithCallback_RegistersCallback()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        Action<IEventRecord> callback = _ => { };

        var filter = wrapper.AddEventFilter(100, callback);

        Assert.Equal(1, filter.CallbackCount);
    }

    [Fact]
    public void AddEventFilter_WithoutCallback_NoCallback()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        var filter = wrapper.AddEventFilter(100);

        Assert.Equal(0, filter.CallbackCount);
    }

    [Fact]
    public void OnAllEvents_ThrowsOnNull()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        Assert.Throws<ArgumentNullException>(() => wrapper.OnAllEvents(null!));
    }

    [Fact]
    public void SetTraceLevel_DoesNotThrow()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        // SetTraceLevel should not throw - we can't verify the value
        // because Provider.Level is a write-only property
        wrapper.SetTraceLevel(TraceLevel.Verbose);
    }

    [Theory]
    [InlineData(TraceLevel.Critical)]
    [InlineData(TraceLevel.Error)]
    [InlineData(TraceLevel.Warning)]
    [InlineData(TraceLevel.Information)]
    [InlineData(TraceLevel.Verbose)]
    public void SetTraceLevel_AllLevelsDoNotThrow(TraceLevel level)
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        // Should not throw for any trace level
        wrapper.SetTraceLevel(level);
    }

    [Fact]
    public void SetKeywords_DoesNotThrow()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        ulong keywords = 0x1234567890ABCDEF;

        // SetKeywords should not throw - we can't verify the value
        // because Provider.Any is a write-only property
        wrapper.SetKeywords(keywords);
    }

    [Fact]
    public void EnableAllEvents_DoesNotThrow()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);

        // EnableAllEvents should not throw
        wrapper.EnableAllEvents();
    }

    [Fact]
    public void SetTraceFlagsRaw_DoesNotThrow()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        byte flags = 0x05;

        // SetTraceFlagsRaw should not throw - we can't verify the value
        // because Provider.TraceFlags is a write-only property
        wrapper.SetTraceFlagsRaw(flags);
    }

    [Fact]
    public void Filters_ReturnsReadOnlyList()
    {
        using var wrapper = new EtwProviderWrapper(TestProviderGuid);
        wrapper.AddFilter(new EtwEventFilter(100));

        var filters = wrapper.Filters;

        Assert.IsAssignableFrom<IReadOnlyList<EtwEventFilter>>(filters);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var wrapper = new EtwProviderWrapper(TestProviderGuid);

        wrapper.Dispose();
        wrapper.Dispose();

        // Should not throw
    }

    [Fact]
    public void Dispose_ClearsFilters()
    {
        var wrapper = new EtwProviderWrapper(TestProviderGuid);
        wrapper.AddFilter(new EtwEventFilter(100));

        wrapper.Dispose();

        Assert.Equal(0, wrapper.FilterCount);
    }
}
