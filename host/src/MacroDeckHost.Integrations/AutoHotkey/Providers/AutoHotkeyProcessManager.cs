using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Protocol;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Localization;
using Serilog;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Errors;
using HelperStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Helper;
using PageStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Page;

namespace MacroDeckHost.Integrations.AutoHotkey.Providers;

internal sealed class AutoHotkeyRuntime
{
	public required IAutoHotkeyLauncher Launcher { get; init; }

	public required AutoHotkeyRunFolders Folders { get; init; }

	public required AutoHotkeyScriptStore Store { get; init; }

	public required Func<AutoHotkeyInstallation> Installation { get; init; }

	public required Func<AutoHotkeySettings> Settings { get; init; }

	public required TimeProvider Time { get; init; }

	public required ILogger Logger { get; init; }

	public IUserVariableApi? UserVariables { get; init; }

	public IUserNotifier? Notifier { get; init; }

	public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

internal sealed partial class AutoHotkeyProcessManager : IAsyncDisposable
{
	public const int MaxConcurrentRuns = 32;

	private const int MaxRestartAttempts = 3;
	private const string PreludeFileName = "macrodeck-prelude.ahk";
	private const int NotificationsPerWindow = 5;

	private static readonly TimeSpan _shutdownGraceCap = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _shutdownBudget = TimeSpan.FromSeconds(8);
	private static readonly TimeSpan _killWait = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _syntaxCheckTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _outputNotifyDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _notificationWindow = TimeSpan.FromSeconds(10);
	private static readonly UTF8Encoding _utf8WithBom = new(true);

	private readonly AutoHotkeyRuntime _runtime;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, AutoHotkeyRun> _active = new();
	private readonly ConcurrentDictionary<Guid, AutoHotkeyRun> _latestByScript = new();
	private readonly ConcurrentDictionary<Guid, AutoHotkeyRun> _recent = new();
	private readonly SemaphoreSlim _startGate = new(1, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _statusGate = new();

	private string? _lastScript;
	private int? _lastExitCode;
	private string? _lastResult;
	private string? _lastError;
	private int _shutdown;

	public AutoHotkeyProcessManager(AutoHotkeyRuntime runtime)
	{
		_runtime = runtime;
		_logger = runtime.Logger;
	}

	public event Action<AutoHotkeyRunSnapshot>? RunChanged;

	public event Action<AutoHotkeyRunSnapshot>? RunStarted;

	public event Action<AutoHotkeyRunSnapshot>? RunExited;

	public event Action<string, string>? RunFailed;

	public int RunningCount => _active.Values.Count(run => run.IsActive);

	public string? LastScript
	{
		get
		{
			lock (_statusGate)
			{
				return _lastScript;
			}
		}
	}

	public int? LastExitCode
	{
		get
		{
			lock (_statusGate)
			{
				return _lastExitCode;
			}
		}
	}

	public string? LastResult
	{
		get
		{
			lock (_statusGate)
			{
				return _lastResult;
			}
		}
	}

	public string? LastError
	{
		get
		{
			lock (_statusGate)
			{
				return _lastError;
			}
		}
	}

	public bool IsRunning(string identity)
	{
		return _active.Values.Any(run => run.IsActive && run.Identity == identity);
	}

	public bool IsScriptRunning(Guid scriptId)
	{
		return IsRunning(AutoHotkeyIdentity.Managed(scriptId));
	}

	public AutoHotkeyRunSnapshot? LatestRun(Guid scriptId)
	{
		return _latestByScript.TryGetValue(scriptId, out var run) ? run.Snapshot() : null;
	}

	public string? FullOutput(Guid runId)
	{
		return _active.TryGetValue(runId, out var run) || _recent.TryGetValue(runId, out run)
			? run.Output.All()
			: null;
	}

	public void ForgetScript(Guid scriptId)
	{
		if (_latestByScript.TryRemove(scriptId, out var run))
		{
			_recent.TryRemove(run.Id, out _);
		}
	}

	public async Task<AutoHotkeyRunResult> RunAsync(AutoHotkeyRunRequest request, CancellationToken cancellationToken)
	{
		var prepared = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
		if (prepared.Failure is { } failure)
		{
			return failure;
		}

		AutoHotkeyRun? run = null;
		for (var attempt = 0; run is null; attempt++)
		{
			List<AutoHotkeyRun> stopFirst = [];
			await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (Volatile.Read(ref _shutdown) == 1)
				{
					return AutoHotkeyRunResult.Failed(ActionErrorCodes.Unavailable, Errors.NotInstalled());
				}

				var running = _active.Values
					.Where(active => active.IsActive && active.Identity == prepared.Identity)
					.ToList();
				if (running.Count > 0)
				{
					switch (request.Policy)
					{
						case AutoHotkeyRunPolicy.Ignore:
							return AutoHotkeyRunResult.Ignored;
						case AutoHotkeyRunPolicy.Fail:
							return AutoHotkeyRunResult.Failed(ActionErrorCodes.ProviderRejected,
								Errors.AlreadyRunning(prepared.DisplayName));
						case AutoHotkeyRunPolicy.Restart when attempt < MaxRestartAttempts:
							stopFirst = running;
							break;
					}
				}

				if (stopFirst.Count == 0)
				{
					if (_active.Values.Count(active => active.IsActive) >= MaxConcurrentRuns)
					{
						return AutoHotkeyRunResult.Failed(ActionErrorCodes.Unavailable, Errors.TooManyRuns());
					}

					var started = Start(prepared, request);
					if (started.Failure is { } startFailure)
					{
						return startFailure;
					}

					run = started.Run!;
				}
			}
			finally
			{
				_startGate.Release();
			}

			if (stopFirst.Count > 0)
			{
				await StopRunsAsync(stopFirst, _runtime.Settings().GracePeriod).ConfigureAwait(false);
			}
		}

		return await FollowAsync(run, request, cancellationToken).ConfigureAwait(false);
	}

