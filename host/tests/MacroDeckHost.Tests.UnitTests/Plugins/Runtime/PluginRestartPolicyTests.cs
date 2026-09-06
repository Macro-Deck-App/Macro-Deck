using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
public class PluginRestartPolicyTests
{
	[Test]
	public void Attempt_zero_returns_no_delay_regardless_of_jitter()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginRestartPolicy.DelayFor(0, 0.0), Is.EqualTo(TimeSpan.Zero));
			Assert.That(PluginRestartPolicy.DelayFor(0, 1.0), Is.EqualTo(TimeSpan.Zero));
		});
	}

	[Test]
	public void Delay_grows_monotonically_up_to_the_reconnect_policy_cap()
	{
		var delays = Enumerable.Range(1, 7).Select(attempt => PluginRestartPolicy.DelayFor(attempt, 1.0)).ToList();

		Assert.Multiple(() =>
		{
			for (var i = 1; i < delays.Count; i++)
			{
				Assert.That(delays[i], Is.GreaterThanOrEqualTo(delays[i - 1]), $"attempt {i + 1} vs {i}");
			}

			Assert.That(delays[^1], Is.EqualTo(TimeSpan.FromSeconds(30)));
			Assert.That(delays[0], Is.EqualTo(TimeSpan.FromSeconds(1)));
		});
	}

	[Test]
	public void Budget_is_exhausted_once_max_restarts_have_landed_inside_the_window()
	{
		var options = new PluginSupervisorOptions { MaxRestarts = 5, RestartWindow = TimeSpan.FromMinutes(10) };
		var now = DateTimeOffset.UtcNow;

		var fiveRecentRestarts = Enumerable.Range(0, 5).Select(i => now - TimeSpan.FromSeconds(i)).ToList();
		var fourRecentRestarts = fiveRecentRestarts.Take(4).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(PluginRestartPolicy.IsBudgetExhausted(fiveRecentRestarts, now, options),
				Is.True,
				"the 6th restart, with 5 already landed, must be refused");
			Assert.That(PluginRestartPolicy.IsBudgetExhausted(fourRecentRestarts, now, options),
				Is.False,
				"a 5th restart, with only 4 landed, is still within budget");
		});
	}

	[Test]
	public void A_restart_outside_the_window_does_not_count_toward_the_budget()
	{
		var options = new PluginSupervisorOptions { MaxRestarts = 5, RestartWindow = TimeSpan.FromMinutes(10) };
		var now = DateTimeOffset.UtcNow;

		var timestamps = new List<DateTimeOffset>
		{
			now - TimeSpan.FromMinutes(11), // outside the window - must not count
			now - TimeSpan.FromSeconds(3),
			now - TimeSpan.FromSeconds(2),
			now - TimeSpan.FromSeconds(1),
			now
		};

		Assert.That(PluginRestartPolicy.IsBudgetExhausted(timestamps, now, options), Is.False);
	}

	[Test]
	public void Stable_runtime_resets_exactly_at_the_threshold()
	{
		var options = new PluginSupervisorOptions { StableRuntime = TimeSpan.FromMinutes(2) };

		Assert.Multiple(() =>
		{
			Assert.That(PluginRestartPolicy.IsRuntimeStable(TimeSpan.FromMinutes(2), options), Is.True);
			Assert.That(
				PluginRestartPolicy.IsRuntimeStable(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1), options),
				Is.False);
			Assert.That(PluginRestartPolicy.IsRuntimeStable(TimeSpan.FromMinutes(3), options), Is.True);
		});
	}
}
