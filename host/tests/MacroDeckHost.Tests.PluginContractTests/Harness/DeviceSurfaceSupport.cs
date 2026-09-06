using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Domain.Icons;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>Walks a serialized frame so a trust-boundary assertion can be made about every key and
/// every string value it carries, at any depth, rather than about a typed view of it.</summary>
internal static class JsonFrame
{
	public static IReadOnlyList<string> Keys(JsonElement element)
	{
		var keys = new List<string>();
		Walk(element, keys, values: null);
		return keys;
	}

	public static IReadOnlyList<string> StringValues(JsonElement element)
	{
		var values = new List<string>();
		Walk(element, keys: null, values);
		return values;
	}

	private static void Walk(JsonElement element, List<string>? keys, List<string>? values)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					keys?.Add(property.Name);
					Walk(property.Value, keys, values);
				}

				break;

			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					Walk(item, keys, values);
				}

				break;

			case JsonValueKind.String:
				values?.Add(element.GetString()!);
				break;
		}
	}
}

/// <summary>An <see cref="IDeviceSurfaceService" /> that records what the host callback router asked of
/// it. The service's own behaviour is a host concern proven by its unit tests; what a contract test has
/// to prove is that a plugin's call reaches it at all, for the right device, and comes back correctly.</summary>
internal sealed class RecordingDeviceSurfaceService : IDeviceSurfaceService
{
	public List<(Guid DeviceId, DeviceInteraction Interaction)> Interactions { get; } = [];

	public List<(Guid DeviceId, string IconId, int? Size, string? KnownETag)> IconRequests { get; } = [];

	public List<(Guid DeviceId, string WidgetId, string? KnownETag)> WidgetIconRequests { get; } = [];

	public List<(Guid DeviceId, string? Reason)> Closes { get; } = [];

	public DeviceInteractionOutcome NextOutcome { get; set; } = DeviceInteractionOutcome.Accepted;

	public DeviceIconImage? NextIcon { get; set; }

	public DeviceWidgetIconImage? NextWidgetIcon { get; set; }

	public bool IsOpen(Guid deviceId) => true;

	public Task OpenAsync(Guid deviceId,
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default)
	{
		Closes.Add((deviceId, reason));
		return Task.CompletedTask;
	}

	public Task<bool> NavigateAsync(Guid deviceId,
		DeckNavigationCommand command,
		CancellationToken cancellationToken = default) => Task.FromResult(true);

	public Task<DeviceInteractionOutcome> SubmitInteractionAsync(Guid deviceId,
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default)
	{
		Interactions.Add((deviceId, interaction));
		return Task.FromResult(NextOutcome);
	}

	public Task<DeviceIconImage?> GetIconAsync(Guid deviceId,
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		IconRequests.Add((deviceId, iconId, size, knownETag));
		return Task.FromResult(NextIcon);
	}

	public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(Guid deviceId,
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		WidgetIconRequests.Add((deviceId, widgetId, knownETag));
		return Task.FromResult(NextWidgetIcon);
	}

	public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

/// <summary>A one-profile, one-folder deck the real <see cref="DeviceSurfaceBuilder" /> can project.</summary>
internal sealed class ContractProfileRegistry : IProfileRegistry
{
	private readonly List<Profile> _profiles = [];
	private readonly Dictionary<string, List<Folder>> _folders = new(StringComparer.Ordinal);

	public void AddProfile(Profile profile) => _profiles.Add(profile);

	public void SetFolders(string profileId, params Folder[] folders) => _folders[profileId] = [.. folders];

	public IReadOnlyList<Profile> GetProfiles() => _profiles;

	public IReadOnlyList<Folder> GetFoldersForProfile(string profileId)
		=> _folders.TryGetValue(profileId, out var folders) ? folders : [];

	public bool IsVirtual(string profileId) => profileId.Contains("::", StringComparison.Ordinal);

	public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction)
		=> Task.FromResult(false);
}

internal sealed class ContractWidgetStates : IWidgetStateService
{
	private readonly Dictionary<Guid, WidgetStateResolution> _states = [];

