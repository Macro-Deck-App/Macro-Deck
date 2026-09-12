using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// The fixture every device-surface test shares: the profile tree the acceptance criteria name, an
/// in-memory device repository, and a recording provider standing in for the hardware end of a session.
/// Only the provider, the trigger pipeline entry point and the clock are doubles - the surface service,
/// its builder, the press tracker and the interaction router are the real ones.
/// </summary>
internal sealed class DeviceSurfaceFixture : IDisposable
{
	public const string StudioProfileId = "studio";
	public const string StageProfileId = "stage";
	public const string HomeFolderId = "home";
	public const string LightsFolderId = "lights";
	public const string ScenesFolderId = "scenes";
	public const string AudioFolderId = "audio";
	public const string StageHomeFolderId = "stage-home";

	public const string DeckLayoutReference = "com.example.deck::3x2";

	/// <summary>Well past any coalescing the service performs, and past the long-press threshold, so a
	/// test that only wants the settled surface never encodes either window.</summary>
	private static readonly TimeSpan _settle = TimeSpan.FromSeconds(5);

	private readonly ServiceProvider _services;

	public DeviceSurfaceFixture(IIconService? icons = null)
	{
		Time = new ManualTimeProvider();
		Profiles = new FakeProfileRegistry();
		Devices = new InMemoryDeviceRepository();
		Provider = new RecordingSurfaceProvider("test-provider");
		Triggers = new RecordingTriggerHandler();
		WidgetStates = new FakeWidgetStateService();
		WidgetIcons = new FakeWidgetIconService();
		Labels = new FakeLabelTextService();
		Icons = new StubIconPackCache();
		Resources = new UiResourceStore();
		Presence = new ProviderDevicePresenceTracker();
		Focus = new RecordingApplicationFocusCoordinator();
		Bus = new RecordingEventBus();
		LockState = new FakeHostLockState();

		SeedProfiles();

		var services = new ServiceCollection();
		services.AddSingleton<IDeviceRepository>(Devices);
		services.AddSingleton<IProfileRegistry>(Profiles);
		services.AddSingleton<IWidgetStateService>(WidgetStates);
		services.AddSingleton<IWidgetIconService>(WidgetIcons);
		services.AddSingleton<ILabelTextService>(Labels);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		services.AddSingleton<IIconPackCache>(Icons);
		services.AddSingleton<IUiResourceStore>(Resources);
		services.AddSingleton<DeviceSurfaceBuilder>();
		if (icons is not null)
		{
			services.AddSingleton(icons);
		}

		services
			.AddSingleton<IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest,
				ExecuteActionButtonTriggerResponse>>(Triggers);
		services.AddSingleton<IUiSessionBroker>(UiBroker);
		services.AddSingleton<IWidgetUiSessionOpener>(UiOpener);
		_services = services.BuildServiceProvider();

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		Service = new DeviceSurfaceService(scopeFactory,
			new SingleProviderResolver(Provider),
			Profiles,
			Presence,
			new DeviceInteractionRouter(scopeFactory, LockState, Time, Serilog.Core.Logger.None),
			Focus,
			Bus,
			new WidgetStateSubscriptionTracker(),
			new LabelSubscriptionTracker(),
			Time,
			Serilog.Core.Logger.None);
	}

	public ManualTimeProvider Time { get; }

	public FakeProfileRegistry Profiles { get; }

	public InMemoryDeviceRepository Devices { get; }

	public RecordingSurfaceProvider Provider { get; }

	public RecordingTriggerHandler Triggers { get; }

	public FakeWidgetStateService WidgetStates { get; }

	public FakeWidgetIconService WidgetIcons { get; }

	public FakeLabelTextService Labels { get; }

	public StubIconPackCache Icons { get; }

	public UiResourceStore Resources { get; }

	public ProviderDevicePresenceTracker Presence { get; }

	public RecordingApplicationFocusCoordinator Focus { get; }

	public RecordingEventBus Bus { get; }

	public FakeHostLockState LockState { get; }

	public RecordingUiSessionBroker UiBroker { get; } = new();

	public RecordingWidgetUiSessionOpener UiOpener { get; } = new();

