using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Application.Triggers;

public sealed class IntegrationEventPublisher : IEventPublisher
{
	private readonly string _integrationId;
	private readonly IEventBus _bus;
	private readonly IEventBindingTracker? _tracker;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private Action? _bindingsChanged;
	private IDisposable? _subscription;

	public IntegrationEventPublisher(string integrationId, IEventBus bus, ILogger logger)
		: this(integrationId, bus, null, logger)
	{
	}

	public IntegrationEventPublisher(string integrationId, IEventBus bus, IEventBindingTracker? tracker, ILogger logger)
	{
		_integrationId = integrationId;
		_bus = bus;
		_tracker = tracker;
		_logger = logger.ForContext<IntegrationEventPublisher>();
	}

	public event Action? BindingsChanged
	{
		add
		{
			lock (_gate)
			{
				_bindingsChanged += value;
				_subscription ??= _tracker?.Subscribe(OnTrackedChange);
			}
		}
		remove
		{
			lock (_gate)
			{
				_bindingsChanged -= value;
			}
		}
	}

	public IReadOnlyList<EventBinding> GetBindings()
		=> _tracker is null ? [] : [.. _tracker.BindingsFor(_integrationId).Select(dto => dto.ToBinding())];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		if (string.IsNullOrWhiteSpace(eventId))
		{
			return;
		}

		if (!QualifiedId.TryCreate(_integrationId, eventId, out var id))
		{
			_logger.Warning(
				"Integration {IntegrationId} published an invalid event id {EventId}; dropping the occurrence",
				_integrationId,
				eventId);
			return;
		}

		_bus.Publish(new EventOccurrence(id.ToString(), parameters ?? EventOccurrence.NoParameters));
	}

	private void RaiseBindingsChanged()
	{
		try
		{
			_bindingsChanged?.Invoke();
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "A BindingsChanged handler of integration {IntegrationId} threw", _integrationId);
		}
	}

	private Task OnTrackedChange(string providerId, IReadOnlyList<EventBindingDto> bindings)
	{
		if (string.Equals(providerId, _integrationId, StringComparison.Ordinal))
		{
			// Off the tracker's drain, so a slow handler cannot hold up delivery to anyone else.
			_ = Task.Run(RaiseBindingsChanged);
		}

		return Task.CompletedTask;
	}
}
