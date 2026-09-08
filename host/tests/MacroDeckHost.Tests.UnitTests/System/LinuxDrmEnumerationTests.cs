using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class LinuxDrmEnumerationTests
{
	private string _root = string.Empty;

	[SetUp]
	public void SetUp() => _root = Directory.CreateTempSubdirectory("macrodeck-drm").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_root, recursive: true);

	[Test]
	public void Every_card_with_a_busy_percent_node_is_its_own_gpu()
	{
		Card("card0", busy: true);
		Card("card1", busy: true);

		Assert.That(LinuxDrmEnumerator.FindAmdGpuBusyPercentPaths(_root), Has.Count.EqualTo(2));
	}

	[Test]
	public void Connector_directories_are_not_gpus()
	{
		Card("card0", busy: true);
		Card("card0-DP-1", busy: true);
		Card("card0-HDMI-A-1", busy: true);

		Assert.That(LinuxDrmEnumerator.FindAmdGpuBusyPercentPaths(_root), Has.Count.EqualTo(1));
	}

	[Test]
	public void A_card_without_a_busy_percent_node_is_not_counted()
	{
		Card("card0", busy: false);

		Assert.That(LinuxDrmEnumerator.FindAmdGpuBusyPercentPaths(_root), Is.Empty);
	}

	[Test]
	public void A_missing_drm_root_is_not_an_error()
	{
		Assert.That(LinuxDrmEnumerator.FindAmdGpuBusyPercentPaths(Path.Combine(_root, "nope")), Is.Empty);
	}

	[Test]
	public void Every_nvidia_card_the_driver_publishes_is_counted()
	{
		var nvidia = Path.Combine(_root, "gpus");
		Directory.CreateDirectory(Path.Combine(nvidia, "0000:01:00.0"));
		Directory.CreateDirectory(Path.Combine(nvidia, "0000:02:00.0"));

		Assert.That(LinuxDrmEnumerator.CountNvidiaGpus(nvidia), Is.EqualTo(2));
	}

	[Test]
	public void A_machine_without_the_nvidia_driver_reports_an_unknown_count()
	{
		Assert.That(LinuxDrmEnumerator.CountNvidiaGpus(Path.Combine(_root, "gpus")), Is.Null);
	}

	[Test]
	public void An_nvidia_driver_with_no_cards_counts_zero()
	{
		var nvidia = Path.Combine(_root, "gpus");
		Directory.CreateDirectory(nvidia);

		Assert.That(LinuxDrmEnumerator.CountNvidiaGpus(nvidia), Is.Zero);
	}

	private void Card(string name, bool busy)
	{
		var device = Path.Combine(_root, name, "device");
		Directory.CreateDirectory(device);
		if (busy)
		{
			File.WriteAllText(Path.Combine(device, "gpu_busy_percent"), "17\n");
		}
	}
}
