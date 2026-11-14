using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Ping.Configuration;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ping.Events;
using MacroDeckHost.Integrations.Ping.Protocol;
using MacroDeckHost.Integrations.Ping.State;
using MacroDeckHost.Integrations.Ping.Variables;
using MacroDeckHost.Integrations.Ping.Widgets;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping;

namespace MacroDeckHost.Integrations.Ping;

[MacroDeckIntegration]
public sealed partial class PingIntegration
	: IIntegration, IVariableProvider, IConfigFlowProvider, IIntegrationIconProvider, IWidgetTypeProvider,
		IUiProvider, IConfigEntryLifecycle, IEventProvider, IDynamicEventOptionsProvider, IIntegrationIssueProvider
{
	public const string IntegrationId = PingWidgetType.OwnerId;

	private const string NotPermittedIssueId = "icmp-not-permitted";

	private static readonly ILogger _logger = IntegrationLog.For<PingIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private static readonly string[] _nullableString = ["string", "null"];

	private readonly IPingProber _prober;
	private readonly TimeProvider _time;
	private readonly PingHub _hub = new();
	private readonly PingUiProvider _ui;

	private IIntegrationContext? _context;
	private volatile IReadOnlyList<PingTarget> _targets = [];

	public PingIntegration()
		: this(new PingProber(), TimeProvider.System)
	{
	}

	internal PingIntegration(IPingProber prober, TimeProvider time)
	{
		_prober = prober;
		_time = time;
		_ui = new PingUiProvider(_hub, () => _context);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => Strings.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions => [];

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _targets.Select(target => target.Snapshot)
				.SelectMany(snapshot => PingVariables.Declare(snapshot.Key, snapshot.EntryId, snapshot.Title))
		];

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> Variables is { Count: > 0 } provided ? provided : PingVariables.Templates;

	public IReadOnlyList<EventDefinition> EventDefinitions => PingEventDefinitions.All;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _ui.Surfaces;

	internal IPingTargets Targets => _hub;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new PingConfigFlow();
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return _ui.CreateSessionAsync(request, cancellationToken);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		await ReloadConfigurationsAsync(CancellationToken.None);
		IsInitialized = true;
		_hub.Announce();
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await ReplaceTargetsAsync([]);
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.RegisterWidgetTypeAsync(WidgetType(), cancellationToken);
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return [WidgetType()];
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (!PingVariables.TrySplit(localId, out var entryId, out var slot))
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var snapshot = _targets.Select(target => target.Snapshot)
			.FirstOrDefault(candidate => candidate.EntryId == entryId);
		return ValueTask.FromResult(PingVariables.Read(snapshot, slot));
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		IReadOnlyList<ActionParameterOption> options = context.ParameterName == PingEventPayload.Configuration
			?
			[
				.. _hub.Targets.Select(target => new ActionParameterOption
				{
					Value = target.EntryId.ToString("D"), Label = target.Title
				})
			]
			: [];

		return Task.FromResult(new DynamicOptionsResult { Options = options, CacheSeconds = 0 });
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<IntegrationIssue> issues = _hub.Targets.Any(target => target.Problem == PingProblem.NotPermitted)
			?
			[
				new IntegrationIssue
				{
					Id = NotPermittedIssueId,
					Title = Strings.Issues.NotPermitted.Title(),
					Description = Strings.Issues.NotPermitted.Description()
				}
			]
			: [];
		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(IssueResolution.Failed());
	}

	public async Task ReloadConfigurationsAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		var entries = await context.Config.GetEntriesAsync(cancellationToken);
		var previous = _targets;
		var targets = new List<PingTarget>(entries.Count);

		foreach (var entry in entries)
		{
			var identity = ConfigurationIdentityJson.TryParse(
				await context.Config.GetStringAsync(entry.Id, PingConfigKeys.VariableIdentityKey, cancellationToken),
				out var parsed)
				? parsed
				: null;
			var settings = await ReadSettingsAsync(context.Config, entry.Id, cancellationToken);
			if (identity is null || settings is null)
			{
				_logger.Warning("Ping config entry {EntryId} is incomplete; skipping", entry.Id);
				continue;
			}

			var running = previous.FirstOrDefault(candidate => candidate.Snapshot.EntryId == entry.Id);
			if (running is not null
				&& string.Equals(running.Settings.Host, settings.Host, StringComparison.OrdinalIgnoreCase))
			{
				if (running.Snapshot.Title != entry.Title
					|| running.Snapshot.Key != identity.Key
					|| running.Settings != settings)
				{
					running.Retune(entry.Title, identity.Key, settings);
				}

				targets.Add(running);
				continue;
			}

			targets.Add(new PingTarget(entry.Id, entry.Title, identity.Key, settings, _prober, _time, Publish));
		}

		await ReplaceTargetsAsync(targets);
	}

	internal static WidgetTypeDescriptor WidgetType()
	{
		return new WidgetTypeDescriptor(PingWidgetType.LocalId,
			Strings.Widget.Name(),
			Strings.Widget.Description(),
			DefaultWidgetData,
			WidgetDataSchema,
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
		};
	}

	private static string DefaultWidgetData
		=> JsonSerializer.Serialize(new Dictionary<string, object>
		{
			[PingWidgetType.TargetKey] = string.Empty,
			[PingWidgetType.DisplayNameKey] = string.Empty,
			[PingWidgetType.ShowLatencyKey] = true,
			[PingWidgetType.ShowPacketLossKey] = true,
			[PingWidgetType.ShowHistoryKey] = true
		});

	private static string WidgetDataSchema
		=> JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["type"] = "object",
			["properties"] = new Dictionary<string, object>
			{
				[PingWidgetType.TargetKey] = new { type = "string" },
				[PingWidgetType.DisplayNameKey] = new { type = "string" },
				[PingWidgetType.ShowLatencyKey] = new { type = "boolean" },
				[PingWidgetType.ShowPacketLossKey] = new { type = "boolean" },
				[PingWidgetType.ShowHistoryKey] = new { type = "boolean" },
				[PingWidgetType.BackgroundColorKey] = new
				{
					type = _nullableString, description = "#rrggbb or transparent"
				}
			}
		});

	private void Publish(PingTarget source, PingSnapshot snapshot, PingReachability? transition)
	{
		if (!_targets.Contains(source))
		{
			return;
		}

		_hub.Update(snapshot);
		RequestVariableRefresh();

		if (transition is { } reachability && _context is { } context)
		{
			var (eventId, payload) = PingEventDefinitions.For(snapshot, reachability);
			context.Events.Publish(eventId, payload);
		}
	}

	private async Task ReplaceTargetsAsync(IReadOnlyList<PingTarget> targets)
	{
		var previous = _targets;
		_targets = targets;
		_hub.Replace([.. targets.Select(target => target.Snapshot)]);

		foreach (var target in previous.Where(target => !targets.Contains(target)))
		{
			await target.DisposeAsync();
		}

		foreach (var target in targets)
		{
			target.Start();
		}

		RequestVariableRefresh();
	}

	private void RequestVariableRefresh()
	{
		if (_context is { } context)
		{
			_ = context.Variables.RequestRefreshAsync();
		}
	}

	private static async Task<PingTargetSettings?> ReadSettingsAsync(
		IIntegrationConfig config,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		var host = PingTargetSettings.NormalizeHost(await config.GetStringAsync(entryId,
			PingConfigKeys.Host,
			cancellationToken));
		if (host is null)
		{
			return null;
		}

		var interval = await ReadNumberAsync(PingConfigKeys.IntervalSeconds,
			PingTargetSettings.DefaultIntervalSeconds,
			PingTargetSettings.MinimumIntervalSeconds,
			PingTargetSettings.MaximumIntervalSeconds);
		var timeout = await ReadNumberAsync(PingConfigKeys.TimeoutMilliseconds,
			PingTargetSettings.DefaultTimeoutMilliseconds,
			PingTargetSettings.MinimumTimeoutMilliseconds,
			PingTargetSettings.MaximumTimeoutMilliseconds);
		var threshold = await ReadNumberAsync(PingConfigKeys.FailureThreshold,
			PingTargetSettings.DefaultFailureThreshold,
			PingTargetSettings.MinimumFailureThreshold,
			PingTargetSettings.MaximumFailureThreshold);

		return new PingTargetSettings(host,
			TimeSpan.FromSeconds(interval),
			TimeSpan.FromMilliseconds(timeout),
			threshold);

		async Task<int> ReadNumberAsync(string key, int fallback, int minimum, int maximum)
		{
			var stored = PingTargetSettings.ReadWholeNumber(
				await config.GetStringAsync(entryId, key, cancellationToken));
			return stored is { } value ? Math.Clamp(value, minimum, maximum) : fallback;
		}
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(PingIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
			$"{typeof(PingIntegration).Namespace}.Icons.ping-icon.svg")!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
