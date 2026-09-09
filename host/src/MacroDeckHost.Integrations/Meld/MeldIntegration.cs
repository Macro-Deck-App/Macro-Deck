using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Meld.Actions;
using MacroDeckHost.Integrations.Meld.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Meld;

[MacroDeckIntegration]
public sealed class MeldIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IVariableRefreshSignalConsumer,
		IDisposable
{
	public const string IntegrationId = MeldObjects.IntegrationId;

	internal const string WrongEndpointIssueId = "wrong-endpoint";

	private static readonly ILogger _logger = IntegrationLog.For<MeldIntegration>(MeldObjects.IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly VariableApiAccessor _variableAccessor = new();
	private readonly MeldVariableCatalog _catalog;

	private MeldConnection? _connection;
	private IVariableRefreshSignal? _refreshSignal;
	private MeldEventEmitter? _events;

	public MeldIntegration()
	{
		Actions = MeldActions.Create(() => _connection, _variableAccessor);
		_catalog = new MeldVariableCatalog(() => _connection);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Meld Studio";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => MeldEventDefinitions.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("meld_is_connected", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = MacroDeckStrings.Connection.Connected()
			},
		VariableDefinition.Eager("meld_is_streaming", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.IsStreaming()
			},
		VariableDefinition.Eager("meld_is_recording", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.IsRecording()
			},
		VariableDefinition.Eager("meld_current_scene", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.CurrentScene()
			},
		VariableDefinition.Eager("meld_current_scene_id", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.CurrentSceneId()
			},
		VariableDefinition.Eager("meld_staged_scene", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.StagedScene()
			},
		VariableDefinition.Eager("meld_staged_scene_id", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.StagedSceneId()
			},
		VariableDefinition.Eager("meld_api_version", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Meld.Variables.ApiVersion()
			}
	];

	public bool SupportsCatalog => true;

	public bool SupportsPush => MeldVariableCatalog.SupportsPush;

	public bool SupportsSearch => MeldVariableCatalog.SupportsSearch;

	public int? CatalogEntryCount => _catalog.CatalogEntryCount;

	public string CatalogName => MeldVariableCatalog.CatalogName;

	public byte[] GetIcon() => _icon;

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public IConfigFlow CreateConfigFlow() => new MeldConfigFlow();

	public ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
		=> _catalog.DiscoverAsync(query, cancellationToken);

	public async ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
	{
		var eager = Variables.FirstOrDefault(v => string.Equals(v.ResolvedId, localId, StringComparison.Ordinal));
		return eager ?? await _catalog.ResolveAsync(localId, cancellationToken).ConfigureAwait(false);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;
		_events = new MeldEventEmitter(context.Events);
		await ConnectFromConfig(context).ConfigureAwait(false);
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		_connection?.Dispose();
		_connection = null;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_connection?.Dispose();
		_connection = null;
	}

	// An eager id is "meld-<slot>" and a catalog id is "track/<track>[/<leaf>]", so the two halves of this
	// provider never collide and anything the eager switch does not claim belongs to the catalog.
	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var state = _connection?.State ?? MeldState.Disconnected;

		object? value = localId switch
		{
			"meld-is-connected" => state.IsConnected,
			"meld-is-streaming" => state.IsConnected ? state.IsStreaming : null,
			"meld-is-recording" => state.IsConnected ? state.IsRecording : null,
			"meld-current-scene" => state.IsConnected ? SceneName(state.Session, state.Session.CurrentSceneId) : null,
			"meld-current-scene-id" => state.IsConnected ? state.Session.CurrentSceneId : null,
			"meld-staged-scene" => state.IsConnected ? SceneName(state.Session, state.Session.StagedSceneId) : null,
			"meld-staged-scene-id" => state.IsConnected ? state.Session.StagedSceneId : null,
			"meld-api-version" => state.IsConnected ? state.ApiVersion : null,
			_ => null
		};

		return localId.StartsWith("meld-", StringComparison.Ordinal)
			? ValueTask.FromResult(VariableReading.Of(value))
			: _catalog.ReadAsync(localId, cancellationToken);
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> _catalog.SetValueAsync(localId, value, cancellationToken);

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _connection?.State.Session ?? MeldSession.Empty;

		var options = context.ParameterName switch
		{
			"sceneId" or "previousSceneId" => MeldOptions.Scenes(session),
			"layerId" => MeldOptions.Layers(session),
			"effectId" => MeldOptions.Effects(session),
			"trackId" => MeldOptions.Tracks(session),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 30
		});
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		// Cheap by contract: this is polled to render a badge. "Meld Studio is not running" is
		// deliberately not an issue - it is the normal state of a long-lived host, it recovers on its
		// own, and meld_is_connected already says so.
		IReadOnlyList<IntegrationIssue> issues = _connection?.NeedsSetup == true
			?
			[
				new IntegrationIssue
				{
					Id = WrongEndpointIssueId,
					Title = AppStrings.Integrations.Meld.Issues.WrongEndpointTitle(),
					Description = AppStrings.Integrations.Meld.Issues.WrongEndpointDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.Meld.Issues.OpenSetupAction()
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == WrongEndpointIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	private static string? SceneName(MeldSession session, string? sceneId)
		=> sceneId is not null && session.ScenesById.TryGetValue(sceneId, out var scene) ? scene.Name : null;

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		_connection?.Dispose();
		_connection = null;

		var entries = await context.Config.GetEntriesAsync().ConfigureAwait(false);
		var entry = entries.Count > 0 ? entries[0] : null;
		if (entry is null)
		{
			_logger.Information("Meld Studio not configured; skipping connection");
			return;
		}

		var host = await context.Config.GetStringAsync(entry.Id, MeldConfigKeys.Host).ConfigureAwait(false);
		var portRaw = await context.Config.GetStringAsync(entry.Id, MeldConfigKeys.Port).ConfigureAwait(false);

		if (string.IsNullOrEmpty(host) ||
			!int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
		{
			_logger.Warning("Meld Studio config entry {EntryId} is incomplete; skipping", entry.Id);
			return;
		}

		Uri endpoint;
		try
		{
			endpoint = MeldEndpoint.Build(host, port);
		}
		catch (UriFormatException ex)
		{
			_logger.Warning(ex,
				"Meld Studio config entry {EntryId} has an unusable host {Host}; skipping",
				entry.Id,
				host);
			return;
		}

		var events = _events;
		_connection = new MeldConnection(() => new QWebChannelClient(),
			endpoint,
			onState: state => events?.Observe(state),
			onTrackMute: (trackId, trackName, muted) => events?.ObserveTrackMute(trackId, trackName, muted),
			onReset: () => events?.Reset(),
			onVariablesChanged: () => _refreshSignal?.RequestEagerRefresh(IntegrationId));
		_connection.Start();
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(MeldIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("meld-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
