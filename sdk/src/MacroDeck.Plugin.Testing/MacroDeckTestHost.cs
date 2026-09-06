using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Configuration;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Reconnection;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A real host for the Macro Deck plugin protocol, on a loopback Kestrel: discovery, registration,
/// sessions and the WebSocket - everything a plugin's <c>PluginConnectionHostedService</c> talks to,
/// without running Macro Deck itself.
///
/// <para>
/// A plugin under test still calls out to <c>IIntegrationContext</c> normally over <c>host.invoke</c>;
/// this host answers those calls against its own internal fakes so a plugin that does not explicitly
/// override <c>IIntegrationContext</c> - the sample, for instance - behaves normally rather than
/// hanging on a call nothing ever replies to. A test that wants to assert on the fakes directly should
/// inject its own from <see cref="Fakes" /> through <c>PluginHostBuilder.ConfigureServices</c>, or use
/// <see cref="PluginTestHarness" />, whose <c>Context</c> exposes exactly that.
/// </para>
/// </summary>
public sealed class MacroDeckTestHost : IAsyncDisposable
{
	private readonly WebApplication _application;
	private readonly MacroDeckTestHostOptions _options;
	private readonly ConcurrentDictionary<string, SessionRecord> _sessionsByToken = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, SessionRecord> _sessionsById = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, PluginConnection> _liveByPluginId = new(StringComparer.Ordinal);
	private readonly Channel<PluginSessionView> _sessionViews = Channel.CreateUnbounded<PluginSessionView>();
	private readonly Channel<ProtocolEnvelope> _fromPlugin = Channel.CreateUnbounded<ProtocolEnvelope>();
	private readonly CancellationTokenSource _stopping = new();
	private readonly List<Task> _connectionLoops = [];
	private readonly Lock _connectionLoopsGate = new();
	private readonly ConcurrentDictionary<string, PairingRecord> _pairingRequestsById = new(StringComparer.Ordinal);

	/// <summary>Advertised, and used as, both the pairing request lifetime and poll interval - short
	/// enough that a suite of checks never spends real seconds polling for one.</summary>
	private const int PairingRequestLifetimeSeconds = 30;

	private const int PairingPollIntervalSeconds = 1;

	private volatile PluginConnection? _current;

	private MacroDeckTestHost(WebApplication application, MacroDeckTestHostOptions options)
	{
		_application = application;
		_options = options;
	}

	/// <summary>The url a plugin under test should be pointed at.</summary>
	public string Url { get; private set; } = string.Empty;

	/// <summary>Every registration request the host has received, in order - both from <c>POST /api/plugins/registration</c>
	/// directly and from a pairing request this host approved and redeemed (see <see cref="PairingRedemptions" />),
	/// which is issued through the exact same bookkeeping so the two are indistinguishable from here on.</summary>
	public ConcurrentQueue<PluginRegistrationRequest> Registrations { get; } = new();

	/// <summary>Every pairing request created through <c>POST /api/plugins/pairing</c>, in order.</summary>
	public ConcurrentQueue<PluginPairingRequest> PairingRequests { get; } = new();

	/// <summary>Every redemption attempt made through <c>POST /api/plugins/pairing/{requestId}/redemption</c>,
	/// in order - including a wrong verifier, so a test can prove the verifier a plugin redeemed with is
	/// exactly the one whose hash matches the challenge it sent when the request was created.</summary>
	public ConcurrentQueue<PluginPairingRedemptionRequest> PairingRedemptions { get; } = new();

	/// <summary>Every session request the host has received, in order. Only fresh sessions call this - a resume does not.</summary>
	public ConcurrentQueue<PluginSessionRequest> Sessions { get; } = new();

	/// <summary>Session ids that will never come back: ended explicitly, or replaced by a newer connection for the same plugin.</summary>
	public ConcurrentQueue<string> DeletedSessions { get; } = new();

	/// <summary>Enrollment tokens presented at registration, so a test can prove one was used exactly once.</summary>
	public ConcurrentQueue<string?> EnrollmentTokens { get; } = new();

	/// <summary>
	/// Secrets this host has issued at registration, in order - so a test can prove the secret a
	/// self-registering plugin persisted (see <see cref="CredentialFile" />) is the very one this host
	/// gave it, not merely that some secret was written to disk.
	/// </summary>
	public ConcurrentQueue<string> IssuedSecrets { get; } = new();

