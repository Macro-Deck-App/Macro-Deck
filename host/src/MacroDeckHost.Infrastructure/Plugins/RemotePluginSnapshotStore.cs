using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeck.Sdk.Variables;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class RemotePluginSnapshotStore : IRemotePluginSnapshotStore
{
	/// <summary>
	/// The schema this store writes. A document with no <c>version</c> at all predates ADR 0081 and is
	/// read through <see cref="PersistedSnapshot.DeclaredVariables" /> instead - see its remarks.
	/// </summary>
	private const int CurrentVersion = 1;

	// The capability kind that carried a provider's browsable catalog before ADR 0081 merged it into
	// "variables". It is gone from CapabilityKinds, but it is still what a pre-ADR-0081 document has in
	// its acceptedKinds, and it is exactly the gate the old host used to decide a plugin had a catalog -
	// so it survives here as a literal, read-only, to seed SupportsVariableCatalog for such a document.
	private const string LegacyDynamicVariablesKind = "dynamic-variables";

	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly object _fileLock = new();
	private readonly string _filePath;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;

	private readonly ConcurrentDictionary<string, RemotePluginCapabilitySnapshot> _byPluginId =
		new(StringComparer.Ordinal);

	public RemotePluginSnapshotStore(IMacroDeckPaths paths, ILogger logger)
	{
		_filePath = Path.Combine(paths.ConfigDirectory, "plugin-capability-snapshots.json");
		_logger = logger;
		_files = new DurableJsonFile("plugin capability snapshot file", _options, logger);

		foreach (var (pluginId, persisted) in LoadFromDisk())
		{
			_byPluginId[pluginId] = ToSnapshot(pluginId, persisted);
		}
	}

	public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
		=> _byPluginId.TryGetValue(pluginId, out var snapshot)
			? snapshot
			: RemotePluginCapabilitySnapshot.Empty(pluginId);

	public bool Has(string pluginId) => _byPluginId.ContainsKey(pluginId);

	public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		_byPluginId[snapshot.PluginId] = snapshot;
		WriteToDisk();
		return Task.CompletedTask;
	}

	private Dictionary<string, PersistedSnapshot> LoadFromDisk()
	{
		lock (_fileLock)
		{
			return _files.Read<Dictionary<string, PersistedSnapshot>>(_filePath) ?? [];
		}
	}

	private void WriteToDisk()
	{
		lock (_fileLock)
		{
			try
			{
				var persisted = _byPluginId.ToDictionary(pair => pair.Key,
					pair => ToPersisted(pair.Value),
					StringComparer.Ordinal);

				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				_files.Write(_filePath, persisted);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
			{
				_logger.Error(ex, "Failed to write remote plugin capability snapshots to {Path}", _filePath);
			}
		}
	}

	private RemotePluginCapabilitySnapshot ToSnapshot(string pluginId, PersistedSnapshot persisted)
	{
		var acceptedKinds = persisted.AcceptedKinds ?? [];
		var variables = ReadVariables(persisted, acceptedKinds);

		return new RemotePluginCapabilitySnapshot
		{
			PluginId = pluginId,
			Actions = persisted.Actions is { } actions ? ActionCatalogMapper.ToDomain(actions) : [],
			AllowsMultipleConfigurations = persisted.AllowsMultipleConfigurations,
			AcceptedKinds = acceptedKinds,
			DeclaredVariables = ToDefinitions(pluginId, variables.Declared),
			Variables = ToDefinitions(pluginId, variables.Eager),
			VariablesDependOnConfiguration = variables.DependOnConfiguration,
			SupportsVariableCatalog = variables.SupportsCatalog,
			SupportsVariablePush = variables.SupportsPush,
			SupportsVariableSearch = variables.SupportsSearch,
			VariableCatalogName = variables.CatalogName ?? string.Empty,
			VariableCatalogEntryCount = variables.CatalogEntryCount,
			EventProviderName = persisted.Events?.ProviderName ?? string.Empty,
			EventDefinitions = persisted.Events?.Events is { } events
				? [.. events.Select(EventCatalogMapper.ToDomain)]
				: [],
			HasDynamicEventOptions = persisted.Events?.HasDynamicEventOptions ?? false,
			MusicPlayerProviderName = persisted.MusicPlayer?.ProviderName ?? string.Empty,
			MusicPlayerInstances = persisted.MusicPlayer?.Instances is { } musicPlayerInstances
				? [.. musicPlayerInstances.Select(MusicPlayerCatalogMapper.ToDomain)]
				: [],
			MusicPlayerCatalogInstanceIds = persisted.MusicPlayer?.Instances is { } catalogInstances
				? [.. catalogInstances.Where(instance => instance.HasCatalog).Select(instance => instance.Id)]
				: [],
			MusicPlayerDeviceInstanceIds = persisted.MusicPlayer?.Instances is { } deviceInstances
				? [.. deviceInstances.Where(instance => instance.HasDevices).Select(instance => instance.Id)]
				: [],
			IconBytes = persisted.Icon?.IconBytes ?? [],
			IconMimeType = persisted.Icon?.IconMimeType ?? "application/octet-stream",
			HasIcon = persisted.Icon?.HasIcon ?? false,
			IconContentHash = persisted.Icon?.IconContentHash ?? string.Empty,
			IconBytesContentHash = persisted.Icon?.IconBytesContentHash ?? string.Empty,
			WeatherProviderName = persisted.Weather?.ProviderName ?? string.Empty,
			WeatherInstances = persisted.Weather?.Instances is { } weatherInstances
				? [.. weatherInstances.Select(WeatherCatalogMapper.ToDomain)]
				: [],
			ProfileProviderName = persisted.VirtualProfiles?.ProviderName ?? string.Empty,
			Profiles = persisted.VirtualProfiles?.Profiles is { } profiles
				? [.. profiles.Select(VirtualProfileCatalogMapper.ToDomain)]
				: []
		};
	}

	/// <summary>
	/// The variable half of a document, from whichever of the two shapes it was written in. The legacy
	/// branch is permanent rather than a one-shot migration: restoring a backup taken before ADR 0081
	/// reintroduces the old shape at any time, and a plugin that is installed but never started again
	/// would otherwise never be rewritten in the new one.
	/// </summary>
	private static PersistedVariables ReadVariables(PersistedSnapshot persisted, IReadOnlyList<string> acceptedKinds)
	{
		if (persisted.Version >= CurrentVersion)
		{
			return persisted.Variables is { } current
				? new PersistedVariables(current.Variables,
					current.DeclaredVariables,
					current.VariablesDependOnConfiguration,
					current.SupportsCatalog,
					current.SupportsPush,
					current.SupportsSearch,
					current.CatalogName)
				: PersistedVariables.None;
		}

		var legacy = persisted.DeclaredVariables;
		if (legacy is null)
		{
			return PersistedVariables.None;
		}

		// A pre-ADR-0081 provider had exactly one materialization policy, so every variable it persisted
		// is an eager one. SupportsPush/SupportsSearch were never persisted at all and stay false until
		// the plugin's next describe; the catalog flag is recoverable, because the kind the old host
		// gated on is in acceptedKinds.
		return new PersistedVariables([.. (legacy.ProvidedVariables ?? []).Select(ToDefinition)],
			[.. (legacy.DeclaredVariables ?? []).Select(ToDefinition)],
			legacy.VariablesDependOnConfiguration,
			acceptedKinds.Contains(LegacyDynamicVariablesKind, StringComparer.Ordinal),
			SupportsPush: false,
			SupportsSearch: false,
			CatalogName: null);
	}

	// A definition whose type this host does not know is dropped, never thrown over: the reader's job is
	// to salvage as much of the document as it can, and a throw here would take every other plugin's
	// cached actions, icons and weather with it.
	private List<VariableDefinition> ToDefinitions(
		string pluginId,
		IReadOnlyList<VariableDefinitionDto> dtos)
	{
		var definitions = new List<VariableDefinition>(dtos.Count);

		foreach (var dto in dtos)
		{
			var definition = VariableCatalogMapper.TryToDomain(dto);
			if (definition is null)
			{
				_logger.Warning("Dropping cached variable '{Variable}' of plugin '{PluginId}': its persisted type " +
					"'{Type}' is not a variable type this host knows",
					dto.Id ?? dto.Name ?? "<unnamed>",
					pluginId,
					dto.Type);
				continue;
			}

			definitions.Add(definition);
		}

		return definitions;
	}

	private static VariableDefinitionDto ToDefinition(LegacyVariableDescriptor legacy)
		=> new()
		{
			Id = legacy.DefinitionId,
			Name = legacy.Name,
			Type = legacy.Type ?? string.Empty,
			Materialization = VariableMaterializations.Eager,
			DisplayName = legacy.DisplayName,
			DecimalPlaces = legacy.DecimalPlaces,
			RefreshIntervalSeconds = legacy.RefreshIntervalSeconds,
			ConfigurationKey = legacy.ConfigurationKey,
			ConfigurationName = legacy.ConfigurationName
		};

	private static PersistedSnapshot ToPersisted(RemotePluginCapabilitySnapshot snapshot)
		=> new()
		{
			Version = CurrentVersion,
			Actions = ActionCatalogMapper.ToDto(snapshot.Actions),
			AllowsMultipleConfigurations = snapshot.AllowsMultipleConfigurations,
			AcceptedKinds = [.. snapshot.AcceptedKinds],
			Variables = new VariableCatalogPayload
			{
				Variables = [.. snapshot.Variables.Select(VariableCatalogMapper.ToDto)],
				DeclaredVariables = [.. snapshot.DeclaredVariables.Select(VariableCatalogMapper.ToDto)],
				VariablesDependOnConfiguration = snapshot.VariablesDependOnConfiguration,
				SupportsCatalog = snapshot.SupportsVariableCatalog,
				SupportsPush = snapshot.SupportsVariablePush,
				SupportsSearch = snapshot.SupportsVariableSearch,
				CatalogName = snapshot.VariableCatalogName is { Length: > 0 } name ? name : null,
				CatalogEntryCount = snapshot.VariableCatalogEntryCount
			},
			Events = new EventCatalogPayload
			{
				ProviderName = snapshot.EventProviderName,
				Events = [.. snapshot.EventDefinitions.Select(EventCatalogMapper.ToDto)],
				HasDynamicEventOptions = snapshot.HasDynamicEventOptions
			},
			MusicPlayer = new MusicPlayerDescribePayload
			{
				ProviderName = snapshot.MusicPlayerProviderName,
				Instances =
				[
					.. snapshot.MusicPlayerInstances.Select(instance => MusicPlayerCatalogMapper.ToDto(instance,
						snapshot.MusicPlayerCatalogInstanceIds.Contains(instance.Id),
						snapshot.MusicPlayerDeviceInstanceIds.Contains(instance.Id)))
				]
			},
			Icon = new PersistedIcon(snapshot.IconBytes,
				snapshot.IconMimeType,
				snapshot.HasIcon,
				snapshot.IconContentHash,
				snapshot.IconBytesContentHash),
			Weather = new WeatherDescribePayload
			{
				ProviderName = snapshot.WeatherProviderName,
				Instances = [.. snapshot.WeatherInstances.Select(WeatherCatalogMapper.ToDto)]
			},
			VirtualProfiles = new VirtualProfilesDescribePayload
			{
				ProviderName = snapshot.ProfileProviderName,
				Profiles = [.. snapshot.Profiles.Select(VirtualProfileCatalogMapper.ToDto)]
			}
		};

	private sealed record PersistedVariables(
		IReadOnlyList<VariableDefinitionDto> Eager,
		IReadOnlyList<VariableDefinitionDto> Declared,
		bool DependOnConfiguration,
		bool SupportsCatalog,
		bool SupportsPush,
		bool SupportsSearch,
		string? CatalogName,
		// Optional with a default so a document written before it existed still deserializes rather
		// than quarantining every plugin's cached capabilities with it.
		int? CatalogEntryCount = null)
	{
		public static readonly PersistedVariables None =
			new([], [], false, false, false, false, null);
	}

	/// <summary>
	/// Every member is optional and nothing is <c>required</c>, deliberately: this file has no
	/// last-known-good backup, so a single missing member would make
	/// <see cref="DurableJsonFile.Read{T}" /> quarantine the whole document and cost every installed
	/// plugin its cached actions, events, icons, weather and virtual profiles - not just the section
	/// that changed.
	/// </summary>
	private sealed record PersistedSnapshot
	{
		/// <summary>Absent (<c>0</c>) in every document written before ADR 0081.</summary>
		public int Version { get; init; }

		public ActionCatalogPayload? Actions { get; init; }

		public bool AllowsMultipleConfigurations { get; init; }

		public IReadOnlyList<string>? AcceptedKinds { get; init; }

		/// <summary>
		/// The pre-ADR-0081 variable section, under the name it was written with. Read-only: a document
		/// this store writes carries <see cref="Variables" /> instead, and this stays <c>null</c>.
		/// </summary>
		public LegacyVariableCatalog? DeclaredVariables { get; init; }

		public VariableCatalogPayload? Variables { get; init; }

		public EventCatalogPayload? Events { get; init; }

		public MusicPlayerDescribePayload? MusicPlayer { get; init; }

		public PersistedIcon? Icon { get; init; }

		public WeatherDescribePayload? Weather { get; init; }

		public VirtualProfilesDescribePayload? VirtualProfiles { get; init; }
	}

	private sealed record LegacyVariableCatalog
	{
		public IReadOnlyList<LegacyVariableDescriptor>? DeclaredVariables { get; init; }

		public IReadOnlyList<LegacyVariableDescriptor>? ProvidedVariables { get; init; }

		public bool VariablesDependOnConfiguration { get; init; }
	}

	/// <summary>
	/// The pre-ADR-0081 <c>VariableDescriptorDto</c>, modelled here rather than read through the current
	/// wire DTO: the two disagree on the id member (<c>definitionId</c> then, <c>id</c> now), and reading
	/// an old document through the new type would silently re-key every variable a plugin addressed by an
	/// explicit definition id.
	/// </summary>
	private sealed record LegacyVariableDescriptor
	{
		public string? Name { get; init; }

		public string? Type { get; init; }

		public int? DecimalPlaces { get; init; }

		public double? RefreshIntervalSeconds { get; init; }

		public string? DefinitionId { get; init; }

		public LocalizedText? DisplayName { get; init; }

		public string? ConfigurationKey { get; init; }

		public LocalizedText? ConfigurationName { get; init; }
	}

	private sealed record PersistedIcon(
		byte[] IconBytes,
		string IconMimeType,
		bool HasIcon,
		string IconContentHash,
		string IconBytesContentHash);
}
