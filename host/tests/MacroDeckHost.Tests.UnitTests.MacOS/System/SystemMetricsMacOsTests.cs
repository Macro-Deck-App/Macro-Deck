using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
public class SystemMetricsMacOsTests
{
	[Test]
	public async Task Factory_creates_macos_service_that_reads_total_memory_and_reports_gpu_support()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			Assert.That(metrics.IsSupported, Is.True);
			Assert.That(metrics.IsGpuSupported, Is.True);

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
	public async Task The_startup_disk_is_listed_with_its_finder_usage_and_activity()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			await metrics.GetDisksAsync();
			await Task.Delay(TimeSpan.FromSeconds(1.6));
			var startup = (await metrics.GetDisksAsync()).SingleOrDefault(disk => disk.MountPoint == "/");
			var data = new DriveInfo("/System/Volumes/Data");

			Assert.That(startup, Is.Not.Null);
			Assert.Multiple(() =>
			{
				Assert.That(startup!.Name, Is.Not.Empty.And.Not.EqualTo("/"));
				Assert.That(startup.FileSystem, Is.EqualTo("apfs"));
				Assert.That(startup.TotalBytes, Is.GreaterThan(0));
				Assert.That(startup.UsedBytes, Is.GreaterThanOrEqualTo(data.TotalSize - data.TotalFreeSpace));
				Assert.That(startup.Activity, Is.Not.Null);
			});
		}
	}

	[Test]
	public async Task System_volumes_hidden_in_finder_are_not_listed()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			var disks = await metrics.GetDisksAsync();

			Assert.That(disks.Select(disk => disk.MountPoint), Has.None.StartsWith("/System/Volumes/"));
		}
	}
}
