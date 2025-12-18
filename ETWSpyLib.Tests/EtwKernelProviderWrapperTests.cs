namespace ETWSpyLib.Tests;

public class EtwKernelProviderWrapperTests
{
    [Fact]
    public void ForProcessEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForProcessEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void ForThreadEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForThreadEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void ForImageLoadEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForImageLoadEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void ForNetworkEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForNetworkEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void ForRegistryEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForRegistryEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void ForFileIOEvents_CreatesProvider()
    {
        var provider = EtwKernelProviderWrapper.ForFileIOEvents();

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void Constructor_WithGuidAndFlags_CreatesProvider()
    {
        var guid = Guid.NewGuid();
        uint flags = 0x01;

        var provider = new EtwKernelProviderWrapper(guid, flags);

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void Constructor_WithFlags_CreatesProvider()
    {
        var provider = new EtwKernelProviderWrapper(KernelProviderFlags.Process | KernelProviderFlags.Thread);

        Assert.NotNull(provider);
        Assert.NotNull(provider.KernelProvider);
    }

    [Fact]
    public void AddFilter_AddsFilterToProvider()
    {
        var provider = EtwKernelProviderWrapper.ForProcessEvents();
        var filter = new EtwEventFilter(1);

        // Should not throw
        provider.AddFilter(filter);
    }
}

public class KernelProviderFlagsTests
{
    [Theory]
    [InlineData(KernelProviderFlags.Process, 0x00000001u)]
    [InlineData(KernelProviderFlags.Thread, 0x00000002u)]
    [InlineData(KernelProviderFlags.ImageLoad, 0x00000004u)]
    [InlineData(KernelProviderFlags.DiskIO, 0x00000100u)]
    [InlineData(KernelProviderFlags.NetworkTCPIP, 0x00010000u)]
    [InlineData(KernelProviderFlags.Registry, 0x00020000u)]
    [InlineData(KernelProviderFlags.FileIO, 0x02000000u)]
    public void FlagValues_AreCorrect(KernelProviderFlags flag, uint expectedValue)
    {
        Assert.Equal(expectedValue, (uint)flag);
    }

    [Fact]
    public void Flags_CanBeCombined()
    {
        var combined = KernelProviderFlags.Process | KernelProviderFlags.Thread | KernelProviderFlags.ImageLoad;

        Assert.True(combined.HasFlag(KernelProviderFlags.Process));
        Assert.True(combined.HasFlag(KernelProviderFlags.Thread));
        Assert.True(combined.HasFlag(KernelProviderFlags.ImageLoad));
        Assert.False(combined.HasFlag(KernelProviderFlags.DiskIO));
    }
}

public class KernelGuidsTests
{
    [Fact]
    public void SystemTraceControlGuid_IsValidGuid()
    {
        var guid = KernelGuids.SystemTraceControlGuid;

        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void SystemTraceControlGuid_HasExpectedValue()
    {
        var expectedGuid = new Guid("9e814aad-3204-11d2-9a82-006008a86939");

        Assert.Equal(expectedGuid, KernelGuids.SystemTraceControlGuid);
    }
}