	/// <summary>Every envelope sent or received across every connection this host has accepted.</summary>
	public ProtocolMessageLog Messages { get; } = new();

	/// <summary>Every structured log event a plugin under test has forwarded.</summary>
	public PluginLogCollector Logs { get; } = new();

	/// <summary>Every event a plugin under test has published.</summary>
	public PluginEventCollector Events { get; } = new();

	/// <summary>Starts a new host on a random loopback port.</summary>
	public static async Task<MacroDeckTestHost> StartAsync(MacroDeckTestHostOptions? options = null)
	{
		options ??= new MacroDeckTestHostOptions();

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
		builder.Logging.ClearProviders();

		var application = builder.Build();
		var host = new MacroDeckTestHost(application, options);

		application.UseWebSockets();
		host.MapEndpoints();

		await application.StartAsync().ConfigureAwait(false);

		host.Url = application.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First();

		return host;
	}

	/// <summary>
	/// Points <paramref name="builder" /> at this host by writing <c>MacroDeck:Plugin:*</c> configuration
	/// (never <c>MACRO_DECK_PLUGIN_*</c> environment variables, which would also apply to every other
	/// process running the same test suite) - the key names come from
	/// <see cref="PluginEnvironmentConfiguration.KeysByVariable" /> so the two never restate them
	/// differently - builds and starts it, then returns once it is running. Does not wait for a session;
	/// call <see cref="WaitForSessionAsync" /> for that.
	/// </summary>
	/// <param name="builder">The plugin, configured but not yet built.</param>
	/// <param name="credentials">How the plugin should authenticate. Defaults to
	/// <see cref="PluginTestCredentials.SelfRegistering" />.</param>
	/// <param name="manifest">
	/// The plugin's identity. Defaults to a fresh <see cref="PluginTestManifest" /> when omitted - owned
	/// and disposed with the returned <see cref="InProcessPlugin" /> - so the common case needs no
	/// manifest of its own. When <paramref name="credentials" /> resolves to
	/// <see cref="PluginTestCredentials.Managed" />, the default manifest declares that same id, matching
	/// what a real supervisor does (it launches with the id it read from the manifest it is activating) -
	/// <c>PluginHostBuilder.Build</c> otherwise rejects a manifest id that disagrees with a configured
	/// one. Pass a manifest explicitly only when the test is about a specific id, name, version,
	/// description or icon; that instance is then the caller's to dispose.
	/// </param>
	public async Task<InProcessPlugin> HostAsync(
		PluginHostBuilder builder,
		PluginTestCredentials? credentials = null,
		PluginTestManifest? manifest = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var configuration = builder.Configuration;
		var resolved = credentials ?? PluginTestCredentials.SelfRegistering;

		var ownedManifest = manifest is null
			? new PluginTestManifest(id: resolved.Mode == PluginRegistrationMode.Managed ? resolved.Id : null)
			: null;
		(manifest ?? ownedManifest!).Apply(builder);

		configuration[ConfigKey("HostUrl")] = Url;

		// Created only when the caller has not already pointed the builder at its own state directory
		// (as A14's tests do); owned by the returned InProcessPlugin from here on, so it is deleted with
		// the plugin rather than left behind under the OS temp directory for the rest of the run.
		TempStateDirectory? ownedStateDirectory = null;

		if (string.IsNullOrEmpty(configuration[ConfigKey("StateDirectory")]))
		{
			ownedStateDirectory = new TempStateDirectory();
			configuration[ConfigKey("StateDirectory")] = ownedStateDirectory.Path;
		}

		if (resolved.Mode == PluginRegistrationMode.Managed)
		{
			configuration[ConfigKey("Mode")] = nameof(PluginRegistrationMode.Managed);
			configuration[ConfigKey("Id")] = resolved.Id;
			configuration[ConfigKey("Secret")] = resolved.Secret;
		}
		else
		{
			configuration[ConfigKey("Mode")] = nameof(PluginRegistrationMode.SelfRegistering);
			configuration[ConfigKey("EnrollmentToken")] = resolved.EnrollmentToken;
		}

		PluginApplication application;
		try
		{
			application = builder.Build();
		}
		catch
		{
			// Building failed - there is no returned InProcessPlugin for a caller to dispose, so
			// anything owned by this call would otherwise leak.
			ownedManifest?.Dispose();
			ownedStateDirectory?.Dispose();
			throw;
		}

		await application.StartAsync().ConfigureAwait(false);

		return new InProcessPlugin(application, ownedStateDirectory, ownedManifest);
	}

