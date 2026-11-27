using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Clipboard.Actions;
using MacroDeckHost.Integrations.Clipboard.Configuration;
using MacroDeckHost.Integrations.Clipboard.Events;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Integrations.Clipboard.State;
using MacroDeckHost.Integrations.Clipboard.Variables;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard;

[MacroDeckIntegration]
public sealed class ClipboardIntegration
	: IIntegration, IConfigFlowProvider, IConfigEntryLifecycle, IVariableProvider, IEventProvider,
		IIntegrationIssueProvider, IIntegrationIconProvider, IDisposable
{
	public const string IntegrationId = "app.macro-deck.clipboard";

	internal const string UnavailableIssueId = "clipboard-unavailable";
	internal const string WatchUnsupportedIssueId = "watch-unsupported";

	private static readonly ILogger _logger = IntegrationLog.For<ClipboardIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly IClipboardBackend _backend;
	private readonly ClipboardMonitor _monitor;
	private readonly ILogger _log;
	private readonly SemaphoreSlim _apply = new(1, 1);

	private IIntegrationContext? _context;
	private volatile ClipboardSettings _settings = ClipboardSettings.Off;

	public ClipboardIntegration()
		: this(ClipboardBackendFactory.Create(), TimeProvider.System, _logger)
	{
	}

	internal ClipboardIntegration(IClipboardBackend backend, TimeProvider time, ILogger logger)
	{
		_backend = backend;
		_log = logger;
		_monitor = new ClipboardMonitor(backend, time, logger);
		_monitor.StateChanged += RequestValueRefresh;

		var services = new ClipboardActionServices(backend, _monitor, () => _context, logger);
		Actions =
		[
			new ReadTextActionDefinition(services),
			new SetTextActionDefinition(services),
			new CopyImageActionDefinition(services),
			new CopyFilesActionDefinition(services),
			new ClearActionDefinition(services),
			new CheckContentActionDefinition(services)
		];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => Strings.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => false;

	public bool RequiresConfiguration => false;

	public IReadOnlyList<VariableDefinition> Variables => ClipboardVariables.For(_settings);

	public IReadOnlyList<VariableDefinition> DeclaredVariables => ClipboardVariables.All;

	public IReadOnlyList<EventDefinition> EventDefinitions => ClipboardEventDefinitions.All;

	internal ClipboardSettings Settings => _settings;

	internal ClipboardMonitor Monitor => _monitor;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new ClipboardConfigFlow(LoadSettingsAsync);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		if (!_backend.Availability.IsAvailable)
		{
			_log.Information("The clipboard is not available: {Reason} {MissingTool}",
				_backend.Availability.Reason,
				_backend.Availability.MissingTool);
		}

		await ApplySettingsAsync(CancellationToken.None).ConfigureAwait(false);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await _monitor.StopAsync().ConfigureAwait(false);
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (IsInitialized)
		{
			await ApplySettingsAsync(cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(ClipboardVariables.Read(ClipboardVariables.NameOf(localId),
			_monitor.Snapshot,
			_settings));
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = new List<IntegrationIssue>();
		var availability = _backend.Availability;
		if (!availability.IsAvailable)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnavailableIssueId,
				Title = Strings.Issues.Unavailable.Title(),
				Description = ClipboardErrors.Describe(availability)
			});
		}
		else if (_settings.Watch && _monitor.IsWatchUnsupported)
		{
			issues.Add(new IntegrationIssue
			{
				Id = WatchUnsupportedIssueId,
				Title = Strings.Issues.WatchUnsupported.Title(),
				Description = Strings.Issues.WatchUnsupported.Description()
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(IssueResolution.Failed());
	}

	public void Dispose()
	{
		_monitor.Dispose();
		_apply.Dispose();
	}

	private async Task<ClipboardSettings> LoadSettingsAsync(CancellationToken cancellationToken)
	{
		return _context is { } context
			? await ClipboardSettings.ReadAsync(context.Config, cancellationToken).ConfigureAwait(false)
			: _settings;
	}

	private async Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		await _apply.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var settings = await ClipboardSettings.ReadAsync(context.Config, cancellationToken).ConfigureAwait(false);
			var previous = _settings;
			var before = ClipboardVariables.For(previous);
			_settings = settings;
			await _monitor.ApplyAsync(settings, context.Events, cancellationToken).ConfigureAwait(false);

			var after = ClipboardVariables.For(settings);
			if (before.Count != after.Count)
			{
				await context.Variables.NotifyDeclarationsChangedAsync(cancellationToken).ConfigureAwait(false);
				await DeleteUndeclaredAsync(context.Variables, before, after).ConfigureAwait(false);
			}

			_log.Information("Clipboard watching {Watch}, text variable {ExposeText}",
				settings.Watch,
				settings.ShowsText);
			await RequestDefinitionRefreshAsync(context, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_apply.Release();
		}
	}

	private async Task DeleteUndeclaredAsync(
		IVariableApi variables,
		IReadOnlyList<VariableDefinition> before,
		IReadOnlyList<VariableDefinition> after)
	{
		foreach (var name in before.Select(definition => definition.Name)
			.OfType<string>()
			.Except(after.Select(definition => definition.Name).OfType<string>(), StringComparer.Ordinal))
		{
			try
			{
				if (await variables.GetByNameAsync(name).ConfigureAwait(false) is { } handle)
				{
					await variables.DeleteAsync(handle.Id).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_log.Warning(exception, "Could not remove the clipboard variable {Variable}", name);
			}
		}
	}

	private void RequestValueRefresh()
	{
		if (_context is { } context)
		{
			_ = RequestDefinitionRefreshAsync(context, CancellationToken.None);
		}
	}

	private async Task RequestDefinitionRefreshAsync(IIntegrationContext context, CancellationToken cancellationToken)
	{
		IReadOnlyCollection<string> ids =
		[
			.. ClipboardVariables.For(_settings).Select(definition => definition.ResolvedId).OfType<string>()
		];
		try
		{
			await context.Variables.RequestDefinitionRefreshAsync(ids, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_log.Debug(exception, "Requesting a clipboard variable refresh failed");
		}
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(ClipboardIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
			$"{typeof(ClipboardIntegration).Namespace}.Icons.clipboard-icon.svg")!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
