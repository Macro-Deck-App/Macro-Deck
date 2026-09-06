using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Migration;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.Icons;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk.Migration;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Migration;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed class RemotePluginSnapshotRefresher
{
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IRemotePluginSnapshotStore _store;

	private readonly
		Dictionary<string, Func<RemotePluginCapabilitySnapshot, JsonElement?, RemotePluginCapabilitySnapshot>> _mappers;

	private readonly
		Dictionary<string, (string Operation,
			Func<RemotePluginCapabilitySnapshot, JsonElement?, RemotePluginCapabilitySnapshot> Mapper)>
		_stateUpdateOperations;

	public RemotePluginSnapshotRefresher(IPluginCapabilityInvoker invoker, IRemotePluginSnapshotStore store)
	{
		_invoker = invoker;
		_store = store;

		_mappers
			= new Dictionary<string,
				Func<RemotePluginCapabilitySnapshot, JsonElement?, RemotePluginCapabilitySnapshot>>(StringComparer
				.Ordinal)
			{
				[CapabilityKinds.Actions] = ApplyActionsDescribe,
				[CapabilityKinds.Variables] = ApplyVariablesDescribe,
				[CapabilityKinds.Events] = ApplyEventsDescribe,
				[CapabilityKinds.MusicPlayer] = ApplyMusicPlayerDescribe,
				[CapabilityKinds.Weather] = ApplyWeatherDescribe,
				[CapabilityKinds.VirtualProfiles] = ApplyVirtualProfilesDescribe,
				[CapabilityKinds.ConfigFlow] = ApplyConfigFlowDescribe,
				[CapabilityKinds.Icons] = ApplyIconsDescribe,
				[CapabilityKinds.Ui] = ApplyUiDescribe,
				[CapabilityKinds.Migration] = ApplyMigrationDescribe,
				[CapabilityKinds.Issues] = (snapshot, _) => snapshot
			};

		_stateUpdateOperations
			= new Dictionary<string, (string,
				Func<RemotePluginCapabilitySnapshot, JsonElement?, RemotePluginCapabilitySnapshot>)>(StringComparer
				.Ordinal)
			{
				[CapabilityKinds.MusicPlayer] = (CapabilityOperations.MusicPlayer.Instances, ApplyMusicPlayerInstances),
				[CapabilityKinds.Weather] = (CapabilityOperations.Weather.Instances, ApplyWeatherInstances),
				[CapabilityKinds.VirtualProfiles] = (CapabilityOperations.VirtualProfiles.Profiles,
					ApplyVirtualProfilesInstances)
			};
	}

	public async Task<RemotePluginSnapshotRefreshResult> RefreshAsync(
		string pluginId,
		IReadOnlyCollection<string> acceptedKinds,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentNullException.ThrowIfNull(acceptedKinds);

		// Recorded regardless of which kinds this host build actually knows how to describe below - a
		// kind this build cannot yet describe was still accepted, and RegisterInstalledButStoppedAsync
		// needs to know that on the next restart just as much as a described one.
		var snapshot = _store.GetSnapshot(pluginId) with { PluginId = pluginId, AcceptedKinds = [.. acceptedKinds] };
		var failedKinds = new List<string>();

		foreach (var kind in acceptedKinds)
		{
			if (!_mappers.TryGetValue(kind, out var mapper))
			{
				continue;
			}

			try
			{
				var data = await _invoker.InvokeAsync(pluginId,
						new CapabilityInvokeRequest
						{
							Kind = kind, LocalId = DescribeLocalId, Operation = CapabilityOperations.Actions.Describe
						},
						cancellationToken)
					.ConfigureAwait(false);

				snapshot = mapper(snapshot, data);
			}
			catch (Exception exception) when (exception is RemoteCapabilityException or OperationCanceledException)
			{
				failedKinds.Add(kind);
			}
		}

		await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

		return new RemotePluginSnapshotRefreshResult(snapshot, failedKinds);
	}

	public async Task<RemotePluginSnapshotRefreshResult> RefreshKindAsync(
		string pluginId,
		string kind,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentException.ThrowIfNullOrEmpty(kind);

		var snapshot = _store.GetSnapshot(pluginId) with { PluginId = pluginId };

		string localId;
		string operation;
		Func<RemotePluginCapabilitySnapshot, JsonElement?, RemotePluginCapabilitySnapshot> mapper;

		if (_stateUpdateOperations.TryGetValue(kind, out var narrow))
		{
			(localId, operation, mapper) = (ProviderCapabilityId.LocalId, narrow.Operation, narrow.Mapper);
		}
		else if (_mappers.TryGetValue(kind, out var describeMapper))
		{
			(localId, operation, mapper) = (DescribeLocalId, CapabilityOperations.Actions.Describe, describeMapper);
		}
		else
		{
			return new RemotePluginSnapshotRefreshResult(snapshot, []);
		}

		try
		{
			var data = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest { Kind = kind, LocalId = localId, Operation = operation },
					cancellationToken)
				.ConfigureAwait(false);

			snapshot = mapper(snapshot, data);
		}
		catch (Exception exception) when (exception is RemoteCapabilityException or OperationCanceledException)
		{
			return new RemotePluginSnapshotRefreshResult(snapshot, [kind]);
		}

		await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

		return new RemotePluginSnapshotRefreshResult(snapshot, []);
	}

	private const string DescribeLocalId = "*";

	private static RemotePluginCapabilitySnapshot ApplyActionsDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<ActionCatalogPayload>(PluginProtocolJson.Options);

		// Preserve the plugin's declared ordering - never re-sort. MusicPlayerRegistry.GetPlayer (and,
		// here, any future action-ordering-sensitive consumer) indexes catalogues positionally.
		return snapshot with { Actions = payload is null ? snapshot.Actions : ActionCatalogMapper.ToDomain(payload) };
	}

	private static RemotePluginCapabilitySnapshot ApplyVariablesDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<VariableCatalogPayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			DeclaredVariables = [.. payload.DeclaredVariables.Select(VariableCatalogMapper.ToDomain)],
			Variables = [.. payload.Variables.Select(VariableCatalogMapper.ToDomain)],
			VariablesDependOnConfiguration = payload.VariablesDependOnConfiguration,
			SupportsVariableCatalog = payload.SupportsCatalog,
			SupportsVariablePush = payload.SupportsPush,
			SupportsVariableSearch = payload.SupportsSearch,
			VariableCatalogName = payload.CatalogName ?? string.Empty,
			VariableCatalogEntryCount = payload.CatalogEntryCount
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyEventsDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<EventCatalogPayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			EventProviderName = payload.ProviderName,
			EventDefinitions = [.. payload.Events.Select(EventCatalogMapper.ToDomain)],
			HasDynamicEventOptions = payload.HasDynamicEventOptions
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyMusicPlayerDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<MusicPlayerDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			MusicPlayerProviderName = payload.ProviderName,
			MusicPlayerInstances = [.. payload.Instances.Select(MusicPlayerCatalogMapper.ToDomain)],
			MusicPlayerCatalogInstanceIds =
			[.. payload.Instances.Where(instance => instance.HasCatalog).Select(instance => instance.Id)],
			MusicPlayerDeviceInstanceIds =
			[.. payload.Instances.Where(instance => instance.HasDevices).Select(instance => instance.Id)]
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyMusicPlayerInstances(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<MusicPlayerInstancesResult>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			MusicPlayerInstances = [.. payload.Instances.Select(MusicPlayerCatalogMapper.ToDomain)],
			MusicPlayerCatalogInstanceIds =
			[.. payload.Instances.Where(instance => instance.HasCatalog).Select(instance => instance.Id)],
			MusicPlayerDeviceInstanceIds =
			[.. payload.Instances.Where(instance => instance.HasDevices).Select(instance => instance.Id)]
		};
	}

	/// <summary>
	/// A declared source this host does not know is dropped rather than refused. Reading that
	/// application's files is the host's own work, so a migration naming a source it cannot read has
	/// nothing to translate - and refusing the whole capability over it would cost the plugin its other,
	/// usable migrations.
	/// </summary>
	private static RemotePluginCapabilitySnapshot ApplyMigrationDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<MigrationDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		var migrations = payload.Migrations
			.Select(descriptor => Enum.TryParse<MigrationSource>(descriptor.Source, ignoreCase: true, out var source)
				? new RemoteMigrationDescriptor(source,
					[.. descriptor.ClaimedActionSources],
					[.. descriptor.ClaimedSettingsSources])
				: null)
			.OfType<RemoteMigrationDescriptor>()
			.ToList();

		return snapshot with { Migrations = migrations };
	}

	private static RemotePluginCapabilitySnapshot ApplyWeatherDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<WeatherDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			WeatherProviderName = payload.ProviderName,
			WeatherInstances = [.. payload.Instances.Select(WeatherCatalogMapper.ToDomain)]
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyWeatherInstances(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<WeatherInstancesResult>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with { WeatherInstances = [.. payload.Instances.Select(WeatherCatalogMapper.ToDomain)] };
	}

	private static RemotePluginCapabilitySnapshot ApplyVirtualProfilesDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<VirtualProfilesDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			ProfileProviderName = payload.ProviderName,
			Profiles = [.. payload.Profiles.Select(VirtualProfileCatalogMapper.ToDomain)]
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyVirtualProfilesInstances(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<VirtualProfilesResult>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with { Profiles = [.. payload.Profiles.Select(VirtualProfileCatalogMapper.ToDomain)] };
	}

	private static RemotePluginCapabilitySnapshot ApplyConfigFlowDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<ConfigFlowDescribePayload>(PluginProtocolJson.Options);
		return payload is null
			? snapshot
			: snapshot with
			{
				AllowsMultipleConfigurations = payload.AllowsMultipleConfigurations,
				ServesConfigUiTree = payload.ServesConfigUiTree
			};
	}

	private static RemotePluginCapabilitySnapshot ApplyUiDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<UiDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		return snapshot with
		{
			UiSurfaces =
			[
				.. payload.Surfaces.Select(surface => new RemoteUiSurfaceDescriptor
				{
					Kind = surface.Kind, SessionMode = surface.SessionMode
				})
			],
			UiModelVersion = payload.UiModelVersion,
			UiPreviews =
			[
				.. (payload.Previews ?? []).Select(preview => new RemoteUiPreviewDescriptor
				{
					Id = preview.Id, View = preview.View, Scenario = preview.Scenario, Profile = preview.Profile
				})
			]
		};
	}

	private static RemotePluginCapabilitySnapshot ApplyIconsDescribe(
		RemotePluginCapabilitySnapshot snapshot,
		JsonElement? data)
	{
		var payload = data?.Deserialize<IconsDescribePayload>(PluginProtocolJson.Options);
		if (payload is null)
		{
			return snapshot;
		}

		if (!AssetContentHash.IsValid(payload.ContentHash))
		{
			throw RemoteCapabilityException.CreateNonRetryable(ProtocolErrorCodes.InvalidPayload,
				"The icons describe reply declared a malformed content hash.");
		}

		return snapshot with { HasIcon = true, IconMimeType = payload.MimeType, IconContentHash = payload.ContentHash };
	}
}

public sealed record RemotePluginSnapshotRefreshResult(
	RemotePluginCapabilitySnapshot Snapshot,
	IReadOnlyList<string> FailedKinds)
{
	public bool AllSucceeded => FailedKinds.Count == 0;
}