	/// <summary>
	/// Launches <paramref name="spec" /> as a real child process, as a supervisor launching a managed
	/// plugin would: a fresh id and secret in its environment (so it never calls the registration
	/// endpoint), and its own loopback port pre-assigned through <c>ASPNETCORE_URLS</c> so this host
	/// knows where to probe it. Environment variable names come from
	/// <see cref="PluginEnvironmentConfiguration.KeysByVariable" />. Returns once the process is serving
	/// its own <c>/_macrodeck/health</c> - before any session necessarily exists, and even while
	/// <see cref="MacroDeckTestHostOptions.SessionCreation" /> is holding every session request, since
	/// this waits on liveness alone and never on readiness; call <see cref="WaitForSessionAsync" /> for
	/// that.
	/// </summary>
	public async Task<ExternalPlugin> LaunchAsync(PluginLaunchSpec spec)
	{
		ArgumentNullException.ThrowIfNull(spec);

		var port = ReserveLoopbackPort();

		// A fresh random id would disagree with whatever id the target's own manifest.json declares -
		// every built executable has had one since #522 - and PluginHostBuilder.Build now treats that
		// disagreement as a build error rather than letting configuration silently win. Peeking the
		// manifest's own id and handing back exactly that is what a real supervisor does anyway: it reads
		// MACRO_DECK_PLUGIN_ID from the manifest of the version it is activating, never invents one.
		var credentials = TryPeekManifestId(spec.WorkingDirectory) is { Length: > 0 } manifestId
			? PluginTestCredentials.ManagedWithId(manifestId)
			: PluginTestCredentials.Managed;

		var stateDirectory = new TempStateDirectory();

		// The values a managed launch needs, keyed by the same MacroDeck:Plugin:* configuration key
		// HostAsync writes - projected onto their MACRO_DECK_PLUGIN_* environment variable names via
		// PluginEnvironmentConfiguration.KeysByVariable, so neither path restates a name the other could drift from.
		var values = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[ConfigKey("HostUrl")] = Url,
			[ConfigKey("Mode")] = nameof(PluginRegistrationMode.Managed),
			[ConfigKey("Id")] = credentials.Id,
			[ConfigKey("Secret")] = credentials.Secret,
			[ConfigKey("StateDirectory")] = stateDirectory.Path
		};

		var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
			{ ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}" };

		// spec.Environment first, so the MACRO_DECK_PLUGIN_* scrub below always runs last and wins - a
		// caller cannot accidentally clobber the values (see PluginEnvironmentConfiguration.KeysByVariable)
		// this host itself depends on to find and authenticate the process it is about to start.
		foreach (var (name, value) in spec.Environment)
		{
			environment[name] = value;
		}

		foreach (var (variable, configKey) in PluginEnvironmentConfiguration.KeysByVariable)
		{
			if (values.TryGetValue(configKey, out var value))
			{
				environment[variable] = value;
			}
		}

		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			FileName = spec.ExecutablePath,
			WorkingDirectory = spec.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		foreach (var argument in spec.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var (name, value) in environment)
		{
			startInfo.Environment[name] = value;
		}

		var process = System.Diagnostics.Process.Start(startInfo) ??
			throw new InvalidOperationException($"Failed to start '{spec.ExecutablePath}'.");

		var baseAddress = new Uri($"http://127.0.0.1:{port}");

		// credentials.Id is always set for PluginTestCredentials.Managed - only SelfRegistering leaves it
		// null - and is unique to this one launch, which is what lets ExternalPlugin address its own
		// connection later instead of whichever one this host most recently accepted. stateDirectory's
		// ownership passes to the plugin here too, so it is deleted with the process rather than left
		// behind under the OS temp directory for the rest of the run.
		var plugin = new ExternalPlugin(process, baseAddress, this, credentials.Id!, stateDirectory);

		try
		{
			await WaitUntilServingAsync(plugin).ConfigureAwait(false);
		}
		catch (PluginProcessExitedException)
		{
			// The process is already gone, so there is nothing left running to leak; leave it undisposed
			// and attached to the exception (see WaitUntilServingAsync) so the caller can still inspect
			// HasExited/ExitCode/StandardError on it.
			throw;
		}
		catch
		{
			// Any other failure (most notably PluginTestTimeoutException, when the process is still
			// running but never started serving) would otherwise leave a live child process running for
			// the rest of the test run with nothing left holding a reference to it.
			await plugin.DisposeAsync().ConfigureAwait(false);
			throw;
		}

