using MacroDeckHost.Application.Connect;
using MacroDeckHost.Infrastructure.Connect;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

/// <summary>
/// The device code grant from the user's point of view (issue #673): what the UI is told to show, and
/// how each answer RFC 8628 §3.5 allows ends the attempt.
/// </summary>
public class ConnectSignInFlowTests
{
	private static readonly ConnectDeviceAuthorization _authorization = new("device-code-1",
		"3389-5291",
		new Uri("https://accounts.macro-deck.app/device"),
		new Uri("https://accounts.macro-deck.app/device?user_code=3389-5291"),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromSeconds(7));

	[Test]
	public async Task The_prompt_repeats_the_issuer_s_own_code_page_and_expiry()
	{
		await using var fixture = new Fixture();
		var startedAt = fixture.Time.Now;

		var start = await fixture.Flow.Begin();

		Assert.Multiple(() =>
		{
			// The complete URI is what gets opened, so the user confirms instead of typing.
			Assert.That(start.VerificationUriComplete, Is.EqualTo(_authorization.VerificationUriComplete));
			Assert.That(start.VerificationUri, Is.EqualTo(_authorization.VerificationUri));
			Assert.That(start.UserCode, Is.EqualTo("3389-5291"));
			Assert.That(start.ExpiresAtUtc, Is.EqualTo(startedAt + TimeSpan.FromMinutes(15)));
		});
	}

	[Test]
	public async Task A_second_start_while_one_is_pending_returns_the_same_code()
	{
		await using var fixture = new Fixture();

		var first = await fixture.Flow.Begin();
		var second = await fixture.Flow.Begin();

		Assert.Multiple(() =>
		{
			Assert.That(second.UserCode, Is.EqualTo(first.UserCode));
			Assert.That(fixture.Identity.DeviceAuthorizationCount,
				Is.EqualTo(1),
				"a second start must not strand the code the user is already looking at");
		});
	}

	[Test]
	public async Task The_poll_waits_the_interval_the_issuer_asked_for()
	{
		await using var fixture = new Fixture();
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Pending));
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Success));

		await fixture.Flow.Begin();
		await fixture.Flow.WaitForOutcome();

		Assert.That(fixture.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7) }));
	}

	[Test]
	public async Task Slow_down_lengthens_the_interval_without_ending_the_attempt()
	{
		await using var fixture = new Fixture();
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.SlowDown));
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Success));

		await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Completed));
			Assert.That(fixture.Delays,
				Is.EqualTo(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(12) }),
				"slow_down must push the next poll out, not be ignored");
		});
	}

	[Test]
	public async Task A_declined_authorization_ends_the_attempt_instead_of_polling_on()
	{
		await using var fixture = new Fixture();
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Denied));

		await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Denied));
			Assert.That(fixture.Identity.PollCount, Is.EqualTo(1), "a declined attempt must not be retried");
		});
	}

	[Test]
	public async Task An_expired_device_code_stops_the_poll()
	{
		await using var fixture = new Fixture();
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Expired));

		await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Expired));
			Assert.That(fixture.Identity.PollCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_attempt_nobody_confirms_expires_at_the_advertised_time()
	{
		await using var fixture = new Fixture();

		var start = await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Expired));
			Assert.That(fixture.Time.Now, Is.GreaterThanOrEqualTo(start.ExpiresAtUtc));
		});
	}

	[Test]
	public async Task A_confirmed_authorization_publishes_the_id_token_identity()
	{
		await using var fixture = new Fixture();
		fixture.Identity.DisplayName = "Ada Lovelace";
		fixture.Identity.Picture = "https://accounts.macro-deck.app/avatars/abc.png";
		fixture.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Success));

		await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Completed));
			Assert.That(outcome.Claims!.DisplayName, Is.EqualTo("Ada Lovelace"));
			Assert.That(outcome.Claims.PictureUrl, Is.EqualTo("https://accounts.macro-deck.app/avatars/abc.png"));
			Assert.That(outcome.Tokens!.RefreshToken, Is.Not.Empty);
		});
	}

	[Test]
	public async Task An_unreachable_issuer_ends_the_attempt_rather_than_polling_forever()
	{
		await using var fixture = new Fixture();
		fixture.Identity.PollFailure =
			new ConnectAuthTransientException("Macro Deck Connect could not be reached.");

		await fixture.Flow.Begin();
		var outcome = await fixture.Flow.WaitForOutcome();

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Unreachable));
			Assert.That(fixture.Identity.PollCount, Is.LessThan(20));
		});
	}

	[Test]
	public async Task Cancelling_stops_the_poll()
	{
		await using var fixture = new Fixture();

		await fixture.Flow.Begin();
		await fixture.Flow.Cancel();
		var polledWhenCancelled = fixture.Identity.PollCount;

		var outcome = await fixture.Flow.WaitForOutcome();
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Result, Is.EqualTo(ConnectSignInResult.Cancelled));
			Assert.That(fixture.Identity.PollCount, Is.EqualTo(polledWhenCancelled));
		});
	}

	private sealed class Fixture : IAsyncDisposable
	{
		public Fixture()
		{
			Time = new ManualTimeProvider();
			Identity = new FakeConnectIdentityClient(Time) { DeviceAuthorization = _authorization };
			Flow = new ConnectSignInFlow(Identity, Time, Log.Logger, Delay);
		}

		public ManualTimeProvider Time { get; }

		public FakeConnectIdentityClient Identity { get; }

		public ConnectSignInFlow Flow { get; }

		public List<TimeSpan> Delays { get; } = [];

		public ValueTask DisposeAsync() => Flow.DisposeAsync();

		/// <summary>Records what was waited for and advances the clock by it, so an attempt nobody confirms
		/// reaches its expiry without the test waiting a quarter of an hour.</summary>
		private Task Delay(TimeSpan span, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled(cancellationToken);
			}

			Delays.Add(span);
			Time.Advance(span);

			return Task.CompletedTask;
		}
	}
}
