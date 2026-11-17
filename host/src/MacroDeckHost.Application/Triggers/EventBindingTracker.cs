using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
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
	private readonly IServiceScopeFactory _scopes;
	private readonly IVariableChangeNotifier _variables;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, string> _delivered = new(StringComparer.Ordinal);
	private IReadOnlyList<Func<string, IReadOnlyList<EventBindingDto>, Task>> _listeners = [];
	private Dictionary<ResolvedKey, JsonElement> _resolved = [];
	private HashSet<string> _referencedNames = new(StringComparer.Ordinal);
	private bool _pending;
	private bool _running;
	private Task _drain = Task.CompletedTask;

	public EventBindingTracker(
		IEventSubscriptionIndex index,
		IServiceScopeFactory scopes,
		IVariableChangeNotifier variables,
		ILogger logger)
	{
		_index = index;
		_scopes = scopes;
		_variables = variables;
		_logger = logger.ForContext<EventBindingTracker>();
		_index.Changed += OnIndexChanged;
		_variables.Changed += OnVariableChanged;
	}

	public IReadOnlyList<EventBindingDto> BindingsFor(string providerId)
	{
		IReadOnlyDictionary<ResolvedKey, JsonElement> resolved;
		lock (_gate)
		{
			resolved = _resolved;
		}

		return [.. _index.FindByProvider(providerId).Select(subscription => ToDto(subscription, resolved))];
	}

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

	public void Dispose()
	{
		_index.Changed -= OnIndexChanged;
		_variables.Changed -= OnVariableChanged;
	}

	// Raised under the index's write lock, so nothing but the wake-up happens here.
	private void OnIndexChanged()
	{
		lock (_gate)
		{
			Wake();
		}
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		lock (_gate)
		{
			if (_referencedNames.Contains(args.Name))
			{
				Wake();
			}
		}
	}

	private void Wake()
	{
		_pending = true;
		if (_running)
		{
			return;
		}

		_running = true;
		_drain = Task.Run(DrainAsync);
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
		try
		{
			await ResolveReferencesAsync(providers).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.Error(exception, "Resolving variables bound to event triggers failed");
		}

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

	private async Task ResolveReferencesAsync(IEnumerable<string> providers)
	{
		var references = providers
			.SelectMany(_index.FindByProvider)
			.SelectMany(subscription => subscription.Configuration.Values
				.Select(value => ReferenceKey(subscription.Owner, value.Value)))
			.OfType<ResolvedKey>()
			.Distinct()
			.ToList();

		var resolved = new Dictionary<ResolvedKey, JsonElement>();
		if (references.Count > 0)
		{
			await using var scope = _scopes.CreateAsyncScope();
			var renderer = scope.ServiceProvider.GetRequiredService<IVariableTemplateRenderer>();
			var evaluator = scope.ServiceProvider.GetRequiredService<IActionConditionEvaluator>();

			foreach (var owner in references.GroupBy(key => (key.Scope, key.ScopeRefId)))
			{
				var context = await renderer.CreateContextAsync(owner.Key.Scope, owner.Key.ScopeRefId)
					.ConfigureAwait(false);
				foreach (var key in owner)
				{
					var value = evaluator.ResolveSide(VariableReference(key.Name), context).Value;
					if (value is not null)
					{
						resolved[key] = JsonSerializer.SerializeToElement(value, value.GetType());
					}
				}
			}
		}

		lock (_gate)
		{
			_resolved = resolved;
			_referencedNames = references.Select(key => key.Name).ToHashSet(StringComparer.Ordinal);
		}
	}

	private static EventBindingDto ToDto(
		EventSubscription subscription,
		IReadOnlyDictionary<ResolvedKey, JsonElement> resolved)
	{
		var localId = QualifiedId.TryParse(subscription.EventId, out var id) ? id.LocalId : subscription.EventId;
		return new EventBindingDto
		{
			EventId = localId,
			Parameters = subscription.Configuration.ToDictionary(pair => pair.Key,
				pair => new EventBindingValueDto
				{
					Value = pair.Value.Value.ValueKind == JsonValueKind.Undefined ? null : pair.Value.Value,
					Operator = pair.Value.Operator,
					ResolvedValue = ReferenceKey(subscription.Owner, pair.Value.Value) is { } key
						&& resolved.TryGetValue(key, out var value)
							? value
							: null
				},
				StringComparer.Ordinal)
		};
	}

	private static ResolvedKey? ReferenceKey(EventTriggerOwner owner, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object
			|| !value.TryGetProperty("$var", out var name)
			|| name.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(name.GetString()))
		{
			return null;
		}

		var scope = owner.Kind == EventTriggerOwnerKind.Widget ? VariableScope.Widget : VariableScope.Automation;
		return new ResolvedKey(scope, owner.Id.ToString(), name.GetString()!);
	}

	private static JsonElement VariableReference(string name)
	{
		return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$var"] = name });
	}

	private readonly record struct ResolvedKey(VariableScope Scope, string ScopeRefId, string Name);

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
