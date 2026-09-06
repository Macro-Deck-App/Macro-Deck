using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class TestVariableIntegration(
	IReadOnlyList<VariableDefinition> variables,
	bool dependsOnConfiguration = false,
	Func<string, CancellationToken, ValueTask<VariableReading>>? read = null,
	Func<string, object?, CancellationToken, ValueTask<VariableWriteResult>>? write = null)
	: IPluginIntegration, IVariableProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables { get; } = variables;

	public bool VariablesDependOnConfiguration { get; } = dependsOnConfiguration;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> read?.Invoke(localId, cancellationToken) ?? ValueTask.FromResult(VariableReading.Unavailable);

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> write?.Invoke(localId, value, cancellationToken) ?? ValueTask.FromResult(VariableWriteResult.NotWritable());
}

internal sealed class TestVariableCatalogIntegration(
	string catalogName,
	bool supportsPush = false,
	bool supportsSearch = false,
	Func<VariableCatalogQuery, CancellationToken, ValueTask<VariableCatalogPage>>? discover = null,
	Func<string, CancellationToken, ValueTask<VariableDefinition?>>? resolve = null,
	Func<string, CancellationToken, ValueTask<VariableReading>>? read = null,
	Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlyList<VariableValue>>>? subscribe = null,
	Func<string, object?, CancellationToken, ValueTask<VariableWriteResult>>? write = null)
	: IPluginIntegration, IVariableProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables { get; } = [];

	public bool SupportsCatalog => true;

	public string CatalogName { get; } = catalogName;

	public bool SupportsPush { get; } = supportsPush;

	public bool SupportsSearch { get; } = supportsSearch;

	public List<IReadOnlyCollection<string>> SubscribeCalls { get; } = [];

	public ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
		=> discover?.Invoke(query, cancellationToken) ?? ValueTask.FromResult(VariableCatalogPage.Empty);

	public ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
		=> resolve?.Invoke(localId, cancellationToken) ?? ValueTask.FromResult<VariableDefinition?>(null);

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> read?.Invoke(localId, cancellationToken) ?? ValueTask.FromResult(VariableReading.Unavailable);

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> write?.Invoke(localId, value, cancellationToken) ?? ValueTask.FromResult(VariableWriteResult.NotWritable());

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken = default)
	{
		SubscribeCalls.Add(localIds);

		return subscribe?.Invoke(localIds, cancellationToken) ??
			ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);
	}
}

internal class TestEventIntegration(string providerName, params EventDefinition[] events)
	: IPluginIntegration, IEventProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; } = providerName;

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } = events;
}

internal sealed class TestDynamicEventIntegration(string providerName, params EventDefinition[] events)
	: TestEventIntegration(providerName, events), IDynamicEventOptionsProvider
{
	public DynamicOptionsResult ResultToReturn { get; set; } = new() { Options = [] };

	public EventOptionsContext? LastContext { get; private set; }

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		LastContext = context;
		return Task.FromResult(ResultToReturn);
	}
}

internal sealed class TestIssueIntegration(
	Func<CancellationToken, Task<IReadOnlyList<IntegrationIssue>>> issues,
	Func<string, CancellationToken, Task<IssueResolution>>? resolve = null)
	: IPluginIntegration, IIntegrationIssueProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> issues(cancellationToken);

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> resolve?.Invoke(issueId, cancellationToken) ??
			Task.FromResult(IssueResolution.Failed("Not resolvable in this test."));
}

internal class TestMusicPlayer : IMusicPlayer
{
	public MusicPlayerState StateToReturn { get; set; } = MusicPlayerState.Disconnected;

	public MusicPlayerArtwork? ArtworkToReturn { get; set; }

	public List<string> Calls { get; } = [];

	public Exception? ThrowOnState { get; set; }

	public Func<CancellationToken, Task<MusicPlayerState>>? StateOverride { get; set; }

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (ThrowOnState is not null)
		{
			throw ThrowOnState;
		}

		return StateOverride?.Invoke(cancellationToken) ?? Task.FromResult(StateToReturn);
	}

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

internal sealed class TestMusicPlayerWithCatalog : TestMusicPlayer, ICatalogMusicPlayer
{
	public IReadOnlyList<MusicPlayerCatalogItem> ItemsToReturn { get; set; } = [];

	public Exception? ThrowOnCatalog { get; set; }

	public Func<CancellationToken, Task<IReadOnlyList<MusicPlayerCatalogItem>>>? CatalogOverride { get; set; }

	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (ThrowOnCatalog is not null)
		{
			throw ThrowOnCatalog;
		}

		return CatalogOverride?.Invoke(cancellationToken) ?? Task.FromResult(ItemsToReturn);
	}

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		Calls.Add($"play-item:{item.Id}");
		return Task.CompletedTask;
	}
}

internal sealed class TestMusicPlayerWithDevices : TestMusicPlayer, IMusicPlayerDeviceProvider
{
	public IReadOnlyList<MusicPlayerDevice> DevicesToReturn { get; set; } = [];

	public Exception? ThrowOnDevices { get; set; }

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> ThrowOnDevices is not null ? throw ThrowOnDevices : Task.FromResult(DevicesToReturn);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		Calls.Add($"transfer:{deviceId}:{startPlayback}");
		return Task.CompletedTask;
	}
}

