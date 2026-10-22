using MacroDeckHost.Integrations.System.Providers;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
public class HostBatteryReaderMacOsTests
{
	[Test]
	public async Task The_factory_reader_answers_with_the_macs_batteries_or_none()
	{
		var reader = HostBatteryReaderFactory.Create();

		var readings = await reader.ReadAsync(CancellationToken.None);

		Assert.That(readings, Is.Not.Null, "pmset -g batt answers on every Mac");
		Assert.That(readings!.Select(reading => reading.LevelPercent),
			Has.All.Null.Or.InRange(0, 100),
			"a laptop reports its level, a desktop Mac reports no battery at all");
	}
}
