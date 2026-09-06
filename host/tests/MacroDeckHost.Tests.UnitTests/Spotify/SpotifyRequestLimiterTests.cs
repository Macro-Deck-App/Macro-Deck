using MacroDeckHost.Integrations.Spotify;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyRequestLimiterTests
{
	private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	[SetUp]
	public void Reset() => _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	[Test]
	public void Polls_stop_at_the_interactive_reserve()
	{
		var limiter = Limiter();

		var granted = 0;
		while (limiter.Reserve(droppable: true) is not null)
		{
			granted++;
		}

		Assert.Multiple(() =>
		{
			Assert.That(granted, Is.EqualTo(5));

			Assert.That(limiter.Reserve(droppable: false), Is.EqualTo(TimeSpan.Zero));
		});
	}

	[Test]
	public void An_interactive_request_waits_rather_than_being_dropped()
	{
		var limiter = Limiter();
		while (limiter.Reserve(droppable: true) is not null)
		{
		}

		for (var i = 0; i < 4; i++)
		{
			limiter.Reserve(droppable: false);
		}

		Assert.That(limiter.Reserve(droppable: false), Is.GreaterThan(TimeSpan.Zero));
	}

	[Test]
	public void Permits_refill_at_the_sustained_rate()
	{
		var limiter = Limiter();
		while (limiter.Reserve(droppable: true) is not null)
		{
		}

		_now += TimeSpan.FromSeconds(1);

		Assert.That(limiter.Reserve(droppable: true), Is.EqualTo(TimeSpan.Zero));
	}

	[Test]
	public void A_long_gap_refills_at_most_one_burst()
	{
		// The screen-unlock case: every TTL, backoff and timer in the integration comes due in the same
		// instant. The bucket absorbs that herd, but it must not hand out an hour's worth of permits.
		var limiter = Limiter();
		_now += TimeSpan.FromHours(8);

		var granted = 0;
		while (limiter.Reserve(droppable: true) is not null)
		{
			granted++;
		}

		Assert.That(granted, Is.EqualTo(5));
	}

	[Test]
	public void A_refusal_halves_the_rate_and_recovery_climbs_back()
	{
		var limiter = Limiter();

		limiter.NoteRefused();
		Assert.That(limiter.PermitsPerSecond, Is.EqualTo(1).Within(0.001));

		limiter.NoteRefused();
		Assert.That(limiter.PermitsPerSecond, Is.EqualTo(0.5).Within(0.001));

		for (var i = 0; i < 100; i++)
		{
			limiter.NoteAccepted();
		}

		Assert.That(limiter.PermitsPerSecond, Is.EqualTo(SpotifyRequestLimiter.DefaultCeilingPerSecond).Within(0.001));
	}

	[Test]
	public void A_refusal_gives_up_the_remaining_burst()
	{
		var limiter = Limiter();
		limiter.NoteRefused();

		Assert.That(limiter.Reserve(droppable: true), Is.Null);
	}

	[Test]
	public void A_token_request_inside_a_poll_is_not_droppable()
	{
		using (SpotifyRequestScope.Poll())
		{
			using (SpotifyRequestScope.Interactive())
			{
				Assert.That(SpotifyRequestScope.IsDroppable, Is.False);
			}

			Assert.That(SpotifyRequestScope.IsDroppable, Is.True);
		}

		Assert.That(SpotifyRequestScope.IsDroppable, Is.False);
	}

	private SpotifyRequestLimiter Limiter() => new(utcNow: () => _now);
}
