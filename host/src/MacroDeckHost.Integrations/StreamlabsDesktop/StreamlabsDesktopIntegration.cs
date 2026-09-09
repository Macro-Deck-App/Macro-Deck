using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.StreamlabsDesktop.Actions;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

[MacroDeckIntegration]
public sealed class StreamlabsDesktopIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IVariableRefreshSignalConsumer,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.streamlabs-desktop";

	internal const string AuthorizationIssueId = "token-rejected";

	private static readonly ILogger _logger = IntegrationLog.For<StreamlabsDesktopIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly VariableApiAccessor _variableAccessor = new();

	private StreamlabsDesktopConnection? _connection;
	private IVariableRefreshSignal? _refreshSignal;
	private StreamlabsDesktopEventEmitter? _events;

	public StreamlabsDesktopIntegration()
	{
		Actions = StreamlabsDesktopActions.Create(() => _connection, _variableAccessor);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Streamlabs Desktop";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/png";

	public IReadOnlyList<EventDefinition> EventDefinitions => StreamlabsDesktopEventDefinitions.All;

	public IReadOnlyList<VariableDefinition> Variables => StreamlabsDesktopVariables.All;

	public bool AllowsMultipleConfigurations => false;

	public byte[] GetIcon() => _icon;

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public IConfigFlow CreateConfigFlow() => new StreamlabsDesktopConfigFlow();

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new StreamlabsDesktopMacroDeck2Migration()];

	public async Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _connection;
		var values = connection is null
			? []
			: context.ParameterName switch
			{
				"sceneName" or "previousSceneName" => await connection.GetSceneNamesAsync().ConfigureAwait(false),
				"sourceName" => await connection.GetAudioSourceNamesAsync().ConfigureAwait(false),
				_ => []
			};

		return new DynamicOptionsResult
		{
			Options = values.Select(value => new ActionParameterOption { Value = value }).ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;
		_events = new StreamlabsDesktopEventEmitter(context.Events);
		await ConnectFromConfig(context).ConfigureAwait(false);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		// Awaited rather than fire-and-forget: the host reinitializes on every config change, and a new
		// session must not race the old one onto the same socket.
		if (_connection is { } connection)
		{
			_connection = null;
			await connection.StopAsync().ConfigureAwait(false);
		}

		IsInitialized = false;
	}

	public void Dispose()
	{
		_connection?.Dispose();
		_connection = null;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var state = _connection?.State ?? StreamlabsDesktopState.Disconnected;

		// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can
		// never contain '-', so swapping the separator back recovers the name exactly.
		var name = localId.Replace('-', '_');

		if (!state.IsConnected)
		{
			return ValueTask.FromResult(name == StreamlabsDesktopVariables.IsConnected
				? VariableReading.Of(false)
				: VariableReading.Unavailable);
		}

		object? value = name switch
		{
			StreamlabsDesktopVariables.IsConnected => true,
			StreamlabsDesktopVariables.CurrentScene => state.CurrentScene,
			StreamlabsDesktopVariables.SceneCount => state.SceneCount,
			StreamlabsDesktopVariables.IsStreaming => state.IsStreaming,
			StreamlabsDesktopVariables.StreamingStatus => StreamlabsModelReader.ToWireString(state.Streaming),
			StreamlabsDesktopVariables.StreamingSeconds => ElapsedSeconds(state.StreamingSince),
			StreamlabsDesktopVariables.IsRecording => state.IsRecording,
			StreamlabsDesktopVariables.RecordingStatus => StreamlabsModelReader.ToWireString(state.Recording),
			StreamlabsDesktopVariables.RecordingSeconds => ElapsedSeconds(state.RecordingSince),
			StreamlabsDesktopVariables.ReplayBufferActive => state.ReplayBufferActive,
			StreamlabsDesktopVariables.ReplayBufferStatus => StreamlabsModelReader.ToWireString(state.ReplayBuffer),
			StreamlabsDesktopVariables.StudioModeActive => state.StudioModeActive,
			_ => null
		};

		return ValueTask.FromResult(VariableReading.Of(value));
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		// Cheap by contract: this is polled to render a badge, so it only reads a flag. "Streamlabs is
		// not running" is deliberately not an issue - it is the normal state of a long-lived host, it
		// recovers on its own, and streamlabs_is_connected already says so.
		IReadOnlyList<IntegrationIssue> issues = _connection?.NeedsAuthorization == true
			?
			[
				new IntegrationIssue
				{
					Id = AuthorizationIssueId,
					Title = AppStrings.Integrations.StreamlabsDesktop.Issues.TokenRejectedTitle(),
					Description = AppStrings.Integrations.StreamlabsDesktop.Issues.TokenRejectedDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.StreamlabsDesktop.Issues.OpenSetupAction()
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == AuthorizationIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	private static int? ElapsedSeconds(DateTimeOffset? since)
		=> since is { } start ? (int)Math.Max(0d, (DateTimeOffset.UtcNow - start).TotalSeconds) : null;

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		if (_connection is { } previous)
		{
			_connection = null;
			await previous.StopAsync().ConfigureAwait(false);
		}

		var entries = await context.Config.GetEntriesAsync().ConfigureAwait(false);
		var entry = entries.Count > 0 ? entries[0] : null;
		if (entry is null)
		{
			_logger.Information("Streamlabs Desktop not configured; skipping connection");
			return;
		}

		var host = await context.Config.GetStringAsync(entry.Id, StreamlabsDesktopConfigKeys.Host)
			.ConfigureAwait(false);
		var portRaw = await context.Config.GetStringAsync(entry.Id, StreamlabsDesktopConfigKeys.Port)
			.ConfigureAwait(false);
		var token = await context.Config.GetSecretAsync(entry.Id, StreamlabsDesktopConfigKeys.Token)
			.ConfigureAwait(false);

		if (string.IsNullOrEmpty(token))
		{
			_logger.Warning("Streamlabs Desktop config entry {EntryId} has no API token; skipping", entry.Id);
			return;
		}

		int? port = int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: null;

		_connection = new StreamlabsDesktopConnection(() => new StreamlabsJsonRpcClient(),
			StreamlabsDesktopEndpoint.Create(host, port),
			token,
			_events,
			onVariablesChanged: () => _refreshSignal?.RequestEagerRefresh(IntegrationId));
		_connection.Start();
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(StreamlabsDesktopIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("streamlabs-icon.png", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
