using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>An integration exposing whatever eager <see cref="IVariableProvider"/> shape a variables
/// handler test needs and nothing else - the catalog half is covered by
/// <c>VariableCatalogCapabilityHandlerTests</c>'s own fixtures.</summary>
internal sealed class TestVariableIntegration : IPluginIntegration, IVariableProvider
{
	private readonly Func<string, VariableReading>? _read;
	private readonly Func<string, object?, VariableWriteResult>? _write;

	public TestVariableIntegration(
		IReadOnlyList<VariableDefinition> variables,
		IReadOnlyList<VariableDefinition>? declaredVariables = null,
		bool dependsOnConfiguration = false,
		Func<string, VariableReading>? read = null,
		Func<string, object?, VariableWriteResult>? write = null)
	{
		Variables = variables;
		DeclaredVariables = declaredVariables ?? variables;
		VariablesDependOnConfiguration = dependsOnConfiguration;
		_read = read;
		_write = write;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables { get; }

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

	public bool VariablesDependOnConfiguration { get; }

	/// <summary>The last local id a caller asked for, so a test can inspect how the handler resolved it.</summary>
	public string? LastRequestedId { get; private set; }

	/// <summary>The last value a caller tried to write, so a test can inspect what crossed the boundary.</summary>
	public object? LastWrittenValue { get; private set; }

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		LastRequestedId = localId;
		return ValueTask.FromResult(_read?.Invoke(localId) ?? VariableReading.Unavailable);
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		LastRequestedId = localId;
		LastWrittenValue = value;
		return ValueTask.FromResult(_write?.Invoke(localId, value) ?? VariableWriteResult.Applied());
	}
}

/// <summary>An integration exposing whatever <see cref="IEventProvider"/> shape an events handler test
/// needs, optionally also <see cref="IDynamicEventOptionsProvider"/>.</summary>
internal class TestEventIntegration : IPluginIntegration, IEventProvider
{
	public TestEventIntegration(string providerName, params EventDefinition[] events)
	{
		ProviderName = providerName;
		EventDefinitions = events;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; }

	public IReadOnlyList<EventDefinition> EventDefinitions { get; }
}

/// <summary>A <see cref="TestEventIntegration"/> that also offers dynamic event options.</summary>
internal sealed class TestDynamicEventIntegration(string providerName, params EventDefinition[] events)
	: TestEventIntegration(providerName, events), IDynamicEventOptionsProvider
{
	public DynamicOptionsResult ResultToReturn { get; set; } = new() { Options = [] };

	/// <summary>The context the last call was handed, so a test can inspect what was bound.</summary>
	public EventOptionsContext? LastContext { get; private set; }

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		LastContext = context;
		return Task.FromResult(ResultToReturn);
	}
}

/// <summary>An integration exposing whatever <see cref="IIntegrationIssueProvider"/> shape an issues
/// handler test needs.</summary>
internal sealed class TestIssueIntegration : IPluginIntegration, IIntegrationIssueProvider
{
	private readonly Func<IReadOnlyList<IntegrationIssue>> _issues;
	private readonly Func<string, IssueResolution>? _resolve;

	public TestIssueIntegration(Func<IReadOnlyList<IntegrationIssue>> issues,
		Func<string, IssueResolution>? resolve = null)
	{
		_issues = issues;
		_resolve = resolve;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	/// <summary>The last issue id a caller asked to resolve, so a test can inspect it.</summary>
	public string? LastResolvedIssueId { get; private set; }

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(_issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		LastResolvedIssueId = issueId;
		return Task.FromResult(_resolve?.Invoke(issueId) ?? IssueResolution.Failed("Not resolvable in this test."));
	}
}

/// <summary>A minimal <see cref="IMusicPlayer"/> a music-player handler test drives, optionally also
/// offering a catalog and/or devices.</summary>
internal class TestMusicPlayer : IMusicPlayer
{
	public MusicPlayerState StateToReturn { get; set; } = MusicPlayerState.Disconnected;

	public MusicPlayerArtwork? ArtworkToReturn { get; set; }

	public List<string> Calls { get; } = [];

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(StateToReturn);

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
		=> Task.FromResult(ArtworkToReturn);

	public Task PlayAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("play");
		return Task.CompletedTask;
	}

