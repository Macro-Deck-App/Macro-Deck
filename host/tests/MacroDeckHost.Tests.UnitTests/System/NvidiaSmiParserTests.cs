using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class NvidiaSmiParserTests
{
	[Test]
	public void Parses_single_gpu_output()
	{
		Assert.That(NvidiaSmiParser.ParseUtilization("42\n"), Is.EqualTo(42.0));
	}

	[Test]
	public void Uses_first_gpu_on_multi_gpu_output()
	{
		Assert.That(NvidiaSmiParser.ParseUtilization("17\n83\n"), Is.EqualTo(17.0));
	}

	[Test]
	public void Returns_null_for_unparsable_output()
	{
		Assert.That(NvidiaSmiParser.ParseUtilization("NVIDIA-SMI has failed\n"), Is.Null);
	}

	[Test]
	public void ParseName_returns_first_gpu_name()
	{
		Assert.That(NvidiaSmiParser.ParseName("NVIDIA GeForce RTX 3080\nNVIDIA GeForce RTX 3060\n"),
			Is.EqualTo("NVIDIA GeForce RTX 3080"));
	}

	[Test]
	public void ParseName_returns_null_for_empty_output()
	{
		Assert.That(NvidiaSmiParser.ParseName("\n  \n"), Is.Null);
	}
}