		return plugin;
	}

	/// <summary>Waits for the next session to complete its handshake - a fresh one or a resume.</summary>
	public async Task<PluginSessionView> WaitForSessionAsync(TimeSpan? timeout = null)
	{
		using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

		try
		{
			return await _sessionViews.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested)
		{
			throw new PluginTestTimeoutException("No plugin session completed its handshake before the deadline.");
		}
	}

	/// <summary>Waits for the next message of type <paramref name="type" /> the plugin sends, failing rather than hanging.</summary>
	public async Task<ProtocolEnvelope> NextAsync(string type, TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(type);
		using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

		try
		{
			await foreach (var envelope in _fromPlugin.Reader.ReadAllAsync(deadline.Token))
			{
				if (string.Equals(envelope.Type, type, StringComparison.Ordinal))
				{
					return envelope;
				}
			}
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested)
		{
		}

		throw new PluginTestTimeoutException($"The plugin never sent a '{type}'.");
	}

	/// <summary>Sends an envelope to the current session's plugin.</summary>
	public Task SendAsync(ProtocolEnvelope envelope) => CurrentConnection().SendAsync(envelope);

	/// <summary>Sends raw text to the current session's plugin, bypassing envelope construction entirely.</summary>
	public Task SendRawAsync(string rawJson) => CurrentConnection().SendRawAsync(rawJson);

	/// <summary>Closes the current session's socket from the host side with <paramref name="closeCode" />.</summary>
	public Task DisconnectAsync(int closeCode) => DisconnectAsync(closeCode, CancellationToken.None);

	/// <summary>Closes the current session's socket from the host side with <paramref name="closeCode" />.</summary>
	public Task DisconnectAsync(int closeCode, CancellationToken cancellationToken)
		=> CurrentConnection().CloseAsync(closeCode, "The test host closed the connection.", cancellationToken);

	/// <summary>Tells the current session's plugin to hold back everything but pause-exempt traffic.</summary>
	public Task PauseDrainingAsync() => CurrentConnection().PauseAsync();

	/// <summary>Tells the current session's plugin to resume normal sending.</summary>
	public Task ResumeDrainingAsync() => CurrentConnection().ResumeAsync();

	/// <summary>
	/// Sends an envelope to a specific plugin's own connection, identified by plugin id - unlike
	/// <see cref="SendAsync(ProtocolEnvelope)" />, unaffected by whichever connection this host happens
	/// to have accepted most recently. What <see cref="ExternalPlugin.StopGracefullyAsync" /> uses so it
	/// addresses its own plugin even when another one is also live.
	/// </summary>
	internal Task SendToPluginAsync(string pluginId, ProtocolEnvelope envelope)
		=> ConnectionFor(pluginId).SendAsync(envelope);

	/// <summary>Closes a specific plugin's own connection, identified by plugin id - the plugin-scoped
	/// counterpart of <see cref="DisconnectAsync(int, CancellationToken)" />. See <see cref="SendToPluginAsync" />.</summary>
	internal Task DisconnectPluginAsync(string pluginId, int closeCode, CancellationToken cancellationToken)
		=> ConnectionFor(pluginId).CloseAsync(closeCode, "The test host closed the connection.", cancellationToken);

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();

		Task[] loops;
		lock (_connectionLoopsGate)
		{
			loops = [.. _connectionLoops];
		}

		try
		{
			await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
		{
		}

		await _application.StopAsync().ConfigureAwait(false);
		await _application.DisposeAsync().ConfigureAwait(false);
		_stopping.Dispose();
	}

	private PluginConnection CurrentConnection()
		=> _current ??
			throw new InvalidOperationException(
				"No plugin session is currently connected. Call HostAsync/LaunchAsync and WaitForSessionAsync first.");

	private PluginConnection ConnectionFor(string pluginId)
		=> _liveByPluginId.TryGetValue(pluginId, out var connection)
			? connection
			: throw new InvalidOperationException(
				$"No plugin session is currently connected for plugin id '{pluginId}'.");

	private void MapEndpoints()
	{
		_application.MapGet(ProtocolConstants.ProtocolDiscoveryPath,
			() => Results.Json(new PluginProtocolDescriptor
				{
					SupportedVersions = _options.OfferedVersions,
					CapabilityKinds = CapabilityKinds.All,
					Limits = _options.Limits,
					Timeouts = _options.Timeouts,
					Pairing = new PluginPairingDescriptor
					{
						Supported = true,
						RequestLifetimeSeconds = PairingRequestLifetimeSeconds,
						PollIntervalSeconds = PairingPollIntervalSeconds
					}
				},
				PluginProtocolJson.Options));

		// Cast to Delegate explicitly: a bare Task<IResult>-returning method group is also assignable to
		// RequestDelegate (Task<IResult> is a Task), which would silently discard the IResult instead of
		// writing it to the response - the cast picks the minimal API request-delegate factory instead.
		_application.MapPost(ProtocolConstants.RegistrationPath, (Delegate)HandleRegistrationAsync);
		_application.MapPost(ProtocolConstants.PairingPath, (Delegate)HandleCreatePairingAsync);
		_application.MapGet($"{ProtocolConstants.PairingPath}/{{requestId}}", HandlePairingStatus);
		_application.MapPost($"{ProtocolConstants.PairingPath}/{{requestId}}/redemption",
			(Delegate)HandleRedeemPairingAsync);
		_application.MapPost(ProtocolConstants.SessionsPath, (Delegate)HandleCreateSessionAsync);
		_application.MapDelete($"{ProtocolConstants.SessionsPath}/{{sessionId}}", HandleDeleteSession);
		_application.Map(ProtocolConstants.WebSocketPath, (Delegate)HandleWebSocketAsync);
	}

	private async Task<IResult> HandleRegistrationAsync(HttpContext context)
	{
		var token = context.Request.Headers[PluginAuthDefaults.EnrollmentTokenHeaderName].FirstOrDefault();
		EnrollmentTokens.Enqueue(token);

		var request = await context.Request.ReadFromJsonAsync<PluginRegistrationRequest>(PluginProtocolJson.Options)
			.ConfigureAwait(false);

		if (request is not null)
		{
			Registrations.Enqueue(request);
		}

		if (!_options.Registration.TryAuthorize(token, out var errorCode))
		{
			// A wrong token is permanently wrong (401, fatal - retrying it would never help); an explicit
			// Reject is a generic host-side refusal (503, retryable), so a self-registering plugin keeps
			// trying rather than giving up for good - see PluginRegistrationException.IsFatal.
			var statusCode = string.Equals(errorCode, ProtocolErrorCodes.Unauthenticated, StringComparison.Ordinal)
				? StatusCodes.Status401Unauthorized
				: StatusCodes.Status503ServiceUnavailable;

			return Results.Json(
				new { error = new { code = errorCode, message = ProtocolErrorMessages.For(errorCode) } },
				statusCode: statusCode);
		}

		return IssueSecret(request?.PluginId ?? string.Empty);
	}

	private async Task<IResult> HandleCreatePairingAsync(HttpContext context)
	{
		var request = await context.Request.ReadFromJsonAsync<PluginPairingRequest>(PluginProtocolJson.Options)
			.ConfigureAwait(false);

		if (request is not null)
		{
			PairingRequests.Enqueue(request);
		}

		var requestId = Guid.CreateVersion7().ToString();

		_pairingRequestsById[requestId] = new PairingRecord
		{
			PluginId = request?.PluginId ?? string.Empty,
			DisplayName = request?.DisplayName ?? string.Empty,
			CodeChallenge = request?.CodeChallenge ?? string.Empty,
			Status = _options.Pairing.InitialStatus
		};

		return Results.Json(new PluginPairingResponse
			{
				RequestId = requestId,
				ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PairingRequestLifetimeSeconds),
				PollIntervalSeconds = PairingPollIntervalSeconds
			},
			PluginProtocolJson.Options,
			statusCode: StatusCodes.Status201Created);
	}

	private IResult HandlePairingStatus(string requestId)
	{
		// Deliberately 200 with status "expired" for an unknown id, exactly like the real host: it keeps
		// a polling plugin's own state machine total instead of adding an error path nothing else needs.
		var status = _pairingRequestsById.TryGetValue(requestId, out var pairing)
			? pairing.Status
			: PluginPairingStatuses.Expired;

		return Results.Json(new PluginPairingStatusResponse
				{ Status = status, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PairingRequestLifetimeSeconds) },
			PluginProtocolJson.Options);
	}

	private async Task<IResult> HandleRedeemPairingAsync(HttpContext context, string requestId)
	{
		var request = await context.Request
			.ReadFromJsonAsync<PluginPairingRedemptionRequest>(PluginProtocolJson.Options).ConfigureAwait(false);

		if (request is not null)
		{
			PairingRedemptions.Enqueue(request);
		}

		// Every failure - unknown request, not yet approved, already redeemed, wrong verifier - answers
		// identically, by contract: a redemption attempt never learns which one happened.
		if (request is null ||
			!_pairingRequestsById.TryGetValue(requestId, out var pairing) ||
			pairing.Redeemed ||
			!string.Equals(pairing.Status, PluginPairingStatuses.Approved, StringComparison.Ordinal))
		{
			return Results.Unauthorized();
		}

		var expectedChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(request.CodeVerifier)));

		if (!string.Equals(expectedChallenge, pairing.CodeChallenge, StringComparison.Ordinal))
		{
			return Results.Unauthorized();
		}

		pairing.Redeemed = true;

		// Recorded here, rather than unconditionally the way HandleRegistrationAsync records every
		// enrollment-token attempt including a rejected one: a pairing request already has its own
		// attempt-level bookkeeping in PairingRequests/PairingRedemptions, so Registrations only gains an
		// entry once pairing actually succeeds - the point at which it becomes indistinguishable from an
		// enrollment-issued registration to the rest of this host.
		Registrations.Enqueue(new PluginRegistrationRequest
			{ PluginId = pairing.PluginId, DisplayName = pairing.DisplayName });

		return IssueSecret(pairing.PluginId);
	}

	/// <summary>Issues a fresh secret and the <see cref="PluginRegistrationResponse" /> both the
	/// enrollment-token path (<see cref="HandleRegistrationAsync" />) and a successful pairing redemption
	/// (<see cref="HandleRedeemPairingAsync" />) answer with - each caller records <see cref="Registrations" />
	/// itself, since the two attempt-tracking semantics differ (see <see cref="HandleRedeemPairingAsync" />'s
	/// own remarks).</summary>
	private IResult IssueSecret(string pluginId)
	{
		var secret = GenerateSecret();
		IssuedSecrets.Enqueue(secret);

		return Results.Json(new PluginRegistrationResponse { PluginId = pluginId, PluginSecret = secret },
			PluginProtocolJson.Options,
			statusCode: StatusCodes.Status201Created);
	}

	private async Task<IResult> HandleCreateSessionAsync(HttpContext context)
	{
		if (!_options.SessionCreation.IsOpen)
		{
			// Retryable (not one of PluginRegistrationException.IsFatal's codes), so a plugin already
			// backing off simply tries again rather than faulting - see PluginSessionGate.
			return Results.Json(new { error = new { message = "MacroDeckTestHost is holding session creation open." } },
				statusCode: StatusCodes.Status503ServiceUnavailable);
		}

		var pluginId = context.Request.Headers[PluginAuthDefaults.PluginIdHeaderName].FirstOrDefault();
		var secret = context.Request.Headers[PluginAuthDefaults.PluginSecretHeaderName].FirstOrDefault();

		if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(secret))
		{
			return Results.Unauthorized();
		}

		var request = await context.Request.ReadFromJsonAsync<PluginSessionRequest>(PluginProtocolJson.Options)
			.ConfigureAwait(false);

		if (request is null)
		{
			return Results.BadRequest();
		}

		Sessions.Enqueue(request);

		var negotiatedVersion = _options.OfferedVersions
			.Where(version =>
				version >= request.RequestedVersion.Minimum && version <= request.RequestedVersion.Maximum)
			.OrderDescending()
			.Select(version => (int?)version)
			.FirstOrDefault();

		if (negotiatedVersion is null)
		{
			return Results.Json(new
				{
					error = new
					{
						code = ProtocolErrorCodes.ProtocolVersionUnsupported,
						message = ProtocolErrorMessages.For(ProtocolErrorCodes.ProtocolVersionUnsupported)
					}
				},
				statusCode: StatusCodes.Status422UnprocessableEntity);
		}

		var accepted = request.Capabilities.Select(_options.Negotiate).ToList();
		var sessionId = Guid.CreateVersion7().ToString();
		var token = Guid.CreateVersion7().ToString("N");

		var record = new SessionRecord
		{
			SessionId = sessionId,
			SessionToken = token,
			PluginId = pluginId!,
			NegotiatedVersion = negotiatedVersion.Value,
			Declared = request.Capabilities,
			Accepted = accepted
		};

		_sessionsByToken[token] = record;
		_sessionsById[sessionId] = record;

		return Results.Json(new PluginSessionResponse
			{
				SessionId = sessionId,
				SessionToken = token,
				NegotiatedVersion = negotiatedVersion.Value,
				Capabilities = accepted,
				Limits = _options.Limits,
				Timeouts = _options.Timeouts
			},
			PluginProtocolJson.Options,
			statusCode: StatusCodes.Status201Created);
	}

	private IResult HandleDeleteSession(string sessionId)
	{
		DeletedSessions.Enqueue(sessionId);

		if (_sessionsById.TryGetValue(sessionId, out var record))
		{
			record.Ended = true;
		}

		return Results.NoContent();
	}

	private async Task<IResult> HandleWebSocketAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest)
		{
			return Results.BadRequest();
		}

		var authorization = context.Request.Headers.Authorization.FirstOrDefault();
		var token = authorization is { } value &&
			value.StartsWith($"{PluginAuthDefaults.BearerScheme} ", StringComparison.Ordinal)
				? value[(PluginAuthDefaults.BearerScheme.Length + 1)..]
				: null;

		if (token is null || !_sessionsByToken.TryGetValue(token, out var record))
		{
			return Results.Unauthorized();
		}

		using var socket = await context.WebSockets.AcceptWebSocketAsync(ProtocolConstants.WebSocketSubProtocol)
			.ConfigureAwait(false);

		var handshake = await PerformHandshakeAsync(socket, record).ConfigureAwait(false);

		if (handshake is null)
		{
			return Results.Empty;
		}

		var connection = new PluginConnection(socket,
			record,
			handshake.Value,
			_options,
			Messages,
			Logs,
			Events,
			_fromPlugin.Writer);

		record.DroppedAt = null;
		record.Ended = false;

		ReplacePriorConnectionIfAny(record.PluginId, connection);

		_current = connection;
		await _sessionViews.Writer.WriteAsync(new PluginSessionView(connection)).ConfigureAwait(false);

		var loop = RunConnectionAsync(connection, record);

		lock (_connectionLoopsGate)
		{
			_connectionLoops.Add(loop);
		}

		await loop.ConfigureAwait(false);
		return Results.Empty;
	}

	private async Task RunConnectionAsync(PluginConnection connection, SessionRecord record)
	{
		try
		{
			await connection.RunAsync(_stopping.Token).ConfigureAwait(false);
		}
		finally
		{
			var stillLive = ((ICollection<KeyValuePair<string, PluginConnection>>)_liveByPluginId)
				.Remove(new KeyValuePair<string, PluginConnection>(record.PluginId, connection));

			if (stillLive)
			{
				record.DroppedAt = DateTimeOffset.UtcNow;
				record.Ended = record.Ended || connection.PluginSaidGoodbye;
			}

			await connection.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>Replaces a prior live connection for the same plugin id, if this one is not resuming it.</summary>
	private void ReplacePriorConnectionIfAny(string pluginId, PluginConnection connection)
	{
		PluginConnection? previous = null;

		_liveByPluginId.AddOrUpdate(pluginId,
			_ => connection,
			(_, existing) =>
			{
				previous = existing;
				return connection;
			});

		if (previous is null || string.Equals(previous.SessionId, connection.SessionId, StringComparison.Ordinal))
		{
			return;
		}

		if (_sessionsById.TryGetValue(previous.SessionId, out var previousRecord))
		{
			previousRecord.Ended = true;
		}

		DeletedSessions.Enqueue(previous.SessionId);

		_ = CloseReplacedConnectionAsync(previous);
	}

	private static async Task CloseReplacedConnectionAsync(PluginConnection previous)
	{
		await previous.CloseAsync(ProtocolCloseCodes.SessionReplaced, "Replaced by a newer connection for this plugin.")
			.ConfigureAwait(false);
	}

	/// <summary>Reads <c>session.hello</c> and answers with <c>session.welcome</c>. Returns whether the
	/// connection resumed, or null when the handshake failed (the socket has already been dealt with).</summary>
	private async Task<bool?> PerformHandshakeAsync(WebSocket socket, SessionRecord record)
	{
		using var deadline = new CancellationTokenSource(_options.Timeouts.Handshake);

		var message = await WebSocketIo.ReceiveOneMessageAsync(socket, _options.Limits.MaxMessageBytes, deadline.Token)
			.ConfigureAwait(false);

		if (message is null)
		{
			return null;
		}

		var read = ProtocolEnvelopeReader.Read(message);

		if (!read.Succeeded || read.Envelope is not { Type: MessageTypes.SessionHello } envelope)
		{
			return null;
		}

		Messages.Record(ProtocolMessageDirection.FromPlugin, envelope);
		await _fromPlugin.Writer.WriteAsync(envelope, CancellationToken.None).ConfigureAwait(false);

		var hello = envelope.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

		var withinWindow = record.DroppedAt is { } droppedAt &&
			DateTimeOffset.UtcNow - droppedAt <= _options.Timeouts.SessionResumeWindow;

		var resumed = !record.Ended &&
			SessionResumeRules.CanResume(hello?.ResumeSessionId, sessionExists: true, withinWindow);

		var welcome = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id,
			Payload = JsonSerializer.SerializeToElement(
				new SessionWelcomePayload { SessionId = record.SessionId, Resumed = resumed },
				PluginProtocolJson.Options)
		};

		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(welcome);
		await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
			.ConfigureAwait(false);
		Messages.Record(ProtocolMessageDirection.ToPlugin, welcome);

		return resumed;
	}

	private static async Task WaitUntilServingAsync(ExternalPlugin plugin)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

		while (DateTime.UtcNow < deadline)
		{
			if (plugin.HasExited)
			{
				// Redirected output is delivered asynchronously and can lag slightly behind the process
				// object reporting HasExited; a short settle delay is what makes StandardError reliably
				// contain the process's last lines rather than racing them.
				await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

				throw new PluginProcessExitedException(
					$"The plugin process exited with code {plugin.ExitCode} before it started serving. " +
					$"Standard error: {plugin.StandardError}",
					plugin);
			}

			var report = await plugin.ProbeHealthAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

			if (report.Live)
			{
				return;
			}

			await Task.Delay(50).ConfigureAwait(false);
		}

		throw new PluginTestTimeoutException("The plugin process never started serving /_macrodeck/health.");
	}

	private static int ReserveLoopbackPort()
	{
		using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/// <summary>
	/// Best-effort peek at the <c>id</c> a <c>manifest.json</c> under <paramref name="workingDirectory" />
	/// declares, if any. Not a validating read - <c>PluginHostBuilder.Build</c> inside the launched
	/// process is the real authority on whether the manifest is valid at all - only enough to choose the
	/// id <see cref="LaunchAsync" /> hands a managed launch. Null when there is no manifest there, it
	/// cannot be read, or it declares no string <c>id</c>; the caller falls back to
	/// <see cref="PluginTestCredentials.Managed" />'s own freshly generated id in that case, which cannot
	/// conflict with a manifest id that was never read.
	/// </summary>
	private static string? TryPeekManifestId(string workingDirectory)
	{
		try
		{
			var path = Path.Combine(workingDirectory, "manifest.json");

			if (!File.Exists(path))
			{
				return null;
			}

			using var document = JsonDocument.Parse(File.ReadAllText(path));

			return document.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			return null;
		}
	}

	private static string GenerateSecret()
		=> Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>One pairing request this host is tracking, from creation to redemption or expiry.</summary>
	private sealed class PairingRecord
	{
		public required string PluginId { get; init; }

		public required string DisplayName { get; init; }

		public required string CodeChallenge { get; init; }

		public required string Status { get; init; }

		public bool Redeemed { get; set; }
	}

	/// <summary>The <c>MacroDeck:Plugin:*</c> configuration key for <paramref name="property" />, matching
	/// <see cref="PluginHostOptions" />'s own binding convention - never one of the <c>MACRO_DECK_PLUGIN_*</c>
	/// environment variable names, which come from <see cref="PluginEnvironmentConfiguration.KeysByVariable" /> alone.</summary>
	private static string ConfigKey(string property) => $"{PluginHostOptions.SectionName}:{property}";
}