	public DeviceSurfaceService Service { get; }

	public Folder Home { get; private set; } = null!;

	public Folder Lights { get; private set; } = null!;

	public Folder Scenes { get; private set; } = null!;

	public Folder Audio { get; private set; } = null!;

	public Folder StageHome { get; private set; } = null!;

	/// <summary>An action button whose stored data carries a press flow, a label and an icon.</summary>
	public static Widget Button(
		string id,
		int x,
		int y,
		int width = 1,
		int height = 1,
		string label = "Button",
		string? iconId = null,
		bool withFlows = true,
		bool isPinned = false,
		PinScope pinScope = PinScope.Subtree)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = label };
		if (iconId is not null)
		{
			data["iconId"] = iconId;
		}

		if (withFlows)
		{
			data["flows"] = new[]
			{
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["triggerType"] = "onShortPress", ["nodes"] = Array.Empty<object>()
				}
			};
		}

		return new Widget
		{
			Id = id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = width,
			Height = height,
			IsPinned = isPinned,
			PinScope = pinScope,
			Data = JsonSerializer.Serialize(data)
		};
	}

	/// <summary>An action button in state mode with an <c>off</c> and an <c>on</c> face.</summary>
	public static Widget StatefulButton(string id, int x, int y, string offIconId, string onIconId)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["stateMode"] = true,
			["activeStateId"] = "off",
			["states"] = new[]
			{
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["id"] = "off",
					["label"] = "Idle",
					["appearance"] = new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["label"] = "Idle", ["iconId"] = offIconId
					}
				},
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["id"] = "on",
					["label"] = "Recording",
					["appearance"] = new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["label"] = "Recording", ["iconId"] = onIconId
					}
				}
			},
			["flows"] = new[]
			{
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["triggerType"] = "onShortPress", ["nodes"] = Array.Empty<object>()
				}
			}
		};

		return new Widget
		{
			Id = id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = 1,
			Height = 1,
			Data = JsonSerializer.Serialize(data)
		};
	}

	public Guid AddDevice(string providerDeviceId, string? startupProfileId = StudioProfileId, int keyCount = 6)
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = "hash",
			Name = providerDeviceId,
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			ProviderId = Provider.ProviderId,
			ProviderDeviceId = providerDeviceId,
			LayoutReference = DeckLayoutReference,
			// Deliberately present and deliberately never consulted: the surface is the folder's grid.
			Capabilities = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["keyCount"] = keyCount
			}),
			StartupProfileId = startupProfileId,
			LastSeenAt = Time.GetUtcNow().UtcDateTime,
			CreatedAt = Time.GetUtcNow().UtcDateTime
		};

		Devices.Devices.Add(device);
		Presence.Set(device.Id, online: true);
		return device.Id;
	}

	public async Task<Guid> OpenDeviceAsync(string providerDeviceId = "DeckA", string? profileId = StudioProfileId)
	{
		var deviceId = AddDevice(providerDeviceId, profileId);
		await Service.OpenAsync(deviceId, Provider.ProviderId, providerDeviceId);
		return deviceId;
	}

	public Task<bool> ChangeToAsync(Guid deviceId, string folderId, string? profileId = null)
		=> Service.NavigateAsync(deviceId, DeckNavigationCommand.ChangeTo(folderId, profileId));

	public Task<bool> ParentAsync(Guid deviceId)
		=> Service.NavigateAsync(deviceId, new DeckNavigationCommand { Command = DeckNavigationCommands.Parent });

	public Task<bool> BackAsync(Guid deviceId)
		=> Service.NavigateAsync(deviceId, new DeckNavigationCommand { Command = DeckNavigationCommands.Back });

	/// <summary>Advances the clock past any coalescing window and lets the resulting work run.</summary>
	public async Task SettleAsync()
	{
		Time.Advance(_settle);
		await DrainAsync();
	}

	/// <summary>Lets already-scheduled continuations run without moving the clock.</summary>
	public static async Task DrainAsync()
	{
		for (var i = 0; i < 16; i++)
		{
			await Task.Yield();
		}
	}

	/// <summary>Applies a change to the profile tree and settles, the way a repository write plus the
	/// host's own invalidation would.</summary>
	public async Task MutateAsync(Action mutate)
	{
		mutate();
		await Service.InvalidateAsync();
		await SettleAsync();
	}

	public void SetFolders(string profileId, params Folder[] folders) => Profiles.SetFolders(profileId, folders);

	public void Dispose()
	{
		Service.Dispose();
		_services.Dispose();
	}

	private void SeedProfiles()
	{
		Profiles.AddProfile(StudioProfileId,
			"Studio",
			defaultRows: 3,
			defaultColumns: 5,
			backgroundColor: "#101010",
			widgetSpacing: 8,
			widgetBorderRadius: 12);
		Profiles.AddProfile(StageProfileId, "Stage", defaultRows: 2, defaultColumns: 2);

		Home = new Folder
		{
			Id = HomeFolderId, Name = "Home", ProfileId = StudioProfileId, IsDefault = true, Rows = 2, Columns = 3
		};
		Lights = new Folder
		{
			Id = LightsFolderId, Name = "Lights", ProfileId = StudioProfileId, ParentId = HomeFolderId, Order = 1
		};
		Scenes = new Folder
		{
			Id = ScenesFolderId,
			Name = "Scenes",
			ProfileId = StudioProfileId,
			ParentId = LightsFolderId,
			Order = 2,
			Rows = 4,
			WidgetSpacing = 2
		};
		Audio = new Folder
		{
			Id = AudioFolderId, Name = "Audio", ProfileId = StudioProfileId, ParentId = HomeFolderId, Order = 3
		};
		StageHome = new Folder
		{
			Id = StageHomeFolderId, Name = "Stage Home", ProfileId = StageProfileId, IsDefault = true
		};

		Profiles.SetFolders(StudioProfileId, Home, Lights, Scenes, Audio);
		Profiles.SetFolders(StageProfileId, StageHome);
	}

	private sealed class SingleProviderResolver : IDeviceSurfaceProviderResolver
	{
		private readonly IDeviceSurfaceProvider _provider;

		public SingleProviderResolver(IDeviceSurfaceProvider provider) => _provider = provider;

		public IDeviceSurfaceProvider? Resolve(string providerId)
			=> string.Equals(providerId, _provider.ProviderId, StringComparison.Ordinal) ? _provider : null;
	}
}

