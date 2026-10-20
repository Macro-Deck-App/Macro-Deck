using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class NvidiaGpuMetricsLinuxTests
{
	private string _directory = null!;

	[SetUp]
	public void CreateDirectory()
	{
		_directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"md-gpu-{Guid.NewGuid():N}")).FullName;
		Directory.CreateDirectory(Path.Combine(_directory, "nvidia", "0000:01:00.0"));
		Directory.CreateDirectory(Path.Combine(_directory, "nvidia", "0000:02:00.0"));

		var nvidiaSmi = Path.Combine(_directory, "nvidia-smi");
		File.WriteAllText(nvidiaSmi,
			"#!/bin/sh\necho run >> \"$(dirname \"$0\")/calls\"\nprintf '0, Smi GPU, 55\\n1, Smi GPU 2, 12\\n'\n");
		File.SetUnixFileMode(nvidiaSmi, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	[TearDown]
	public void DeleteDirectory()
	{
		Directory.Delete(_directory, true);
	}

	[Test]
	public async Task Readings_from_the_driver_library_are_served_without_running_nvidia_smi()
	{
		using var metrics = Metrics(count =>
			count == 2 ? [new GpuSample("Library GPU", 30), new GpuSample("Library GPU 2", null)] : null);

		var firstUsage = await metrics.GetGpuUsageAsync(0);
		var secondUsage = await metrics.GetGpuUsageAsync(1);
		var secondName = await metrics.GetGpuNameAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(firstUsage, Is.EqualTo(30));
			Assert.That(secondUsage, Is.Null);
			Assert.That(secondName, Is.EqualTo("Library GPU 2"));
			Assert.That(File.Exists(Path.Combine(_directory, "calls")), Is.False);
		});
	}

	[Test]
	public async Task Without_the_driver_library_the_usage_comes_from_nvidia_smi()
	{
		using var metrics = Metrics(_ => null);

		var firstUsage = await metrics.GetGpuUsageAsync(0);
		var secondName = await metrics.GetGpuNameAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(firstUsage, Is.EqualTo(55));
			Assert.That(secondName, Is.EqualTo("Smi GPU 2"));
		});
	}

	[Test]
	public void Without_the_driver_library_reading_it_reports_nothing_instead_of_throwing()
	{
		var available = NativeLibrary.TryLoad("libnvidia-ml.so.1", out var handle);
		if (available)
		{
			NativeLibrary.Free(handle);
		}

		Assume.That(available, Is.False, "the NVIDIA driver library is installed here");

		Assert.That(new LinuxNvml().TryRead(1), Is.Null);
	}

	private LinuxSystemMetricsService Metrics(Func<int, IReadOnlyList<GpuSample>?> readNvml)
	{
		return new LinuxSystemMetricsService(Path.Combine(_directory, "no-drm"),
			Path.Combine(_directory, "nvidia"),
			Path.Combine(_directory, "nvidia-smi"),
			readNvml);
	}
}
