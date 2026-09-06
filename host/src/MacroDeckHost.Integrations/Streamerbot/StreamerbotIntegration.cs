using System.Globalization;
using MacroDeckHost.Integrations.Streamerbot.Actions;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot;

[MacroDeckIntegration]
public sealed class StreamerbotIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.streamerbot";

	internal const string AuthenticationIssueId = "authentication-failed";

	private static readonly ILogger _logger = IntegrationLog.For<StreamerbotIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly StreamerbotVariableAccessor _variableAccessor = new();

	private StreamerbotConnection? _connection;
	private StreamerbotEventEmitter? _events;

	public StreamerbotIntegration()
	{
		Actions = StreamerbotActions.Create(() => _connection, _variableAccessor);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Streamer.bot";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => StreamerbotEventDefinitions.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new StreamerbotMacroDeck2Migration()];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("streamerbot_is_connected",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = MacroDeckStrings.Connection.Connected()
			},
		VariableDefinition.Eager("streamerbot_version", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Streamerbot.Variables.Version()
			},
		VariableDefinition.Eager("streamerbot_instance_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Streamerbot.Variables.InstanceName()
			},
		VariableDefinition.Eager("streamerbot_broadcaster_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Streamerbot.Variables.BroadcasterName()
			},
		VariableDefinition.Eager("streamerbot_broadcaster_platform",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.Streamerbot.Variables.BroadcasterPlatform()
			}
	];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new StreamerbotConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;
		_events = new StreamerbotEventEmitter(context.Events);
		await ConnectFromConfig(context);
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

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var state = _connection?.State ?? StreamerbotState.Disconnected;

		object? value = localId switch
		{
			"streamerbot-is-connected" => state.IsConnected,
			"streamerbot-version" => state.Version,
			"streamerbot-instance-name" => state.InstanceName,
			"streamerbot-broadcaster-name" => state.BroadcasterName,
			"streamerbot-broadcaster-platform" => state.BroadcasterPlatform,
			_ => null
		};

		return ValueTask.FromResult(VariableReading.Of(value));
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var catalog = _connection?.Catalog ?? StreamerbotCatalog.Empty;

		var values = context.ParameterName switch
		{
			"source" => catalog.Events.Keys.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToList(),
			"type" => EventTypes(catalog, context.CurrentParameters.GetValueOrDefault("source") as string),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = values.Select(value => new ActionParameterOption { Value = value }).ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 30
		});
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		// Cheap by contract: this is polled to render a badge, so it only reads a flag. "Streamer.bot
		// is not running" is deliberately not an issue - it is the normal state of a long-lived host,
		// it recovers on its own, and streamerbot_is_connected already says so.
		IReadOnlyList<IntegrationIssue> issues = _connection?.NeedsAuthentication == true
			?
			[
				new IntegrationIssue
				{
					Id = AuthenticationIssueId,
					Title = AppStrings.Integrations.Streamerbot.Issues.AuthenticationFailedTitle(),
					Description = AppStrings.Integrations.Streamerbot.Issues.AuthenticationFailedDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.Streamerbot.Issues.OpenSetupActionLabel()
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == AuthenticationIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	private static List<string> EventTypes(StreamerbotCatalog catalog, string? source)
	{
		if (!string.IsNullOrEmpty(source))
		{
			return catalog.Events.TryGetValue(source, out var types)
				? types.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToList()
				: [];
		}

		return catalog.Events.Values
			.SelectMany(types => types)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		_connection?.Dispose();
		_connection = null;

		var entries = await context.Config.GetEntriesAsync();
		var entry = entries.Count > 0 ? entries[0] : null;
		if (entry is null)
		{
			_logger.Information("Streamer.bot not configured; skipping connection");
			return;
		}

		var host = await context.Config.GetStringAsync(entry.Id, StreamerbotConfigKeys.Host);
		var portRaw = await context.Config.GetStringAsync(entry.Id, StreamerbotConfigKeys.Port);
		var endpoint = await context.Config.GetStringAsync(entry.Id, StreamerbotConfigKeys.Endpoint);
		var password = await context.Config.GetSecretAsync(entry.Id, StreamerbotConfigKeys.Password);

		if (string.IsNullOrEmpty(host) ||
			!int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
		{
			_logger.Warning("Streamer.bot config entry {EntryId} is incomplete; skipping", entry.Id);
			return;
		}

		_connection = new StreamerbotConnection(() => new StreamerbotClient(),
			StreamerbotEndpoint.Build(host, port, endpoint),
			password,
			_events);
		_connection.Start();
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(StreamerbotIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("streamerbot-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