	public Task PauseAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("pause");
		return Task.CompletedTask;
	}

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("toggle");
		return Task.CompletedTask;
	}

	public Task NextAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("next");
		return Task.CompletedTask;
	}

	public Task PreviousAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("previous");
		return Task.CompletedTask;
	}

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		Calls.Add($"seek:{position.TotalSeconds}");
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
	{
		Calls.Add($"volume:{volumePercent}");
		return Task.CompletedTask;
	}

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
	{
		Calls.Add($"shuffle:{enabled}");
		return Task.CompletedTask;
	}

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
	{
		Calls.Add($"repeat:{mode}");
		return Task.CompletedTask;
	}
}

/// <summary>A <see cref="TestMusicPlayer"/> that also offers a catalog it can play from. Throws
/// whatever <see cref="ThrowOnCatalog"/> is set to, rather than degrading - see
/// <see cref="IMusicPlayerCatalogProvider.GetCatalogAsync"/>'s doc comments on why that inversion is
/// load-bearing.</summary>
internal sealed class TestMusicPlayerWithCatalog : TestMusicPlayer, ICatalogMusicPlayer
{
	public IReadOnlyList<MusicPlayerCatalogItem> ItemsToReturn { get; set; } = [];

	public Exception? ThrowOnCatalog { get; set; }

	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
		=> ThrowOnCatalog is not null ? throw ThrowOnCatalog : Task.FromResult(ItemsToReturn);

	public MusicPlayerCatalogItem? PlayedItem { get; private set; }

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		PlayedItem = item;
		Calls.Add($"play-item:{item.Id}");
		return Task.CompletedTask;
	}
}

/// <summary>A <see cref="TestMusicPlayer"/> that also offers device switching. Throws on
/// <see cref="ThrowOnDevices"/> rather than degrading - see <see cref="TestMusicPlayerWithCatalog"/>'s
/// identical remarks.</summary>
internal sealed class TestMusicPlayerWithDevices : TestMusicPlayer, IMusicPlayerDeviceProvider
{
	public IReadOnlyList<MusicPlayerDevice> DevicesToReturn { get; set; } = [];

	public Exception? ThrowOnDevices { get; set; }

	public string? LastTransferDeviceId { get; private set; }

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> ThrowOnDevices is not null ? throw ThrowOnDevices : Task.FromResult(DevicesToReturn);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		LastTransferDeviceId = deviceId;
		Calls.Add($"transfer:{deviceId}:{startPlayback}");
		return Task.CompletedTask;
	}
}

/// <summary>
/// A bare <see cref="TestMusicPlayer"/> that also declares a non-interface <c>PlayItemAsync</c> - a
/// decoy that a wrong implementation dispatching via reflection/dynamic rather than the
/// <see cref="ICatalogMusicPlayer"/> gate would call anyway.
/// </summary>
internal sealed class TestMusicPlayerDecoy : TestMusicPlayer
{
	public bool WasCalled { get; private set; }

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		WasCalled = true;
		return Task.CompletedTask;
	}
}

/// <summary>
/// A player that can browse its catalog (<see cref="IMusicPlayerCatalogProvider"/>) but does not
/// implement <see cref="ICatalogMusicPlayer"/> - proves browsing stays gated on the former while
/// playback gates on the latter. The decoy <c>PlayItemAsync</c> catches a wrong gate that widens
/// playback to any browse-capable player.
/// </summary>
internal sealed class TestMusicPlayerBrowseOnly : TestMusicPlayer, IMusicPlayerCatalogProvider
{
	public IReadOnlyList<MusicPlayerCatalogItem> ItemsToReturn { get; set; } = [];

	public bool WasCalled { get; private set; }

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		WasCalled = true;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
		=> Task.FromResult(ItemsToReturn);
}

/// <summary>An integration exposing whatever <see cref="IMusicPlayerProvider"/> shape a music-player
/// handler test needs.</summary>
internal sealed class TestMusicPlayerIntegration : IPluginIntegration, IMusicPlayerProvider
{
	private readonly IReadOnlyDictionary<string, IMusicPlayer> _players;

	public TestMusicPlayerIntegration(string providerName, IReadOnlyDictionary<string, IMusicPlayer> players)
	{
		ProviderName = providerName;
		_players = players;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; }

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> [.. _players.Keys.Select(id => new MusicPlayerInstance(id, id))];

