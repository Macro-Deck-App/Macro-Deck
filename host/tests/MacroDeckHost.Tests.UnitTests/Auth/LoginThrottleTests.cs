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
		var custom = new LoginThrottle(_time, freeAttempts: 2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

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
}
