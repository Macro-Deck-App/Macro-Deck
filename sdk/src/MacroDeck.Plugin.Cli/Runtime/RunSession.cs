using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeck.Plugin.Cli.Commands;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// The orchestration <c>run</c>'s command action delegates to - kept out of
/// <c>Commands/RunCommand.cs</c> so that file stays option parsing only.
/// <para>
/// Launches its own <see cref="SupervisedPluginProcess" /> rather than going through
/// <see cref="MacroDeckTestHost.LaunchAsync" />/<c>ExternalPlugin</c>: that pair only ever launches
/// managed mode with test-host-issued credentials and only ever buffers output for later polling, while
/// <c>run</c> needs both registration modes and output streamed live as it happens. What is reused
/// instead is <see cref="MacroDeckTestHost" /> itself (the actual registration/session/WebSocket
/// protocol implementation, load-bearing and non-trivial) and the exact wire messages a real shutdown
/// uses (<see cref="MacroDeck.Plugin.Protocol.Envelope.MessageTypes.SessionGoodbye" />,
/// <see cref="MacroDeck.Plugin.Protocol.Errors.ProtocolCloseCodes.SupervisorShutdown" />) - see
/// <see cref="ShutdownAsync" />.
/// </para>
/// </summary>
internal static class RunSession
{
	private static readonly TimeSpan _defaultGrace = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _logPollInterval = TimeSpan.FromMilliseconds(150);

