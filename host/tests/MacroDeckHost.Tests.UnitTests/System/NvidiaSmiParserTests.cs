using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class NvidiaSmiParserTests
{
	[Test]
	public void Every_reported_card_becomes_a_gpu_in_index_order()
	{
		var samples = NvidiaSmiParser.ParseGpus("1, NVIDIA GeForce RTX 4070, 42\n0, NVIDIA RTX A2000, 7\n");

		Assert.That(samples,
			Is.EqualTo(new[]
			{
				new GpuSample("NVIDIA RTX A2000", 7),
				new GpuSample("NVIDIA GeForce RTX 4070", 42)
			}));
	}

	[Test]
	public void Blank_and_partial_lines_are_skipped()
	{
		var samples = NvidiaSmiParser.ParseGpus("\n0, NVIDIA GeForce RTX 4070, 42\nnot a row\n1, only two fields\n");

		Assert.That(samples, Is.EqualTo(new[] { new GpuSample("NVIDIA GeForce RTX 4070", 42) }));
	}

	[Test]
	public void An_unreadable_utilization_leaves_the_name_usable()
	{
		var samples = NvidiaSmiParser.ParseGpus("0, NVIDIA GeForce RTX 4070, [N/A]\n");

		Assert.That(samples, Is.EqualTo(new[] { new GpuSample("NVIDIA GeForce RTX 4070", null) }));
	}

	[Test]
	public void Utilization_is_clamped()
	{
		Assert.That(NvidiaSmiParser.ParseGpus("0, GPU, 140\n")[0].UsagePercent, Is.EqualTo(100));
	}
}
