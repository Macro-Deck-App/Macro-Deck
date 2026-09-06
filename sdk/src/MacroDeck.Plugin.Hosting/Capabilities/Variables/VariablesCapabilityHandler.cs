using System.Text.Json;
using MacroDeck.Plugin.Hosting.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Variables;

/// <summary>
/// Exposes every registered integration's variables as the <c>variables</c> capability - both halves of
/// <see cref="IVariableProvider" />: the eager set, declared one capability per variable and read through
/// <c>get</c>, and the opt-in catalog half browsed through <c>discover</c>/<c>resolve</c> and watched
/// through <c>subscribe</c>.
///
/// <para>
/// Unlike <c>actions</c>, an eager variable's local id is not the SDK's own identifier -
/// <see cref="IVariableProvider" /> lets a provider name a variable and derive its id, while the
/// capability layer needs a stable, declared-id-shaped local id up front (at handshake, before any
/// provider has necessarily connected). <see cref="VariableDescriptorMapper.LocalIdOf" /> is the one
/// place that translation happens, on both the declare and the invoke path, so the two can never
/// disagree.
/// </para>
///
/// <para>
/// Any number of integrations may provide eager variables, but at most one may report
/// <see cref="IVariableProvider.SupportsCatalog" />: the host models a plugin as one integration, so
/// there is no way to route a catalog resource id back to "the second provider" the way
/// <c>device-provider</c> routes a session by device id.
/// </para>
/// </summary>
internal sealed class VariablesCapabilityHandler : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 2, Maximum = 2 };

	private readonly IVariableProvider? _catalog;
	private readonly ILogger _logger;
	private readonly PluginMetadata _metadata;
	private readonly IReadOnlyList<IVariableProvider> _providers;
	private readonly VariableSubscriptions _subscriptions;

	public VariablesCapabilityHandler(
		IEnumerable<IPluginIntegration> integrations,
		PluginMetadata metadata,
		VariableSubscriptions subscriptions,
		ILogger logger)
	{
		_providers = [.. integrations.OfType<IVariableProvider>()];

		var catalogs = _providers.Where(provider => provider.SupportsCatalog).ToList();

		if (catalogs.Count > 1)
		{
			throw new InvalidOperationException(
				"Only one IVariableProvider with SupportsCatalog may be registered in a single plugin. " +
				"The host models a plugin as one integration, so a second catalog's resource ids could " +
				"never be routed back to the provider that owns them.");
		}

		_catalog = catalogs.Count == 1 ? catalogs[0] : null;
		_metadata = metadata;
		_subscriptions = subscriptions;
		_logger = logger.ForContext<VariablesCapabilityHandler>();
	}

	public string Kind => CapabilityKinds.Variables;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> [.. _providers.SelectMany(EagerDeclarationsOf)];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		return invocation.Operation switch
		{
			CapabilityOperations.Variables.Describe => Task.FromResult(Describe()),
			CapabilityOperations.Variables.Get => GetAsync(invocation, cancellationToken),
			CapabilityOperations.Variables.Set => SetAsync(invocation, cancellationToken),
			CapabilityOperations.Variables.Discover => DiscoverAsync(invocation, cancellationToken),
			CapabilityOperations.Variables.Resolve => ResolveAsync(invocation, cancellationToken),
			CapabilityOperations.Variables.Subscribe => SubscribeAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The variables capability has no operation '{invocation.Operation}'."))
		};
	}

	/// <summary>
	/// One declared capability per eager definition that resolves to an id, clamped to
	/// <see cref="VariableLimits.MaxEagerVariablesPerProvider" /> per provider. A provider that declares
	/// more keeps the first N in declaration order rather than losing its whole variable surface - the
	/// tail is dropped with an error line, the same failure shape the host-side registration uses.
	/// </summary>
	private IEnumerable<DeclaredCapability> EagerDeclarationsOf(IVariableProvider provider)
	{
		var eager = provider.DeclaredVariables
			.Where(definition => definition.Materialization == VariableMaterialization.Eager &&
				definition.ResolvedId is not null)
			.ToList();

		if (eager.Count > VariableLimits.MaxEagerVariablesPerProvider)
		{
			_logger.Error("Integration '{Integration}' declares {Count} eager variables, more than the " +
				"{Max} one provider may declare; the first {Max} in declaration order were kept and the " +
				"rest are unavailable",
				provider.GetType().Name,
				eager.Count,
				VariableLimits.MaxEagerVariablesPerProvider,
				VariableLimits.MaxEagerVariablesPerProvider);
		}

		return eager
			.Take(VariableLimits.MaxEagerVariablesPerProvider)
			.Select(definition => new DeclaredCapability
			{
				Kind = CapabilityKinds.Variables,
				LocalId = definition.ResolvedId!,
				VersionRange = _version,
				DisplayName = definition.Name
			});
	}

	private CapabilityInvocationResult Describe()
	{
		var payload = new VariableCatalogPayload
		{
			Variables = [.. _providers.SelectMany(p => p.Variables).Select(VariableDescriptorMapper.ToDto)],
			DeclaredVariables =
				[.. _providers.SelectMany(p => p.DeclaredVariables).Select(VariableDescriptorMapper.ToDto)],
			VariablesDependOnConfiguration = _providers.Any(p => p.VariablesDependOnConfiguration),
			SupportsCatalog = _catalog is not null,
			SupportsPush = _catalog?.SupportsPush ?? false,
			SupportsSearch = _catalog?.SupportsSearch ?? false,
			CatalogName = _catalog is null
				? null
				: _catalog.CatalogName is { Length: > 0 } name
					? name
					: _metadata.Name,
			CatalogEntryCount = _catalog?.CatalogEntryCount
		};

		return CapabilityInvocationResult.Ok(payload);
	}

	private async Task<CapabilityInvocationResult> GetAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var provider = OwnerOf(invocation.LocalId);
		if (provider is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No variable '{invocation.LocalId}' is registered in this plugin.");
		}

		var reading = await provider.ReadAsync(invocation.LocalId, cancellationToken).ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(VariableValueMapper.ToDto(reading));
	}

	private async Task<CapabilityInvocationResult> SetAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = Arguments<VariableSetArguments>(invocation);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"'set' requires a value.");
		}

		var provider = OwnerOf(invocation.LocalId);
		if (provider is null)
		{
			return Written(VariableWriteResult.NotFound());
		}

		var definition = EagerDefinition(provider, invocation.LocalId) ??
			await provider.ResolveAsync(invocation.LocalId, cancellationToken).ConfigureAwait(false);

		if (definition is null)
		{
			return Written(VariableWriteResult.NotFound());
		}

		// The declaration is the gate, not the provider: a definition carrying no write capability is
		// refused here, so SetValueAsync is only ever reached for a variable that said it accepts one.
		if (definition.Write is null)
		{
			return Written(VariableWriteResult.NotWritable());
		}

		var result = await provider
			.SetValueAsync(invocation.LocalId, VariableValueMapper.ToDomain(arguments.Value), cancellationToken)
			.ConfigureAwait(false);

		return Written(result);
	}

	private async Task<CapabilityInvocationResult> DiscoverAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (_catalog is null)
		{
			return CapabilityInvocationResult.Ok(new VariableCatalogPageResult { Items = [] });
		}

		var arguments = Arguments<VariableDiscoverArguments>(invocation);

		var requestedPageSize = arguments?.PageSize ?? 100;
		var pageSize = requestedPageSize > 0
			? Math.Min(requestedPageSize, ProtocolLimits.MaxVariableCatalogPageSize)
			: ProtocolLimits.MaxVariableCatalogPageSize;

		var query = new VariableCatalogQuery
		{
			ParentId = arguments?.ParentId,
			Search = arguments?.Search,
			ContinuationToken = arguments?.ContinuationToken,
			PageSize = pageSize
		};

		var page = await _catalog.DiscoverAsync(query, cancellationToken).ConfigureAwait(false);

		var items = ValidItems(page.Items)
			.Take(ProtocolLimits.MaxVariableCatalogPageSize)
			.Select(VariableDescriptorMapper.ToDto)
			.ToList();

		return CapabilityInvocationResult.Ok(new VariableCatalogPageResult
		{
			Items = items, ContinuationToken = page.ContinuationToken
		});
	}

	private async Task<CapabilityInvocationResult> ResolveAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = Arguments<VariableResolveArguments>(invocation);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"'resolve' requires an id.");
		}

		if (_catalog is null)
		{
			return CapabilityInvocationResult.Ok(new VariableResolveResult { Definition = null });
		}

		var definition = await _catalog.ResolveAsync(arguments.Id, cancellationToken).ConfigureAwait(false);

		if (definition is not null && !IsValidCatalogDefinition(definition))
		{
			_logger.Warning("Variable catalog '{CatalogName}' resolved id '{Id}' to a definition whose own " +
				"id '{DefinitionId}' is not a valid on-demand resource id; dropping it.",
				_catalog.CatalogName,
				arguments.Id,
				definition.Id);

			definition = null;
		}

		return CapabilityInvocationResult.Ok(new VariableResolveResult
		{
			Definition = definition is null ? null : VariableDescriptorMapper.ToDto(definition)
		});
	}

	private async Task<CapabilityInvocationResult> SubscribeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = Arguments<VariableSubscribeArguments>(invocation);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"'subscribe' requires an ids list.");
		}

		var working = _subscriptions.Replace(arguments.Ids);

		if (_catalog is null)
		{
			return CapabilityInvocationResult.Ok(new VariableSubscribeResult { Values = [] });
		}

		var values = await _catalog.SubscribeAsync(working, cancellationToken).ConfigureAwait(false);

		// The provider must only report values for the set it was just handed - see
		// IVariableProvider.SubscribeAsync's remarks - so anything else is dropped rather than trusted,
		// the same way RemoteVariableSink.PublishAsync polices a push.
		var filtered = values
			.Where(value => working.Contains(value.Id))
			.Select(value => new VariableIdValueDto
				{ Id = value.Id, Reading = VariableValueMapper.ToDto(value.Reading) })
			.ToList();

		return CapabilityInvocationResult.Ok(new VariableSubscribeResult { Values = filtered });
	}

	/// <summary>
	/// The provider that owns <paramref name="localId" />: the one declaring it eagerly, otherwise the
	/// catalog, whose resource ids never appear in any eager list.
	/// </summary>
	private IVariableProvider? OwnerOf(string localId)
		=> _providers.FirstOrDefault(provider => EagerDefinition(provider, localId) is not null) ?? _catalog;

	private static VariableDefinition? EagerDefinition(IVariableProvider provider, string localId)
		=> provider.Variables.FirstOrDefault(definition =>
			definition.Materialization == VariableMaterialization.Eager &&
			string.Equals(definition.ResolvedId, localId, StringComparison.Ordinal));

	private static CapabilityInvocationResult Written(VariableWriteResult result)
		=> CapabilityInvocationResult.Ok(new VariableSetResult
		{
			Status = result.Status.ToString(), Message = PluginText.ToWireOrNull(result.Message)
		});

	private IEnumerable<VariableDefinition> ValidItems(IEnumerable<VariableDefinition> items)
	{
		foreach (var item in items)
		{
			if (IsValidCatalogDefinition(item))
			{
				yield return item;
			}
			else
			{
				_logger.Warning("Variable catalog '{CatalogName}' returned an invalid on-demand definition with id " +
					"'{Id}'; dropping it.",
					_catalog?.CatalogName,
					item.Id);
			}
		}
	}

	private static bool IsValidCatalogDefinition(VariableDefinition definition)
		=> definition.Materialization == VariableMaterialization.OnDemand &&
			definition.Id is { } id &&
			MacroDeckId.IsValidLocalId(id, LocalIdKind.Resource);

	private static T? Arguments<T>(CapabilityInvocation invocation)
		where T : class
	{
		try
		{
			return invocation.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
