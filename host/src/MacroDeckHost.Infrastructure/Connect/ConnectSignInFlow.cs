using MacroDeckHost.Application.Connect;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

public enum ConnectSignInResult
{
	Completed,
	Cancelled,
	Expired,
	Denied,
	Unreachable,
	Failed
}

public sealed record ConnectSignInOutcome(
	ConnectSignInResult Result,
	ConnectTokenResponse? Tokens,
	ConnectIdTokenClaims? Claims,
	string? Message)
{
	public static readonly ConnectSignInOutcome Cancelled = new(ConnectSignInResult.Cancelled, null, null, null);
}

/// <summary>
/// Signs in with the device code grant (RFC 8628). It carries no redirect URI, which is what lets the
/// user approve on whichever machine is showing the UI - the loopback callback it replaced could only
/// ever resolve to the host itself.
/// </summary>
public sealed class ConnectSignInFlow : IConnectSignInFlow, IAsyncDisposable
{
	internal static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

	// The poll survives a blip, but not an outage: past this the attempt ends instead of holding the UI
	// in "signing in" for the full quarter hour the code is valid.
	private const int TransientFailureBudget = 5;

	private readonly IConnectIdentityClient _identityClient;
	private readonly TimeProvider _timeProvider;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private Attempt? _attempt;

	public ConnectSignInFlow(
		IConnectIdentityClient identityClient,
		TimeProvider timeProvider,
		ILogger logger,
		Func<TimeSpan, CancellationToken, Task>? delay = null)
	{
		_identityClient = identityClient;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ConnectSignInFlow>();
		_delay = delay ?? ((span, ct) => Task.Delay(span, ct));
	}

	public async Task<ConnectSignInStart> Begin(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_attempt is { Completion.Task.IsCompleted: false } pending)
			{
				return pending.Start;
			}

			await ReleaseCurrent();

			var authorization = await _identityClient.RequestDeviceAuthorization(cancellationToken);
			var attempt = new Attempt(authorization,
				new ConnectSignInStart(authorization.VerificationUri,
					authorization.VerificationUriComplete,
					authorization.UserCode,
					_timeProvider.GetUtcNow() + authorization.ExpiresIn));

			attempt.Poll = Task.Run(() => Poll(attempt), CancellationToken.None);

			_attempt = attempt;
			return attempt.Start;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Resolves when the current attempt finishes, one way or another.</summary>
	public async Task<ConnectSignInOutcome> WaitForOutcome(CancellationToken cancellationToken = default)
	{
		var attempt = Volatile.Read(ref _attempt);

		return attempt is null
			? ConnectSignInOutcome.Cancelled
			: await attempt.Completion.Task.WaitAsync(cancellationToken);
	}

	public async Task Cancel(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_attempt?.Completion.TrySetResult(ConnectSignInOutcome.Cancelled);
			await ReleaseCurrent();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await Cancel(CancellationToken.None);
		_gate.Dispose();
	}

	private async Task Poll(Attempt attempt)
	{
		var cancellationToken = attempt.Cancellation.Token;
		var interval = attempt.Authorization.Interval > TimeSpan.Zero
			? attempt.Authorization.Interval
			: SlowDownStep;
		var transientFailures = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await _delay(interval, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (_timeProvider.GetUtcNow() >= attempt.Start.ExpiresAtUtc)
			{
				Finish(attempt,
					ConnectSignInResult.Expired,
					"The device authorization expired before it was confirmed.");
				return;
			}

			ConnectDevicePollResult poll;
			try
			{
				poll = await _identityClient.PollDeviceToken(attempt.Authorization.DeviceCode, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				if (++transientFailures > TransientFailureBudget)
				{
					_logger.Warning(ex, "Giving up on a Macro Deck Connect device authorization poll");
					Finish(attempt,
						ConnectSignInResult.Unreachable,
						"Macro Deck Connect could not be reached while polling the device authorization.");
					return;
				}

				_logger.Debug(ex, "A Macro Deck Connect device authorization poll failed");
				continue;
			}

			transientFailures = 0;

			switch (poll.Status)
			{
				case ConnectDevicePollStatus.Success when poll.Tokens is not null:
					Complete(attempt, poll.Tokens);
					return;

				case ConnectDevicePollStatus.Denied:
					Finish(attempt, ConnectSignInResult.Denied, "The user declined the device authorization.");
					return;

				case ConnectDevicePollStatus.Expired:
					Finish(attempt,
						ConnectSignInResult.Expired,
						"The device authorization expired before it was confirmed.");
					return;

				case ConnectDevicePollStatus.SlowDown:
					interval += SlowDownStep;
					break;

				case ConnectDevicePollStatus.Success:
				case ConnectDevicePollStatus.Pending:
				default:
					break;
			}
		}
	}

	private void Complete(Attempt attempt, ConnectTokenResponse tokens)
	{
		try
		{
			// No nonce: the device grant has no authorization request to bind one to, so the id token is
			// validated exactly like the one a refresh returns.
			var claims = ConnectIdTokenReader.Read(tokens.IdToken, null, _timeProvider);
			attempt.Completion.TrySetResult(new ConnectSignInOutcome(ConnectSignInResult.Completed,
				tokens,
				claims,
				null));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "A freshly issued Macro Deck Connect id token could not be read");
			Finish(attempt,
				ConnectSignInResult.Failed,
				"The id token issued for the device authorization could not be read.");
		}
	}

	private static void Finish(Attempt attempt, ConnectSignInResult result, string message)
		=> attempt.Completion.TrySetResult(new ConnectSignInOutcome(result, null, null, message));

	private async Task ReleaseCurrent()
	{
		var attempt = _attempt;
		_attempt = null;

		if (attempt is null)
		{
			return;
		}

		attempt.Completion.TrySetResult(ConnectSignInOutcome.Cancelled);
		await attempt.Cancellation.CancelAsync();

		// Awaited before the source is disposed, and safe to await under the gate because the poll loop
		// never takes it.
		if (attempt.Poll is { } poll)
		{
			try
			{
				await poll;
			}
			catch (OperationCanceledException)
			{
			}
		}

		attempt.Cancellation.Dispose();
	}

	private sealed class Attempt
	{
		public Attempt(ConnectDeviceAuthorization authorization, ConnectSignInStart start)
		{
			Authorization = authorization;
			Start = start;
		}

		public ConnectDeviceAuthorization Authorization { get; }

		public ConnectSignInStart Start { get; }

		public CancellationTokenSource Cancellation { get; } = new();

		public Task? Poll { get; set; }

		public TaskCompletionSource<ConnectSignInOutcome> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
