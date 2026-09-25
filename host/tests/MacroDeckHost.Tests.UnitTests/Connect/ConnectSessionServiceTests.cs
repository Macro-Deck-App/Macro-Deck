using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

[TestFixture]
public class ConnectSessionServiceTests
{
	[Test]
	public async Task A_rotated_refresh_token_is_durable_before_the_access_token_is_handed_out()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Store.Gate = gate;

		var pending = harness.Service.GetAccessToken();
		await Task.Delay(200);

		Assert.That(pending.IsCompleted, Is.False, "the access token was handed out before the rotation landed");

		gate.SetResult();
		await pending;

		Assert.That(harness.Store.Saved[^1].RefreshToken, Is.EqualTo(harness.Identity.CurrentRefreshToken));

		var restarted = harness.CreateService();
		await restarted.Initialize();
		harness.Time.Advance(TimeSpan.FromMinutes(11));
		await restarted.GetAccessToken();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Identity.PresentedRefreshTokens,
				Is.Unique,
				"a redeemed refresh token was replayed after the restart");
			Assert.That(harness.Identity.ChainIsAlive, Is.True);
		});
	}

	[Test]
	public async Task A_failed_credential_write_never_leads_to_replaying_a_redeemed_token()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Store.FailNextSave = true;
		await harness.Service.GetAccessToken();

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		await harness.Service.GetAccessToken();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Identity.PresentedRefreshTokens, Is.Unique);
			Assert.That(harness.Identity.ChainIsAlive, Is.True);
		});
	}

	[Test]
	public async Task Three_weeks_of_inactivity_costs_one_silent_refresh_and_no_interactive_login()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromDays(21));
		var token = await harness.Service.GetAccessToken();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(1));
			Assert.That(token, Is.Not.Null.And.Not.Empty);
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
			Assert.That(harness.Service.Current.Connectivity, Is.EqualTo(ConnectConnectivity.Ok));
			Assert.That(harness.Store.ClearCount, Is.Zero);
			Assert.That(harness.Identity.DeviceAuthorizationCount, Is.Zero);
		});
	}

	[Test]
	public async Task The_refresh_credential_slides_past_its_own_180_day_lifetime()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromDays(179));
		var first = await harness.Service.GetAccessToken();

		harness.Time.Advance(TimeSpan.FromDays(179));
		var second = await harness.Service.GetAccessToken();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.Empty);
			Assert.That(second, Is.Not.Empty);
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(2));
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
		});
	}

	[Test]
	public async Task A_valid_access_token_is_reused_instead_of_refreshed_on_every_call()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		var first = await harness.Service.GetAccessToken();
		var afterFirst = harness.Identity.RefreshCount;

		harness.Time.Advance(TimeSpan.FromMinutes(5));
		var second = await harness.Service.GetAccessToken();
		var afterSecond = harness.Identity.RefreshCount;

		harness.Time.Advance(TimeSpan.FromMinutes(5));
		await harness.Service.GetAccessToken();

		Assert.Multiple(() =>
		{
			Assert.That(afterFirst, Is.EqualTo(1));
			Assert.That(afterSecond, Is.EqualTo(1));
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(2));
			Assert.That(second, Is.EqualTo(first));
		});
	}

	[Test]
	public async Task Concurrent_callers_share_a_single_refresh()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Identity.Stall = true;
		var callers = Enumerable.Range(0, 5).Select(_ => harness.Service.GetAccessToken()).ToArray();

		await ConnectTestHarness.WaitUntil(() => harness.Identity.RefreshCount == 1, "no refresh started");
		await Task.Delay(100);
		harness.Identity.StallGate.SetResult();

		var tokens = await Task.WhenAll(callers);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(1));
			Assert.That(tokens.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(1));
			Assert.That(harness.Identity.PresentedRefreshTokens, Is.Unique);
		});
	}

	[Test]
	public async Task A_network_failure_marks_the_session_offline_and_keeps_it_signed_in()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Unreachable());

		Assert.ThrowsAsync<ConnectAuthTransientException>(() => harness.Service.GetAccessToken());

		var snapshot = harness.Service.Current;

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
			Assert.That(snapshot.Connectivity, Is.EqualTo(ConnectConnectivity.Offline));
			Assert.That(snapshot.OfflineSince, Is.Not.Null);
			Assert.That(harness.Store.ClearCount, Is.Zero);
		});

		Assert.That(await harness.Store.Load(), Is.Not.Null);
	}

	[TestCase(500)]
	[TestCase(502)]
	[TestCase(503)]
	[TestCase(504)]
	[TestCase(0)]
	public async Task A_five_hundred_from_the_token_endpoint_is_offline_not_invalid(int status)
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		harness.Identity.Results.Enqueue(status == 0
			? FakeConnectIdentityClient.MalformedBody()
			: FakeConnectIdentityClient.ServerError(status));

		Assert.ThrowsAsync<ConnectAuthTransientException>(() => harness.Service.GetAccessToken());

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
			Assert.That(harness.Service.Current.Connectivity, Is.EqualTo(ConnectConnectivity.Offline));
			Assert.That(harness.Store.ClearCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_three_day_outage_backs_off_and_recovers_without_ever_signing_out()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		var outageEnds = harness.Time.Now + TimeSpan.FromHours(72);
		var breaches = new List<string>();

		harness.Identity.BeforeRefresh = () =>
		{
			if (harness.Time.Now < outageEnds)
			{
				harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Unreachable());
			}

			return Task.CompletedTask;
		};

		harness.DelayHandler = (span, _) =>
		{
			var snapshot = harness.Service.Current;
			if (snapshot.Status is not ConnectAccountStatus.SignedIn ||
				snapshot.Connectivity is not ConnectConnectivity.Offline ||
				harness.Store.ClearCount != 0)
			{
				breaches.Add($"{snapshot.Status}/{snapshot.Connectivity}/clears={harness.Store.ClearCount}");
			}

			harness.Delays.Add(span);
			harness.Time.Advance(span);
			return Task.CompletedTask;
		};

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		Assert.ThrowsAsync<ConnectAuthTransientException>(() => harness.Service.GetAccessToken());

		await ConnectTestHarness.WaitUntil(() => harness.Service.Current.Connectivity is ConnectConnectivity.Ok,
			"the session never came back online");

		var gaps = harness.Delays;

		Assert.Multiple(() =>
		{
			Assert.That(breaches, Is.Empty);
			Assert.That(harness.Store.ClearCount, Is.Zero);
			Assert.That(harness.Identity.RefreshCount,
				Is.LessThanOrEqualTo(200),
				"the backoff did not bound the attempt rate over the outage");
			Assert.That(gaps, Is.Ordered, "the backoff gaps must never shrink inside an episode");
			Assert.That(gaps[^1], Is.EqualTo(TimeSpan.FromMinutes(30)), "the backoff never reached its cap");
			Assert.That(gaps[^2], Is.EqualTo(gaps[^1]), "the cap is not constant");
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
			Assert.That(harness.Service.Current.OfflineSince, Is.Null);
			Assert.That(harness.Identity.DeviceAuthorizationCount, Is.Zero);
		});
	}

	[TestCase("invalid_grant: the refresh token was revoked or reused.")]
	[TestCase("invalid_grant: the security stamp was rotated.")]
	[TestCase("invalid_grant: the account was deleted or locked out.")]
	public async Task An_invalid_grant_requires_interactive_sign_in_and_stops_refreshing(string framing)
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		harness.Identity.Results.Enqueue(() => throw new ConnectAuthRejectedException(framing));

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());

		var afterRejection = harness.Identity.RefreshCount;

		for (var day = 0; day < 30; day++)
		{
			harness.Time.Advance(TimeSpan.FromDays(1));
			Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());
		}

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status,
				Is.EqualTo(ConnectAccountStatus.ReauthenticationRequired));
			Assert.That(harness.Service.Current.Message, Is.Not.Null.And.Not.Empty);
			Assert.That(afterRejection, Is.EqualTo(1));
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(1));
		});

		Assert.That(await harness.Store.Load(), Is.Null);
	}

	[Test]
	public async Task A_suspended_account_stops_dead_and_keeps_the_credential()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Time.Advance(TimeSpan.FromMinutes(11));
		harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Suspended());

		Assert.ThrowsAsync<ConnectAccountSuspendedException>(() => harness.Service.GetAccessToken());

		for (var day = 0; day < 7; day++)
		{
			harness.Time.Advance(TimeSpan.FromDays(1));
		}

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.Suspended));
			Assert.That(harness.Service.Current.Message, Is.EqualTo("This account has been suspended."));
			Assert.That(harness.Identity.RefreshCount, Is.EqualTo(1));
			Assert.That(harness.Store.ClearCount, Is.Zero);
		});

		Assert.That(await harness.Store.Load(), Is.Not.Null);
	}

	[Test]
	public async Task The_host_starts_signed_in_without_waiting_for_identity()
	{
		await using var harness = new ConnectTestHarness();
		harness.Identity.Stall = true;
		harness.Store.Seed(harness.Identity.SeedCredential(harness.Time.Now - TimeSpan.FromDays(30)));

		await harness.Service.Initialize().WaitAsync(TimeSpan.FromSeconds(2));

		Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
	}

	[Test]
	public async Task Signing_out_revokes_at_the_server_and_clears_the_local_credential()
	{
		await using var harness = new ConnectTestHarness();
		var credential = await SignedIn(harness);

		await harness.Service.SignOut();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Identity.RevokeCount, Is.EqualTo(1));
			Assert.That(harness.Identity.RevokedTokens.Single(), Is.EqualTo(credential.RefreshToken));
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Account, Is.Null);
			// There is no end-session endpoint for this client: revoking is the entire server-side sign-out.
			Assert.That(harness.Identity.RefreshCount, Is.Zero);
			Assert.That(harness.Identity.DeviceAuthorizationCount, Is.Zero);
		});

		Assert.That(await harness.Store.Load(), Is.Null);
	}

	[Test]
	public async Task Signing_out_while_offline_still_ends_the_local_session()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Unreachable());

		Assert.DoesNotThrowAsync(() => harness.Service.SignOut());

		Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
		Assert.That(await harness.Store.Load(), Is.Null);
	}

	[Test]
	public async Task Only_real_transitions_notify_and_the_notification_carries_nothing()
	{
		await using var harness = new ConnectTestHarness();
		var notifications = 0;
		harness.Service.SessionChanged += (_, _) => Interlocked.Increment(ref notifications);

		await harness.Service.Initialize();
		Assert.That(notifications, Is.Zero, "publishing the state the session was already in is not a transition");

		await SignedIn(harness);
		await harness.Service.GetAccessToken();
		var afterSignIn = Volatile.Read(ref notifications);

		await harness.Service.GetAccessToken();
		await harness.Service.GetAccessToken();
		await harness.Service.GetAccessToken();

		Assert.That(Volatile.Read(ref notifications),
			Is.EqualTo(afterSignIn),
			"a cached-token access must not notify anybody");

		await harness.Service.SignOut();

		Assert.Multiple(() =>
		{
			Assert.That(Volatile.Read(ref notifications), Is.EqualTo(afterSignIn + 1));
			Assert.That(JsonSerializer.Serialize(new ConnectSessionChangedNotification()), Is.EqualTo("{}"));
		});
	}

	[Test]
	public async Task A_sign_in_that_cannot_even_be_started_says_so_instead_of_failing_silently()
	{
		await using var harness = new ConnectTestHarness();
		harness.Identity.DeviceAuthorizationFailure =
			new ConnectAuthTransientException("Macro Deck Connect could not be reached.");

		Assert.ThrowsAsync<ConnectAuthTransientException>(() => harness.Service.StartSignIn());

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.SignInFailure, Is.EqualTo(ConnectSignInFailure.Unreachable));
			Assert.That(harness.Service.Current.Status,
				Is.EqualTo(ConnectAccountStatus.SignedOut),
				"a failed start must not leave the session claiming an attempt is running");
		});
	}

	[Test]
	public async Task Shutting_the_session_down_twice_is_not_a_crash()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);
		await harness.Service.GetAccessToken();

		// The container tracks the service once per registration, so a clean shutdown completes it and then
		// disposes it more than once. None of that may surface as a fatal error.
		await harness.Service.CompleteAsync();
		await harness.Service.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.DoesNotThrowAsync(async () => await harness.Service.DisposeAsync());
			Assert.DoesNotThrowAsync(async () => await harness.Service.CompleteAsync());
		});
	}

	[Test]
	public async Task A_completed_sign_in_is_stored_and_survives_a_restart()
	{
		await using var harness = new ConnectTestHarness();

		await SignIn(harness);

		var restarted = harness.CreateService();
		await restarted.Initialize();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Store.Saved[^1].RefreshToken, Is.EqualTo(harness.Identity.CurrentRefreshToken));
			Assert.That(restarted.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedIn));
			Assert.That(restarted.Current.Account!.Subject, Is.EqualTo(harness.Service.Current.Account!.Subject));
		});
	}

	[Test]
	public async Task A_declined_sign_in_is_reported()
	{
		await using var harness = new ConnectTestHarness();
		harness.SignInDelayHandler = (_, _) => Task.CompletedTask;
		harness.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Denied));

		await harness.Service.StartSignIn();

		await ConnectTestHarness.WaitUntil(
			() => harness.Service.Current.SignInFailure == ConnectSignInFailure.Denied,
			"the declined sign-in was never reported");
	}

	[Test]
	public async Task Signing_out_while_a_rotation_is_answered_does_not_bring_the_session_back()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);
		string? rotated = null;

		harness.Identity.AfterRefresh = async () =>
		{
			harness.Identity.AfterRefresh = null;
			rotated = harness.Identity.CurrentRefreshToken;
			await harness.Service.SignOut();
		};

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());
		await harness.Persister.FlushAsync();
		var stored = await harness.Store.Load();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Account, Is.Null);
			Assert.That(stored, Is.Null);
			Assert.That(harness.Store.Saved, Is.Empty);
			Assert.That(harness.Identity.RevokedTokens, Does.Contain(rotated));
			Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());
		});
	}

	[Test]
	public async Task Signing_out_while_a_refresh_is_rejected_stays_signed_out()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Identity.BeforeRefresh = async () =>
		{
			harness.Identity.BeforeRefresh = null;
			await harness.Service.SignOut();
		};

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Message, Is.Null);
			Assert.That(harness.Store.ClearCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Signing_out_while_a_refresh_reports_a_suspension_stays_signed_out()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Identity.BeforeRefresh = async () =>
		{
			harness.Identity.BeforeRefresh = null;
			await harness.Service.SignOut();
			harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Suspended());
		};

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Message, Is.Null);
			Assert.That(harness.Floor.NotBefore, Is.Null);
		});
	}

	[Test]
	public async Task Signing_out_while_the_rotated_credential_is_being_stored_leaves_nothing_stored()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Store.Gate = gate;

		var pending = harness.Service.GetAccessToken();
		await harness.Store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var signingOut = harness.Service.SignOut();
		gate.SetResult();

		await signingOut;
		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => pending);
		var stored = await harness.Store.Load();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Account, Is.Null);
			Assert.That(stored, Is.Null);
		});
	}

	[Test]
	public async Task Signing_out_finishes_even_when_the_credential_store_never_completes_a_write()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Store.Gate = gate;

		var pending = harness.Service.GetAccessToken();
		await harness.Store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var signingOut = harness.Service.SignOut();

		await ConnectTestHarness.WaitUntil(() =>
			{
				harness.Time.Advance(TimeSpan.FromSeconds(16));
				return signingOut.IsCompleted;
			},
			"the sign-out waited for the stuck write without a bound");

		await signingOut;
		Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));

		gate.SetResult();
		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => pending);
		await harness.Persister.FlushAsync();

		Assert.That(await harness.Store.Load(), Is.Null, "the queued removal did not run after the stuck write");
	}

	[Test]
	public async Task Signing_out_while_a_refresh_is_unreachable_stays_signed_out()
	{
		await using var harness = new ConnectTestHarness();
		await SignedIn(harness);

		harness.Identity.BeforeRefresh = async () =>
		{
			harness.Identity.BeforeRefresh = null;
			await harness.Service.SignOut();
			harness.Identity.Results.Enqueue(FakeConnectIdentityClient.Unreachable());
		};

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => harness.Service.GetAccessToken());

		Assert.Multiple(() =>
		{
			Assert.That(harness.Service.Current.Status, Is.EqualTo(ConnectAccountStatus.SignedOut));
			Assert.That(harness.Service.Current.Connectivity, Is.EqualTo(ConnectConnectivity.Ok));
			Assert.That(harness.Service.Current.OfflineSince, Is.Null);
		});
	}

	private static async Task<ConnectCredential> SignedIn(ConnectTestHarness harness)
	{
		var credential = harness.Identity.SeedCredential(harness.Time.Now);
		harness.Store.Seed(credential);
		await harness.Service.Initialize();

		return credential;
	}

	private static async Task SignIn(ConnectTestHarness harness)
	{
		harness.SignInDelayHandler = (_, _) => Task.CompletedTask;
		harness.Identity.PollResults.Enqueue(new ConnectDevicePollResult(ConnectDevicePollStatus.Success));

		await harness.Service.StartSignIn();
		await ConnectTestHarness.WaitUntil(() => harness.Service.Current.Status == ConnectAccountStatus.SignedIn,
			"the sign-in never completed");
	}
}
