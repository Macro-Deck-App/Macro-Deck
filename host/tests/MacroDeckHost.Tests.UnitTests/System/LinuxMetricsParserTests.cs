using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class LinuxMetricsParserTests
{
	[Test]
	public void ParseProcStat_reads_aggregate_cpu_line()
	{
		const string content =
			"cpu  10132153 290696 3084719 46828483 16683 0 25195 0 0 0\n" +
			"cpu0 1393280 32966 572056 13343292 6130 0 17875 0 0 0\n";

		var times = LinuxMetricsParser.ParseProcStat(content);

		const ulong total = 10132153ul + 290696 + 3084719 + 46828483 + 16683 + 0 + 25195 + 0;
		const ulong idle = 46828483ul + 16683;
		Assert.That(times, Is.EqualTo(new CpuTimes(total - idle, total)));
	}

	[Test]
	public void ParseProcStat_returns_null_without_cpu_line()
	{
		Assert.That(LinuxMetricsParser.ParseProcStat("intr 114930548\nctxt 1990473\n"), Is.Null);
	}

	[Test]
	public void ParseMemInfo_reads_total_and_available()
	{
		const string content =
			"MemTotal:       16384256 kB\n" +
			"MemFree:         8290312 kB\n" +
			"MemAvailable:   12345678 kB\n" +
			"Buffers:          517424 kB\n";

		var memory = LinuxMetricsParser.ParseMemInfo(content);

		Assert.That(memory, Is.EqualTo(new MemoryInfo(16384256L * 1024, 12345678L * 1024)));
	}

	[Test]
	public void ParseMemInfo_returns_null_without_available()
	{
		Assert.That(LinuxMetricsParser.ParseMemInfo("MemTotal:       16384256 kB\n"), Is.Null);
	}

	[Test]
	public void ParseGpuBusyPercent_reads_and_clamps_value()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LinuxMetricsParser.ParseGpuBusyPercent("42\n"), Is.EqualTo(42.0));
			Assert.That(LinuxMetricsParser.ParseGpuBusyPercent("150"), Is.EqualTo(100.0));
			Assert.That(LinuxMetricsParser.ParseGpuBusyPercent("not-a-number"), Is.Null);
		});
	}
}
