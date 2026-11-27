using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
public class SystemMetricsMacOsTests
{
	private const long ConcurrentDiskChurnBytes = 256L * 1024 * 1024;

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
	[SupportedOSPlatform("macos")]
	public async Task The_gpu_reading_matches_what_ioreg_reports()
	{
		var ioreg = await RunIoRegAsync();
		var reportsModel = ioreg.Contains("\"model\"", StringComparison.Ordinal);
		var reportsUtilization = ioreg.Contains("\"Device Utilization %\"", StringComparison.Ordinal);

		var sample = MacOsGpuInterop.ReadAccelerator();

		Assert.Multiple(() =>
		{
			Assert.That(sample.Name is not null, Is.EqualTo(reportsModel));
			if (sample.Name is not null)
			{
				Assert.That(ioreg, Does.Contain(sample.Name));
			}

			Assert.That(sample.UsagePercent is not null, Is.EqualTo(reportsUtilization));
			if (sample.UsagePercent is { } usage)
			{
				Assert.That(usage, Is.InRange(0.0, 100.0));
			}
		});
	}

	private static async Task<string> RunIoRegAsync()
	{
		var startInfo = new ProcessStartInfo("ioreg")
		{
			RedirectStandardOutput = true,
			UseShellExecute = false
		};
		foreach (var argument in new[] { "-r", "-d", "1", "-w", "0", "-c", "IOAccelerator" })
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo)!;
		var output = await process.StandardOutput.ReadToEndAsync();
		await process.WaitForExitAsync();
		return output;
	}

	[Test]
	public async Task The_startup_disk_is_listed_with_its_finder_usage_and_activity()
	{
		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			await metrics.GetDisksAsync();
			await Task.Delay(TimeSpan.FromSeconds(1.6));
			var data = new DriveInfo("/System/Volumes/Data");
			var dataUsedBytes = data.TotalSize - data.TotalFreeSpace;
			var startup = (await metrics.GetDisksAsync()).SingleOrDefault(disk => disk.MountPoint == "/");

			Assert.That(startup, Is.Not.Null);
			Assert.Multiple(() =>
			{
				Assert.That(startup!.Name, Is.Not.Empty.And.Not.EqualTo("/"));
				Assert.That(startup.FileSystem, Is.EqualTo("apfs"));
				Assert.That(startup.TotalBytes, Is.GreaterThan(0));
				// Other processes write and delete files between the two samples; allow that churn.
				Assert.That(startup.UsedBytes, Is.GreaterThanOrEqualTo(dataUsedBytes - ConcurrentDiskChurnBytes));
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
