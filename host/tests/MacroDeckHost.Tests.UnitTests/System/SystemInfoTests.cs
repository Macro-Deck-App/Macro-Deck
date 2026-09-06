using MacroDeckHost.Integrations.System;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemInfoTests
{
	[Test]
	public void PcName_matches_machine_name()
	{
		Assert.That(SystemInfo.PcName, Is.EqualTo(Environment.MachineName));
	}

	[Test]
	public void OsName_is_never_empty()
	{
		Assert.That(SystemInfo.OsName, Is.Not.Empty);
	}

	[Test]
	public void ParseOsReleasePrettyName_returns_unquoted_value()
	{
		var lines = new[]
		{
			"NAME=\"Ubuntu\"",
			"PRETTY_NAME=\"Ubuntu 24.04.1 LTS\"",
			"VERSION_ID=\"24.04\""
		};

		Assert.That(SystemInfo.ParseOsReleasePrettyName(lines), Is.EqualTo("Ubuntu 24.04.1 LTS"));
	}

	[Test]
	public void ParseOsReleasePrettyName_handles_unquoted_value()
	{
		Assert.That(SystemInfo.ParseOsReleasePrettyName(["PRETTY_NAME=Arch Linux"]), Is.EqualTo("Arch Linux"));
	}

	[Test]
	public void ParseOsReleasePrettyName_returns_null_when_missing_or_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SystemInfo.ParseOsReleasePrettyName(["NAME=\"Ubuntu\""]), Is.Null);
			Assert.That(SystemInfo.ParseOsReleasePrettyName(["PRETTY_NAME=\"\""]), Is.Null);
			Assert.That(SystemInfo.ParseOsReleasePrettyName([]), Is.Null);
		});
	}

	[Test]
	public void ParseCpuModelName_returns_first_model_name_entry()
	{
		var lines = new[]
		{
			"processor\t: 0",
			"vendor_id\t: AuthenticAMD",
			"model name\t: AMD Ryzen 7 2700X Eight-Core Processor",
			"processor\t: 1",
			"model name\t: AMD Ryzen 7 2700X Eight-Core Processor"
		};

		Assert.That(SystemInfo.ParseCpuModelName(lines), Is.EqualTo("AMD Ryzen 7 2700X Eight-Core Processor"));
	}

	[Test]
	public void ParseCpuModelName_returns_null_when_absent_or_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SystemInfo.ParseCpuModelName(["vendor_id\t: AuthenticAMD"]), Is.Null);
			Assert.That(SystemInfo.ParseCpuModelName(["model name\t: "]), Is.Null);
			Assert.That(SystemInfo.ParseCpuModelName([]), Is.Null);
		});
	}
}
