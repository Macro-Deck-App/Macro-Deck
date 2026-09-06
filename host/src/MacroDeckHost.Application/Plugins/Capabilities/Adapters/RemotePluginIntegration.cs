using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.ConfigFlow;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.MusicPlayer;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Weather;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Localization;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Migration;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters;

public abstract class RemotePluginIntegration :
	IIntegration,
	IDeclaredCapabilityKinds,
	IVariableProvider,
	IEventProvider,
	IMusicPlayerProvider,
	IWeatherProvider,
	IProfileProvider,
	IMigrationProvider,
	IDeviceProvider,
	ILayoutProvider,
	IFolderViewProvider,
	IWidgetTypeProvider,
	IIntegrationIssueProvider
{
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IRemotePluginConnectionState _connectionState;
	private readonly IPluginAssetCache _assetCache;

	protected RemotePluginIntegration(
		string pluginId,
		LocalizedText displayName,
		string version,
		RemotePluginCapabilitySnapshot snapshot,
		IPluginCapabilityInvoker invoker,
		IRemotePluginConnectionState connectionState,
		IPluginAssetCache assetCache)
	{
		_invoker = invoker;
		_connectionState = connectionState;
		_assetCache = assetCache;
		Id = pluginId;
		Name = displayName;
		Version = version;
		Snapshot = snapshot;
	}

	protected RemotePluginCapabilitySnapshot Snapshot { get; }

	/// <summary>
	/// Set by <see cref="RemotePluginIntegrationRegistrar" /> after construction rather than threaded
	/// through the constructor: a new leaf-typed adapter instance replaces this one on every snapshot
	/// refresh, and adding a constructor parameter here would mean touching all eight leaves in
	/// <c>RemotePluginIntegrationLeaves.cs</c> for a dependency that changes what none of them are, only
	/// what one already-shared member (<see cref="IVariableProvider.SubscribeAsync" />) does.
	/// <c>null</c> is a legitimate value for a plugin that never declared the capability.
	/// </summary>
	internal RemoteVariableSubscriptions? VariableSubscriptions { get; set; }

	IReadOnlyList<string> IDeclaredCapabilityKinds.DeclaredCapabilityKinds => Snapshot.AcceptedKinds;

	public string Id { get; }

	public LocalizedText Name { get; }

	public string Version { get; }

	public bool IsInitialized => _connectionState.IsConnected(Id);

	public IReadOnlyList<IActionDefinition> Actions
		=> [.. Snapshot.Actions.Select(descriptor => RemoteActionDefinitionFactory.Create(_invoker, Id, descriptor))];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	/// <summary>
	/// Implemented unconditionally, like the other provider-shaped capabilities: a plugin that declared no
	/// migration is indistinguishable from one that declared an empty list, and every consumer reads the
	/// list rather than testing for the interface.
	/// </summary>
	public IReadOnlyList<IIntegrationMigration> Migrations
		=> [.. Snapshot.Migrations.Select(descriptor => new RemoteIntegrationMigration(_invoker, Id, descriptor))];

	public IReadOnlyList<VariableDefinition> Variables => Snapshot.Variables;

	public IReadOnlyList<VariableDefinition> DeclaredVariables => Snapshot.DeclaredVariables;

	public bool VariablesDependOnConfiguration => Snapshot.VariablesDependOnConfiguration;

	public bool SupportsCatalog => Snapshot.SupportsVariableCatalog;

	public bool SupportsPush => Snapshot.SupportsVariablePush;

	public bool SupportsSearch => Snapshot.SupportsVariableSearch;

	public string CatalogName => Snapshot.VariableCatalogName;

	public int? CatalogEntryCount => Snapshot.VariableCatalogEntryCount;

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Variables, LocalId = localId,
						Operation = CapabilityOperations.Variables.Get
					},
					cancellationToken)
				.ConfigureAwait(false);

			return VariableValueMapper.ToDomain(data?.Deserialize<VariableReadingDto>(PluginProtocolJson.Options));
		}
		catch (RemoteCapabilityException)
		{
			return VariableReading.Unavailable;
		}
	}

	// The one member of this class that does not degrade to an empty answer. Every read here may safely
	// report "nothing right now" because the host asks again on the next poll, but a write happens once:
	// swallowing its failure would tell the user their slider moved when the plugin never applied it. A
	// retryable transport failure is therefore reported as Unavailable and anything else as Failed, and a
	// cancellation is left to propagate rather than being reported as a refusal.
	public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Variables,
						LocalId = localId,
						Operation = CapabilityOperations.Variables.Set,
						Arguments = new VariableSetArguments { Value = VariableValueMapper.ToDto(value) },
						Timeout = TimeSpan.FromSeconds(3)
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<VariableSetResult>(PluginProtocolJson.Options);
			return result is null
				? VariableWriteResult.Failed()
				: new VariableWriteResult
				{
					Status = ToWriteStatus(result.Status), Message = result.Message ?? default
				};
		}
		catch (RemoteCapabilityException exception)
		{
			return exception.Retryable
				? VariableWriteResult.Unavailable(exception.Message)
				: VariableWriteResult.Failed(exception.Message);
		}
	}

	// Matched by name rather than through Enum.TryParse, which also accepts "3" and a comma list and would
	// turn a status this build has no name for into a specific one. A write that could not be classified
	// is not a write that demonstrably landed, so anything unrecognised degrades to Failed.
	private static VariableWriteStatus ToWriteStatus(string status) => status switch
	{
		nameof(VariableWriteStatus.Applied) => VariableWriteStatus.Applied,
		nameof(VariableWriteStatus.NotWritable) => VariableWriteStatus.NotWritable,
		nameof(VariableWriteStatus.NotFound) => VariableWriteStatus.NotFound,
		nameof(VariableWriteStatus.Unavailable) => VariableWriteStatus.Unavailable,
		nameof(VariableWriteStatus.InvalidValue) => VariableWriteStatus.InvalidValue,
		_ => VariableWriteStatus.Failed
	};

	public async ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Variables,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Variables.Discover,
						Arguments = new VariableDiscoverArguments
						{
							ParentId = query.ParentId,
							Search = query.Search,
							ContinuationToken = query.ContinuationToken,
							PageSize = query.PageSize
						}
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<VariableCatalogPageResult>(PluginProtocolJson.Options);
			return result is null ? VariableCatalogPage.Empty : VariableCatalogMapper.ToDomain(result);
		}
		catch (RemoteCapabilityException)
		{
			return VariableCatalogPage.Empty;
		}
	}

	public async ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Variables,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Variables.Resolve,
						Arguments = new VariableResolveArguments { Id = localId }
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<VariableResolveResult>(PluginProtocolJson.Options);
			return result?.Definition is null ? null : VariableCatalogMapper.ToDomain(result.Definition);
		}
		catch (RemoteCapabilityException)
		{
			return null;
		}
	}

	public async ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken)
	{
		// Recorded before the round trip and unconditionally, mirroring VariableSubscriptionCoordinator's
		// own bookkeeping: what the host most recently declared is the working set PluginCallbackRouter
		// polices pushes against, regardless of whether the plugin's reply below succeeds.
		VariableSubscriptions?.Replace(Id, localIds);

		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Variables,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Variables.Subscribe,
						Arguments = new VariableSubscribeArguments { Ids = [.. localIds] }
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<VariableSubscribeResult>(PluginProtocolJson.Options);
			return result is null ? [] : [.. result.Values.Select(VariableCatalogMapper.ToDomain)];
		}
		catch (RemoteCapabilityException)
		{
			return [];
		}
	}

	string IEventProvider.ProviderName => Snapshot.EventProviderName;

	public IReadOnlyList<EventDefinition> EventDefinitions => Snapshot.EventDefinitions;

	protected async Task<DynamicOptionsResult> GetEventOptionsCoreAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Events,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Events.Options,
						Arguments = new EventOptionsArguments
						{
							EventId = context.EventId,
							ParameterName = context.ParameterName,
							Filter = context.Filter,
							CurrentParameters = ToWireParameters(context.CurrentParameters)
						}
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<DynamicOptionsResultDto>(PluginProtocolJson.Options);

			return new DynamicOptionsResult
			{
				Options = result?.Options.Select(option => new ActionParameterOption
						{
							Value = option.Value, Label = option.Label ?? default, Metadata = option.Metadata
						})
						.ToList() ??
					[],
				AllowsCustomValue = result?.AllowsCustomValue ?? false,
				CacheSeconds = result?.CacheSeconds,
				Error = result?.Error ?? default
			};
		}
		catch (RemoteCapabilityException)
		{
			return new DynamicOptionsResult { Options = [] };
		}
	}

	private static Dictionary<string, JsonElement> ToWireParameters(IReadOnlyDictionary<string, object?> parameters)
	{
		var wire = new Dictionary<string, JsonElement>(parameters.Count, StringComparer.Ordinal);

		foreach (var (name, value) in parameters)
		{
			wire[name] = value is JsonElement element
				? element
				: JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
		}

		return wire;
	}

	string IMusicPlayerProvider.ProviderName => Snapshot.MusicPlayerProviderName;

	public IReadOnlyList<MusicPlayerInstance> GetInstances() => Snapshot.MusicPlayerInstances;

	public IMusicPlayer? GetPlayer(string instanceId)
	{
		if (!Snapshot.MusicPlayerInstances.Any(instance =>
			string.Equals(instance.Id, instanceId, StringComparison.Ordinal)))
		{
			return null;
		}

		return RemoteMusicPlayerFactory.Create(Id,
			instanceId,
			_invoker,
			_assetCache,
			Snapshot.MusicPlayerCatalogInstanceIds.Contains(instanceId),
			Snapshot.MusicPlayerDeviceInstanceIds.Contains(instanceId));
	}

	protected IConfigFlow CreateConfigFlowCore() => new RemoteConfigFlow(Id, _invoker);

	string IWeatherProvider.ProviderName => Snapshot.WeatherProviderName;

	IReadOnlyList<WeatherStationInstance> IWeatherProvider.GetInstances() => Snapshot.WeatherInstances;

	public IWeatherStation? GetStation(string instanceId)
		=> Snapshot.WeatherInstances.Any(instance => string.Equals(instance.Id, instanceId, StringComparison.Ordinal))
			? new RemoteWeatherStation(Id, instanceId, _invoker)
			: null;

	// A plugin owns its own device discovery, so the host neither starts nor stops it - the plugin's own
	// hosting runs InitializeAsync/ShutdownAsync in its process, and registrations arrive as callbacks.
	// The host still withdraws the plugin's devices when the integration stops; see DeviceProviderHost.
	Task IDeviceProvider.InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	Task IDeviceProvider.ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	// Same reasoning as IDeviceProvider above: a plugin owns its own layout discovery, and registrations
	// arrive as host.invoke callbacks (see RemotePluginIntegrationRegistrar.RegisterProviderLayoutsAsync)
	// rather than through a host-run InitializeAsync.
	Task ILayoutProvider.InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	// Same reasoning again: a plugin owns its own folder view catalog, and registrations arrive as
	// host.invoke callbacks (see RemotePluginIntegrationRegistrar.RegisterProviderFolderViewsAsync) rather
	// than through a host-run InitializeAsync.
	Task IFolderViewProvider.InitializeAsync(
		IFolderViewProviderContext context,
		CancellationToken cancellationToken)
		=> Task.CompletedTask;

	// Same reasoning again: a plugin owns its own widget type catalog, and registrations arrive as
	// host.invoke callbacks (see RemotePluginIntegrationRegistrar.RegisterProviderWidgetTypesAsync) rather
	// than through a host-run InitializeAsync.
	Task IWidgetTypeProvider.InitializeAsync(
		IWidgetTypeProviderContext context,
		CancellationToken cancellationToken)
		=> Task.CompletedTask;

	string IProfileProvider.ProviderName => Snapshot.ProfileProviderName;

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => Snapshot.Profiles;

	async Task IProfileProvider.HandleWidgetInteractionAsync(
		string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction)
	{
		try
		{
			await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.VirtualProfiles,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.VirtualProfiles.WidgetInteraction,
						Arguments = new WidgetInteractionArguments
						{
							ProfileId = profileId, FolderId = folderId, WidgetId = widgetId,
							TriggerType = interaction.TriggerType
						}
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (RemoteCapabilityException)
		{
		}
	}

	public async Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Issues,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Issues.List
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<IssueListResult>(PluginProtocolJson.Options);
			return result is null ? [] : [.. result.Issues.Select(IssueMapper.ToDomain)];
		}
		catch (RemoteCapabilityException)
		{
			return [];
		}
	}

	public async Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Issues,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Issues.Resolve,
						Arguments = new IssueResolveArguments { IssueId = issueId }
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<IssueResolveResult>(PluginProtocolJson.Options);
			return result is null
				? IssueResolution.Failed(AppStrings.Plugins.Issues.NoResolveResponse())
				: IssueMapper.ToDomain(result);
		}
		catch (RemoteCapabilityException exception)
		{
			return IssueResolution.Failed(exception.Message);
		}
	}
}