	public static async Task<int> RunAsync(CliConsole console,
		RunOptions options,
		IHostPairingProbe? probe,
		CancellationToken cancellationToken)
	{
		if (!TryValidateOptions(console, options, out var useStub))
		{
			return ExitCode.UsageError;
		}

		if (!TryResolveHostUrl(console, options, useStub, out var resolvedHostUrl))
		{
			return ExitCode.InputUnreadable;
		}

		PluginLaunchSpec spec;
		try
		{
			spec = await ResolveSubjectAsync(console, options, cancellationToken).ConfigureAwait(false);
		}
		catch (PluginSubjectException ex)
		{
			console.WriteError(ex.Code, ex.Message, ex.Detail);
			return ExitCode.InputUnreadable;
		}
		catch (InvalidOperationException ex)
		{
			// PluginLaunchSpec.ForArtifactAsync's own failure shape for an unreadable artifact.
			console.WriteError("artifact-unreadable", ex.Message);
			return ExitCode.InputUnreadable;
		}

		var manifestPeek = ManifestPeek.Read(spec.WorkingDirectory);
		var grace = manifestPeek.GracePeriod ?? _defaultGrace;

		await using var stubHost = useStub ? await MacroDeckTestHost.StartAsync().ConfigureAwait(false) : null;
		var hostUrl = useStub ? stubHost!.Url : resolvedHostUrl!;

		console.Info(useStub
			? $"Started a disposable stub host at {hostUrl}."
			: $"Using host at {hostUrl}.");

		if (!useStub &&
			options.Mode == PluginRegistrationMode.SelfRegistering &&
			options.EnrollmentToken is null &&
			options.PairingEnabled)
		{
			await ReportPairingExpectationAsync(console,
				probe ?? new HostPairingProbe(),
				hostUrl,
				cancellationToken).ConfigureAwait(false);
		}

		var (dataDirectory, ownedDataDirectory) = ResolveManagedDirectory(
			options.Mode == PluginRegistrationMode.Managed,
			options.DataDirectory,
			"data");
		var (stateDirectory, ownedStateDirectory) = ResolveManagedDirectory(
			options.Mode == PluginRegistrationMode.SelfRegistering,
			options.StateDirectory,
			"state");

		var pluginId = ResolveManagedPluginId(options.PluginId, manifestPeek.Id, useStub);

		if (options.Mode == PluginRegistrationMode.Managed &&
			useStub &&
			options.PluginId is null &&
			manifestPeek.Id is null)
		{
			console.WriteWarning("plugin-id-generated",
				"No manifest.json next to the launch target, so a development id was generated. If the " +
				"plugin's own manifest declares an id, the host will reject the mismatch - pass --plugin-id " +
				"<id>, or use --mode self-registering.");
		}

		var request = new PluginEnvironmentRequest
		{
			Mode = options.Mode,
			HostUrl = hostUrl,
			InstanceId = options.InstanceId ?? NewId(),
			ListenUrl = options.ListenUrl ?? "http://127.0.0.1:0",
			PluginId = pluginId,
			Secret = options.Secret ?? (useStub ? NewSecret() : null),
			DataDirectory = dataDirectory,
			LaunchId = options.LaunchId ?? NewId(),
			EnrollmentToken = options.EnrollmentToken ?? (useStub ? $"dev-token-{NewId()}" : null),
			StateDirectory = stateDirectory,
			PairingEnabled = options.PairingEnabled
		};

		var environment = PluginEnvironmentComposer.Compose(request, SnapshotEnvironment());
		var envKeys = environment.Keys.OrderBy(key => key, StringComparer.Ordinal);
		console.Trace("Composed environment: " + string.Join(", ", envKeys));

		SupervisedPluginProcess process;
		try
		{
			// These are the plugin's own console output - the command's actual result, per
			// docs/src/content/docs/guides/packaging.md's --verbosity row - so they print at every verbosity,
			// each stream on this process's matching one, unlike the stub host's forwarded log narration in
			// PollLogsAsync below, which goes through console.Info so --verbosity quiet can suppress it.
			process = SupervisedPluginProcess.Start(spec,
				environment,
				line => WritePluginOutput(console, line, fromStandardError: false),
				line => WritePluginOutput(console, line, fromStandardError: true));
		}
		catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
		{
			console.WriteError("plugin-start-failed", $"Failed to start the plugin: {ex.Message}");
			return ExitCode.InputUnreadable;
		}

		console.Info(
			$"Started process {process.ProcessId} (mode: {request.Mode}, host: {hostUrl}). Press Ctrl-C to stop.");

		using var backgroundCts = new CancellationTokenSource();
		var backgroundTasks = useStub
			? new[]
			{
				PollLogsAsync(console, stubHost!, backgroundCts.Token),
				AnnounceSessionAsync(console, stubHost!, backgroundCts.Token)
			}
			: [];

		try
		{
			// cancellationToken is Program.cs's single Ctrl-C/SIGTERM token (see ShutdownSignal), shared by
			// every command. Every other command lets its own OperationCanceledException propagate to
			// Program.cs's top-level catch, which reports ExitCode.Cancelled and exits - but run must not:
			// the whole point of catching it here, instead of letting it propagate, is to run the
			// documented supervisor shutdown sequence first.
			try
			{
				await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
				console.Info($"The plugin process exited with code {process.ExitCode}.");
				return process.ExitCode;
			}
			catch (OperationCanceledException)
			{
				console.Info("Stopping the plugin...");
				await ShutdownAsync(console, stubHost, process, grace).ConfigureAwait(false);
				return ExitCode.Cancelled;
			}
		}
		finally
		{
			await backgroundCts.CancelAsync().ConfigureAwait(false);

			try
			{
				await Task.WhenAll(backgroundTasks).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
			{
			}

			await process.DisposeAsync().ConfigureAwait(false);

			if (ownedDataDirectory is not null)
			{
				TryDeleteDirectory(ownedDataDirectory);
			}

			if (ownedStateDirectory is not null)
			{
				TryDeleteDirectory(ownedStateDirectory);
			}
		}
	}

	/// <summary>
	/// The documented supervisor shutdown sequence: <c>session.goodbye</c>, then a close with
	/// <see cref="ProtocolCloseCodes.SupervisorShutdown" />, then up to <paramref name="grace" /> for the
	/// process to exit on its own, and only then a kill - identical in substance to
	/// <c>ExternalPlugin.StopGracefullyAsync</c>, built from the same public
	/// <see cref="MacroDeckTestHost" /> primitives that method itself uses, since
	/// <see cref="MacroDeckTestHost.LaunchAsync" /> (which alone can produce an <c>ExternalPlugin</c>) only
	/// ever launches managed mode - see this file's own remarks on why <c>run</c> launches its own
	/// process instead. Against a real host (<paramref name="stubHost" /> null), there is no session
	/// channel this process owns to say goodbye on, so only the grace-then-kill half applies.
	/// </summary>
	private static async Task ShutdownAsync(CliConsole console,
		MacroDeckTestHost? stubHost,
		SupervisedPluginProcess process,
		TimeSpan grace)
	{
		if (stubHost is not null)
		{
			try
			{
				await stubHost.SendAsync(new ProtocolEnvelope
				{
					Type = MessageTypes.SessionGoodbye,
					Id = Guid.CreateVersion7().ToString(),
					Payload = JsonSerializer.SerializeToElement(
						new SessionGoodbyePayload { Reason = "macrodeck-plugin run is stopping." },
						PluginProtocolJson.Options)
				}).ConfigureAwait(false);
			}
			catch (InvalidOperationException)
			{
				// No session ever formed - proceed straight to closing/killing the process.
			}

			try
			{
				await stubHost.DisconnectAsync(ProtocolCloseCodes.SupervisorShutdown).ConfigureAwait(false);
			}
			catch (InvalidOperationException)
			{
			}
		}

		try
		{
			// CancellationToken.None deliberately: this wait is already the shutdown sequence a prior
			// cancellation triggered, bounded by grace alone rather than the (already cancelled) outer
			// token.
			await process.WaitForExitAsync().WaitAsync(grace, CancellationToken.None).ConfigureAwait(false);
			console.Info($"The plugin exited within its {grace.TotalSeconds:0}s grace period.");
		}
		catch (TimeoutException)
		{
			console.Info($"The plugin did not exit within its {grace.TotalSeconds:0}s grace period; killing it.");
			process.Kill();
		}
	}

	private static bool TryValidateOptions(CliConsole console, RunOptions options, out bool useStub)
	{
		useStub = options.StubHost;

		var selectorCount
			= new[] { options.Project, options.Executable, options.Artifact }.Count(value => value is not null);
		if (selectorCount != 1)
		{
			console.WriteError("invalid-selector-count",
				"Specify exactly one of --project, --executable or --artifact.");
			return false;
		}

		if (options.HostUrl is not null && options.StubHost)
		{
			console.WriteError("conflicting-options", "Specify at most one of --host-url or --stub-host.");
			return false;
		}

		if (options.Mode == PluginRegistrationMode.Managed && !useStub)
		{
			console.WriteError("managed-needs-stub-host",
				"Managed mode needs a launch bootstrap token only a real supervisor can mint - " +
				"run cannot manufacture one against a real host. Use --stub-host, or --mode " +
				"self-registering (the default) against the running host.");
			return false;
		}

		// What pairing will actually do is reported later, from ReportPairingExpectationAsync: it depends on
		// the host, which is not resolved yet here.
		if (options.Mode == PluginRegistrationMode.SelfRegistering &&
			!useStub &&
			options.EnrollmentToken is null &&
			!options.PairingEnabled)
		{
			console.WriteError("enrollment-token-required",
				"--mode self-registering against a real host needs --enrollment-token when --pairing is off.");
			return false;
		}

		return true;
	}

	/// <summary>
	/// Says what pairing is actually going to do, once the host is known. Deliberately not part of
	/// <see cref="TryValidateOptions" />: that runs before a host URL exists, which is how the old
	/// unconditional "a pairing prompt will appear" ended up promising a prompt that Developer Mode
	/// being off makes impossible.
	/// </summary>
	private static async Task ReportPairingExpectationAsync(CliConsole console,
		IHostPairingProbe probe,
		string hostUrl,
		CancellationToken cancellationToken)
	{
		var pairing = await probe.ProbeAsync(hostUrl, cancellationToken).ConfigureAwait(false);

		if (pairing?.DeveloperModeEnabled == false)
		{
			console.WriteError("developer-mode-disabled",
				"Developer Mode is disabled in Macro Deck. Enable it under Settings > Developer before " +
				"running local plugins.",
				"The plugin keeps waiting and pairs as soon as you enable it - no restart needed.");
			return;
		}

		if (pairing?.DeveloperModeEnabled == true)
		{
			console.Info("Developer Mode is enabled; a pairing prompt will appear in Macro Deck.");
			return;
		}

		// Null: unreachable, or a host too old to report it. Neither is a reason to claim Developer Mode
		// is off, so this stays exactly what run reported before the probe existed.
		console.WriteWarning("pairing-prompt-expected",
			"No --enrollment-token given; a pairing prompt will appear in Macro Deck and must be approved " +
			"there before the plugin can connect.");
	}

	/// <summary>
	/// Which host <c>run</c> launches against: <c>--stub-host</c>, else an explicit <c>--host-url</c>, else
	/// the running desktop host discovered through its loopback port file - the default, so that a bare
	/// <c>run</c> reproduces the real thing rather than a stub. Not finding one is reported rather than
	/// silently falling back to the stub: a plugin talking to a stub behaves differently enough from one
	/// talking to Macro Deck that a silent substitution would be its own debugging session.
	/// </summary>
	private static bool TryResolveHostUrl(CliConsole console, RunOptions options, bool useStub, out string? hostUrl)
	{
		hostUrl = null;

		if (useStub)
		{
			return true;
		}

		if (options.HostUrl is { } explicitUrl)
		{
			hostUrl = explicitUrl;
			return true;
		}

		if (HostDiscovery.Discover() is { } discovered)
		{
			hostUrl = discovered.Url;
			console.Trace($"Discovered the running host through {discovered.PortFilePath}.");
			return true;
		}

		console.WriteError("host-not-found",
			"No running Macro Deck host was found. Its loopback port file is written while the host runs " +
			"and removed when it stops; none was readable at " +
			string.Join(" or ", HostDiscovery.ProbedPaths(Path.GetTempPath())) +
			". Start Macro Deck, pass " +
			"--host-url <url>, or use --stub-host to run against a disposable stub host instead.");
		return false;
	}

	private static Task<PluginLaunchSpec> ResolveSubjectAsync(CliConsole console,
		RunOptions options,
		CancellationToken cancellationToken)
	{
		if (options.Project is { } project)
		{
			return PluginSubjectResolver.ResolveProjectAsync(project, console, cancellationToken);
		}

		if (options.Executable is { } executable)
		{
			return Task.FromResult(PluginSubjectResolver.ResolveExecutable(executable));
		}

		return PluginLaunchSpec.ForArtifactAsync(options.Artifact!, cancellationToken);
	}

	/// <summary>The managed-mode <c>MACRO_DECK_PLUGIN_ID</c> precedence: an explicit <c>--plugin-id</c> wins,
	/// then the id the plugin's own <c>manifest.json</c> declares, then a generated development id as the
	/// last resort. The manifest id is preferred over inventing one because
	/// <see cref="MacroDeck.Plugin.Hosting.PluginHostBuilder.Build" /> rejects a configured id that disagrees
	/// with the manifest's own - a real supervisor reads the id from the manifest of the version it
	/// activates rather than making one up, and <c>run</c> has to agree or it can never succeed against a
	/// plugin whose manifest declares a real id.</summary>
	internal static string? ResolveManagedPluginId(string? explicitId, string? manifestId, bool useStub) =>
		explicitId ?? manifestId ?? (useStub ? $"dev.macrodeck-plugin.{NewId()}" : null);

	private static (string? Directory, string? Owned) ResolveManagedDirectory(bool needed,
		string? requested,
		string label)
	{
		if (!needed)
		{
			return (null, null);
		}

		if (requested is { Length: > 0 })
		{
			Directory.CreateDirectory(requested);
			return (requested, null);
		}

		var owned = Directory.CreateTempSubdirectory($"macrodeck-plugin-run-{label}-").FullName;
		return (owned, owned);
	}

	private static async Task PollLogsAsync(CliConsole console,
		MacroDeckTestHost host,
		CancellationToken cancellationToken)
	{
		var seen = 0;

		try
		{
			while (true)
			{
				var events = host.Logs.Events;

				for (; seen < events.Count; seen++)
				{
					var logEvent = events[seen];
					WriteForwardedLog(console, logEvent.Level, logEvent.SourceContext, logEvent.Message);
				}

				await Task.Delay(_logPollInterval, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>The stub host's forwarded log-event narration, kept as a small seam so its formatting and
	/// its use of <see cref="CliConsole.Info" /> (so <c>--verbosity quiet</c> suppresses it, unlike the
	/// plugin's own <c>[plugin]</c>/<c>[plugin:stderr]</c> output) are unit-testable without a running
	/// <see cref="MacroDeckTestHost" />.</summary>
	internal static void WriteForwardedLog(CliConsole console, string level, string? sourceContext, string message) =>
		console.Info($"[log:{level}] {sourceContext}: {message}");

	/// <summary>The launched plugin's own console output. The counterpart seam to
	/// <see cref="WriteForwardedLog" />, and never gated by <c>--verbosity quiet</c>: this is the command's
	/// actual result. Each of the child's streams is forwarded to this process's matching one, so a plugin's
	/// stderr stays stderr for whatever consumes the CLI's output.</summary>
	internal static void WritePluginOutput(CliConsole console, string line, bool fromStandardError)
	{
		if (fromStandardError)
		{
			console.WriteErrorLine($"[plugin:stderr] {line}");
		}
		else
		{
			console.WriteLine($"[plugin] {line}");
		}
	}

	private static async Task AnnounceSessionAsync(CliConsole console,
		MacroDeckTestHost host,
		CancellationToken cancellationToken)
	{
		try
		{
			var waitTask = host.WaitForSessionAsync(TimeSpan.FromMinutes(5));
			var cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

			if (await Task.WhenAny(waitTask, cancelTask).ConfigureAwait(false) == waitTask)
			{
				var session = await waitTask.ConfigureAwait(false);
				console.Info($"Session established (negotiated plugin protocol v{session.NegotiatedVersion}).");
			}
		}
		catch (PluginTestTimeoutException)
		{
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static Dictionary<string, string?> SnapshotEnvironment()
	{
		var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);

		foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
		{
			snapshot[(string)entry.Key] = (string?)entry.Value;
		}

		return snapshot;
	}

	private static string NewId() => Guid.NewGuid().ToString("N");

	private static string NewSecret()
		=> Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
