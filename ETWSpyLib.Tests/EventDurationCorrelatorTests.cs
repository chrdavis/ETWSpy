namespace ETWSpyLib.Tests;

public class EventDurationCorrelatorTests
{
    private const string Provider = "Google.Chrome";
    private const string EventName = "Startup.FirstWebContents.NonEmptyPaint3";

    /// <summary>
    /// Stand-in for the caller's event type.
    /// </summary>
    private sealed class TestEvent
    {
        public required string Label { get; init; }
    }

    private static Dictionary<string, string> Payload(string phase, string id, string timestamp) =>
        new()
        {
            ["Phase"] = phase,
            ["Id"] = id,
            ["Timestamp"] = timestamp
        };

    private static EventDurationCorrelator<TestEvent> CreateCorrelator() => new();

    [Fact]
    public void TryCorrelate_FirstEventOfPair_DoesNotMatch()
    {
        var correlator = CreateCorrelator();

        bool matched = correlator.TryCorrelate(
            Provider, EventName, 100, Payload("Begin", "42", "455879265"),
            new TestEvent { Label = "begin" }, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryCorrelate_BeginThenEnd_ReturnsElapsedDurationOnEndEvent()
    {
        var correlator = CreateCorrelator();
        var begin = new TestEvent { Label = "begin" };
        var end = new TestEvent { Label = "end" };

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "455879265"), begin, out _, out _);
        bool matched = correlator.TryCorrelate(
            Provider, EventName, 100, Payload("End", "42", "457166231"), end, out var endEvent, out var duration);

        Assert.True(matched);
        Assert.Same(end, endEvent);
        Assert.Equal(1286966UL, duration);
    }

    [Fact]
    public void TryCorrelate_EndDeliveredBeforeBegin_StillMatchesAndTargetsEndEvent()
    {
        var correlator = CreateCorrelator();
        var end = new TestEvent { Label = "end" };
        var begin = new TestEvent { Label = "begin" };

        // ETW buffers per CPU, so the End half can arrive first.
        correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "457166231"), end, out _, out _);
        bool matched = correlator.TryCorrelate(
            Provider, EventName, 100, Payload("Begin", "42", "455879265"), begin, out var endEvent, out var duration);

        Assert.True(matched);
        Assert.Same(end, endEvent);
        Assert.Equal(1286966UL, duration);
    }

    [Fact]
    public void TryCorrelate_EndTimestampLowerThanBegin_UsesAbsoluteDifference()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "526380505963"),
            new TestEvent { Label = "begin" }, out _, out _);
        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "526380059548"),
            new TestEvent { Label = "end" }, out _, out var duration);

        Assert.True(matched);
        Assert.Equal(446415UL, duration);
    }

    [Fact]
    public void TryCorrelate_DifferentId_DoesNotMatch()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "455879265"),
            new TestEvent { Label = "begin" }, out _, out _);
        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "99", "457166231"),
            new TestEvent { Label = "end" }, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryCorrelate_DifferentProcess_DoesNotMatch()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "455879265"),
            new TestEvent { Label = "begin" }, out _, out _);
        bool matched = correlator.TryCorrelate(Provider, EventName, 200, Payload("End", "42", "457166231"),
            new TestEvent { Label = "end" }, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryCorrelate_StartStopPhases_Match()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Start", "7", "1000"),
            new TestEvent { Label = "start" }, out _, out _);
        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("Stop", "7", "2500"),
            new TestEvent { Label = "stop" }, out _, out var duration);

        Assert.True(matched);
        Assert.Equal(1500UL, duration);
    }

    [Fact]
    public void TryCorrelate_PairConsumedOnlyOnce()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "1000"),
            new TestEvent { Label = "begin" }, out _, out _);
        bool first = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "2000"),
            new TestEvent { Label = "end1" }, out _, out var duration);
        bool second = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "3000"),
            new TestEvent { Label = "end2" }, out _, out _);

        Assert.True(first);
        Assert.Equal(1000UL, duration);
        Assert.False(second);
    }

    [Fact]
    public void TryCorrelate_RepeatedSamePhase_KeepsMostRecentForLaterMatch()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "1000"),
            new TestEvent { Label = "begin1" }, out _, out _);
        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "1500"),
            new TestEvent { Label = "begin2" }, out _, out _);
        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "2000"),
            new TestEvent { Label = "end" }, out _, out var duration);

        Assert.True(matched);
        Assert.Equal(500UL, duration);
    }

    [Fact]
    public void TryCorrelate_PayloadMissingCorrelationFields_DoesNotMatch()
    {
        var correlator = CreateCorrelator();

        bool matched = correlator.TryCorrelate(Provider, EventName, 100,
            new Dictionary<string, string> { ["SomeOtherField"] = "value" },
            new TestEvent { Label = "x" }, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryCorrelate_UnrelatedPhaseValue_DoesNotMatch()
    {
        var correlator = CreateCorrelator();

        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("Instant", "42", "1000"),
            new TestEvent { Label = "x" }, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void Reset_DiscardsPendingEvents()
    {
        var correlator = CreateCorrelator();

        correlator.TryCorrelate(Provider, EventName, 100, Payload("Begin", "42", "1000"),
            new TestEvent { Label = "begin" }, out _, out _);
        correlator.Reset();
        bool matched = correlator.TryCorrelate(Provider, EventName, 100, Payload("End", "42", "2000"),
            new TestEvent { Label = "end" }, out _, out _);

        Assert.False(matched);
    }
}
