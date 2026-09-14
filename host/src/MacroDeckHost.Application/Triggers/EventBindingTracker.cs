using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Application.Triggers;

public interface IEventBindingTracker
{
	IReadOnlyList<EventBindingDto> BindingsFor(string providerId);

	IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged);

	Task FlushAsync();
}

public sealed class EventBindingTracker : IEventBindingTracker, IDisposable
{
	private readonly IEventSubscriptionIndex _index;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, string> _delivered = new(StringComparer.Ordinal);
	private IReadOnlyList<Func<string, IReadOnlyList<EventBindingDto>, Task>> _listeners = [];
	private bool _pending;
	private bool _running;
	private Task _drain = Task.CompletedTask;

	public EventBindingTracker(IEventSubscriptionIndex index, ILogger logger)
	{
		_index = index;
		_logger = logger.ForContext<EventBindingTracker>();
		_index.Changed += OnIndexChanged;
	}

	public IReadOnlyList<EventBindingDto> BindingsFor(string providerId)
		=> [.. _index.FindByProvider(providerId).Select(ToDto)];

	public IDisposable Subscribe(Func<string, IReadOnlyList<EventBindingDto>, Task> onChanged)
	{
		lock (_gate)
		{
			_listeners = [.. _listeners, onChanged];
		}

		return new Subscription(this, onChanged);
	}

	public Task FlushAsync()
	{
		lock (_gate)
		{
			return _drain;
		}
	}

	public void Dispose() => _index.Changed -= OnIndexChanged;

	// Raised under the index's write lock, so nothing but the wake-up happens here.
	private void OnIndexChanged()
	{
		lock (_gate)
		{
			_pending = true;
			if (_running)
			{
				return;
			}

			_running = true;
			_drain = Task.Run(DrainAsync);
		}
	}

	private async Task DrainAsync()
	{
		while (true)
		{
			lock (_gate)
			{
				if (!_pending)
				{
					_running = false;
					return;
				}

				_pending = false;
			}

			try
			{
				await DeliverAsync().ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				_logger.Error(exception, "Delivering event binding changes failed");
			}
		}
	}

	private async Task DeliverAsync()
	{
		IReadOnlyList<Func<string, IReadOnlyList<EventBindingDto>, Task>> listeners;
		HashSet<string> providers;
		lock (_gate)
		{
			listeners = _listeners;
			providers = [.. _delivered.Keys];
		}

		providers.UnionWith(_index.ProviderIds());

		foreach (var providerId in providers)
		{
			var bindings = BindingsFor(providerId);
			var json = JsonSerializer.Serialize(bindings, PluginProtocolJson.Options);

			lock (_gate)
			{
				var previous = _delivered.GetValueOrDefault(providerId, "[]");
				if (string.Equals(previous, json, StringComparison.Ordinal))
				{
					continue;
				}

				if (bindings.Count == 0)
				{
					_delivered.Remove(providerId);
				}
				else
				{
					_delivered[providerId] = json;
				}
			}

			foreach (var listener in listeners)
			{
				try
				{
					await listener(providerId, bindings).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					_logger.Error(exception, "An event binding listener failed for provider {ProviderId}", providerId);
				}
			}
		}
	}

	private static EventBindingDto ToDto(EventSubscription subscription)
	{
		var localId = QualifiedId.TryParse(subscription.EventId, out var id) ? id.LocalId : subscription.EventId;
		return new EventBindingDto
		{
			EventId = localId,
			Parameters = subscription.Configuration.ToDictionary(pair => pair.Key,
				pair => new EventBindingValueDto
				{
					Value = pair.Value.Value.ValueKind == JsonValueKind.Undefined ? null : pair.Value.Value,
					Operator = pair.Value.Operator
				},
				StringComparer.Ordinal)
		};
	}

	private sealed class Subscription(
		EventBindingTracker owner,
		Func<string, IReadOnlyList<EventBindingDto>, Task> listener) : IDisposable
	{
		public void Dispose()
		{
			lock (owner._gate)
			{
				owner._listeners = [.. owner._listeners.Where(existing => !ReferenceEquals(existing, listener))];
			}
		}
	}
}
