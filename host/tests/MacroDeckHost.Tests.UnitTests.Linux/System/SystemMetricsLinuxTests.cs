using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
public class SystemMetricsLinuxTests
{
	[Test]
	public async Task Factory_creates_linux_service_that_reads_total_memory_from_proc()
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
	public async Task The_root_file_system_is_listed_with_capacity_and_activity()
	{
		var rootSource = File.ReadAllLines("/proc/self/mountinfo")
			.Where(line => line.Split(' ')[4] == "/")
			.Select(line => line[(line.IndexOf(" - ", StringComparison.Ordinal) + 3)..].Split(' ')[1])
			.LastOrDefault();
		Assume.That(rootSource, Does.StartWith("/dev/"), "the root file system is not on a block device, as in a container");

		var metrics = SystemMetricsServiceFactory.Create();
		using (metrics as IDisposable)
		{
			await metrics.GetDisksAsync();
			await Task.Delay(TimeSpan.FromSeconds(1.6));
			var root = (await metrics.GetDisksAsync()).SingleOrDefault(disk => disk.MountPoint == "/");

			Assert.That(root, Is.Not.Null);
			Assert.Multiple(() =>
			{
				Assert.That(root!.Name, Is.Not.Empty);
				Assert.That(root.TotalBytes, Is.GreaterThan(0));
				Assert.That(root.FreeBytes, Is.InRange(0, root.TotalBytes!.Value));
				Assert.That(root.Activity, Is.Not.Null);
			});
		}
	}
}