	public IMusicPlayer? GetPlayer(string instanceId) => _players.GetValueOrDefault(instanceId);
}

/// <summary>A minimal <see cref="IWeatherStation"/> a weather handler test drives.</summary>
internal sealed class TestWeatherStation : IWeatherStation
{
	public WeatherSnapshot SnapshotToReturn { get; set; } = WeatherSnapshot.Unavailable();

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(SnapshotToReturn);
}

/// <summary>An integration exposing whatever <see cref="IWeatherProvider"/> shape a weather handler
/// test needs.</summary>
internal sealed class TestWeatherIntegration : IPluginIntegration, IWeatherProvider
{
	private readonly IReadOnlyDictionary<string, IWeatherStation> _stations;

	public TestWeatherIntegration(string providerName, IReadOnlyDictionary<string, IWeatherStation> stations)
	{
		ProviderName = providerName;
		_stations = stations;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; }

	public IReadOnlyList<WeatherStationInstance> GetInstances()
		=> [.. _stations.Keys.Select(id => new WeatherStationInstance(id, id))];

	public IWeatherStation? GetStation(string instanceId) => _stations.GetValueOrDefault(instanceId);
}

/// <summary>An integration exposing whatever <see cref="IProfileProvider"/> shape a virtual-profiles
/// handler test needs.</summary>
internal sealed class TestProfileIntegration : IPluginIntegration, IProfileProvider
{
	private readonly IReadOnlyList<VirtualProfileDescriptor> _profiles;

	public TestProfileIntegration(string providerName, IReadOnlyList<VirtualProfileDescriptor> profiles)
	{
		ProviderName = providerName;
		_profiles = profiles;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; }

	/// <summary>The last interaction this provider was handed, so a test can inspect it - including
	/// whatever <c>profileId</c> was forwarded, which the remote path must carry faithfully.</summary>
	public (string ProfileId, string FolderId, string WidgetId, WidgetInteraction Interaction)? LastInteraction
	{
		get;
		private set;
	}

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => _profiles;

	public Task HandleWidgetInteractionAsync(string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction)
	{
		LastInteraction = (profileId, folderId, widgetId, interaction);
		return Task.CompletedTask;
	}
}

/// <summary>A minimal <see cref="IConfigFlow"/> a config-flow handler test drives, recording every call
/// it received so a multi-step test can assert state genuinely carried in the instance's own fields
/// (<see cref="StepCount"/>) rather than reconstructed per call.</summary>
internal class TestConfigFlow : IConfigFlow
{
	public int StepCount { get; private set; }

	public List<string> Calls { get; } = [];

	public IReadOnlyDictionary<string, object?>? LastInput { get; private set; }

	public IConfigFlowContext? LastContext { get; private set; }

	public ConfigFlowResult ResultToReturn { get; set; } = ConfigFlowResult.Complete("Test Entry");

	public Func<string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ConfigFlowResult>>? SubmitOverride
	{
		get;
		set;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		StepCount++;
		Calls.Add("start");
		LastContext = context;
		return Task.FromResult(ResultToReturn);
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		StepCount++;
		Calls.Add($"submit:{stepId}");
		LastInput = input;
		LastContext = context;
		return SubmitOverride?.Invoke(stepId, input, cancellationToken) ?? Task.FromResult(ResultToReturn);
	}
}

/// <summary>A <see cref="TestConfigFlow"/> that also tracks disposal, for the abandon/idle-timeout
/// release assertions.</summary>
internal sealed class DisposableTestConfigFlow : TestConfigFlow, IAsyncDisposable
{
	public bool Disposed { get; private set; }

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}
}

/// <summary>An integration exposing whatever <see cref="IConfigFlowProvider"/> shape a config-flow
/// handler test needs. <paramref name="factory"/> is called once per <c>CreateConfigFlow</c>, mirroring
/// how a real integration hands back a fresh instance per setup session.</summary>
internal sealed class TestConfigFlowIntegration(Func<IConfigFlow> factory, bool allowsMultipleConfigurations = true)
	: IPluginIntegration, IConfigFlowProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public bool AllowsMultipleConfigurations { get; } = allowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => factory();
}
