using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Compatibility;
using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Reconnection;
using MacroDeck.Plugin.Hosting.Localization;
using MacroDeck.Plugin.Protocol.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Keeps the plugin connected: registers when it has to, opens sessions, resumes what can be resumed,
/// backs off when it cannot, and says goodbye on the way out.
///
/// <para>
/// The only hosted service in the transport, deliberately. Everything asynchronous hangs off one
/// cancellation token owned here, so shutdown is one cancel rather than a set of loops that have to
/// agree on an order.
/// </para>
/// </summary>
internal sealed class PluginConnectionHostedService(
	PluginRegistrationClient registrationClient,
	PluginPairingClient pairingClient,
	IPluginCredentialStore credentialStore,
	CapabilityCatalog catalog,
	CapabilityDispatcher dispatcher,
	PluginConnectionState state,
	PluginMetadata metadata,
	PluginRegistrationModeAccessor registrationMode,
	IOptions<PluginHostOptions> options,
	IHostApplicationLifetime lifetime,
	TimeProvider timeProvider,
	ILogger logger,
	IHostInvoker hostInvoker,
	HostStateCache hostStateCache,
	IPluginAssetUploader assetUploader,
	IPluginHostAssetReceiver hostAssets) : IHostedService, IAsyncDisposable
{
	private readonly ILogger _logger = logger.ForContext<PluginConnectionHostedService>();

	private readonly PluginHostOptions _options = options.Value;
	private readonly CancellationTokenSource _stopping = new();

	private PluginSessionConnection? _connection;
	private PluginSession? _session;
	private Task _loop = Task.CompletedTask;
	private int _authenticationFailures;
	private bool _everConnected;
	private bool _sessionEnded;

	// Pairing puts a prompt in front of a human, so a request must be created at most once per process
	// lifetime - never retried by the ordinary reconnect backoff below. _pairingAttempt survives across
	// ConnectAsync calls so a mid-poll network blip resumes the same request instead of minting a new
	// one, and _pairingFailure - set only once that attempt has resolved terminally (rejected, expired,
	// host too old) - is replayed on every later call without touching the network again. Without this,
	// RunAsync's backoff loop would call ResolveCredentialsAsync on every retry tick and flood the
	// desktop app with a fresh prompt each time; a later reader "simplifying" this back into a plain
	// retry is exactly the regression this comment exists to prevent. Developer Mode being off is the
	// one refusal that is deliberately retried, because it happens before any request exists and so has
	// no prompt to re-raise - see ResolveCredentialsAsync.
	private PluginPairingAttempt? _pairingAttempt;
	private PluginRegistrationException? _pairingFailure;

	/// <summary>
	/// Starts connecting and returns. It deliberately does not wait for the first connection: a plugin
	/// launched before the host, or during a host restart, should come up and keep trying rather than
	/// fail to start over a race it did not cause.
	/// </summary>
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_loop = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		state.Status = PluginConnectionStatus.Stopped;

		// Goodbye first, then cancel. Cancelling unwinds the connection and clears the field, so the
		// other order usually finds nothing to say goodbye on.
		var connection = _connection;
		if (connection is not null)
		{
			await connection.GoodbyeAsync("The plugin is shutting down.", cancellationToken);
		}

		await _stopping.CancelAsync();
		await EndSessionAsync(cancellationToken);

		try
		{
			await _loop.WaitAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
		{
			// Shutdown has a budget and the loop is already cancelled; waiting past it would only
			// delay the process exit.
		}
	}

	public async ValueTask DisposeAsync()
	{
		_stopping.Dispose();

		if (_connection is not null)
		{
			await _connection.DisposeAsync();
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var droppedAt = DateTimeOffset.MinValue;

		while (!cancellationToken.IsCancellationRequested)
		{
			ConnectionOutcome outcome;

			try
			{
				outcome = await ConnectAsync(droppedAt, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (PluginRegistrationException exception)
			{
				outcome = exception.IsFatal
					? ConnectionOutcome.Fail(exception.Message)
					: ConnectionOutcome.Retry(exception.Message);
			}
			catch (Exception exception)
			{
				// The reconnect loop must survive anything the host does, including answering the
				// handshake with something that is not the protocol at all. Dying here would leave a
				// plugin that looks alive and is permanently disconnected.
				outcome = ConnectionOutcome.Retry(exception.Message);
			}

			droppedAt = timeProvider.GetUtcNow();
			state.LastCloseCode = outcome.CloseCode;

			if (outcome.Fatal)
			{
				Fault(outcome.Reason);
				return;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			// Fail-fast is about startup: a plugin that has been connected once and then dropped is a
			// reconnect, whatever the option says.
			if (!_everConnected && _options.FailFastOnFirstConnect)
			{
				Fault(outcome.Reason);
				return;
			}

			// The number the policy is asked for is the number diagnostics reports - one counter, so the
			// schedule and GET /_macrodeck/diagnostics can never tell different stories. It is reset by
			// session.welcome, which is both what "connected" means and the only point that knows this
			// attempt truly reached a session: a host that accepts the socket and then fails the
			// handshake never welcomes, so its backoff keeps growing instead of being pinned at the
			// initial delay.
			var attempt = state.ReconnectAttempt + 1;
			state.Status = PluginConnectionStatus.Reconnecting;
			state.SetReconnectAttempt(attempt);

			var delay = ReconnectPolicy.DelayFor(attempt, Random.Shared.NextDouble());
			_logger.Reconnecting(outcome.Reason, delay);

			try
			{
				await Task.Delay(delay, timeProvider, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task<ConnectionOutcome> ConnectAsync(DateTimeOffset droppedAt, CancellationToken cancellationToken)
	{
		state.Status = PluginConnectionStatus.Connecting;

		var descriptor = await registrationClient.GetProtocolAsync(cancellationToken);

		// Checked against what the host advertises rather than with ProtocolVersionNegotiator, which
		// answers the host's side of the question and assumes this process defines the ceiling.
		if (!descriptor.SupportedVersions.Any(version
			=> version >= ProtocolVersions.Minimum && version <= ProtocolVersions.Current))
		{
			return ConnectionOutcome.Fail(
				$"The host supports protocol versions {string.Join(", ", descriptor.SupportedVersions)}, " +
				$"this plugin supports {ProtocolVersions.Minimum}-{ProtocolVersions.Current}.");
		}

		var credentials = await ResolveCredentialsAsync(descriptor.Pairing, cancellationToken);

		// A resume needs the previous session's token, which is why the existing session is kept
		// rather than replaced eagerly. Outside the window there is nothing to resume, so a fresh
		// session is opened - which also issues a fresh token, so the fifteen-minute lifetime never
		// needs its own refresh path.
		var resuming = _session is not null &&
			!_sessionEnded &&
			_session.CanResumeAt(timeProvider.GetUtcNow(), droppedAt);

		if (!resuming)
		{
			_session = await OpenSessionAsync(credentials, cancellationToken);
			dispatcher.ResetIdempotency();
		}

		var session = _session!;

		// The connection owns the socket from here: two owners would mean a disposed socket being
		// closed a second time during shutdown.
		var socket = await ClientWebSocketPluginSocket.ConnectAsync(new Uri(credentials.HostUrl),
			session.SessionToken,
			session.Limits.MaxMessageBytes,
			cancellationToken);

		await using var connection = new PluginSessionConnection(socket,
			session,
			dispatcher,
			state,
			timeProvider,
			logger.ForContext<PluginSessionConnection>(),
			hostInvoker,
			hostStateCache,
			assetUploader,
			hostAssets);

		_connection = connection;
		state.ActiveConnection = connection;

		try
		{
			var outcome = await connection.RunAsync(resuming ? session.SessionId : null,
				InstanceId,
				cancellationToken);

			_everConnected |= state.SessionId is not null && state.Status == PluginConnectionStatus.Connected;

			// A voluntary goodbye from either side, or a refused resume, makes the session unusable at
			// once - so the next attempt opens a new one rather than presenting a dead id.
			_sessionEnded = connection.HostSaidGoodbye;

			if (outcome.CloseCode == ProtocolCloseCodes.SessionExpired)
			{
				// Expired is not fatal, it just means the credential is stale. Dropping the session
				// makes the next attempt open a new one instead of trying to resume a dead id.
				_session = null;
				return ConnectionOutcome.Retry("The session expired.", outcome.CloseCode);
			}

			if (outcome.CloseCode == ProtocolCloseCodes.AuthenticationFailed &&
				++_authenticationFailures <= _options.MaxAuthenticationFailures)
			{
				_session = null;
				return ConnectionOutcome.Retry("The host rejected the session token.", outcome.CloseCode);
			}

			if (!outcome.Fatal)
			{
				_authenticationFailures = 0;
			}

			return outcome;
		}
		finally
		{
			// A half-received transfer cannot be resumed on the next connection, and whoever was waiting
			// for it must be told rather than left waiting out its own timeout.
			hostAssets.Reset();

			_connection = null;
			state.ActiveConnection = null;
			state.Status = PluginConnectionStatus.Reconnecting;
		}
	}

	private async Task<PluginSession> OpenSessionAsync(
		PluginCredentials credentials,
		CancellationToken cancellationToken)
	{
		var declared = catalog.Declare();
		state.DeclaredCapabilities = declared.Count;

		var response = await registrationClient.CreateSessionAsync(credentials,
			BuildSessionRequest(declared, metadata, SdkUsageManifestReader.Read(Assembly.GetEntryAssembly())),
			cancellationToken);

		foreach (var rejected in response.Capabilities.Where(capability => !capability.Accepted))
		{
			// Never fatal by contract: a plugin that declares more than this host understands runs
			// with less, rather than refusing to run at all.
			_logger.CapabilityRejected(rejected.Kind, rejected.RejectionReason);
		}

		state.SessionId = response.SessionId;
		state.NegotiatedVersion = response.NegotiatedVersion;

		// Before any descriptor is built: whether a localized reference may cross the wire, or has to be
		// flattened into the plugin's own language first, is settled here and nowhere else.
		PluginText.Negotiated(response.NegotiatedVersion);
		state.AcceptedCapabilities = response.Capabilities.Count(capability => capability.Accepted);

		return new PluginSession(response.SessionId,
			response.SessionToken,
			response.NegotiatedVersion,
			response.Limits,
			response.Timeouts,
			timeProvider.GetUtcNow());
	}

	/// <summary>Extracted for direct unit testing - see <see cref="MacroDeck.Plugin.Hosting.Tests.UnitTests" />
	/// PluginConnectionHostedServiceTests, since running the whole hosted service just to inspect one
	/// outgoing request is out of proportion to what changes here.</summary>
	internal static PluginSessionRequest BuildSessionRequest(IReadOnlyList<DeclaredCapability> declared,
		PluginMetadata metadata,
		PluginSdkUsage? sdkUsage) => new()
	{
		RequestedVersion = new ProtocolVersionRange
		{
			Minimum = ProtocolVersions.Minimum,
			Maximum = ProtocolVersions.Current
		},
		Capabilities = declared,
		DeclaredName = metadata.Name,
		DeclaredVersion = metadata.Version,
		Sdk = sdkUsage
	};

	private async Task<PluginCredentials> ResolveCredentialsAsync(
		PluginPairingDescriptor? pairingDescriptor,
		CancellationToken cancellationToken)
	{
		var stored = await credentialStore.LoadAsync(cancellationToken);

		if (stored is not null)
		{
			return stored;
		}

		if (registrationMode.Mode == PluginRegistrationMode.Managed)
		{
			throw new PluginRegistrationException(
				"Managed mode expects the host to supply credentials, but MACRO_DECK_PLUGIN_ID and " +
				"MACRO_DECK_PLUGIN_SECRET are not set.",
				HttpStatusCode.Unauthorized);
		}

		// Precedence, load-bearing: stored credential (above) > an explicitly supplied EnrollmentToken >
		// interactive pairing. FakePluginHost, MacroDeckTestHost, the conformance harness and
		// `macrodeck run --stub-host` all supply a token, so this is what stops every automated harness
		// from hanging forever waiting for a human to click Approve - an implementation that tried
		// pairing first would never reach the token at all.
		if (!string.IsNullOrEmpty(_options.EnrollmentToken))
		{
			var registration = await registrationClient.RegisterAsync(metadata.Id,
				metadata.Name,
				_options.EnrollmentToken,
				cancellationToken);

			var credentials
				= new PluginCredentials(registration.PluginId, _options.HostUrl, registration.PluginSecret);

			// Persisted before it is used: the secret is shown exactly once, so a crash between using it
			// and storing it would cost the user another enrollment token.
			await credentialStore.SaveAsync(credentials, cancellationToken);
			_logger.Registered(registration.PluginId);

			return credentials;
		}

		// A pairing attempt that already resolved terminally never touches the network again - see the
		// comment on _pairingAttempt/_pairingFailure above.
		if (_pairingFailure is not null)
		{
			throw _pairingFailure;
		}

		if (!_options.PairingEnabled)
		{
			_logger.HeadlessEnrollmentHint();

			throw new PluginRegistrationException(
				"This plugin has no stored credentials, and interactive pairing is disabled.",
				HttpStatusCode.Unauthorized);
		}

		if (pairingDescriptor is not { Supported: true })
		{
			_logger.HeadlessEnrollmentHint();

			throw Terminal(new PluginRegistrationException(
				"This host does not support interactive pairing. Use the headless enrollment fallback " +
				"described in the plugin authentication guide instead.",
				HttpStatusCode.NotFound,
				fatal: true));
		}

		// Developer Mode is the one pairing refusal that is not terminal, and the only one this can be
		// asked about before a request exists. Retrying it floods nothing - no prompt has been shown -
		// and it is what lets a developer enable Developer Mode without restarting the plugin. It is
		// deliberately answered from the descriptor rather than from the create call's 403: the host
		// counts every refused create against a throttle shared by all plugins, so a retry loop that
		// kept posting would lock out the pairing the developer is about to need. A host that does not
		// report the flag therefore keeps the old fatal behaviour. Once a request exists, the poll below
		// owns the outcome - Developer Mode going off mid-prompt is terminal for that request.
		if (_pairingAttempt is null && pairingDescriptor.DeveloperModeEnabled == false)
		{
			_logger.PairingDeveloperModeDisabled();
			PairingProgress.Write(
				"Developer Mode is disabled in Macro Deck, so pairing is not possible yet. Enable it " +
				"under Settings > Developer - this plugin keeps waiting and pairs as soon as you do.");

			// Explicitly not fatal: a 403 is fatal by default, and here it is precisely not - the whole
			// point is that the user can still turn Developer Mode on.
			throw new PluginRegistrationException("Interactive pairing needs Developer Mode enabled on the host.",
				HttpStatusCode.Forbidden,
				fatal: false);
		}

		try
		{
			return await PairAsync(pairingDescriptor, cancellationToken);
		}
		catch (PluginRegistrationException exception) when (exception.IsFatal)
		{
			throw Terminal(exception);
		}
	}

	private async Task<PluginCredentials> PairAsync(
		PluginPairingDescriptor pairingDescriptor,
		CancellationToken cancellationToken)
	{
		var attempt = _pairingAttempt;

		if (attempt is null)
		{
			var verifierBytes = RandomNumberGenerator.GetBytes(32);
			var codeVerifier = Base64Url(verifierBytes);
			var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

			var clientInfo = new PluginPairingClientInfo
			{
				ExecutablePath = Environment.ProcessPath,
				ProcessId = Environment.ProcessId,
				SdkVersion = typeof(PluginConnectionHostedService).Assembly.GetName().Version?.ToString()
			};

			var response = await pairingClient.CreateAsync(metadata.Id,
				metadata.Name,
				codeChallenge,
				clientInfo,
				cancellationToken);

			var deadline = _options.PairingTimeout is { } timeout
				? timeProvider.GetUtcNow() + timeout
				: response.ExpiresAt;

			attempt = new PluginPairingAttempt(response.RequestId,
				codeVerifier,
				Math.Max(1, response.PollIntervalSeconds),
				deadline);
			_pairingAttempt = attempt;

			_logger.PairingRequested(metadata.Id);
			PairingProgress.Write("Waiting for pairing approval in Macro Deck...");
		}

		while (true)
		{
			if (timeProvider.GetUtcNow() >= attempt.Deadline)
			{
				_logger.PairingExpired(metadata.Id);
				throw new PluginRegistrationException(
					$"The pairing request for '{metadata.Id}' expired before it was approved.",
					HttpStatusCode.Unauthorized);
			}

			var status = await pairingClient.GetStatusAsync(attempt.RequestId, cancellationToken);

			if (string.Equals(status.Status, PluginPairingStatuses.Approved, StringComparison.Ordinal))
			{
				var registration = await pairingClient.RedeemAsync(attempt.RequestId,
					attempt.CodeVerifier,
					cancellationToken);

				var credentials
					= new PluginCredentials(registration.PluginId, _options.HostUrl, registration.PluginSecret);

				// Persisted before it is used, same as the enrollment-token path above and for the same
				// reason: the secret is shown exactly once.
				await credentialStore.SaveAsync(credentials, cancellationToken);
				_pairingAttempt = null;
				_logger.Paired(registration.PluginId);
				PairingProgress.Write($"Pairing approved in Macro Deck. Registered as '{registration.PluginId}'.");

				return credentials;
			}

			if (string.Equals(status.Status, PluginPairingStatuses.Rejected, StringComparison.Ordinal))
			{
				_logger.PairingRejected(metadata.Id);
				PairingProgress.Write("The pairing request was rejected in Macro Deck.");
				throw new PluginRegistrationException(
					$"The pairing request for '{metadata.Id}' was rejected in the Macro Deck desktop app.",
					HttpStatusCode.Unauthorized);
			}

			if (string.Equals(status.Status, PluginPairingStatuses.Expired, StringComparison.Ordinal))
			{
				_logger.PairingExpired(metadata.Id);
				throw new PluginRegistrationException(
					$"The pairing request for '{metadata.Id}' expired before it was approved.",
					HttpStatusCode.Unauthorized);
			}

			await Task.Delay(TimeSpan.FromSeconds(attempt.PollIntervalSeconds), timeProvider, cancellationToken);
		}
	}

	private PluginRegistrationException Terminal(PluginRegistrationException exception)
	{
		_pairingFailure = exception;
		return exception;
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>State of an in-flight pairing request, kept across reconnect attempts so a transient
	/// failure mid-poll resumes the same request instead of creating another one.</summary>
	private sealed record PluginPairingAttempt(
		string RequestId,
		string CodeVerifier,
		int PollIntervalSeconds,
		DateTimeOffset Deadline);

	private async Task EndSessionAsync(CancellationToken cancellationToken)
	{
		var session = _session;
		if (session is null)
		{
			return;
		}

		try
		{
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			budget.CancelAfter(TimeSpan.FromSeconds(2));

			await registrationClient.DeleteSessionAsync(session.SessionId, session.SessionToken, budget.Token);
		}
		catch (Exception exception) when (exception is PluginRegistrationException
			or HttpRequestException
			or OperationCanceledException)
		{
			// Releasing the slot is a courtesy; the host expires it anyway, and a plugin must not
			// hang on shutdown because the host is already gone.
			_logger.SessionTeardownFailed(exception);
		}
		finally
		{
			_session = null;
		}
	}

	private string InstanceId => _options.InstanceId ??
		Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

	private void Fault(string reason)
	{
		state.Status = PluginConnectionStatus.Faulted;
		state.FaultReason = reason;
		_logger.ConnectionFaulted(reason);

		// Managed plugins default to stopping, because the supervisor owns restarts and a process
		// that is up but permanently disconnected looks healthy while doing nothing. A self-hosted
		// plugin defaults to staying up: it may be doing other work, and a loud log is the better
		// outcome there.
		var stop = _options.StopApplicationOnFatalProtocolError ??
			registrationMode.Mode == PluginRegistrationMode.Managed;

		if (stop)
		{
			lifetime.StopApplication();
		}
	}
}