	public void Set(Guid widgetId, string stateId, string label)
		=> _states[widgetId] = new WidgetStateResolution(stateId,
			LocalizedText.FromLiteral(label),
			[],
			ProviderSetChanged: false);

	public Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		=> Task.FromResult(_states.GetValueOrDefault(widgetId));

	public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;
}

internal sealed class ContractWidgetIcons : IWidgetIconService
{
	private readonly Dictionary<Guid, WidgetIconResolution> _resolutions = [];

	public void Set(Guid widgetId, WidgetIconResolution resolution) => _resolutions[widgetId] = resolution;

	public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		=> Task.FromResult(_resolutions.GetValueOrDefault(widgetId, WidgetIconResolution.Inactive));

	public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;
}

internal sealed class ContractLabelText : ILabelTextService
{
	public Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(null);

	public Task<string?> ResolvePreview(LabelImagePreviewRequest request, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(null);
}

internal sealed class ContractIconPacks : IIconPackCache
{
	public Task InitializeCache() => Task.CompletedTask;

	public IconPackEntity? GetPackById(Guid id) => null;

	public List<IconPackEntity> GetAllPacks() => [];

	public IconPackEntity? GetDefaultPack() => null;

	public Task AddOrUpdatePack(IconPackEntity pack) => Task.CompletedTask;

	public Task RemovePack(Guid id) => Task.CompletedTask;

	public IconEntity? GetIconById(Guid iconId) => null;

	public List<IconEntity> GetIconsByPackId(Guid packId) => [];

	public List<IconEntity> GetIconsByBatchId(Guid batchId) => [];

	public List<IconEntity> GetIconsByState(params IconProcessingState[] states) => [];

	public int GetIconCount(Guid packId) => 0;

	public IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null) => null;

	public IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null) => null;

	public List<IconEntity> GetIconsMissingMasterContentHash() => [];

	public Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons) => Task.CompletedTask;

	public Task UpdateIcon(IconEntity icon) => Task.CompletedTask;

	public Task RemoveIcon(Guid iconId) => Task.CompletedTask;

	public Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds) => Task.CompletedTask;

	public Task FlushPendingWrites() => Task.CompletedTask;
}

/// <summary>Answers the active culture and refuses everything else, so a preference reached by accident
/// fails loudly instead of quietly standing in for something.</summary>
internal sealed class ContractAppPreferences : IAppPreferenceService
{
	public Task<LocalizationSettings> GetLocalization() => Task.FromResult(new LocalizationSettings("en"));

	public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

	public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

	public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor)
		=> throw new NotSupportedException();

	public Task<Guid> GetInstallationId() => throw new NotSupportedException();

	public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

	public Task<LoggingSettings> SetLogging(LogEntryLevel? minimumLevel) => throw new NotSupportedException();

	public Task<NetworkSettings> GetNetwork() => throw new NotSupportedException();

	public Task<NetworkSettings> SetNetwork(int? publicPort,
		bool? tlsEnabled = null,
		string? tlsMode = null,
		int? tlsHttpsPort = null) => throw new NotSupportedException();

	public Task<AdbSettings> GetAdb() => throw new NotSupportedException();

	public Task<AdbSettings> SetAdb(bool? enabled,
		string? executablePath,
		bool? usbConnectionsEnabled,
		string? defaultDeviceSerial) => throw new NotSupportedException();

	public Task<DeveloperSettings> GetDeveloper() => throw new NotSupportedException();

	public Task<DeveloperSettings> SetDeveloper(bool? enabled) => throw new NotSupportedException();

	public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

	public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

	public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

	public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

	public Task<BackupSettings> GetBackups() => throw new NotSupportedException();

	public Task<BackupSettings> SetBackups(string? scheduleFrequency,
		string? scheduleTimeOfDay,
		string? scheduleDayOfWeek,
		int? scheduleDayOfMonth,
		string? retentionPolicy,
		int? retentionKeepLatest,
		bool? beforeHostUpdate,
		bool? beforePluginUpdate) => throw new NotSupportedException();

	public Task<DateTimeOffset?> GetBackupScheduleLastRun() => throw new NotSupportedException();

	public Task SetBackupScheduleLastRun(DateTimeOffset value) => throw new NotSupportedException();

	public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

	public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
		bool? checkForUpdates,
		bool? notifyOnUpdates,
		int? refreshIntervalMinutes) => throw new NotSupportedException();
}

