using Microsoft.O365.Security.ETW;
using Moq;

namespace ETWSpyLib.Tests;

public class EtwEventFilterTests
{
    [Fact]
    public void Constructor_SetsEventId()
    {
        ushort expectedEventId = 1234;

        var filter = new EtwEventFilter(expectedEventId);

        Assert.Equal(expectedEventId, filter.EventId);
    }

    [Fact]
    public void Constructor_CreatesFilter()
    {
        var filter = new EtwEventFilter(100);

        Assert.NotNull(filter.Filter);
    }

    [Fact]
    public void Constructor_InitializesCallbacksEmpty()
    {
        var filter = new EtwEventFilter(100);

        Assert.Equal(0, filter.CallbackCount);
        Assert.Empty(filter.Callbacks);
    }

    [Fact]
    public void Constructor_InitializesPredicatesEmpty()
    {
        var filter = new EtwEventFilter(100);

        Assert.Equal(0, filter.PredicateCount);
    }

    [Fact]
    public void OnEvent_ThrowsOnNullCallback()
    {
        var filter = new EtwEventFilter(100);

        Assert.Throws<ArgumentNullException>(() => filter.OnEvent(null!));
    }

    [Fact]
    public void OnEvent_AddsCallback()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        filter.OnEvent(callback);

        Assert.Equal(1, filter.CallbackCount);
        Assert.Contains(callback, filter.Callbacks);
    }

    [Fact]
    public void OnEvent_CanAddMultipleCallbacks()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback1 = _ => { };
        Action<IEventRecord> callback2 = _ => { };

        filter.OnEvent(callback1);
        filter.OnEvent(callback2);

        Assert.Equal(2, filter.CallbackCount);
    }

    [Fact]
    public void Where_ThrowsOnNullPredicate()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        Assert.Throws<ArgumentNullException>(() => filter.Where(null!, callback));
    }

    [Fact]
    public void Where_ThrowsOnNullCallback()
    {
        var filter = new EtwEventFilter(100);
        Predicate<IEventRecord> predicate = _ => true;

        Assert.Throws<ArgumentNullException>(() => filter.Where(predicate, null!));
    }

    [Fact]
    public void Where_AddsPredicateCount()
    {
        var filter = new EtwEventFilter(100);
        Predicate<IEventRecord> predicate = _ => true;
        Action<IEventRecord> callback = _ => { };

        filter.Where(predicate, callback);

        Assert.Equal(1, filter.PredicateCount);
    }

    [Fact]
    public void WhereProcessId_AddsPredicateCount()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        filter.WhereProcessId(1234, callback);

        Assert.Equal(1, filter.PredicateCount);
    }

    [Fact]
    public void WhereThreadId_AddsPredicateCount()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        filter.WhereThreadId(5678, callback);

        Assert.Equal(1, filter.PredicateCount);
    }

    [Fact]
    public void WherePropertyContains_AddsPredicateCount()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        filter.WherePropertyContains("PropertyName", "SearchText", callback);

        Assert.Equal(1, filter.PredicateCount);
    }

    [Fact]
    public void WhereProperty_AddsPredicateCount()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };

        filter.WhereProperty("PropertyName", 42u, callback);

        Assert.Equal(1, filter.PredicateCount);
    }

    [Fact]
    public void Callbacks_ReturnsReadOnlyList()
    {
        var filter = new EtwEventFilter(100);
        Action<IEventRecord> callback = _ => { };
        filter.OnEvent(callback);

        var callbacks = filter.Callbacks;

        Assert.IsAssignableFrom<IReadOnlyList<Action<IEventRecord>>>(callbacks);
    }
}
