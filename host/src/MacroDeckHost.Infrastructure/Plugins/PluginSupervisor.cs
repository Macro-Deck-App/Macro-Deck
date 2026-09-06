using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginSupervisor : IPluginSupervisor
{
	private static readonly TimeSpan _postKillWait = TimeSpan.FromSeconds(2);

	private readonly IPluginManifestReader _manifestReader;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginRuntimeStateStore _stateStore;
	private readonly IPluginProcessJournal _journal;
	private readonly IPluginProcessLauncher _launcher;
	private readonly IDotnetMuxerLocator _muxerLocator;
	private readonly IPluginHealthProbe _healthProbe;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginLaunchTokenService _launchTokenService;
	private readonly IHostListenerState _hostListenerState;
	private readonly IPluginTrustEvaluator _trustEvaluator;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly PluginSupervisorOptions _options;
	private readonly ILogger _logger;
	private readonly Random _random = new();

	private readonly Dictionary<string, PluginRuntimeEntry> _entries = new(StringComparer.Ordinal);
	private readonly object _entriesLock = new();

	public PluginSupervisor(
		IPluginManifestReader manifestReader,
		IPluginInstallationCatalog catalog,
		IPluginRuntimeStateStore stateStore,
		IPluginProcessJournal journal,
		IPluginProcessLauncher launcher,
		IDotnetMuxerLocator muxerLocator,
		IPluginHealthProbe healthProbe,
		IPluginSessionRegistry sessionRegistry,
		IPluginLaunchTokenService launchTokenService,
		IHostListenerState hostListenerState,
		IPluginTrustEvaluator trustEvaluator,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider,
		PluginSupervisorOptions options,
		ILogger logger)
	{
		_manifestReader = manifestReader;
		_catalog = catalog;
		_stateStore = stateStore;
		_journal = journal;
		_launcher = launcher;
		_muxerLocator = muxerLocator;
		_healthProbe = healthProbe;
		_sessionRegistry = sessionRegistry;
		_launchTokenService = launchTokenService;
		_hostListenerState = hostListenerState;
		_trustEvaluator = trustEvaluator;
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
		_options = options;
		_logger = logger.ForContext<PluginSupervisor>();
	}

	public IReadOnlyList<PluginRuntimeSnapshot> Snapshot()
	{
		var installed = SafeDiscover();
		var installedIds = installed.Select(p => p.PluginId).ToHashSet(StringComparer.Ordinal);
		var sessions = _sessionRegistry.Snapshot();

		var result = new List<PluginRuntimeSnapshot>();

		foreach (var plugin in installed)
		{
			var entry = GetOrCreateEntry(plugin.PluginId);
			result.Add(BuildSnapshot(entry, managed: true));
		}

		foreach (var session in sessions)
		{
			if (session.Origin == PluginSessionOrigin.Managed || installedIds.Contains(session.PluginId))
			{
				continue;
			}

			result.Add(new PluginRuntimeSnapshot
			{
				PluginId = session.PluginId,
				DisplayName = PluginDisplayNameResolver.Resolve(session, manifestName: null, session.PluginId),
				Version = PluginVersionResolver.Resolve(installedVersion: null, session.DeclaredVersion),
				State = session.State == PluginSessionState.Connected
					? PluginRuntimeState.Running
					: PluginRuntimeState.Stopped,
				Health = session.State == PluginSessionState.Connected
					? PluginHealthState.Healthy
					: PluginHealthState.Unknown,
				Managed = false,
				LastHeartbeatAt = session.LastInboundAt
			});
		}

		return result;
	}

	public async Task<PluginSupervisorResult> Start(string pluginId, CancellationToken cancellationToken = default)
	{
		try
		{
			if (!TryResolveInstalled(pluginId, out var installed))
			{
				return NotSupervisable(pluginId);
			}

			var entry = GetOrCreateEntry(pluginId);

			await entry.Gate.WaitAsync(cancellationToken);
			bool alreadyRunning;
			bool wasFailed;
			try
			{
				alreadyRunning = entry.State is PluginRuntimeState.Starting or PluginRuntimeState.Running;
				wasFailed = entry.State == PluginRuntimeState.Failed;
			}
			finally
			{
				entry.Gate.Release();
			}

			if (alreadyRunning)
			{
				return PluginSupervisorResult.Fail(PluginSupervisorError.AlreadyRunning);
			}

			if (wasFailed)
			{
				ResetRestartBudget(entry);
			}

			var result = await AttemptLaunch(entry, installed!, allowStartupGraceRetry: true, cancellationToken);
			if (result.Success)
			{
				await _stateStore.Save(pluginId, true);
				await PublishRuntimeChanged(cancellationToken);
			}

			return result;
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.StartFailed(_logger, pluginId, ex);
			return PluginSupervisorResult.Fail(PluginSupervisorError.Failed, ex.Message);
		}
	}

	public async Task<PluginSupervisorResult> Stop(string pluginId,
		PluginStopReason reason,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (!TryResolveInstalled(pluginId, out _))
			{
				return NotSupervisable(pluginId);
			}

			var entry = GetOrCreateEntry(pluginId);

			await entry.Gate.WaitAsync(cancellationToken);
			bool canStop;
			try
			{
				canStop = entry.State is PluginRuntimeState.Starting
					or PluginRuntimeState.Running
					or PluginRuntimeState.Backoff;
			}
			finally
			{
				entry.Gate.Release();
			}

			if (!canStop)
			{
				return PluginSupervisorResult.Fail(PluginSupervisorError.NotRunning);
			}

			if (reason == PluginStopReason.UserRequested)
			{
				await _stateStore.Save(pluginId, false);
			}

			if (entry.Process is null)
			{
				await entry.Gate.WaitAsync(cancellationToken);
				try
				{
					entry.State = PluginRuntimeState.Stopped;
					entry.Health = PluginHealthState.Unknown;
					entry.NextRestartAt = null;
					entry.LastStopReason = reason;
				}
				finally
				{
					entry.Gate.Release();
				}
			}
			else
			{
				await InitiateTermination(entry, reason, cancellationToken);
			}

			await PublishRuntimeChanged(cancellationToken);
			return PluginSupervisorResult.Ok();
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.StopFailed(_logger, pluginId, ex);
			return PluginSupervisorResult.Fail(PluginSupervisorError.Failed, ex.Message);
		}
	}

	public async Task<PluginSupervisorResult> Restart(string pluginId, CancellationToken cancellationToken = default)
	{
		try
		{
			if (!TryResolveInstalled(pluginId, out var installed))
			{
				return NotSupervisable(pluginId);
			}

			var entry = GetOrCreateEntry(pluginId);

			await entry.Gate.WaitAsync(cancellationToken);
			var running = entry.State is PluginRuntimeState.Starting
				or PluginRuntimeState.Running
				or PluginRuntimeState.Backoff;
			entry.Gate.Release();

			if (running)
			{
				if (entry.Process is not null)
				{
					await InitiateTermination(entry, PluginStopReason.ManualRestart, cancellationToken);
				}
			}

			ResetRestartBudget(entry);

			var result = await AttemptLaunch(entry, installed!, allowStartupGraceRetry: true, cancellationToken);
			if (result.Success)
			{
				await _stateStore.Save(pluginId, true);
			}

			await PublishRuntimeChanged(cancellationToken);
			return result;
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.RestartFailed(_logger, pluginId, ex);
			return PluginSupervisorResult.Fail(PluginSupervisorError.Failed, ex.Message);
		}
	}

	// Every plugin can burn its full graceful budget and then the same budget again waiting for a kill
	// to land, so the entries are terminated concurrently. Stopping them one after another summed those
	// budgets and pushed host shutdown past the generic host's stop timeout, which cost an intentional
	// restart its exit code and made the shell report a crash (#737).
	public async Task StopAll(PluginStopReason reason, CancellationToken cancellationToken = default)
	{
		List<PluginRuntimeEntry> entries;
		lock (_entriesLock)
		{
			entries = _entries.Values.ToList();
		}

		await Task.WhenAll(entries.Select(entry => StopAllEntry(entry, reason, cancellationToken)));
	}

	private async Task StopAllEntry(PluginRuntimeEntry entry, PluginStopReason reason, CancellationToken ct)
	{
		try
		{
			if (entry.Process is not null)
			{
				await InitiateTermination(entry, reason, ct);
			}
			else if (entry.State is PluginRuntimeState.Backoff or PluginRuntimeState.Starting)
			{
				await entry.Gate.WaitAsync(ct);
				try
				{
					entry.State = PluginRuntimeState.Stopped;
					entry.NextRestartAt = null;
					entry.LastStopReason = reason;
				}
				finally
				{
					entry.Gate.Release();
				}
			}
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.StopAllEntryFailed(_logger, entry.PluginId, ex);
		}
	}

	public async Task Forget(string pluginId, CancellationToken cancellationToken = default)
	{
		lock (_entriesLock)
		{
			_entries.Remove(pluginId);
		}

		await _stateStore.Remove(pluginId);
		await PublishRuntimeChanged(cancellationToken);
	}

	public async Task Reconcile(CancellationToken cancellationToken = default)
	{
		try
		{
			var installed = SafeDiscover().ToDictionary(p => p.PluginId, StringComparer.Ordinal);
			var desired = _stateStore.Load();
			var sessions = _sessionRegistry.Snapshot()
				.Where(s => s.Origin == PluginSessionOrigin.Managed)
				.ToDictionary(s => s.PluginId, StringComparer.Ordinal);
			var now = _timeProvider.GetUtcNow();

			foreach (var plugin in installed.Values)
			{
				GetOrCreateEntry(plugin.PluginId);
			}

			List<PluginRuntimeEntry> entries;
			lock (_entriesLock)
			{
				entries = _entries.Values.ToList();
			}

			var anyChanged = false;

			foreach (var entry in entries)
			{
				try
				{
					var before = BuildSnapshot(entry, managed: true);
					installed.TryGetValue(entry.PluginId, out var plugin);
					if (plugin is not null)
					{
						await SeedMetadataIfNeeded(entry, plugin, cancellationToken);
					}

					var wantsStarted = desired.TryGetValue(entry.PluginId, out var w) && w;
					sessions.TryGetValue(entry.PluginId, out var session);

					await ReconcileEntry(entry, plugin, wantsStarted, session, now, cancellationToken);

					var after = BuildSnapshot(entry, managed: true);
					if (!SnapshotEqualsIgnoringLiveness(before, after))
					{
						anyChanged = true;
					}
				}
				catch (Exception ex)
				{
					PluginInfrastructureLog.ReconcileEntryFailed(_logger, entry.PluginId, ex);
				}
			}

			if (anyChanged)
			{
				await PublishRuntimeChanged(cancellationToken);
			}
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.ReconcileTickFailed(_logger, ex);
		}
	}

	private async Task ReconcileEntry(PluginRuntimeEntry entry,
		InstalledPlugin? installed,
		bool wantsStarted,
		PluginSessionSnapshot? session,
		DateTimeOffset now,
		CancellationToken ct)
	{
		switch (entry.State)
		{
			case PluginRuntimeState.Stopped:
				if (wantsStarted && installed?.ActiveVersion is not null && !entry.IntegrityFailed)
				{
					await AttemptLaunch(entry, installed, allowStartupGraceRetry: true, ct);
				}

				break;

			case PluginRuntimeState.Backoff:
				if (installed?.ActiveVersion is null)
				{
					await entry.Gate.WaitAsync(ct);
					try
					{
						entry.State = PluginRuntimeState.Stopped;
						entry.NextRestartAt = null;
					}
					finally
					{
						entry.Gate.Release();
					}
				}
				else if (entry.NextRestartAt is { } next && now >= next)
				{
					await AttemptLaunch(entry, installed, allowStartupGraceRetry: true, ct);
				}

				break;

			case PluginRuntimeState.Starting:
				await EvaluateStarting(entry, session, now, ct);
				break;

			case PluginRuntimeState.Running:
				await EvaluateRunning(entry, session, now, ct);
				break;

			case PluginRuntimeState.Stopping:
			case PluginRuntimeState.Failed:
				break;
		}
	}

	private async Task EvaluateStarting(PluginRuntimeEntry entry,
		PluginSessionSnapshot? session,
		DateTimeOffset now,
		CancellationToken ct)
	{
		var attached = session is { State: PluginSessionState.Connected };
		var probedHealthy = await ProbeIfDue(entry, now, ct);

		if (attached || probedHealthy == true)
		{
			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.State = PluginRuntimeState.Running;
				entry.RunningSince = now;
				entry.Health = PluginHealthState.Healthy;
				entry.ConsecutiveHealthFailures = 0;
				entry.StartupGraceRetried = false;
				if (session?.LastInboundAt is { } inboundAt)
				{
					entry.LastHeartbeatAt = inboundAt;
				}
			}
			finally
			{
				entry.Gate.Release();
			}

			return;
		}

		if (entry.StartedAt is { } startedAt && now - startedAt > _options.StartupGrace)
		{
			IPluginProcess? doomed;
			string? doomedLaunchId;

			await entry.Gate.WaitAsync(ct);
			try
			{
				doomed = entry.PendingIntent == PluginStopReason.None ? entry.Process : null;
				doomedLaunchId = entry.LaunchId;
				if (doomed is not null)
				{
					entry.PendingIntent = PluginStopReason.LaunchFailure;
					entry.State = PluginRuntimeState.Stopping;
				}
			}
			finally
			{
				entry.Gate.Release();
			}

			if (doomed is not null)
			{
				// Fire-and-forget: the grace-period wait must not stall the 1s reconcile tick behind
				// this one plugin - see the lock-discipline note on the class. The process is captured
				// above rather than re-read inside, so a relaunch that lands first is never the one killed.
				_ = TerminateInBackground(entry, doomed, doomedLaunchId, ct);
			}
		}
	}

	private async Task EvaluateRunning(PluginRuntimeEntry entry,
		PluginSessionSnapshot? session,
		DateTimeOffset now,
		CancellationToken ct)
	{
		await ProbeIfDue(entry, now, ct);

		var sessionConnected = session is { State: PluginSessionState.Connected };
		var heartbeatAt = session?.LastInboundAt ?? session?.ConnectedAt;
		var heartbeatFresh = heartbeatAt is { } at && now - at <= ProtocolTimeouts.KeepAliveTimeout;
		var sessionDroppedWithinResumeWindow
			= session is { State: PluginSessionState.Dropped, DroppedAt: { } droppedAt } &&
			now - droppedAt <= ProtocolTimeouts.SessionResumeWindow;

		PluginHealthState health;

		if (sessionConnected && heartbeatFresh)
		{
			health = entry.ConsecutiveHealthFailures == 0 ? PluginHealthState.Healthy : PluginHealthState.Degraded;
		}
		else if (sessionConnected || sessionDroppedWithinResumeWindow)
		{
			health = PluginHealthState.Degraded;
		}
		else if (entry.ConsecutiveHealthFailures >= (entry.HealthUnhealthyThreshold ?? _options.UnhealthyThreshold))
		{
			health = PluginHealthState.Unhealthy;
		}
		else if (entry.ConsecutiveHealthFailures > 0)
		{
			health = PluginHealthState.Degraded;
		}
		else
		{
			health = PluginHealthState.Unknown;
		}

		IPluginProcess? doomed;
		string? doomedLaunchId;

		await entry.Gate.WaitAsync(ct);
		try
		{
			entry.Health = health;
			if (session?.LastInboundAt is { } inboundAt)
			{
				entry.LastHeartbeatAt = inboundAt;
			}

			if (entry.RunningSince is { } since &&
				PluginRestartPolicy.IsRuntimeStable(now - since, _options) &&
				(entry.Attempt > 0 || entry.RestartTimestamps.Count > 0))
			{
				entry.Attempt = 0;
				entry.RestartTimestamps.Clear();
			}

			doomed = health == PluginHealthState.Unhealthy && entry.PendingIntent == PluginStopReason.None
				? entry.Process
				: null;
			doomedLaunchId = entry.LaunchId;
			if (doomed is not null)
			{
				entry.PendingIntent = PluginStopReason.HealthFailure;
				entry.State = PluginRuntimeState.Stopping;
			}
		}
		finally
		{
			entry.Gate.Release();
		}

		if (doomed is not null)
		{
			_ = TerminateInBackground(entry, doomed, doomedLaunchId, ct);
		}
	}

	private async Task TerminateInBackground(PluginRuntimeEntry entry,
		IPluginProcess process,
		string? launchId,
		CancellationToken ct)
	{
		try
		{
			await TerminateAfterIntentSet(entry, ct, process, launchId);
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.BackgroundTerminationFailed(_logger, entry.PluginId, ex);
		}
	}

	private async Task<bool?> ProbeIfDue(PluginRuntimeEntry entry, DateTimeOffset now, CancellationToken ct)
	{
		var interval = entry.HealthIntervalSeconds is { } s ? TimeSpan.FromSeconds(s) : _options.HealthPollInterval;
		if (entry.LastHealthCheckAt is { } last && now - last < interval)
		{
			return null;
		}

		if (entry.HealthPort is not { } port)
		{
			return null;
		}

		var timeout = entry.HealthTimeoutSeconds is { } t ? TimeSpan.FromSeconds(t) : _options.HealthTimeout;
		// Mirrors MacroDeck.Plugin.Hosting.Endpoints.ReservedPaths.Health - the host does not (and
		// should not) reference that plugin-side SDK project, so the literal is duplicated here rather
		// than pulled in through a backwards project reference.
		var path = entry.HealthPath ?? "/_macrodeck/health";

		bool succeeded;
		try
		{
			succeeded = await _healthProbe.Probe(port, path, timeout, entry.PluginId, ct);
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.HealthProbeThrew(_logger, entry.PluginId, ex);
			succeeded = false;
		}

		await entry.Gate.WaitAsync(ct);
		try
		{
			entry.LastHealthCheckAt = now;
			entry.ConsecutiveHealthFailures = succeeded ? 0 : entry.ConsecutiveHealthFailures + 1;
		}
		finally
		{
			entry.Gate.Release();
		}

		return succeeded;
	}

	private async Task InitiateTermination(PluginRuntimeEntry entry, PluginStopReason reason, CancellationToken ct)
	{
		IPluginProcess? process;
		string? launchId;

		await entry.Gate.WaitAsync(ct);
		try
		{
			process = entry.Process;
			if (process is null)
			{
				return;
			}

			entry.PendingIntent = reason;
			entry.State = PluginRuntimeState.Stopping;
			launchId = entry.LaunchId;
		}
		finally
		{
			entry.Gate.Release();
		}

		await TerminateAfterIntentSet(entry, ct, process, launchId);
	}

	private async Task TerminateAfterIntentSet(PluginRuntimeEntry entry,
		CancellationToken ct,
		IPluginProcess? processOverride = null,
		string? launchIdOverride = null)
	{
		var process = processOverride ?? entry.Process;
		var launchId = launchIdOverride ?? entry.LaunchId;

		if (process is null)
		{
			return;
		}

		if (launchId is not null)
		{
			_launchTokenService.Discard(launchId);
		}

		try
		{
			await _sessionRegistry.SendToPlugin(entry.PluginId, BuildGoodbyeEnvelope(), ct);
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.GoodbyeSendFailed(_logger, entry.PluginId, ex);
		}

		try
		{
			await _sessionRegistry.TerminateForPlugin(entry.PluginId,
				ProtocolCloseCodes.SupervisorShutdown,
				"The supervisor is stopping this plugin.");
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.SessionCloseFailed(_logger, entry.PluginId, ex);
		}

		var grace = entry.GracefulShutdownTimeout ?? _options.GracefulShutdownTimeout;

		if (!process.HasExited)
		{
			await Task.WhenAny(process.Exited, Task.Delay(grace, _timeProvider, ct));
		}

		if (!process.HasExited)
		{
			await process.KillTree(ct);
			// Deliberately not on the shutdown token: during host shutdown it is already cancelled, and a
			// zero length wait here would skip FinalizeExit and lose the exit accounting entirely.
			await Task.WhenAny(process.Exited, Task.Delay(_postKillWait, _timeProvider));
		}

		if (!process.HasExited)
		{
			PluginInfrastructureLog.ProcessSurvivedKill(_logger, entry.PluginId, process.Id);
			return;
		}

		await FinalizeExit(entry, process, launchId);
	}

	private async Task FinalizeExit(PluginRuntimeEntry entry, IPluginProcess process, string? launchId)
	{
		string? finishedLaunchId = null;

		await entry.Gate.WaitAsync();
		try
		{
			if (!ReferenceEquals(entry.Process, process))
			{
				return;
			}

			var now = _timeProvider.GetUtcNow();
			var pendingIntent = entry.PendingIntent;
			entry.PendingIntent = PluginStopReason.None;

			entry.LastExitCode = process.ExitCode;
			entry.LastExitAt = now;
			entry.LastBootstrapOutput = process.BootstrapOutput;
			entry.Process = null;
			entry.HealthPort = null;

			// Idempotent - TerminateAfterIntentSet already discarded it on the graceful-stop path, but a
			// crash reaches this method without ever going through there, and every exit path must
			// discard before the next Mint so a stale process cannot authenticate.
			if (launchId is not null)
			{
				_launchTokenService.Discard(launchId);
				finishedLaunchId = launchId;
			}

			process.Dispose();

			// No PluginStopReason.SessionReplaced: after the enrollment-claim guard (PluginRegistrationService
			// refuses enrolling onto an installed id) and the launch latch (PluginSessionsController refuses a
			// registration-authenticated session while a launch for that id is live), a live managed process
			// can no longer be displaced at all - the graceful and health-failure paths both terminate the
			// session before any relaunch, and this handler only runs once the process has already exited. A
			// dedicated reason here would have no reachable production path.
			var stopReason = pendingIntent == PluginStopReason.None ? PluginStopReason.Crash : pendingIntent;
			entry.LastStopReason = stopReason;

			switch (stopReason)
			{
				case PluginStopReason.UserRequested:
				case PluginStopReason.HostShutdown:
				case PluginStopReason.Update:
				case PluginStopReason.ManualRestart:
					entry.State = PluginRuntimeState.Stopped;
					entry.Health = PluginHealthState.Unknown;
					entry.NextRestartAt = null;
					entry.StartupGraceRetried = false;
					break;

				default:
					entry.Health = PluginHealthState.Crashed;
					HandleAutomaticExitLocked(entry, now, allowStartupGraceRetry: true);
					break;
			}
		}
		finally
		{
			entry.Gate.Release();
		}

		if (finishedLaunchId is not null)
		{
			await _journal.Remove(finishedLaunchId);
		}
	}

	private void HandleAutomaticExitLocked(PluginRuntimeEntry entry, DateTimeOffset now, bool allowStartupGraceRetry)
	{
		var wasStarting = entry.State == PluginRuntimeState.Starting;
		var withinStartupGrace = entry.StartedAt is { } startedAt && now - startedAt <= _options.StartupGrace;

		if (allowStartupGraceRetry && wasStarting && withinStartupGrace && !entry.StartupGraceRetried)
		{
			entry.StartupGraceRetried = true;
			entry.State = PluginRuntimeState.Backoff;
			entry.NextRestartAt = now;
			return;
		}

		// StartupGraceRetried is deliberately left as-is here: it is consumed at most once per backoff
		// run (reset only on reaching Running, or on a manual Start/Restart), not once per crash.
		entry.RestartTimestamps.Add(now);
		entry.RestartTimestamps.RemoveAll(timestamp => now - timestamp > _options.RestartWindow);

		if (PluginRestartPolicy.IsBudgetExhausted(entry.RestartTimestamps, now, _options))
		{
			entry.State = PluginRuntimeState.Failed;
			entry.Health = PluginHealthState.Failed;
			entry.LastError = $"Exceeded {_options.MaxRestarts} restarts within {_options.RestartWindow}.";
			entry.NextRestartAt = null;
			return;
		}

		entry.State = PluginRuntimeState.Backoff;
		var delay = PluginRestartPolicy.DelayFor(entry.Attempt, _random.NextDouble());
		entry.Attempt++;
		entry.RestartCount++;
		entry.NextRestartAt = now + delay;
	}

	private static void TryCreateDataDirectory(string path)
	{
		try
		{
			Directory.CreateDirectory(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	private async Task SeedMetadataIfNeeded(PluginRuntimeEntry entry, InstalledPlugin installed, CancellationToken ct)
	{
		if (installed.ActiveVersion is not { } active ||
			string.Equals(entry.MetadataSeededForVersion, active.Version, StringComparison.Ordinal) ||
			entry.Process is not null)
		{
			return;
		}

		string? manifestName;
		try
		{
			var manifestResult = _manifestReader.Read(active.ManifestPath, entry.PluginId, active.Version);
			manifestName = manifestResult.Success ? manifestResult.Manifest!.Name : null;
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.MetadataSeedReadFailed(_logger, entry.PluginId, ex);
			manifestName = null;
		}

		await entry.Gate.WaitAsync(ct);
		try
		{
			if (entry.Process is null &&
				!string.Equals(entry.MetadataSeededForVersion, active.Version, StringComparison.Ordinal))
			{
				entry.DisplayName = manifestName ?? entry.PluginId;
				entry.Version = active.Version;
				entry.MetadataSeededForVersion = active.Version;
			}
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	private async Task<PluginSupervisorResult> AttemptLaunch(PluginRuntimeEntry entry,
		InstalledPlugin installed,
		bool allowStartupGraceRetry,
		CancellationToken ct)
	{
		if (installed.ActiveVersion is not { } activeVersion)
		{
			return PluginSupervisorResult.Fail(PluginSupervisorError.NotInstalled);
		}

		var manifestResult
			= _manifestReader.Read(activeVersion.ManifestPath, installed.PluginId, activeVersion.Version);
		if (!manifestResult.Success)
		{
			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.State = PluginRuntimeState.Stopped;
				entry.LastError = manifestResult.ErrorMessage;
			}
			finally
			{
				entry.Gate.Release();
			}

			return PluginSupervisorResult.Fail(PluginSupervisorError.ManifestInvalid, manifestResult.ErrorMessage);
		}

		var manifest = manifestResult.Manifest!;

		var trust = await _trustEvaluator.EvaluateInstalledAsync(activeVersion.VersionDirectory, ct);
		var trustDecision = await EvaluateTrustGate(installed.PluginId, activeVersion.Version, trust, ct);

		if (!trustDecision.Permitted)
		{
			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.State = PluginRuntimeState.Stopped;
				entry.LastError = trustDecision.RefusalReason;
				// Terminal for this process lifetime, not a restart-budget consumer: Reconcile's Stopped
				// case must not keep relaunching a plugin whose integrity check just failed. A user-driven
				// Start/Restart re-runs this check from scratch and clears the flag on success, so it is
				// never a one-way lockout - just not something the automatic reconcile loop retries.
				entry.IntegrityFailed = true;
			}
			finally
			{
				entry.Gate.Release();
			}

			PluginInfrastructureLog.IntegrityCheckFailed(_logger, installed.PluginId, trust.Verdict);
			return PluginSupervisorResult.Fail(PluginSupervisorError.IntegrityFailed, trustDecision.RefusalReason);
		}

		await ApplyTrustGateAction(installed.PluginId, activeVersion.Version, trustDecision.Action, trust);

		var candidateEntrypoint = PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current)
			.Select(rid => manifest.Entrypoints.GetValueOrDefault(rid))
			.FirstOrDefault(candidate => candidate is not null);

		if (candidateEntrypoint is null)
		{
			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.State = PluginRuntimeState.Stopped;
				entry.LastError = "No entrypoint matches this host's runtime identifier.";
			}
			finally
			{
				entry.Gate.Release();
			}

			return PluginSupervisorResult.Fail(PluginSupervisorError.NoEntrypointForRuntime);
		}

		var versionDirectory = activeVersion.VersionDirectory;
		var executablePath = Path.GetFullPath(Path.Combine(versionDirectory, candidateEntrypoint.Executable));

		var launchExecutablePath = executablePath;
		var launchArguments = candidateEntrypoint.Arguments ?? [];

		if (candidateEntrypoint.Runtime?.Kind == PluginEntrypointRuntimeKind.FrameworkDependent)
		{
			var muxer = _muxerLocator.Locate();
			if (muxer is null)
			{
				const string message = "No dotnet runtime was found to launch this framework-dependent plugin.";

				await entry.Gate.WaitAsync(ct);
				try
				{
					entry.State = PluginRuntimeState.Stopped;
					entry.LastError = message;
				}
				finally
				{
					entry.Gate.Release();
				}

				return PluginSupervisorResult.Fail(PluginSupervisorError.DotnetRuntimeMissing, message);
			}

			if (muxer.InstalledRuntimeVersions.Count > 0 &&
				!DotnetMuxerLocator.IsRuntimeSatisfied(candidateEntrypoint.Runtime.DotnetVersion!,
					muxer.InstalledRuntimeVersions))
			{
				var message =
					$"No installed dotnet runtime satisfies the required version {candidateEntrypoint.Runtime.DotnetVersion}.";

				await entry.Gate.WaitAsync(ct);
				try
				{
					entry.State = PluginRuntimeState.Stopped;
					entry.LastError = message;
				}
				finally
				{
					entry.Gate.Release();
				}

				return PluginSupervisorResult.Fail(PluginSupervisorError.DotnetRuntimeMissing, message);
			}

			launchExecutablePath = muxer.ExecutablePath;
			launchArguments = [executablePath, .. candidateEntrypoint.Arguments ?? []];
		}

		var now = _timeProvider.GetUtcNow();

		await entry.Gate.WaitAsync(ct);
		try
		{
			entry.DisplayName = manifest.Name;
			entry.Version = manifest.Version;
			entry.MetadataSeededForVersion = manifest.Version;
			entry.State = PluginRuntimeState.Starting;
			entry.Health = PluginHealthState.Unknown;
			entry.StartedAt = now;
			entry.NextRestartAt = null;
			entry.LastError = null;
			entry.IntegrityFailed = false;
			entry.GracefulShutdownTimeout = manifest.Shutdown is { } s
				? TimeSpan.FromSeconds(s.GracefulTimeoutSeconds)
				: null;
			entry.HealthPath = manifest.Health?.Path;
			entry.HealthIntervalSeconds = manifest.Health?.IntervalSeconds;
			entry.HealthTimeoutSeconds = manifest.Health?.TimeoutSeconds;
			if (manifest.Health is { } h)
			{
				entry.HealthUnhealthyThreshold = h.UnhealthyThreshold;
			}
		}
		finally
		{
			entry.Gate.Release();
		}

		int healthPort;
		try
		{
			healthPort = BindEphemeralPort();
		}
		catch (Exception ex)
		{
			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.LastError = ex.Message;
				HandleAutomaticExitLocked(entry, now, allowStartupGraceRetry);
			}
			finally
			{
				entry.Gate.Release();
			}

			return PluginSupervisorResult.Fail(PluginSupervisorError.LaunchFailed, ex.Message);
		}

		var launchId = Guid.CreateVersion7().ToString();
		// Uses this call's own local `manifest`, never entry.DisplayName/entry.Version: the gate that
		// wrote those was released above, AttemptLaunch is reentrant, and reading entry.* here would
		// race a concurrent relaunch that has since overwritten them.
		var secret = _launchTokenService.Mint(entry.PluginId, launchId, manifest.Name, manifest.Version);
		var hostPort = _hostListenerState.LoopbackPort ?? 0;

		// Plugin-owned state lives beside versions/, never inside a version directory, so it survives
		// every update and rollback. The plugin is told where it is because it cannot derive the path -
		// the data root moves with the installation and in portable mode.
		var dataDirectory = Path.Combine(installed.PluginDirectory, PluginArtifactFiles.DataDirectoryName);
		TryCreateDataDirectory(dataDirectory);

		var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["MACRO_DECK_PLUGIN_MODE"] = "Managed",
			["MACRO_DECK_PLUGIN_HOST_URL"] = $"http://127.0.0.1:{hostPort}",
			["MACRO_DECK_PLUGIN_ID"] = entry.PluginId,
			["MACRO_DECK_PLUGIN_DATA_DIRECTORY"] = dataDirectory,
			["MACRO_DECK_PLUGIN_SECRET"] = secret,
			["MACRO_DECK_PLUGIN_INSTANCE_ID"] = Guid.CreateVersion7().ToString(),
			["MACRO_DECK_PLUGIN_LAUNCH_ID"] = launchId,
			["MACRO_DECK_PLUGIN_HOST_PROCESS_ID"] =
				HostProcessIdentity.ProcessId.ToString(CultureInfo.InvariantCulture),
			["MACRO_DECK_PLUGIN_HOST_STARTED_AT"] = HostProcessIdentity.StartedAt.ToString("O",
				CultureInfo.InvariantCulture),
			["ASPNETCORE_URLS"] = $"http://127.0.0.1:{healthPort}"
		};

		var request = new PluginProcessStartRequest
		{
			ExecutablePath = launchExecutablePath,
			WorkingDirectory = versionDirectory,
			Arguments = launchArguments,
			Environment = environment,
			BootstrapOutputMaxLines = _options.BootstrapOutputLines,
			BootstrapOutputMaxBytes = _options.BootstrapOutputBytes
		};

		IPluginProcess process;
		try
		{
			process = _launcher.Start(request);
		}
		catch (Exception ex)
		{
			_launchTokenService.Discard(launchId);

			await entry.Gate.WaitAsync(ct);
			try
			{
				entry.LastError = ex.Message;
				HandleAutomaticExitLocked(entry, _timeProvider.GetUtcNow(), allowStartupGraceRetry: false);
			}
			finally
			{
				entry.Gate.Release();
			}

			return PluginSupervisorResult.Fail(PluginSupervisorError.LaunchFailed, ex.Message);
		}

		await _journal.Record(new PluginProcessJournalEntry
		{
			LaunchId = launchId,
			PluginId = entry.PluginId,
			ProcessId = process.Id,
			StartedAt = process.StartedAt
		});

		await entry.Gate.WaitAsync(ct);
		try
		{
			entry.Process = process;
			entry.LaunchId = launchId;
			entry.HealthPort = healthPort;
		}
		finally
		{
			entry.Gate.Release();
		}

		_ = WatchForExit(entry, process, launchId);

		return PluginSupervisorResult.Ok();
	}

	private async Task WatchForExit(PluginRuntimeEntry entry, IPluginProcess process, string launchId)
	{
		try
		{
			await process.Exited;
			await FinalizeExit(entry, process, launchId);
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.ObserveExitFailed(_logger, entry.PluginId, ex);
		}
	}

	private static int BindEphemeralPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		try
		{
			listener.Start();
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally
		{
			listener.Stop();
		}
	}

	private static ProtocolEnvelope BuildGoodbyeEnvelope() => new()
	{
		Type = MessageTypes.SessionGoodbye,
		Id = Guid.CreateVersion7().ToString(),
		Payload = JsonSerializer.SerializeToElement(
			new SessionGoodbyePayload { Reason = "The supervisor is stopping this plugin." },
			PluginProtocolJson.Options)
	};

	private static void ResetRestartBudget(PluginRuntimeEntry entry)
	{
		entry.Gate.Wait();
		try
		{
			entry.Attempt = 0;
			entry.RestartTimestamps.Clear();
			entry.StartupGraceRetried = false;
			entry.LastError = null;
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	private PluginSupervisorResult NotSupervisable(string pluginId)
	{
		var selfRegistering = _sessionRegistry.Snapshot()
			.Any(session => session.Origin == PluginSessionOrigin.SelfRegistered &&
				string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));

		return selfRegistering
			? PluginSupervisorResult.Fail(PluginSupervisorError.SelfRegistering,
				"This plugin registered itself with a Developer token; it manages its own process.")
			: PluginSupervisorResult.Fail(PluginSupervisorError.NotInstalled,
				"No installed version is active for this plugin id.");
	}


	private bool TryResolveInstalled(string pluginId, out InstalledPlugin? installed)
	{
		installed = SafeDiscover().FirstOrDefault(p =>
			string.Equals(p.PluginId, pluginId, StringComparison.Ordinal) && p.ActiveVersion is not null);
		return installed is not null;
	}

	private IReadOnlyList<InstalledPlugin> SafeDiscover()
	{
		try
		{
			return [.. _catalog.Discover().Where(plugin => plugin.Versions.Count > 0)];
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.DiscoverFailed(_logger, ex);
			return [];
		}
	}

	private PluginRuntimeEntry GetOrCreateEntry(string pluginId)
	{
		lock (_entriesLock)
		{
			if (_entries.TryGetValue(pluginId, out var existing))
			{
				return existing;
			}

			var created = new PluginRuntimeEntry(pluginId);
			_entries[pluginId] = created;
			return created;
		}
	}

	private static PluginRuntimeSnapshot BuildSnapshot(PluginRuntimeEntry entry, bool managed) => new()
	{
		PluginId = entry.PluginId,
		DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? entry.PluginId : entry.DisplayName,
		Version = string.IsNullOrEmpty(entry.Version) ? PluginRuntimeSnapshot.UnknownVersion : entry.Version,
		State = entry.State,
		Health = entry.Health,
		Managed = managed,
		ProcessId = entry.Process?.Id,
		LaunchId = entry.LaunchId,
		StartedAt = entry.StartedAt,
		LastExitCode = entry.LastExitCode,
		LastStopReason = entry.LastStopReason,
		LastExitAt = entry.LastExitAt,
		LastHeartbeatAt = entry.LastHeartbeatAt,
		LastHealthCheckAt = entry.LastHealthCheckAt,
		ConsecutiveHealthFailures = entry.ConsecutiveHealthFailures,
		RestartCount = entry.RestartCount,
		NextRestartAt = entry.NextRestartAt,
		LastError = entry.LastError,
		BootstrapOutput = entry.Process?.BootstrapOutput ?? entry.LastBootstrapOutput
	};

	// Timestamps and captured output are excluded: they change on almost every tick for a healthy,
	// chatty plugin, which would turn the one-second tick into a one-hertz broadcast. Nothing is lost,
	// because output only becomes worth reading alongside a state, health or exit-code change.
	private static bool SnapshotEqualsIgnoringLiveness(PluginRuntimeSnapshot a, PluginRuntimeSnapshot b)
		=> a.PluginId == b.PluginId &&
			a.DisplayName == b.DisplayName &&
			a.Version == b.Version &&
			a.State == b.State &&
			a.Health == b.Health &&
			a.Managed == b.Managed &&
			a.ProcessId == b.ProcessId &&
			a.LaunchId == b.LaunchId &&
			a.StartedAt == b.StartedAt &&
			a.LastExitCode == b.LastExitCode &&
			a.LastStopReason == b.LastStopReason &&
			a.LastExitAt == b.LastExitAt &&
			a.ConsecutiveHealthFailures == b.ConsecutiveHealthFailures &&
			a.RestartCount == b.RestartCount &&
			a.NextRestartAt == b.NextRestartAt &&
			a.LastError == b.LastError;

	private async Task<PluginTrustGateDecision> EvaluateTrustGate(string pluginId,
		string version,
		PluginTrustResult onDisk,
		CancellationToken ct)
	{
		using var scope = _scopeFactory.CreateScope();
		var trustRecords = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		var baseline = scope.ServiceProvider.GetRequiredService<IPluginTrustBaseline>();

		var record = await trustRecords.GetVersion(pluginId, version);
		var baselineExists = await baseline.Exists(ct);

		return PluginTrustGate.Evaluate(PluginTrustRecordVerdicts.Parse(record?.AdmittedVerdict),
			record?.CertificateId,
			onDisk,
			baselineExists);
	}

	private async Task ApplyTrustGateAction(string pluginId,
		string version,
		PluginTrustGateAction action,
		PluginTrustResult onDisk)
	{
		if (action == PluginTrustGateAction.None)
		{
			return;
		}

		using var scope = _scopeFactory.CreateScope();
		var trustRecords = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		await trustRecords.Upsert(pluginId,
			version,
			PluginTrustRecordVerdicts.From(onDisk.Verdict),
			onDisk.CertificateId,
			_timeProvider.GetUtcNow().UtcDateTime);
	}

	private async Task PublishRuntimeChanged(CancellationToken ct)
	{
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
			await mediator.Publish(new PluginRuntimeChangedNotification(), ct);
		}
		catch (Exception ex)
		{
			PluginInfrastructureLog.PublishRuntimeChangedFailed(_logger, ex);
		}
	}

	private sealed class PluginRuntimeEntry
	{
		public PluginRuntimeEntry(string pluginId)
		{
			PluginId = pluginId;
			DisplayName = pluginId;
		}

		public string PluginId { get; }

		public SemaphoreSlim Gate { get; } = new(1, 1);

		public string DisplayName;
		public string Version = string.Empty;

		public string? MetadataSeededForVersion;

		public PluginRuntimeState State = PluginRuntimeState.Stopped;
		public PluginHealthState Health = PluginHealthState.Unknown;

		public IPluginProcess? Process;

		public IReadOnlyList<string> LastBootstrapOutput = [];

		public string? LaunchId;
		public int? HealthPort;
		public string? HealthPath;
		public int? HealthIntervalSeconds;
		public int? HealthTimeoutSeconds;
		public int? HealthUnhealthyThreshold;
		public TimeSpan? GracefulShutdownTimeout;

		public DateTimeOffset? StartedAt;
		public DateTimeOffset? RunningSince;
		public int? LastExitCode;
		public PluginStopReason LastStopReason = PluginStopReason.None;
		public DateTimeOffset? LastExitAt;
		public DateTimeOffset? LastHeartbeatAt;
		public DateTimeOffset? LastHealthCheckAt;
		public int ConsecutiveHealthFailures;
		public int RestartCount;
		public List<DateTimeOffset> RestartTimestamps { get; } = [];
		public int Attempt;
		public DateTimeOffset? NextRestartAt;
		public string? LastError;
		public bool StartupGraceRetried;
		public bool IntegrityFailed;

		public PluginStopReason PendingIntent = PluginStopReason.None;
	}
}
