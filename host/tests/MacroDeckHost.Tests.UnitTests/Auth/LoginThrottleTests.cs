using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class LoginThrottleTests
{
	private ManualTimeProvider _time = null!;
	private LoginThrottle _throttle = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_throttle = new LoginThrottle(_time);
	}

	[Test]
	public void First_failures_do_not_throttle()
	{
		for (var i = 0; i < 4; i++)
		{
			_throttle.RegisterFailure("key");
		}

		Assert.That(_throttle.IsThrottled("key", out _), Is.False);
	}

	[Test]
	public void Fifth_failure_locks_out_and_lockout_expires()
	{
		for (var i = 0; i < 5; i++)
		{
			_throttle.RegisterFailure("key");
		}

		var throttled = _throttle.IsThrottled("key", out var retryAfter);
		_time.Advance(TimeSpan.FromSeconds(31));
		var afterExpiry = _throttle.IsThrottled("key", out _);

		Assert.Multiple(() =>
		{
			Assert.That(throttled, Is.True);
			Assert.That(retryAfter, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(afterExpiry, Is.False);
		});
	}

	[Test]
	public void A_full_table_gives_up_its_oldest_client_but_keeps_global_lockouts_and_admits_a_new_client()
	{
		var throttle = new LoginThrottle(_time, 5, null, null, 8);
		for (var i = 0; i < 5; i++)
		{
			throttle.RegisterFailure("pairing-code");
		}

		for (var address = 0; address < 50; address++)
		{
			_time.Advance(TimeSpan.FromMilliseconds(100));
			var key = LoginThrottle.LoginKey($"2001:db8::{address:x}");
			for (var i = 0; i < 5; i++)
			{
				throttle.TryReserve(key, out _);
			}
		}

		var admitted = throttle.TryReserve(LoginThrottle.LoginKey("192.0.2.10"), out _);

		Assert.Multiple(() =>
		{
			Assert.That(throttle.IsThrottled("pairing-code", out _), Is.True);
			Assert.That(admitted, Is.True);
			Assert.That(throttle.IsThrottled(LoginThrottle.LoginKey("2001:db8::0"), out _), Is.False);
			Assert.That(throttle.IsThrottled(LoginThrottle.LoginKey("2001:db8::31"), out _), Is.True);
		});
	}

	[Test]
	public void Lockout_grows_with_further_failures_but_is_capped()
	{
		for (var i = 0; i < 20; i++)
		{
			_throttle.RegisterFailure("key");
		}

		_throttle.IsThrottled("key", out var retryAfter);

		Assert.That(retryAfter, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(15)));
	}

	[Test]
	public void Success_clears_the_counter()
	{
		for (var i = 0; i < 5; i++)
		{
			_throttle.RegisterFailure("key");
		}

		_throttle.RegisterSuccess("key");

		Assert.That(_throttle.IsThrottled("key", out _), Is.False);
	}

	[Test]
	public void Keys_are_tracked_independently()
	{
		for (var i = 0; i < 5; i++)
		{
			_throttle.RegisterFailure("a");
		}

		Assert.Multiple(() =>
		{
			Assert.That(_throttle.IsThrottled("a", out _), Is.True);
			Assert.That(_throttle.IsThrottled("b", out _), Is.False);
		});
	}

	[Test]
	public void Stale_entries_are_pruned()
	{
		for (var i = 0; i < 5; i++)
		{
			_throttle.RegisterFailure("key");
		}

		_time.Advance(TimeSpan.FromHours(2));

		Assert.That(_throttle.IsThrottled("key", out _), Is.False);
	}

	[Test]
	public void Custom_curve_throttles_after_its_own_free_attempt_count()
	{
		var custom = new LoginThrottle(_time, 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

		custom.RegisterFailure("plugin-session|com.example.plugin");
		Assert.That(custom.IsThrottled("plugin-session|com.example.plugin", out _), Is.False);

		custom.RegisterFailure("plugin-session|com.example.plugin");

		Assert.That(custom.IsThrottled("plugin-session|com.example.plugin", out var retryAfter), Is.True);
		Assert.That(retryAfter, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(30)));
	}

	[Test]
	public void Default_curve_is_unaffected_by_the_new_optional_parameters()
	{
		var defaultThrottle = new LoginThrottle(_time);

		for (var i = 0; i < 4; i++)
		{
			defaultThrottle.RegisterFailure("key");
		}

		Assert.That(defaultThrottle.IsThrottled("key", out _), Is.False);
	}

	[Test]
	public async Task Parallel_reservations_on_one_key_admit_only_the_free_attempts()
	{
		using var start = new Barrier(32);
		var attempts = Enumerable.Range(0, 32)
			.Select(_ => Task.Factory.StartNew(() =>
				{
					start.SignalAndWait();
					return _throttle.TryReserve("key", out var _);
				},
				TaskCreationOptions.LongRunning))
			.ToArray();

		var admitted = await Task.WhenAll(attempts);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(admitted.Count(granted => granted), Is.EqualTo(5));
			Assert.That(_throttle.IsThrottled("key", out var retryAfter), Is.True);
			Assert.That(retryAfter, Is.GreaterThan(TimeSpan.Zero));
			return Task.CompletedTask;
		});
	}

	[Test]
	public void A_refused_reservation_reports_the_remaining_lockout()
	{
		for (var i = 0; i < 5; i++)
		{
			_throttle.TryReserve("key", out _);
		}

		_time.Advance(TimeSpan.FromSeconds(10));
		var admitted = _throttle.TryReserve("key", out var retryAfter);

		Assert.Multiple(() =>
		{
			Assert.That(admitted, Is.False);
			Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromSeconds(20)));
		});
	}

	[Test]
	public void Clearing_logins_lifts_every_source_and_leaves_other_buckets_alone()
	{
		var first = LoginThrottle.LoginKey("203.0.113.7");
		var second = LoginThrottle.LoginKey("usb:EXAMPLE0001");
		for (var i = 0; i < 5; i++)
		{
			_throttle.RegisterFailure(first);
			_throttle.RegisterFailure(second);
			_throttle.RegisterFailure("203.0.113.7|device-enrollment");
			_throttle.RegisterFailure("pairing-code");
		}

		_throttle.ClearLogins();

		Assert.Multiple(() =>
		{
			Assert.That(_throttle.IsThrottled(first, out _), Is.False);
			Assert.That(_throttle.IsThrottled(second, out _), Is.False);
			Assert.That(_throttle.IsThrottled("203.0.113.7|device-enrollment", out _), Is.True);
			Assert.That(_throttle.IsThrottled("pairing-code", out _), Is.True);
		});
	}
}
