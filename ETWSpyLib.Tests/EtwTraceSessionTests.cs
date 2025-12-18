namespace ETWSpyLib.Tests;

public class EtwTraceSessionTests
{
    // Use a GUID instead of a name to avoid SEHException when provider doesn't exist
    private static readonly Guid TestProviderGuid = Guid.Parse("3A5F2396-5C8F-4F1F-9B67-6CCA6C990E61");

    [Fact]
    public void CreateUserSession_CreatesSession()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";

        using var session = EtwTraceSession.CreateUserSession(sessionName);

        Assert.NotNull(session);
        Assert.Equal(sessionName, session.SessionName);
    }

    [Fact]
    public void CreateUserSession_IsNotRunningInitially()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";

        using var session = EtwTraceSession.CreateUserSession(sessionName);

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void CreateKernelSession_CreatesSession()
    {
        var sessionName = $"TestKernelSession_{Guid.NewGuid():N}";

        using var session = EtwTraceSession.CreateKernelSession(sessionName);

        Assert.NotNull(session);
        Assert.Equal(sessionName, session.SessionName);
    }

    [Fact]
    public void CreateKernelSession_IsNotRunningInitially()
    {
        var sessionName = $"TestKernelSession_{Guid.NewGuid():N}";

        using var session = EtwTraceSession.CreateKernelSession(sessionName);

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Stop_CanBeCalledOnNonRunningSession()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateUserSession(sessionName);

        // Should not throw
        session.Stop();

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        var session = EtwTraceSession.CreateUserSession(sessionName);

        session.Dispose();
        session.Dispose();

        // Should not throw
    }

    [Fact]
    public void EnableProvider_OnUserSession_DoesNotThrow()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateUserSession(sessionName);
        using var provider = new EtwProviderWrapper(TestProviderGuid);

        // Should not throw
        session.EnableProvider(provider);
    }

    [Fact]
    public void EnableProvider_OnKernelSession_ThrowsInvalidOperation()
    {
        var sessionName = $"TestKernelSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateKernelSession(sessionName);
        using var provider = new EtwProviderWrapper(TestProviderGuid);

        Assert.Throws<InvalidOperationException>(() => session.EnableProvider(provider));
    }

    [Fact]
    public void EnableKernelProvider_OnUserSession_ThrowsInvalidOperation()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateUserSession(sessionName);
        var kernelProvider = EtwKernelProviderWrapper.ForProcessEvents();

        Assert.Throws<InvalidOperationException>(() => session.EnableKernelProvider(kernelProvider));
    }

    [Fact]
    public void EnableKernelProvider_OnKernelSession_DoesNotThrow()
    {
        var sessionName = $"TestKernelSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateKernelSession(sessionName);
        var kernelProvider = EtwKernelProviderWrapper.ForProcessEvents();

        // Should not throw
        session.EnableKernelProvider(kernelProvider);
    }

    [Fact]
    public void ErrorOccurred_CanSubscribe()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateUserSession(sessionName);
        var eventRaised = false;

        session.ErrorOccurred += (_, _) => eventRaised = true;

        // Event subscription should work (event won't be raised in this test)
        Assert.False(eventRaised);
    }

    [Fact]
    public void ErrorOccurred_CanUnsubscribe()
    {
        var sessionName = $"TestSession_{Guid.NewGuid():N}";
        using var session = EtwTraceSession.CreateUserSession(sessionName);
        EventHandler<TraceSessionErrorEventArgs> handler = (_, _) => { };

        session.ErrorOccurred += handler;
        session.ErrorOccurred -= handler;

        // Should not throw
    }
}
