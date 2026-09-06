using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// Queues events for <see cref="MacroDeckLogShipper" /> to drain and forward to the host. Runs on
/// Serilog's synchronous dispatch path, so every guarantee here is about what <see cref="Emit" /> must
/// never do: block, allocate a task, throw past <see cref="OutOfMemoryException" />, or recurse into
/// itself if something on the emit path logs.
///
/// <para>
/// Two bounded channels, not one: <see cref="LogEventLevel.Warning" /> and above go to
/// <c>_priority</c>, everything else to <c>_bulk</c>. A single queue cannot express priority dropping -
/// an <see cref="LogEventLevel.Information" /> flood filling it would evict or block behind a queued
/// <see cref="LogEventLevel.Error" /> exactly as often as it evicted anything else. Splitting the queue
/// means the flood can only ever fill <c>_bulk</c>; an error already queued, or arriving after, always
/// has room in <c>_priority</c>. This governs only what gets <em>dropped</em> under pressure - see
/// <see cref="Drain" /> for why it is never allowed to also decide wire order.
/// </para>
/// </summary>
internal sealed class MacroDeckLogSink : ILogEventSink
{
	/// <summary>Roughly a quarter of the configured capacity goes to the priority queue - generous for
	/// how rarely warnings and above should occur relative to everything else, while still leaving the
	/// bulk queue the large majority of the budget.</summary>
	private const double PriorityShare = 0.25;

	private readonly Channel<LogEvent> _priority;
	private readonly Channel<LogEvent> _bulk;

	private readonly Channel<byte> _flushRequested = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
		{ FullMode = BoundedChannelFullMode.DropWrite });

	private readonly int _batchSizeHint;
	private long _priorityDropped;
	private long _bulkDropped;
	private int _pendingSinceSignal;

	[ThreadStatic]
	private static bool _inEmit;

	public MacroDeckLogSink(IOptions<MacroDeckLoggingOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var capacity = Math.Max(2, options.Value.QueueCapacity);
		var priorityCapacity = Math.Max(1, (int)(capacity * PriorityShare));
		var bulkCapacity = Math.Max(1, capacity - priorityCapacity);
		_batchSizeHint = Math.Max(1, options.Value.BatchSize);

		_priority = Channel.CreateBounded<LogEvent>(BoundedOptions(priorityCapacity));
		_bulk = Channel.CreateBounded<LogEvent>(BoundedOptions(bulkCapacity));
	}

	/// <summary>Signalled once enough events have queued up to be worth a batch, so
	/// <see cref="MacroDeckLogShipper" /> does not have to wait out the full flush interval for a burst.
	/// A heuristic, not an exact count - see <see cref="MacroDeckLogShipper" />'s remarks.</summary>
	internal ChannelReader<byte> FlushRequested => _flushRequested.Reader;

	public void Emit(LogEvent? logEvent)
	{
		if (logEvent is null || _inEmit)
		{
			return;
		}

		_inEmit = true;

		try
		{
			var priority = IsPriority(logEvent.Level);
			var queue = priority ? _priority : _bulk;

			if (queue.Writer.TryWrite(logEvent))
			{
				if (Interlocked.Increment(ref _pendingSinceSignal) >= _batchSizeHint)
				{
					Volatile.Write(ref _pendingSinceSignal, 0);
					_flushRequested.Writer.TryWrite(0);
				}
			}
			else if (priority)
			{
				Interlocked.Increment(ref _priorityDropped);
			}
			else
			{
				Interlocked.Increment(ref _bulkDropped);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// Never through ILogger or Log - either would re-enter this exact method through whatever
			// sink the author's own pipeline includes. SelfLog is Serilog's own escape hatch for
			// exactly this: diagnostics about the logging pipeline that must never themselves become
			// log events.
			SelfLog.WriteLine("MacroDeckLogSink: failed to queue an event: {0}", exception);
		}
		finally
		{
			_inEmit = false;
		}
	}

	/// <summary>
	/// Drains up to <paramref name="maxTotal" /> queued events, merged by timestamp so the batch stays
	/// in the order the events were logged in - never priority-first. Priority-first draining would put
	/// a warning ahead of an information line logged earlier, and since the host writes a batch in
	/// arrival order and the log viewer approximates time order by file order, that would show up as a
	/// plugin's own lines rendering out of sequence. The channel split exists only to decide what gets
	/// dropped under pressure, not to reorder what survives.
	/// </summary>
	internal MacroDeckLogDrainResult Drain(int maxTotal)
	{
		var dropped = TakeDropped();

		if (maxTotal <= 0)
		{
			return new MacroDeckLogDrainResult([], dropped);
		}

		var priorityEvents = new List<LogEvent>();
		while (priorityEvents.Count < maxTotal && _priority.Reader.TryRead(out var priorityEvent))
		{
			priorityEvents.Add(priorityEvent);
		}

		var bulkBudget = maxTotal - priorityEvents.Count;
		var bulkEvents = new List<LogEvent>();
		while (bulkEvents.Count < bulkBudget && _bulk.Reader.TryRead(out var bulkEvent))
		{
			bulkEvents.Add(bulkEvent);
		}

		return new MacroDeckLogDrainResult(Merge(priorityEvents, bulkEvents), dropped);
	}

	private int TakeDropped()
	{
		var total = Interlocked.Exchange(ref _priorityDropped, 0) + Interlocked.Exchange(ref _bulkDropped, 0);
		return (int)Math.Min(total, int.MaxValue);
	}

	private static bool IsPriority(LogEventLevel level)
		=> level is LogEventLevel.Warning or LogEventLevel.Error or LogEventLevel.Fatal;

	/// <summary>Two-way merge of two already time-ordered lists. Each channel preserves its own write
	/// order, and a <see cref="LogEvent" /> is timestamped by Serilog before it ever reaches this sink,
	/// so both inputs are already ascending by <see cref="LogEvent.Timestamp" />.</summary>
	private static List<LogEvent> Merge(List<LogEvent> a, List<LogEvent> b)
	{
		var merged = new List<LogEvent>(a.Count + b.Count);
		int i = 0, j = 0;

		while (i < a.Count && j < b.Count)
		{
			merged.Add(a[i].Timestamp <= b[j].Timestamp ? a[i++] : b[j++]);
		}

		while (i < a.Count)
		{
			merged.Add(a[i++]);
		}

		while (j < b.Count)
		{
			merged.Add(b[j++]);
		}

		return merged;
	}

	private static BoundedChannelOptions BoundedOptions(int capacity) => new(capacity)
	{
		// DropWrite, not Wait: Emit must never block the caller, and not a drop-oldest ring buffer
		// either - dropping the newest write under pressure is what keeps a queue that is already
		// full from doing extra work discarding what it already holds just to make room.
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true
	};
}

/// <summary>One drain of <see cref="MacroDeckLogSink" />: the events to ship, and how many more were
/// dropped by queue pressure since the previous drain.</summary>
internal readonly record struct MacroDeckLogDrainResult(IReadOnlyList<LogEvent> Events, int Dropped);
