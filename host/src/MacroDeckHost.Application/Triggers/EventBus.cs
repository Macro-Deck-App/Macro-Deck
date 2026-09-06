using System.Threading.Channels;
using Serilog;

namespace MacroDeckHost.Application.Triggers;

public interface IEventBus
{
	void Publish(EventOccurrence occurrence);

	ChannelReader<EventOccurrence> Reader { get; }
}

public sealed class EventBus : IEventBus
{
	private const int Capacity = 512;

	private static readonly TimeSpan _dropLogInterval = TimeSpan.FromMinutes(1);

	private readonly Channel<EventOccurrence> _channel = Channel.CreateBounded<EventOccurrence>(
		new BoundedChannelOptions(Capacity)
		{
			SingleReader = true,
			FullMode = BoundedChannelFullMode.DropWrite
		});

	private readonly IEventSubscriptionIndex _index;
	private readonly EventSampleStore _samples;
	private readonly ILogger _logger;
	private readonly Lock _dropLock = new();

	private long _dropped;
	private DateTimeOffset _lastDropLog = DateTimeOffset.MinValue;

	public EventBus(IEventSubscriptionIndex index, EventSampleStore samples, ILogger logger)
	{
		_index = index;
		_samples = samples;
		_logger = logger.ForContext<EventBus>();
	}

	public ChannelReader<EventOccurrence> Reader => _channel.Reader;

	public void Publish(EventOccurrence occurrence)
	{
		_samples.Record(occurrence);

		if (occurrence.Target is null && !_index.HasSubscribers(occurrence.EventId))
		{
			return;
		}

		if (!_channel.Writer.TryWrite(occurrence))
		{
			RecordDrop(occurrence.EventId);
		}
	}

	private void RecordDrop(string eventId)
	{
		long total;
		var shouldLog = false;

		lock (_dropLock)
		{
			total = ++_dropped;
			var now = DateTimeOffset.UtcNow;
			if (now - _lastDropLog >= _dropLogInterval)
			{
				_lastDropLog = now;
				shouldLog = true;
			}
		}

		if (shouldLog)
		{
			_logger.Warning(
				"Event queue is full; dropped an occurrence of {EventId} ({DroppedTotal} dropped in total). " +
				"A triggered flow is taking longer than events are arriving",
				eventId,
				total);
		}
	}
}