/// <summary>The deck the contract tests project: one profile, one folder, and a widget whose stored data
/// carries everything a provider must never be handed.</summary>
internal static class ContractDeck
{
	public const string ProfileId = "studio";
	public const string FolderId = "home";
	public const string LightsFolderId = "lights";

	/// <summary>The device's own layout reference, echoed back verbatim. It says 3x2; the surface's grid
	/// is the profile's, and the two are deliberately different so a test cannot confuse them.</summary>
	public const string LayoutReference = "com.example.contract::3x2";

	public static readonly Guid WidgetId = new("11111111-2222-3333-4444-555555555555");

	public static readonly Guid LightsWidgetId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

	public static readonly Guid IconId = new("66666666-7777-8888-9999-aaaaaaaaaaaa");

	/// <summary>Stored widget data of the shape the host persists: the flow definitions, the state
	/// mapping and a stored secret reference all live here and none of them may travel.</summary>
	public static readonly string WidgetData = JsonSerializer.Serialize(
		new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["label"] = "Record",
			["iconId"] = IconId.ToString(),
			["secretHash"] = "sh_do_not_ship",
			["stateMapping"] = new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["rules"] = new[]
				{
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["id"] = "r1", ["stateId"] = "on",
						["when"] = new Dictionary<string, object?>(StringComparer.Ordinal)
					}
				}
			},
			["flows"] = new[]
			{
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["triggerType"] = "onShortPress",
					["nodes"] = new[]
					{
						new Dictionary<string, object?>(StringComparer.Ordinal)
							{ ["id"] = "n1", ["actionId"] = "secret" }
					}
				}
			}
		});

	public static DeviceSurfaceBuilder Builder()
	{
		var profiles = Registry();
		return new DeviceSurfaceBuilder(profiles,
			new ContractWidgetStates(),
			new ContractWidgetIcons(),
			new ContractLabelText(),
			new LocalizationResolver(new LocalizationCatalogRegistry()),
			new ContractAppPreferences(),
			new ContractIconPacks());
	}

	/// <summary>The deck itself: a Studio profile whose default grid is 3x5, a Home folder that inherits
	/// it, and a Lights folder to navigate to.</summary>
	public static ContractProfileRegistry Registry()
	{
		var profiles = new ContractProfileRegistry();
		profiles.AddProfile(new Profile
		{
			Id = ProfileId, Name = "Studio", DefaultRows = 3, DefaultColumns = 5, DefaultBackgroundColor = "#101010"
		});

		var home = new Folder { Id = FolderId, Name = "Home", ProfileId = ProfileId, IsDefault = true };
		home.Widgets.Add(new Widget
		{
			Id = WidgetId.ToString(),
			Type = WidgetTypeIds.ActionButton,
			PositionX = 1,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = WidgetData
		});

		var lights = new Folder
		{
			Id = LightsFolderId, Name = "Lights", ProfileId = ProfileId, ParentId = FolderId, Order = 1
		};
		lights.Widgets.Add(new Widget
		{
			Id = LightsWidgetId.ToString(),
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 1,
			Width = 1,
			Height = 1,
			Data = WidgetData
		});

		profiles.SetFolders(ProfileId, home, lights);
		return profiles;
	}

	public static DeviceEntity Device(Guid deviceId, string providerId, string providerDeviceId)
		=> new()
		{
			Id = deviceId,
			SecretHash = string.Empty,
			Name = providerDeviceId,
			ClientType = DeviceClientType.Provider,
			ProviderId = providerId,
			ProviderDeviceId = providerDeviceId,
			LayoutReference = LayoutReference,
			StartupProfileId = ProfileId,
			CreatedAt = DateTime.UnixEpoch,
			LastSeenAt = DateTime.UnixEpoch
		};
}
