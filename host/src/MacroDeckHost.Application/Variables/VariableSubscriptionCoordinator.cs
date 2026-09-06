using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableSubscriptionCoordinator : IVariableSubscriptionCoordinator
{
	private readonly VariableCatalogProviders _providers;
	private readonly IVariableBindingStore _bindingStore;
	private readonly VariableUpdateChannel _channel;
	private readonly VariableCatalogInvalidationSignal _invalidation;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	private readonly object _lock = new();
	private readonly Dictionary<string, HashSet<string>> _subscribed = new(StringComparer.Ordinal);

	// Keyed by provider instance, not just integration id: RemotePluginIntegration replaces its adapter
	// instance on every snapshot refresh, and the replacement needs its own OnAttachedAsync call or its
	// pushes go to a sink no one reconciles against.
	private readonly Dictionary<string, IVariableProvider> _attached = new(StringComparer.Ordinal);

	public VariableSubscriptionCoordinator(
		VariableCatalogProviders providers,
		IVariableBindingStore bindingStore,
		VariableUpdateChannel channel,
		VariableCatalogInvalidationSignal invalidation,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
	{
		_providers = providers;
		_bindingStore = bindingStore;
		_channel = channel;
		_invalidation = invalidation;
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	public bool IsSubscribed(string integrationId, string localResourceId)
	{
		lock (_lock)
		{
			return _subscribed.TryGetValue(integrationId, out var ids) && ids.Contains(localResourceId);
		}
	}

	public async Task ReconcileAsync(CancellationToken cancellationToken = default)
	{
		var bindings = _bindingStore.Load();
		var byIntegration = bindings
			.GroupBy(b => b.IntegrationId, StringComparer.Ordinal)
			.ToDictionary(g => g.Key,
				g => (IReadOnlyList<VariableBinding>)g.ToList(),
				StringComparer.Ordinal);

		// An integration that just lost its last binding must still be reconciled once, so the provider
		// hears "watch nothing" rather than simply being forgotten about.
		var toReconcile = new HashSet<string>(byIntegration.Keys, StringComparer.Ordinal);
		lock (_lock)
		{
			toReconcile.UnionWith(_subscribed.Keys);
		}

		foreach (var integrationId in toReconcile)
		{
			var bound = byIntegration.TryGetValue(integrationId, out var list)
				? list
				: (IReadOnlyList<VariableBinding>)[];
			await ReconcileIntegration(integrationId, bound, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Re-applies each bound resource's declared attributes from the provider's own definition.
	/// </summary>
	/// <remarks>
	/// A binding persists identity - id, name, type - and deliberately not the declared attributes, so a
	/// variable restored at boot carries none of them and, without this, stays unitless and unwritable
	/// until the user re-binds it. Reconcile is the right place because it already runs whenever a
	/// provider becomes reachable, so a definition that was unresolvable at boot still heals.
	/// </remarks>
	private async Task RefreshDeclarations(
		string integrationId,
		IVariableProvider provider,
		IReadOnlyList<VariableBinding> bound,
		CancellationToken ct)
	{
		if (bound.Count == 0)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var binding in bound)
		{
			VariableDefinition? definition;
			try
			{
				definition = await provider.ResolveAsync(binding.LocalResourceId, ct).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Error(ex,
					"Variable catalog provider '{Integration}' failed to resolve '{Resource}'",
					integrationId,
					binding.LocalResourceId);
				continue;
			}

			if (definition is null)
			{
				continue;
			}

			// The binding's own name, never the definition's suggestion: the user may have renamed it,
			// and re-materializing under the suggested name would rename it back on every reconcile.
			await variables.MaterializeCatalogVariable(integrationId,
					binding.LocalResourceId,
					binding.Name,
					binding.Type,
					definition.DecimalPlaces,
					VariableDeclarationFactory.From(definition))
				.ConfigureAwait(false);
		}
	}

	private async Task ReconcileIntegration(
		string integrationId,
		IReadOnlyList<VariableBinding> bound,
		CancellationToken ct)
	{
		var ids = bound.Select(b => b.LocalResourceId).ToList();
		var provider = _providers.Resolve(integrationId);
		if (provider is null)
		{
			HashSet<string>? previous;
			lock (_lock)
			{
				_subscribed.Remove(integrationId, out previous);
			}

			if (previous is not null)
			{
				foreach (var id in previous)
				{
					_channel.Write(integrationId, id, null);
				}
			}

			return;
		}

		await EnsureAttached(integrationId, provider, ct).ConfigureAwait(false);
		await RefreshDeclarations(integrationId, provider, bound, ct).ConfigureAwait(false);

		HashSet<string> previouslySubscribed;
		lock (_lock)
		{
			previouslySubscribed = _subscribed.TryGetValue(integrationId, out var existing)
				? existing
				: [];
		}

		IReadOnlyList<VariableValue> results;
		try
		{
			results = await provider.SubscribeAsync(ids, ct).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Variable catalog provider '{Integration}' failed to subscribe", integrationId);
			results = [];
		}

		var byId = new Dictionary<string, VariableReading>(StringComparer.Ordinal);
		foreach (var result in results)
		{
			byId[result.Id] = result.Reading;
		}

		foreach (var id in ids)
		{
			if (byId.TryGetValue(id, out var reading))
			{
				_channel.Write(integrationId, id, reading.Value, HostVariableSink.BoundsOf(reading));
				continue;
			}

			// A provider with nothing cached yet may return an empty list; the host falls back to a direct
			// read so binding a resource still seeds a value without the caller waiting on a later push.
			// Only newly bound ids get this fallback - an id already subscribed before this reconcile
			// already has a value in the registry, and re-reading it here would cost one round trip per
			// bound id on every reconcile instead of relying on the provider's next push.
			if (previouslySubscribed.Contains(id))
			{
				continue;
			}

			VariableReading fallback;
			try
			{
				fallback = await provider.ReadAsync(id, ct).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Error(ex,
					"Variable catalog provider '{Integration}' failed to read '{Id}'",
					integrationId,
					id);
				fallback = VariableReading.Unavailable;
			}

			_channel.Write(integrationId, id, fallback.Value, HostVariableSink.BoundsOf(fallback));
		}

		lock (_lock)
		{
			if (ids.Count == 0)
			{
				_subscribed.Remove(integrationId);
			}
			else
			{
				_subscribed[integrationId] = new HashSet<string>(ids, StringComparer.Ordinal);
			}
		}
	}

	private async Task EnsureAttached(string integrationId, IVariableProvider provider, CancellationToken ct)
	{
		lock (_lock)
		{
			if (_attached.TryGetValue(integrationId, out var attachedProvider) &&
				ReferenceEquals(attachedProvider, provider))
			{
				return;
			}

			_attached[integrationId] = provider;
		}

		try
		{
			var sink = new HostVariableSink(integrationId, _channel, this, _invalidation);
			await provider.OnAttachedAsync(sink, ct).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Variable catalog provider '{Integration}' failed to attach", integrationId);
		}
	}
}