	public Task<AutoHotkeyRunResult> RunFromEditorAsync(
		Guid scriptId,
		string source,
		CancellationToken cancellationToken)
	{
		return RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Managed,
				ScriptId = scriptId,
				SourceOverride = source,
				Policy = AutoHotkeyRunPolicy.Restart
			},
			cancellationToken);
	}

	public Task<int> StopScriptAsync(Guid scriptId, CancellationToken cancellationToken = default)
	{
		return StopAsync(AutoHotkeyIdentity.Managed(scriptId), true, cancellationToken);
	}

	public async Task<int> StopAsync(string identity, bool all, CancellationToken cancellationToken = default)
	{
		var running = _active.Values.Where(run => run.IsActive && run.Identity == identity)
			.OrderByDescending(run => run.StartedAt)
			.ToList();
		if (!all && running.Count > 1)
		{
			running = [running[0]];
		}

		await StopRunsAsync(running, _runtime.Settings().GracePeriod)
			.WaitAsync(cancellationToken)
			.ConfigureAwait(false);
		return running.Count;
	}

	public async Task<AutoHotkeySyntaxResult> CheckSyntaxAsync(
		string source,
		bool includeHelper,
		CancellationToken cancellationToken)
	{
		var installation = _runtime.Installation();
		if (!installation.IsValid)
		{
			return new AutoHotkeySyntaxResult(false, AutoHotkeyText.Resolve(Errors.NotInstalled()), null);
		}

		using var folder = _runtime.Folders.Create(Guid.NewGuid());
		var main = folder.PathOf("script.ahk");
		var prelude = folder.PathOf(PreludeFileName);
		await File.WriteAllTextAsync(prelude,
				AhkPrelude.Build(new Dictionary<string, string>(StringComparer.Ordinal), includeHelper),
				_utf8WithBom,
				cancellationToken)
			.ConfigureAwait(false);
		await File.WriteAllTextAsync(main, source, _utf8WithBom, cancellationToken).ConfigureAwait(false);

		IAutoHotkeyProcess process;
		try
		{
			process = _runtime.Launcher.Start(new AutoHotkeyStartInfo(installation.ExecutablePath!,
				["/ErrorStdOut=UTF-8", "/Validate", "/include", prelude, main],
				folder.FullPath));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "AutoHotkey could not be started to check a script");
			return new AutoHotkeySyntaxResult(false, AutoHotkeyText.Resolve(Errors.NotInstalled()), null);
		}

		using var started = process;
		var output = process.Output.ReadToEndAsync(cancellationToken);
		var error = process.Error.ReadToEndAsync(cancellationToken);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_syntaxCheckTimeout);
		try
		{
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			process.Kill();
			cancellationToken.ThrowIfCancellationRequested();
			return new AutoHotkeySyntaxResult(false, AutoHotkeyText.Resolve(Errors.CheckTimedOut()), null);
		}

		var text = await error.ConfigureAwait(false) + await output.ConfigureAwait(false);
		if (process.ExitCode == 0)
		{
			return new AutoHotkeySyntaxResult(true, null, null);
		}

		var load = ParseLoadError(text, main);
		return new AutoHotkeySyntaxResult(false, load?.Message ?? text.Trim(), load?.Line);
	}

	public async Task StartAutostartScriptsAsync()
	{
		if (!_runtime.Installation().IsValid)
		{
			return;
		}

		foreach (var script in _runtime.Store.Scripts.Where(script => script.StartWithMacroDeck))
		{
			if (_lifetime.IsCancellationRequested)
			{
				return;
			}

			try
			{
				var result = await RunAsync(new AutoHotkeyRunRequest
						{
							Kind = AutoHotkeyTargetKind.Managed,
							ScriptId = script.Id,
							Policy = AutoHotkeyRunPolicy.Ignore
						},
						_lifetime.Token)
					.ConfigureAwait(false);
				if (result.Status == AutoHotkeyRunStatus.Failed)
				{
					_logger.Warning("AutoHotkey script {ScriptId} did not start with Macro Deck: {Code}",
						script.Id,
						result.ErrorCode);
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	public async Task ShutdownAsync()
	{
		if (Interlocked.Exchange(ref _shutdown, 1) == 1)
		{
			return;
		}

		await _lifetime.CancelAsync().ConfigureAwait(false);

		// A start in flight finishes under the gate; once the gate is free every later start sees the shutdown.
		await _startGate.WaitAsync().ConfigureAwait(false);
		_startGate.Release();

		var grace = _runtime.Settings().GracePeriod;
		if (grace > _shutdownGraceCap)
		{
			grace = _shutdownGraceCap;
		}

		var running = _active.Values.ToList();
		try
		{
			await StopRunsAsync(running, grace).WaitAsync(_shutdownBudget).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.Warning("AutoHotkey scripts did not stop within the shutdown budget; ending them");
			foreach (var run in running)
			{
				run.Process?.Kill();
			}
		}

		_runtime.Launcher.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		await ShutdownAsync().ConfigureAwait(false);
		_lifetime.Dispose();
	}

	internal static AutoHotkeyScriptError? ParseLoadError(string text, string mainScriptPath)
	{
		foreach (var line in text.Split('\n'))
		{
			var match = LoadErrorPattern().Match(line.TrimEnd('\r'));
			if (!match.Success)
			{
				continue;
			}

			var inMain = string.Equals(Path.GetFullPath(match.Groups["file"].Value.Trim()),
				Path.GetFullPath(mainScriptPath),
				StringComparison.OrdinalIgnoreCase);
			var number = int.TryParse(match.Groups["line"].Value,
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var parsed)
				&& parsed > 0
				&& inMain
					? parsed
					: (int?)null;
			return new AutoHotkeyScriptError(match.Groups["message"].Value.Trim(), number);
		}

		return null;
	}

	private async Task<Prepared> PrepareAsync(AutoHotkeyRunRequest request, CancellationToken cancellationToken)
	{
		var installation = _runtime.Installation();
		if (!installation.IsValid)
		{
			return Prepared.Fail(ActionErrorCodes.Unavailable, Errors.NotInstalled());
		}

		Prepared prepared;
		switch (request.Kind)
		{
			case AutoHotkeyTargetKind.Managed:
				prepared = await PrepareManagedAsync(request, cancellationToken).ConfigureAwait(false);
				break;
			case AutoHotkeyTargetKind.File:
				prepared = PrepareFile(request);
				break;
			default:
				if (string.IsNullOrWhiteSpace(request.Code))
				{
					return Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.NoCode());
				}

				var inlineName = AutoHotkeyText.Resolve(AppStrings.Integrations.AutoHotkey.InlineCodeName());
				prepared = new Prepared(AutoHotkeyIdentity.Inline(request.VariableContext, request.Code),
					inlineName,
					null,
					request.Code,
					null,
					request.IncludeHelper ?? true,
					null);
				break;
		}

		if (prepared.Failure is not null)
		{
			return prepared;
		}

		try
		{
			return prepared with
			{
				Executable = installation.ExecutablePath!,
				Prelude = AhkPrelude.Build(request.Parameters, prepared.IncludeHelper)
			};
		}
		catch (AhkPreludeException ex)
		{
			return ex.Key is { } key
				? Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.InvalidParameterName(key))
				: Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.InvalidParameterValue());
		}
	}

	private async Task<Prepared> PrepareManagedAsync(AutoHotkeyRunRequest request, CancellationToken cancellationToken)
	{
		if (request.ScriptId is not { } scriptId)
		{
			return Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.NoScriptSelected());
		}

		var store = _runtime.Store;
		if (store.Find(scriptId) is not { } script)
		{
			return store.IsReadOnly
				? Prepared.Fail(ActionErrorCodes.Unavailable, Errors.LibraryUnreadable())
				: Prepared.Fail(ActionErrorCodes.NotFound, Errors.ScriptNotFound());
		}

		var source = request.SourceOverride;
		if (source is null)
		{
			var stored = await store.ReadSourceAsync(scriptId, cancellationToken).ConfigureAwait(false);
			if (stored.Status == AutoHotkeySourceStatus.Unreadable)
			{
				return Prepared.Fail(ActionErrorCodes.Unavailable, Errors.SourceUnreadable(script.Name));
			}

			source = stored.Text;
		}

		var workingDirectory = script.WorkingDirectory;
		if (workingDirectory is not null && !Directory.Exists(workingDirectory))
		{
			return Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.WorkingDirectoryNotFound());
		}

		return new Prepared(AutoHotkeyIdentity.Managed(scriptId),
			script.Name,
			scriptId,
			source,
			null,
			request.IncludeHelper ?? script.IncludeHelper,
			workingDirectory);
	}

	private static Prepared PrepareFile(AutoHotkeyRunRequest request)
	{
		var path = request.FilePath?.Trim().Trim('"');
		if (string.IsNullOrEmpty(path))
		{
			return Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.NoFileSelected());
		}

		if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
		{
			return Prepared.Fail(ActionErrorCodes.NotFound, Errors.FileNotFound());
		}

		var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
			? Path.GetDirectoryName(path)
			: request.WorkingDirectory.Trim();
		if (workingDirectory is null || !Directory.Exists(workingDirectory))
		{
			return Prepared.Fail(ActionErrorCodes.InvalidParameter, Errors.WorkingDirectoryNotFound());
		}

		return new Prepared(AutoHotkeyIdentity.File(path),
			Path.GetFileName(path),
			null,
			null,
			path,
			request.IncludeHelper ?? false,
			workingDirectory);
	}

	private (AutoHotkeyRun? Run, AutoHotkeyRunResult? Failure) Start(Prepared prepared, AutoHotkeyRunRequest request)
	{
		var runId = Guid.NewGuid();
		AutoHotkeyRunFolder? folder = null;
		try
		{
			folder = _runtime.Folders.Create(runId);
			var preludePath = folder.PathOf(PreludeFileName);
			File.WriteAllText(preludePath, prepared.Prelude, _utf8WithBom);

			var mainPath = prepared.FilePath;
			if (mainPath is null)
			{
				mainPath = folder.PathOf(AutoHotkeyScriptStore.ExportFileName(prepared.DisplayName));
				File.WriteAllText(mainPath, prepared.Source ?? string.Empty, _utf8WithBom);
			}

			var arguments = new List<string> { "/ErrorStdOut=UTF-8", "/include", preludePath, mainPath };
			if (request.Kind == AutoHotkeyTargetKind.File)
			{
				arguments.AddRange(request.Arguments);
			}

			var run = new AutoHotkeyRun(runId,
				prepared.Identity,
				request.Kind,
				prepared.ScriptId,
				prepared.DisplayName,
				mainPath,
				prepared.IncludeHelper,
				request.VariableContext,
				_runtime.Time.GetUtcNow()) { Folder = folder };

			var process = _runtime.Launcher.Start(new AutoHotkeyStartInfo(prepared.Executable,
				arguments,
				prepared.WorkingDirectory ?? folder.FullPath));
			run.Process = process;
			if (prepared.IncludeHelper)
			{
				run.Channel = new HelperChannel(process.Input, new RunHelper(this, run), _logger);
			}

			_active[run.Id] = run;
			if (run.ScriptId is { } scriptId)
			{
				_latestByScript[scriptId] = run;
			}

			_ = Task.Run(() => MonitorAsync(run));
			_logger.Information("Started AutoHotkey run {RunId} (process {ProcessId})", run.Id, process.Id);
			RaiseChanged(run);
			return (run, null);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "An AutoHotkey script could not be started");
			folder?.Dispose();
			RecordFailure(prepared.DisplayName, Errors.StartFailed(prepared.DisplayName));
			RaiseFailed(prepared.DisplayName, AutoHotkeyFailureReasons.StartFailed);
			return (null,
				AutoHotkeyRunResult.Failed(AutoHotkeyRunErrorCodes.ProcessStartFailed,
					Errors.StartFailed(prepared.DisplayName)));
		}
	}

	private async Task<AutoHotkeyRunResult> FollowAsync(
		AutoHotkeyRun run,
		AutoHotkeyRunRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var loaded = await run.Loaded.WaitAsync(cancellationToken).ConfigureAwait(false);
			if (!loaded)
			{
				await run.Completed.WaitAsync(cancellationToken).ConfigureAwait(false);
				return LoadFailure(run);
			}

			if (!request.Wait)
			{
				return new AutoHotkeyRunResult(AutoHotkeyRunStatus.Started) { RunId = run.Id };
			}

			var timeout = request.Timeout <= TimeSpan.Zero
				? AutoHotkeyRunRequest.DefaultTimeout
				: request.Timeout > AutoHotkeyRunRequest.MaxTimeout
					? AutoHotkeyRunRequest.MaxTimeout
					: request.Timeout;
			try
			{
				await run.Completed.WaitAsync(timeout, _runtime.Time, cancellationToken).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				run.TimedOut = true;
				await StopRunsAsync([run], _runtime.Settings().GracePeriod).ConfigureAwait(false);
				return AutoHotkeyRunResult.Failed(ActionErrorCodes.Timeout, Errors.TimedOut(run.DisplayName), run.Id);
			}

			return Completion(run);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await StopRunsAsync([run], _runtime.Settings().GracePeriod).ConfigureAwait(false);
			throw;
		}
	}

	private static AutoHotkeyRunResult LoadFailure(AutoHotkeyRun run)
	{
		var message = run.Error is { Line: { } line }
			? Errors.LoadFailed(run.DisplayName, line)
			: Errors.LoadFailedNoLine(run.DisplayName);
		return AutoHotkeyRunResult.Failed(AutoHotkeyRunErrorCodes.ExitCode, message, run.Id) with
		{
			ExitCode = run.ExitCode
		};
	}

	private static AutoHotkeyRunResult Completion(AutoHotkeyRun run)
	{
		var output = run.Output.All();
		if (run.ExitCode is { } code and not 0)
		{
			return AutoHotkeyRunResult.Failed(AutoHotkeyRunErrorCodes.ExitCode,
					Errors.ExitCode(run.DisplayName, code),
					run.Id) with
				{
					ExitCode = code, Result = run.Result, Output = output
				};
		}

		if (run.Error is { } error)
		{
			LocalizedText message = error.Line is { } line
				? Errors.RuntimeError(run.DisplayName, line)
				: Errors.RuntimeErrorNoLine(run.DisplayName);
			return AutoHotkeyRunResult.Failed(ActionErrorCodes.ProviderError, message, run.Id) with
			{
				ExitCode = run.ExitCode, Result = run.Result, Output = output
			};
		}

		return new AutoHotkeyRunResult(AutoHotkeyRunStatus.Completed)
		{
			RunId = run.Id, ExitCode = run.ExitCode, Result = run.Result, Output = output
		};
	}

	private async Task MonitorAsync(AutoHotkeyRun run)
	{
		var process = run.Process!;
		var output = Task.Run(() => PumpOutputAsync(run, process));
		var error = Task.Run(() => PumpErrorAsync(run, process));
		_ = Task.Run(() => LoadFallbackAsync(run));

		try
		{
			await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
		{
			_logger.Debug(ex, "Waiting for AutoHotkey run {RunId} failed", run.Id);
		}

		// A program the script launched may still hold the inherited pipes open; the run ends anyway.
		try
		{
			await Task.WhenAll(output, error).WaitAsync(_killWait).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.Debug("Output of AutoHotkey run {RunId} is still open after exit", run.Id);
		}

		try
		{
			await FinishAsync(run, process).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "AutoHotkey run {RunId} could not be finished cleanly", run.Id);
			_active.TryRemove(run.Id, out _);
		}
		finally
		{
			run.MarkCompleted();
		}

		RaiseChanged(run);
	}

	private async Task FinishAsync(AutoHotkeyRun run, IAutoHotkeyProcess process)
	{
		var exitCode = SafeExitCode(process);
		var ready = run.Loaded.IsCompletedSuccessfully && run.Loaded.Result;
		var loadError = !ready && exitCode is not 0
			? ParseLoadError(run.ErrorText, run.MainScriptPath)
			: null;
		var loaded = loadError is null;
		if (loadError is not null)
		{
			run.SetError(loadError);
		}
		else if (!ready)
		{
			run.MarkLoaded(true);
		}

		var state = run.TimedOut
			? AutoHotkeyRunState.TimedOut
			: loaded
				? AutoHotkeyRunState.Exited
				: AutoHotkeyRunState.FailedToStart;
		var ended = _runtime.Time.GetUtcNow();
		run.MarkEnded(state, exitCode, ended);

		if (run.Channel is { } channel)
		{
			await channel.DisposeAsync().ConfigureAwait(false);
		}

		process.Dispose();
		run.Folder?.Dispose();
		_active.TryRemove(run.Id, out _);
		RememberRecent(run);

		if (loaded)
		{
			EnsureStartedEvent(run);
		}

		var snapshot = run.Snapshot();
		lock (_statusGate)
		{
			_lastScript = run.DisplayName;
			_lastExitCode = exitCode;
			_lastResult = run.Result;
			_lastError = DescribeEnd(run, exitCode, loaded) is { } failure ? AutoHotkeyText.Resolve(failure) : null;
		}

		if (!loaded)
		{
			RaiseFailed(run.DisplayName, AutoHotkeyFailureReasons.LoadError);
		}
		else
		{
			if (run.TimedOut)
			{
				RaiseFailed(run.DisplayName, AutoHotkeyFailureReasons.Timeout);
			}

			Raise(RunExited, snapshot);
		}

		_logger.Information("AutoHotkey run {RunId} ended with exit code {ExitCode}", run.Id, exitCode);
	}

	private static LocalizedText? DescribeEnd(AutoHotkeyRun run, int? exitCode, bool loaded)
	{
		if (!loaded)
		{
			return run.Error is { Line: { } line }
				? Errors.LoadFailed(run.DisplayName, line)
				: Errors.LoadFailedNoLine(run.DisplayName);
		}

		if (run.TimedOut)
		{
			return Errors.TimedOut(run.DisplayName);
		}

		if (run.Error is { } error)
		{
			return error.Line is { } line
				? Errors.RuntimeError(run.DisplayName, line)
				: Errors.RuntimeErrorNoLine(run.DisplayName);
		}

		return exitCode is { } code and not 0 && !run.StopRequested ? Errors.ExitCode(run.DisplayName, code) : null;
	}

	private void EnsureStartedEvent(AutoHotkeyRun run)
	{
		if (!run.TryClaimStartedEvent())
		{
			return;
		}

		Raise(RunStarted, run.Snapshot());
	}

	private async Task LoadFallbackAsync(AutoHotkeyRun run)
	{
		try
		{
			await Task.Delay(_runtime.ReadyTimeout, _runtime.Time, _lifetime.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (!run.Completed.IsCompleted && !run.Loaded.IsCompleted)
		{
			_logger.Debug("AutoHotkey run {RunId} sent no ready message; treating it as started", run.Id);
			MarkRunning(run);
		}
	}

	private void MarkRunning(AutoHotkeyRun run)
	{
		if (run.Loaded.IsCompleted)
		{
			return;
		}

		run.MarkLoaded(true);
		EnsureStartedEvent(run);
		RaiseChanged(run);
	}

	private async Task PumpOutputAsync(AutoHotkeyRun run, IAutoHotkeyProcess process)
	{
		var parser = new HelperMessageParser(_runtime.Time);
		try
		{
			await foreach (var line in HelperLineReader
				.ReadAsync(process.Output, HelperMessageParser.MaxMessageLength, _lifetime.Token)
				.ConfigureAwait(false))
			{
				var parsed = parser.Parse(line);
				if (parsed.Status == HelperParseStatus.Output)
				{
					AppendOutput(run, line.Complete ? line.Text + "\n" : line.Text);
					continue;
				}

				if (!string.IsNullOrEmpty(parsed.Output))
				{
					AppendOutput(run, parsed.Output + "\n");
				}

				if (run.Channel is { } channel)
				{
					channel.Accept(parsed);
				}
				else if (parsed.Message is { Kind: HelperMessageKind.Ready })
				{
					MarkRunning(run);
				}
				else if (parsed.Message is { Kind: HelperMessageKind.Error } error)
				{
					ReportRuntimeError(run, error);
				}
			}
		}
		catch (Exception ex) when (ex is IOException
			or ObjectDisposedException
			or OperationCanceledException
			or InvalidOperationException)
		{
			_logger.Debug(ex, "Standard output of AutoHotkey run {RunId} closed", run.Id);
		}
	}

	private async Task PumpErrorAsync(AutoHotkeyRun run, IAutoHotkeyProcess process)
	{
		try
		{
			await foreach (var line in HelperLineReader
				.ReadAsync(process.Error, HelperMessageParser.MaxMessageLength, _lifetime.Token)
				.ConfigureAwait(false))
			{
				run.AppendErrorText(line.Text);
				AppendOutput(run, line.Complete ? line.Text + "\n" : line.Text);
			}
		}
		catch (Exception ex) when (ex is IOException
			or ObjectDisposedException
			or OperationCanceledException
			or InvalidOperationException)
		{
			_logger.Debug(ex, "Standard error of AutoHotkey run {RunId} closed", run.Id);
		}
	}

	private void AppendOutput(AutoHotkeyRun run, string text)
	{
		run.Output.Append(text);
		if (!run.TryBeginOutputNotification())
		{
			return;
		}

		_ = NotifyOutputAsync(run);
	}

	private async Task NotifyOutputAsync(AutoHotkeyRun run)
	{
		try
		{
			await Task.Delay(_outputNotifyDelay, _runtime.Time).ConfigureAwait(false);
		}
		finally
		{
			run.EndOutputNotification();
		}

		RaiseChanged(run);
	}

	private async Task StopRunsAsync(IReadOnlyList<AutoHotkeyRun> runs, TimeSpan grace)
	{
		await Task.WhenAll(runs.Select(run => StopRunAsync(run, grace))).ConfigureAwait(false);
	}

	private async Task StopRunAsync(AutoHotkeyRun run, TimeSpan grace)
	{
		run.StopRequested = true;
		if (run.Process is not { } process || run.Completed.IsCompleted)
		{
			await run.Completed.ConfigureAwait(false);
			return;
		}

		var closing = false;
		try
		{
			closing = !process.HasExited && process.RequestClose();
		}
		catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
		{
			_logger.Debug(ex, "Could not ask AutoHotkey run {RunId} to close", run.Id);
		}

		if (closing)
		{
			try
			{
				await run.Completed.WaitAsync(grace, _runtime.Time).ConfigureAwait(false);
				return;
			}
			catch (TimeoutException)
			{
				_logger.Information("AutoHotkey run {RunId} did not close within the grace period", run.Id);
			}
		}

		try
		{
			process.Kill();
		}
		catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
		{
			_logger.Debug(ex, "Could not end AutoHotkey run {RunId}", run.Id);
		}

		try
		{
			await run.Completed.WaitAsync(_killWait + _killWait, _runtime.Time).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.Warning("AutoHotkey run {RunId} did not end after it was killed", run.Id);
		}
	}

	private void RememberRecent(AutoHotkeyRun run)
	{
		_recent[run.Id] = run;
		foreach (var stale in _recent.Values.OrderByDescending(recent => recent.StartedAt).Skip(MaxConcurrentRuns))
		{
			if (!_latestByScript.Values.Contains(stale))
			{
				_recent.TryRemove(stale.Id, out _);
			}
		}
	}

	private void ReportRuntimeError(AutoHotkeyRun run, HelperMessage error)
	{
		var inMain = error.File is { Length: > 0 } file
			&& string.Equals(Path.GetFullPath(file),
				Path.GetFullPath(run.MainScriptPath),
				StringComparison.OrdinalIgnoreCase);
		var line = inMain && error.Line > 0 ? error.Line : (int?)null;
		run.SetError(new AutoHotkeyScriptError(error.Text, line));
		var shown = line is { } number
			? PageStrings.ErrorWithLine(number, error.Text)
			: PageStrings.ErrorNoLine(error.Text);
		AppendOutput(run, AutoHotkeyText.Resolve(shown) + "\n");
		RaiseFailed(run.DisplayName, AutoHotkeyFailureReasons.RuntimeError);
		lock (_statusGate)
		{
			_lastError = AutoHotkeyText.Resolve(line is { } at
				? Errors.RuntimeError(run.DisplayName, at)
				: Errors.RuntimeErrorNoLine(run.DisplayName));
		}
	}

	private void RecordFailure(string displayName, LocalizedText message)
	{
		lock (_statusGate)
		{
			_lastScript = displayName;
			_lastExitCode = null;
			_lastResult = null;
			_lastError = AutoHotkeyText.Resolve(message);
		}
	}

	private static int? SafeExitCode(IAutoHotkeyProcess process)
	{
		try
		{
			return process.HasExited ? process.ExitCode : null;
		}
		catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
		{
			return null;
		}
	}

	private void RaiseChanged(AutoHotkeyRun run)
	{
		Raise(RunChanged, run.Snapshot());
	}

	private void RaiseFailed(string displayName, string reason)
	{
		try
		{
			RunFailed?.Invoke(displayName, reason);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "An AutoHotkey run listener failed");
		}
	}

	private void Raise(Action<AutoHotkeyRunSnapshot>? handler, AutoHotkeyRunSnapshot snapshot)
	{
		try
		{
			handler?.Invoke(snapshot);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "An AutoHotkey run listener failed");
		}
	}

	[GeneratedRegex(@"\A(?<file>.+?) \((?<line>\d+)\) : ==> (?<message>.+)\z", RegexOptions.CultureInvariant)]
	private static partial Regex LoadErrorPattern();

	private sealed record Prepared(
		string Identity,
		string DisplayName,
		Guid? ScriptId,
		string? Source,
		string? FilePath,
		bool IncludeHelper,
		string? WorkingDirectory)
	{
		public string Executable { get; init; } = string.Empty;

		public string Prelude { get; init; } = string.Empty;

		public AutoHotkeyRunResult? Failure { get; init; }

		public static Prepared Fail(string code, LocalizedText message)
		{
			return new Prepared(string.Empty, string.Empty, null, null, null, false, null)
			{
				Failure = AutoHotkeyRunResult.Failed(code, message)
			};
		}
	}

	private sealed class RunHelper : IHelperHandler
	{
		private readonly AutoHotkeyProcessManager _manager;
		private readonly AutoHotkeyRun _run;
		private readonly Queue<long> _notifications = new();

		public RunHelper(AutoHotkeyProcessManager manager, AutoHotkeyRun run)
		{
			_manager = manager;
			_run = run;
		}

		public void OnReady()
		{
			_manager.MarkRunning(_run);
		}

		public async Task SetVariableAsync(string name, string value, CancellationToken cancellationToken)
		{
			await ActionVariableTarget.WriteAsync(_manager._runtime.UserVariables,
					null,
					name,
					_run.VariableContext,
					VariableType.Text,
					value,
					_manager._logger,
					cancellationToken)
				.ConfigureAwait(false);
		}

		public async Task<HelperAnswer> GetVariableAsync(string name, CancellationToken cancellationToken)
		{
			if (_manager._runtime.UserVariables is not { } variables)
			{
				return new HelperAnswer(false, AutoHotkeyText.Resolve(HelperStrings.VariableUnavailable(name)));
			}

			var read = await variables.ReadInContextAsync(name, _run.VariableContext, cancellationToken)
				.ConfigureAwait(false);
			return read.Status switch
			{
				UserVariableReadStatus.Found => new HelperAnswer(true, read.Text ?? Format(read.Value)),
				UserVariableReadStatus.NotFound =>
					new HelperAnswer(false, AutoHotkeyText.Resolve(HelperStrings.VariableNotFound(name))),
				UserVariableReadStatus.InvalidName =>
					new HelperAnswer(false, AutoHotkeyText.Resolve(HelperStrings.VariableInvalidName(name))),
				_ => new HelperAnswer(false, AutoHotkeyText.Resolve(HelperStrings.VariableUnavailable(name)))
			};
		}

		public void Notify(string title, string message, string level)
		{
			if (_manager._runtime.Notifier is not { } notifier || !TryTakeNotification())
			{
				return;
			}

			notifier.Notify(new UserNotificationRequest
			{
				Title = string.IsNullOrWhiteSpace(title) ? _run.DisplayName : title,
				Message = string.IsNullOrEmpty(message) ? null : message,
				Level = level switch
				{
					"warning" => UserNotificationLevel.Warning,
					"error" => UserNotificationLevel.Error,
					_ => UserNotificationLevel.Info
				}
			});
		}

		public void SetResult(string text)
		{
			_run.SetResult(text);
			_manager.RaiseChanged(_run);
		}

		public void Log(string text)
		{
			_manager.AppendOutput(_run, text + "\n");
		}

		public void ReportError(HelperMessage error)
		{
			_manager.ReportRuntimeError(_run, error);
		}

		public string RejectedRequestText()
		{
			return AutoHotkeyText.Resolve(HelperStrings.RequestRejected());
		}

		public string NoAnswerText()
		{
			return AutoHotkeyText.Resolve(HelperStrings.NoAnswer());
		}

		private bool TryTakeNotification()
		{
			var now = _manager._runtime.Time.GetTimestamp();
			lock (_notifications)
			{
				while (_notifications.Count > 0
					&& _manager._runtime.Time.GetElapsedTime(_notifications.Peek(), now) > _notificationWindow)
				{
					_notifications.Dequeue();
				}

				if (_notifications.Count >= NotificationsPerWindow)
				{
					return false;
				}

				_notifications.Enqueue(now);
				return true;
			}
		}

		private static string Format(object? value)
		{
			return value switch
			{
				null => string.Empty,
				bool flag => flag ? "true" : "false",
				IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
				_ => value.ToString() ?? string.Empty
			};
		}
	}
}