/// <summary>Stands in for the hardware end of a session, recording every surface the host pushes.</summary>
internal sealed class RecordingSurfaceProvider : IDeviceSurfaceProvider
{
	private readonly List<(string DeviceId, DeviceSurface Surface)> _pushes = [];
	private readonly Lock _sync = new();

	public RecordingSurfaceProvider(string providerId) => ProviderId = providerId;

	public string ProviderId { get; }

	public bool AcceptOpens { get; set; } = true;

	public List<DeviceSurfaceSessionDescriptor> Opens { get; } = [];

	public List<(string DeviceId, string? Reason)> Closes { get; } = [];

	public IReadOnlyList<(string DeviceId, DeviceSurface Surface)> Pushes
	{
		get
		{
			lock (_sync)
			{
				return [.. _pushes];
			}
		}
	}

	public IReadOnlyList<DeviceSurface> SurfacesFor(Guid deviceId)
	{
		var id = deviceId.ToString();
		return
		[
			.. Pushes.Where(push => string.Equals(push.DeviceId, id, StringComparison.Ordinal)).Select(p => p.Surface)
		];
	}

	public DeviceSurface Latest(Guid deviceId) => SurfacesFor(deviceId)[^1];

	public int PushCount(Guid deviceId) => SurfacesFor(deviceId).Count;

	public Task<bool> OpenAsync(DeviceSurfaceSessionDescriptor session, CancellationToken cancellationToken)
	{
		Opens.Add(session);
		return Task.FromResult(AcceptOpens);
	}

	public Task PushAsync(string deviceId, DeviceSurface surface, CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			_pushes.Add((deviceId, surface));
		}

		return Task.CompletedTask;
	}

	public Task CloseAsync(string deviceId, string? reason, CancellationToken cancellationToken)
	{
		Closes.Add((deviceId, reason));
		return Task.CompletedTask;
	}
}

