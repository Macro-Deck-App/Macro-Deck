using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class MacOsIoRegParserTests
{
	[Test]
	public void Parses_device_utilization_from_performance_statistics()
	{
		const string output =
			"+-o AGXAcceleratorG14X  <class AGXAcceleratorG14X, id 0x100000304, registered, matched, active>\n" +
			"    {\n" +
			"      \"PerformanceStatistics\" = {\"Device Utilization %\"=27,\"Renderer Utilization %\"=25," +
			"\"Alloc system memory\"=1735786496}\n" +
			"    }\n";

		Assert.That(MacOsIoRegParser.ParseDeviceUtilization(output), Is.EqualTo(27.0));
	}

	[Test]
	public void Returns_null_without_utilization_entry()
	{
		Assert.That(MacOsIoRegParser.ParseDeviceUtilization("+-o IOAccelerator\n{ }\n"), Is.Null);
	}

	[Test]
	public void ParseAcceleratorModel_reads_quoted_model_string()
	{
		const string output =
			"+-o AGXAcceleratorG14X  <class AGXAcceleratorG14X>\n" +
			"    {\n" +
			"      \"model\" = \"Apple M2 Pro\"\n" +
			"    }\n";

		Assert.That(MacOsIoRegParser.ParseAcceleratorModel(output), Is.EqualTo("Apple M2 Pro"));
	}

	[Test]
	public void ParseAcceleratorModel_reads_model_stored_as_data()
	{
		Assert.That(MacOsIoRegParser.ParseAcceleratorModel("      \"model\" = <\"Intel Iris Pro\">\n"),
			Is.EqualTo("Intel Iris Pro"));
	}

	[Test]
	public void ParseAcceleratorModel_returns_null_without_model_entry()
	{
		Assert.That(MacOsIoRegParser.ParseAcceleratorModel("+-o IOAccelerator\n{ }\n"), Is.Null);
	}
}
