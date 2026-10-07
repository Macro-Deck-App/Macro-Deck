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

	[Test]
	public async Task The_system_drive_is_listed_with_capacity_and_activity()
	{
		var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			await metrics.GetDisksAsync();
			await Task.Delay(TimeSpan.FromSeconds(1.6));
			var drive = (await metrics.GetDisksAsync())
				.SingleOrDefault(disk => string.Equals(disk.MountPoint, systemDrive, StringComparison.OrdinalIgnoreCase));

			Assert.That(drive, Is.Not.Null);
			Assert.Multiple(() =>
			{
				Assert.That(drive!.Name, Is.Not.Empty);
				Assert.That(drive.TotalBytes, Is.GreaterThan(0));
				Assert.That(drive.FreeBytes, Is.InRange(0, drive.TotalBytes!.Value));
				Assert.That(drive.Activity, Is.Not.Null);
			});
		}
	}
}
