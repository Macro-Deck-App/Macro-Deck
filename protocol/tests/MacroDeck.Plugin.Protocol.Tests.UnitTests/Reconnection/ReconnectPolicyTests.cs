using MacroDeck.Plugin.Protocol.Reconnection;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Reconnection;

[TestFixture]
public class ReconnectPolicyTests
{
	private static readonly double[] _jitterSamples = [0.0, 0.25, 0.5, 0.75, 1.0];

	[Test]
	public void Delay_doubles_per_attempt_before_saturating()
	{
		var first = ReconnectPolicy.DelayFor(1, 1.0);
		var second = ReconnectPolicy.DelayFor(2, 1.0);
		var third = ReconnectPolicy.DelayFor(3, 1.0);

		Assert.Multiple(() =>
		{
			Assert.That(second, Is.EqualTo(first * 2));
			Assert.That(third, Is.EqualTo(first * 4));
		});
	}

	[Test]
	public void Delay_saturates_at_the_max_delay()
		=> Assert.That(ReconnectPolicy.DelayFor(20, 1.0), Is.EqualTo(ReconnectPolicy.MaxDelay));

	[Test]
	public void Jitter_sample_of_one_reproduces_the_unjittered_ceiling()
	{
		var delay = ReconnectPolicy.DelayFor(3, 1.0);
		var expectedMilliseconds = Math.Min(
			ReconnectPolicy.InitialDelay.TotalMilliseconds * Math.Pow(ReconnectPolicy.BackoffFactor, 2),
			ReconnectPolicy.MaxDelay.TotalMilliseconds);

		Assert.That(delay, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMilliseconds)));
	}

	[Test]
	public void Jitter_sample_of_zero_yields_zero_delay()
		=> Assert.That(ReconnectPolicy.DelayFor(1, 0.0), Is.EqualTo(TimeSpan.Zero));

	[Test]
	public void Delay_is_never_negative_across_a_wide_range_of_attempts_and_jitter_samples()
	{
		Assert.Multiple(() =>
		{
			for (var attempt = 1; attempt <= 15; attempt++)
			{
				foreach (var jitterSample in _jitterSamples)
				{
					var delay = ReconnectPolicy.DelayFor(attempt, jitterSample);
					Assert.That(delay, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
				}
			}
		});
	}

	[TestCase(0)]
	[TestCase(-1)]
	public void An_attempt_below_one_throws(int attempt)
		=> Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectPolicy.DelayFor(attempt, 0.5));

	[TestCase(-0.1)]
	[TestCase(1.1)]
	public void A_jitter_sample_outside_zero_to_one_throws(double jitterSample)
		=> Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectPolicy.DelayFor(1, jitterSample));

	[Test]
	public void Initial_delay_is_one_second_and_max_delay_is_thirty_seconds()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ReconnectPolicy.InitialDelay, Is.EqualTo(TimeSpan.FromSeconds(1)));
			Assert.That(ReconnectPolicy.MaxDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
		});
	}
}
