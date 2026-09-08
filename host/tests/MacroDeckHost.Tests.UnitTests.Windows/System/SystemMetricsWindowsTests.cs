using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class SystemMetricsWindowsTests
{
	[Test]
	public async Task Factory_creates_windows_service_that_reads_total_physical_memory()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			Assert.That(metrics.IsSupported, Is.True);

			var memory = await metrics.GetMemoryAsync();

			Assert.That(memory, Is.Not.Null);
			Assert.Multiple(() =>
			{
				Assert.That(memory!.TotalBytes, Is.GreaterThan(0));
				Assert.That(memory.AvailableBytes, Is.InRange(0, memory.TotalBytes));
			});
		}
	}

	[Test]
	public async Task Gpu_enumeration_and_reads_stay_within_contract()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			for (var index = 0; index < metrics.GpuCount; index++)
			{
				var usage = await metrics.GetGpuUsageAsync(index);
				Assert.That(usage, Is.Null.Or.InRange(0d, 100d));
			}

			Assert.That(await metrics.GetGpuUsageAsync(metrics.GpuCount), Is.Null);
		}
	}
}
