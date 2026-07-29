namespace ETWSpyLib
{
    /// <summary>
    /// Correlates paired Begin/End (or Start/Stop) events and derives the elapsed time
    /// between them.
    /// </summary>
    /// <typeparam name="T">
    /// The caller's event representation. The matching end event instance is handed back so
    /// the caller can attach the derived duration to it.
    /// </typeparam>
    /// <remarks>
    /// Some providers - notably Chrome/Edge - emit an activity as two separate events that
    /// share an <c>Id</c> payload field: one with <c>Phase=Begin</c> and one with
    /// <c>Phase=End</c>, each carrying its own <c>Timestamp</c> payload field. The elapsed
    /// time is the difference between those two payload timestamps.
    ///
    /// ETW buffers events per CPU, so the two halves of a pair are frequently delivered out
    /// of order. This type therefore holds whichever half arrives first and completes the
    /// match when its counterpart shows up, rather than assuming Begin precedes End.
    ///
    /// The value produced here is <b>derived</b>, not decoded from the event payload. It is
    /// reported in the same raw units as the provider's <c>Timestamp</c> payload field so it
    /// can be compared directly against other tools.
    ///
    /// Instances are safe for use from multiple ETW callback threads.
    /// </remarks>
    public sealed class EventDurationCorrelator<T> where T : class
    {
        /// <summary>
        /// Payload field holding the activity phase.
        /// </summary>
        private const string PhaseField = "Phase";

        /// <summary>
        /// Payload field holding the activity correlation id.
        /// </summary>
        private const string IdField = "Id";

        /// <summary>
        /// Payload field holding the provider-supplied timestamp.
        /// </summary>
        private const string TimestampField = "Timestamp";

        /// <summary>
        /// Upper bound on unmatched events held for correlation. Prevents unbounded growth
        /// when one half of a pair is never observed.
        /// </summary>
        private const int MaxPending = 50000;

        private readonly record struct PendingEvent(ulong Timestamp, bool IsBegin, T Item);

        private readonly Dictionary<string, PendingEvent> _pending = new(StringComparer.Ordinal);
        private readonly object _sync = new();

        /// <summary>
        /// Processes an event and reports whether it completed a Begin/End pair.
        /// </summary>
        /// <param name="providerName">The provider name.</param>
        /// <param name="eventName">The event name.</param>
        /// <param name="processId">The originating process id.</param>
        /// <param name="payload">The decoded payload fields.</param>
        /// <param name="item">The caller's representation of this event.</param>
        /// <param name="endEvent">
        /// When a pair completes, the end half of that pair - which may be
        /// <paramref name="item"/> or a previously seen event.
        /// </param>
        /// <param name="duration">
        /// When a pair completes, the elapsed time in the provider's raw timestamp units.
        /// </param>
        /// <returns><c>true</c> when this event completed a pair; otherwise <c>false</c>.</returns>
        public bool TryCorrelate(
            string providerName,
            string eventName,
            uint processId,
            IReadOnlyDictionary<string, string> payload,
            T item,
            out T? endEvent,
            out ulong duration)
        {
            endEvent = null;
            duration = 0;

            if (payload == null || item == null)
            {
                return false;
            }

            if (!payload.TryGetValue(PhaseField, out var phase) ||
                !payload.TryGetValue(IdField, out var id) ||
                !payload.TryGetValue(TimestampField, out var timestampText) ||
                !ulong.TryParse(timestampText, out var timestamp))
            {
                return false;
            }

            bool isBegin = IsBeginPhase(phase);
            if (!isBegin && !IsEndPhase(phase))
            {
                return false;
            }

            string key = $"{providerName}\u0001{eventName}\u0001{processId}\u0001{id}";
            var current = new PendingEvent(timestamp, isBegin, item);

            lock (_sync)
            {
                if (_pending.TryGetValue(key, out var existing))
                {
                    // Two events of the same phase in a row cannot be paired. Keep the newer
                    // one so a later counterpart can still match.
                    if (existing.IsBegin == isBegin)
                    {
                        _pending[key] = current;
                        return false;
                    }

                    _pending.Remove(key);

                    var begin = isBegin ? current : existing;
                    var end = isBegin ? existing : current;

                    endEvent = end.Item;

                    // Use the absolute difference: ETW delivery order and per-CPU clock skew
                    // mean the end timestamp is not guaranteed to be the larger value.
                    duration = end.Timestamp >= begin.Timestamp
                        ? end.Timestamp - begin.Timestamp
                        : begin.Timestamp - end.Timestamp;

                    return true;
                }

                // Guard against unbounded growth from events that are never matched.
                if (_pending.Count < MaxPending)
                {
                    _pending[key] = current;
                }
            }

            return false;
        }

        /// <summary>
        /// Discards all events awaiting correlation.
        /// </summary>
        public void Reset()
        {
            lock (_sync)
            {
                _pending.Clear();
            }
        }

        private static bool IsBeginPhase(string phase) =>
            phase.Equals("Begin", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Start", StringComparison.OrdinalIgnoreCase);

        private static bool IsEndPhase(string phase) =>
            phase.Equals("End", StringComparison.OrdinalIgnoreCase) ||
            phase.Equals("Stop", StringComparison.OrdinalIgnoreCase);
    }
}
