using Microsoft.O365.Security.ETW;

namespace ETWSpyLib.Tests;

public class TraceSessionErrorEventArgsTests
{
    [Fact]
    public void Constructor_SetsException()
    {
        var exception = new InvalidOperationException("Test error");

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.Same(exception, args.Exception);
    }

    [Fact]
    public void Constructor_WithRegularException_SetsMessage()
    {
        var expectedMessage = "Test error message";
        var exception = new InvalidOperationException(expectedMessage);

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.Equal(expectedMessage, args.Message);
    }

    [Fact]
    public void Constructor_WithRegularException_IsNoSessionsRemainingIsFalse()
    {
        var exception = new InvalidOperationException("Test error");

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.False(args.IsNoSessionsRemaining);
    }

    [Fact]
    public void Constructor_WithNoTraceSessionsRemaining_IsNoSessionsRemainingIsTrue()
    {
        var exception = new NoTraceSessionsRemaining();

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.True(args.IsNoSessionsRemaining);
    }

    [Fact]
    public void Constructor_WithNoTraceSessionsRemaining_SetsSpecialMessage()
    {
        var exception = new NoTraceSessionsRemaining();

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.Contains("No ETW trace sessions are available", args.Message);
        Assert.Contains("Windows has a limited number of trace sessions", args.Message);
    }

    [Fact]
    public void Constructor_WithArgumentNullException_SetsExceptionMessage()
    {
        var exception = new ArgumentNullException("paramName");

        var args = new TraceSessionErrorEventArgs(exception);

        Assert.Equal(exception.Message, args.Message);
        Assert.False(args.IsNoSessionsRemaining);
    }
}