internal sealed class TestMusicPlayerWithCatalogAndDevices : TestMusicPlayer, ICatalogMusicPlayer,
	IMusicPlayerDeviceProvider
{
	public IReadOnlyList<MusicPlayerCatalogItem> ItemsToReturn { get; set; } = [];

	public Exception? ThrowOnCatalog { get; set; }

	public Func<CancellationToken, Task<IReadOnlyList<MusicPlayerCatalogItem>>>? CatalogOverride { get; set; }

	public IReadOnlyList<MusicPlayerDevice> DevicesToReturn { get; set; } = [];

	public Exception? ThrowOnDevices { get; set; }

	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (ThrowOnCatalog is not null)
		{
			throw ThrowOnCatalog;
		}

		return CatalogOverride?.Invoke(cancellationToken) ?? Task.FromResult(ItemsToReturn);
	}

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		Calls.Add($"play-item:{item.Id}");
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> ThrowOnDevices is not null ? throw ThrowOnDevices : Task.FromResult(DevicesToReturn);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		Calls.Add($"transfer:{deviceId}:{startPlayback}");
		return Task.CompletedTask;
	}
}

internal sealed class TestMusicPlayerIntegration(string providerName, IReadOnlyDictionary<string, IMusicPlayer> players)
	: IPluginIntegration, IMusicPlayerProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; } = providerName;

	public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
		[.. players.Keys.Select(id => new MusicPlayerInstance(id, id))];

	public IMusicPlayer? GetPlayer(string instanceId) => players.GetValueOrDefault(instanceId);
}

internal sealed class TestWeatherStation : IWeatherStation
{
	public WeatherSnapshot SnapshotToReturn { get; set; } = WeatherSnapshot.Unavailable();

	public Func<CancellationToken, Task<WeatherSnapshot>>? SnapshotOverride { get; set; }

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) =>
		SnapshotOverride?.Invoke(ct) ?? Task.FromResult(SnapshotToReturn);
}

internal sealed class TestWeatherIntegration(string providerName, IReadOnlyDictionary<string, IWeatherStation> stations)
	: IPluginIntegration, IWeatherProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; } = providerName;

	public IReadOnlyList<WeatherStationInstance> GetInstances()
		=> [.. stations.Keys.Select(id => new WeatherStationInstance(id, id))];

	public IWeatherStation? GetStation(string instanceId) => stations.GetValueOrDefault(instanceId);
}

internal sealed class TestProfileIntegration(string providerName, IReadOnlyList<VirtualProfileDescriptor> profiles)
	: IPluginIntegration, IProfileProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName { get; } = providerName;

	public (string ProfileId, string FolderId, string WidgetId, WidgetInteraction Interaction)? LastInteraction
	{
		get;
		private set;
	}

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => profiles;

	public Task HandleWidgetInteractionAsync(string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction)
	{
		LastInteraction = (profileId, folderId, widgetId, interaction);
		return Task.CompletedTask;
	}
}

internal sealed class TestConfigFlow : IConfigFlow
{
	public ConfigFlowResult ResultToReturn { get; set; }
		= ConfigFlowResult.Step(new ConfigFlowStep { StepId = "step", Fields = [] });

	public Func<IConfigFlowContext, CancellationToken, Task<ConfigFlowResult>>? StartOverride { get; set; }

	public Func<string, IReadOnlyDictionary<string, object?>, IConfigFlowContext, CancellationToken,
		Task<ConfigFlowResult>>? SubmitOverride { get; set; }

	public List<string> Calls { get; } = [];

	public IReadOnlyDictionary<string, object?>? LastInput { get; private set; }

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		Calls.Add("start");
		return StartOverride?.Invoke(context, cancellationToken) ?? Task.FromResult(ResultToReturn);
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		Calls.Add($"submit:{stepId}");
		LastInput = input;
		return SubmitOverride?.Invoke(stepId, input, context, cancellationToken) ?? Task.FromResult(ResultToReturn);
	}
}

internal sealed class ThrowingTestConfigFlow : IConfigFlow
{
	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> throw new InvalidOperationException("boom: token=abc123");

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> throw new InvalidOperationException("boom: token=abc123");
}

internal sealed class TestConfigFlowIntegration(Func<IConfigFlow> factory, bool allowsMultipleConfigurations = true)
	: IPluginIntegration, IConfigFlowProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public bool AllowsMultipleConfigurations { get; } = allowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => factory();
}

internal sealed class TestConfigFlowContext : IConfigFlowContext
{
	public IOAuthSession OAuth { get; init; } = new TestOAuthSession();

	private sealed class TestOAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://127.0.0.1:9696/api/integrations/oauth/callback";

		public string State => "state-1";

		public string? AuthorizationCode => null;
	}
}

internal sealed class TestMigrationIntegration(params IIntegrationMigration[] migrations)
	: IPluginIntegration, IMigrationProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = migrations;
}

internal sealed class TestIntegrationMigration : IIntegrationMigration
{
	public MigrationSource Source { get; init; } = MigrationSource.MacroDeck2;

	public IReadOnlyList<string> ClaimedActionSources { get; init; } = [];

	public IReadOnlyList<string> ClaimedSettingsSources { get; init; } = [];

	public Func<ForeignAction, ActionMigrationResult?>? TranslateAction { get; init; }

	public Func<ForeignPluginSettings, IReadOnlyList<MigratedConfiguration>>? TranslateConfiguration { get; init; }

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(TranslateAction is null ? null : TranslateAction(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(TranslateConfiguration is null ? [] : TranslateConfiguration(settings));
}
