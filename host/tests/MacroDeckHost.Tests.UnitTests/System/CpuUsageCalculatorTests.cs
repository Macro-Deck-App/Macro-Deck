using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class CpuUsageCalculatorTests
{
	[Test]
	public void Returns_null_without_previous_sample()
	{
		Assert.That(CpuUsageCalculator.Calculate(null, new CpuTimes(100, 200)), Is.Null);
	}

	[Test]
	public void Returns_percentage_of_busy_delta()
	{
		var usage = CpuUsageCalculator.Calculate(new CpuTimes(100, 200), new CpuTimes(150, 300));

		Assert.That(usage, Is.EqualTo(50.0));
	}

	[Test]
	public void Returns_null_without_elapsed_ticks()
	{
		Assert.That(CpuUsageCalculator.Calculate(new CpuTimes(100, 200), new CpuTimes(100, 200)), Is.Null);
	}

	[Test]
	public void Returns_null_after_counter_reset()
	{
		Assert.That(CpuUsageCalculator.Calculate(new CpuTimes(100, 200), new CpuTimes(10, 20)), Is.Null);
	}

	[Test]
	public void Clamps_to_valid_percent_range()
	{
		var usage = CpuUsageCalculator.Calculate(new CpuTimes(0, 100), new CpuTimes(300, 200));

		Assert.That(usage, Is.EqualTo(100.0));
	}
}