internal sealed class RecordingWidgetUiSessionOpener : IWidgetUiSessionOpener
{
	public List<string?> OpenedWidgetIds { get; } = [];

	public string? RefusalCode { get; set; }

	public UiSessionOpenTicket Open(OpenWidgetUiSessionRequest request, string ownerPrincipal, bool isAdmin)
	{
		OpenedWidgetIds.Add(request.WidgetId);
		return RefusalCode is { } code
			? UiSessionOpenTicket.Rejected(code, "refused by the test")
			: UiSessionOpenTicket.Opened($"session-{OpenedWidgetIds.Count}");
	}
}

/// <summary>The host's trigger pipeline entry point, recording what a device session ran through it.</summary>
internal sealed class RecordingTriggerHandler
	: IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest, ExecuteActionButtonTriggerResponse>
{
	private readonly List<ExecuteActionButtonTriggerRequest> _requests = [];
	private readonly Lock _sync = new();

	public IReadOnlyList<ExecuteActionButtonTriggerRequest> Requests
	{
		get
		{
			lock (_sync)
			{
				return [.. _requests];
			}
		}
	}

	/// <summary>Every trigger run, in order, as (widget id, trigger type) pairs.</summary>
	public IReadOnlyList<(string WidgetId, string TriggerType)> Triggers
		=> [.. Requests.Select(request => (request.WidgetId, request.TriggerType))];

	public IReadOnlyList<string> TriggerTypes => [.. Requests.Select(request => request.TriggerType)];

	/// <summary>Stands in for what the real pipeline would run for a matched flow.</summary>
	public Func<ExecuteActionButtonTriggerRequest, Task>? Execute { get; set; }

	/// <summary>The answer the host's pipeline gives back, so a device sees a refused trigger as one.</summary>
	public ExecuteActionButtonTriggerResponse Response { get; set; } = new() { Success = true };

	public void Clear()
	{
		lock (_sync)
		{
			_requests.Clear();
		}
	}

	public async ValueTask<ExecuteActionButtonTriggerResponse> Handle(
		ExecuteActionButtonTriggerRequest request,
		CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			_requests.Add(request);
		}

		if (Execute is { } execute)
		{
			await execute(request);
		}

		return Response;
	}
}

internal sealed class FakeWidgetStateService : IWidgetStateService
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

/// <summary>No widget has an active icon provider until a test says otherwise, matching a deck with no
/// provider-capable action configured at all.</summary>
internal sealed class FakeWidgetIconService : IWidgetIconService
{
	private readonly Dictionary<Guid, WidgetIconResolution> _resolutions = [];

	public void Set(Guid widgetId, WidgetIconResolution resolution) => _resolutions[widgetId] = resolution;

	public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		=> Task.FromResult(_resolutions.GetValueOrDefault(widgetId, WidgetIconResolution.Inactive));

	public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;
}

internal sealed class FakeLabelTextService : ILabelTextService
{
	private readonly Dictionary<(Guid WidgetId, string State), string> _texts = [];

	public void Set(Guid widgetId, string state, string text) => _texts[(widgetId, state)] = text;

	public Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default)
		=> Task.FromResult(_texts.GetValueOrDefault((widgetId, state)));

	public Task<string?> ResolvePreview(LabelImagePreviewRequest request, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(null);
}

internal sealed class StubIconPackCache : IIconPackCache
{
	private readonly Dictionary<Guid, IconEntity> _icons = [];

	public void Add(IconEntity icon) => _icons[icon.Id] = icon;

	public IconEntity? GetIconById(Guid iconId) => _icons.GetValueOrDefault(iconId);

	public Task InitializeCache() => Task.CompletedTask;

	public IconPackEntity? GetPackById(Guid id) => null;

	public List<IconPackEntity> GetAllPacks() => [];

	public IconPackEntity? GetDefaultPack() => null;

	public Task AddOrUpdatePack(IconPackEntity pack) => Task.CompletedTask;

	public Task RemovePack(Guid id) => Task.CompletedTask;

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
