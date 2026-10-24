using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Storage;
using MacroDeckHost.Integrations.AutoHotkey.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Events;
using MacroDeckHost.Integrations.AutoHotkey.Migration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using Serilog;

namespace MacroDeckHost.Integrations.AutoHotkey;

[MacroDeckIntegration(Platforms = MacroDeckPlatform.Windows, EnabledByDefault = false)]
public sealed partial class AutoHotkeyIntegration
	: IIntegration,
		IConfigFlowProvider,
		IEventProvider,
		IIntegrationIconProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.autohotkey";

	private static readonly ILogger _logger = IntegrationLog.For<AutoHotkeyIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private readonly IAutoHotkeyMachine _machine;
	private readonly Func<IAutoHotkeyLauncher> _launcherFactory;
	private readonly string _tempRoot;
	private readonly TimeProvider _time;
	private readonly AutoHotkeyActionHost _actionHost = new(_logger);
	private readonly SemaphoreSlim _lifecycle = new(1, 1);

	private IIntegrationContext? _context;
	private AutoHotkeyScriptStore? _store;
	private AutoHotkeyProcessManager? _manager;
	private AutoHotkeyInstallation _installation = AutoHotkeyInstallation.NotFound;
	private AutoHotkeySettings _settings = AutoHotkeySettings.Default;
	private IIntegrationStorage? _storage;

	public AutoHotkeyIntegration()
		: this(new AutoHotkeyMachine(),
			() => new AutoHotkeyLauncher(),
			AutoHotkeyRunFolders.DefaultRoot,
			TimeProvider.System)
	{
	}

	internal AutoHotkeyIntegration(
		IAutoHotkeyMachine machine,
		Func<IAutoHotkeyLauncher> launcherFactory,
		string tempRoot,
		TimeProvider time)
	{
		_machine = machine;
		_launcherFactory = launcherFactory;
		_tempRoot = tempRoot;
		_time = time;
		Actions = AutoHotkeyActions.Create(_actionHost);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "AutoHotkey";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<EventDefinition> EventDefinitions => AutoHotkeyEvents.All;

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => false;

	public bool RequiresConfiguration => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new AutoHotkeyMacroDeck2Migration()];

	internal AutoHotkeyScriptStore? Library => _store;

	internal AutoHotkeyProcessManager? Runs => _manager;

	internal AutoHotkeyInstallation Installation => _installation;

	internal AutoHotkeySettings Settings => _settings;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new AutoHotkeyConfigFlow((manual, cancellationToken)
				=> AutoHotkeyInstallation.DetectAsync(_machine, manual, cancellationToken),
			LoadSettingsAsync,
			SaveSettingsAsync);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await _lifecycle.WaitAsync().ConfigureAwait(false);
		try
		{
			await StopAsync().ConfigureAwait(false);

			_context = context;
			_storage = context.Storage;
			_settings = await AutoHotkeySettingsStore.ReadAsync(context.Storage, _logger).ConfigureAwait(false);
			_installation = await AutoHotkeyInstallation
				.DetectAsync(_machine, _settings.ManualExecutable, CancellationToken.None)
				.ConfigureAwait(false);
			_logger.Information("AutoHotkey detection: {Status} {Version}",
				_installation.Status,
				_installation.Version);

			var store = new AutoHotkeyScriptStore(context.Storage, _time, _logger);
			await store.LoadAsync().ConfigureAwait(false);

			var folders = new AutoHotkeyRunFolders(_tempRoot, _logger);
			try
			{
				var deleted = folders.DeleteStale();
				if (deleted > 0)
				{
					_logger.Information("Deleted {Count} stale AutoHotkey run folders", deleted);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.Warning(ex, "Stale AutoHotkey run folders could not be cleaned up");
			}

			var manager = new AutoHotkeyProcessManager(new AutoHotkeyRuntime
			{
				Launcher = _launcherFactory(),
				Folders = folders,
				Store = store,
				Installation = () => _installation,
				Settings = () => _settings,
				Time = _time,
				Logger = _logger,
				UserVariables = context.UserVariables,
				Notifier = context.Notifications
			});

			_store = store;
			_manager = manager;
			store.Changed += OnLibraryChanged;
			manager.RunStarted += OnRunStarted;
			manager.RunExited += OnRunExited;
			manager.RunFailed += OnRunFailed;

			_actionHost.Store = store;
			_actionHost.Manager = manager;
			_actionHost.UserVariables = context.UserVariables;
			_actionHost.Installation = _installation;
			IsInitialized = true;

			_ = Task.Run(manager.StartAutostartScriptsAsync);
		}
		finally
		{
			_lifecycle.Release();
		}

		RaiseRuntimeChanged();

		await NotifyDeclarationsChangedAsync().ConfigureAwait(false);
		await RefreshVariablesAsync().ConfigureAwait(false);
	}

	public async Task ShutdownAsync()
	{
		await _lifecycle.WaitAsync().ConfigureAwait(false);
		try
		{
			await StopAsync().ConfigureAwait(false);
		}
		finally
		{
			_lifecycle.Release();
		}
	}

	public void Dispose()
	{
		var manager = _manager;
		_manager = null;
		if (manager is null)
		{
			return;
		}

		try
		{
			manager.ShutdownAsync().Wait(TimeSpan.FromSeconds(10));
		}
		catch (AggregateException ex)
		{
			_logger.Warning(ex, "AutoHotkey scripts could not be stopped cleanly");
		}
	}

	private async Task StopAsync()
	{
		IsInitialized = false;
		_actionHost.Manager = null;
		_actionHost.Store = null;

		if (_store is { } store)
		{
			store.Changed -= OnLibraryChanged;
		}

		if (_manager is { } manager)
		{
			manager.RunStarted -= OnRunStarted;
			manager.RunExited -= OnRunExited;
			manager.RunFailed -= OnRunFailed;
			await manager.DisposeAsync().ConfigureAwait(false);
		}

		_store?.Dispose();

		_manager = null;
		_store = null;
		_context = null;
	}

	private void OnRunStarted(AutoHotkeyRunSnapshot run)
	{
		_context?.Events.Publish(AutoHotkeyEvents.Started, AutoHotkeyEvents.StartedPayload(run));
		_ = RefreshVariablesAsync(run.ScriptId);
	}

	private void OnRunExited(AutoHotkeyRunSnapshot run)
	{
		_context?.Events.Publish(AutoHotkeyEvents.Exited, AutoHotkeyEvents.ExitedPayload(run));
		_ = RefreshVariablesAsync(run.ScriptId);
	}

	private void OnRunFailed(string script, string reason)
	{
		_context?.Events.Publish(AutoHotkeyEvents.Failed, AutoHotkeyEvents.FailedPayload(script, reason));
		_ = RefreshVariablesAsync();
	}

	private void OnLibraryChanged(AutoHotkeyLibraryChange change)
	{
		if (change is { Kind: AutoHotkeyLibraryChangeKind.Deleted, ScriptId: { } deleted }
			&& _manager is { } manager)
		{
			manager.ForgetScript(deleted);
			_ = manager.StopAsync(AutoHotkeyIdentity.Managed(deleted), true);
		}

		if (change.Kind is AutoHotkeyLibraryChangeKind.Added
			or AutoHotkeyLibraryChangeKind.Renamed
			or AutoHotkeyLibraryChangeKind.Deleted
			or AutoHotkeyLibraryChangeKind.Loaded)
		{
			_ = NotifyDeclarationsChangedAsync();
		}
	}

	private async Task<AutoHotkeySettings?> LoadSettingsAsync(CancellationToken cancellationToken)
	{
		return _storage is { } storage
			? await AutoHotkeySettingsStore.ReadAsync(storage, _logger, cancellationToken).ConfigureAwait(false)
			: null;
	}

	private async Task SaveSettingsAsync(AutoHotkeySettings settings, CancellationToken cancellationToken)
	{
		var storage = _storage ?? throw new InvalidOperationException("The AutoHotkey integration has no storage yet.");
		AutoHotkeyInstallation? detected = null;
		await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await AutoHotkeySettingsStore.WriteAsync(storage, settings, cancellationToken).ConfigureAwait(false);
			if (IsInitialized)
			{
				_settings = settings;
				detected = await AutoHotkeyInstallation
					.DetectAsync(_machine, settings.ManualExecutable, cancellationToken)
					.ConfigureAwait(false);
			}
		}
		finally
		{
			_lifecycle.Release();
		}

		if (detected is not null)
		{
			await ApplyInstallationAsync(detected).ConfigureAwait(false);
		}
	}

	private async Task ApplyInstallationAsync(AutoHotkeyInstallation detected)
	{
		var wasValid = _installation.IsValid;
		_installation = detected;
		_actionHost.Installation = detected;
		await RefreshVariablesAsync().ConfigureAwait(false);
		RaiseRuntimeChanged();
		if (!wasValid && detected.IsValid && _manager is { } manager)
		{
			_ = Task.Run(manager.StartAutostartScriptsAsync);
		}
	}

	private async Task NotifyDeclarationsChangedAsync()
	{
		try
		{
			if (_context is { } context)
			{
				await RemoveStaleScriptVariablesAsync(context.Variables).ConfigureAwait(false);
				await context.Variables.NotifyDeclarationsChangedAsync().ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "AutoHotkey variable declarations could not be refreshed");
		}
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(AutoHotkeyIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("autohotkey-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
