using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemIntegrationDiskTests
{
	private static readonly DiskSample _system = Disk("/", "Macintosh HD", total: 1000, free: 250,
		new DiskActivity(2048, 512, 40, 60));

	private static readonly DiskSample _stick = Disk("/Volumes/STICK", "STICK", total: 64, free: 32, activity: null);

	private static readonly DiskSample _backup = Disk("/Volumes/Backup", "Backup", total: 500, free: 100, activity: null);

	private static DiskSample Disk(string mountPoint, string name, long total, long free, DiskActivity? activity)
		=> new(mountPoint, name, "apfs", total, free, activity);

	private static (SystemIntegration Integration, FakeSystemMetricsService Metrics, StaleCounter Stale) Create(
		FakeVolumeService? volume = null,
		int gpuCount = 1)
	{
		var metrics = new FakeSystemMetricsService(gpuCount: gpuCount);
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			volume ?? new FakeVolumeService(),
			new SilentNotifications(),
			metrics,
			new FakePowerService(true));
		var stale = new StaleCounter();
		integration.UseVariablePollingInvalidation(stale);
		return (integration, metrics, stale);
	}

	private static async Task<object?> Read(SystemIntegration integration, string name)
		=> (await integration.ReadAsync(name.Replace('_', '-'))).Value;

	[Test]
	public async Task Each_disk_gets_indexed_variables_with_its_statistics()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.Add(_system);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Is.SupersetOf(new[]
			{
				"system_disk_0_name", "system_disk_0_mount_point", "system_disk_0_file_system",
				"system_disk_0_total_bytes", "system_disk_0_used_bytes", "system_disk_0_free_bytes",
				"system_disk_0_usage_percent", "system_disk_0_read_bytes_per_second",
				"system_disk_0_write_bytes_per_second", "system_disk_0_read_usage_percent",
				"system_disk_0_write_usage_percent"
			}));
			Assert.That(await Read(integration, "system_disk_0_name"), Is.EqualTo("Macintosh HD"));
			Assert.That(await Read(integration, "system_disk_0_mount_point"), Is.EqualTo("/"));
			Assert.That(await Read(integration, "system_disk_0_file_system"), Is.EqualTo("apfs"));
			Assert.That(await Read(integration, "system_disk_0_total_bytes"), Is.EqualTo(1000));
			Assert.That(await Read(integration, "system_disk_0_free_bytes"), Is.EqualTo(250));
			Assert.That(await Read(integration, "system_disk_0_used_bytes"), Is.EqualTo(750));
			Assert.That(await Read(integration, "system_disk_0_usage_percent"), Is.EqualTo(75));
			Assert.That(await Read(integration, "system_disk_0_read_bytes_per_second"), Is.EqualTo(2048));
			Assert.That(await Read(integration, "system_disk_0_write_bytes_per_second"), Is.EqualTo(512));
			Assert.That(await Read(integration, "system_disk_0_read_usage_percent"), Is.EqualTo(40));
			Assert.That(await Read(integration, "system_disk_0_write_usage_percent"), Is.EqualTo(60));
		});
	}

	[Test]
	public async Task Sizes_are_bytes_rates_are_bytes_per_second_and_shares_are_percentages()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.Add(_system);
		await integration.RefreshDisksAsync();

		string? KindOf(string name) => integration.Variables.Single(v => v.Name == name).SemanticKind;

		Assert.Multiple(() =>
		{
			Assert.That(KindOf("system_disk_0_free_bytes"), Is.EqualTo(VariableSemanticKinds.Bytes));
			Assert.That(KindOf("system_disk_0_read_bytes_per_second"), Is.EqualTo(VariableSemanticKinds.BytesPerSecond));
			Assert.That(KindOf("system_disk_0_usage_percent"), Is.EqualTo(VariableSemanticKinds.Percentage));
			Assert.That(KindOf("system_disk_0_write_usage_percent"), Is.EqualTo(VariableSemanticKinds.Percentage));
		});
	}

	[Test]
	public async Task A_platform_without_activity_figures_reads_them_as_unavailable()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.Add(_stick);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(await Read(integration, "system_disk_0_free_bytes"), Is.EqualTo(32));
			Assert.That(await Read(integration, "system_disk_0_read_bytes_per_second"), Is.Null);
			Assert.That(await Read(integration, "system_disk_0_write_usage_percent"), Is.Null);
		});
	}

	[Test]
	public async Task The_system_disk_comes_first_and_the_others_follow_by_mount_point()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.AddRange([_stick, _backup, _system]);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(await Read(integration, "system_disk_0_mount_point"), Is.EqualTo("/"));
			Assert.That(await Read(integration, "system_disk_1_mount_point"), Is.EqualTo("/Volumes/Backup"));
			Assert.That(await Read(integration, "system_disk_2_mount_point"), Is.EqualTo("/Volumes/STICK"));
		});
	}

	[Test]
	public async Task A_removed_disk_reads_unavailable_while_the_others_keep_their_numbers()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.AddRange([_system, _backup, _stick]);
		await integration.RefreshDisksAsync();

		metrics.Disks.Remove(_backup);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_disk_1_free_bytes"));
			Assert.That(await Read(integration, "system_disk_1_free_bytes"), Is.Null);
			Assert.That(await Read(integration, "system_disk_1_name"), Is.Null);
			Assert.That(await Read(integration, "system_disk_0_name"), Is.EqualTo("Macintosh HD"));
			Assert.That(await Read(integration, "system_disk_2_name"), Is.EqualTo("STICK"));
			Assert.That(await Read(integration, "system_disk_2_free_bytes"), Is.EqualTo(32));
		});
	}

	[Test]
	public async Task A_new_disk_takes_the_lowest_free_number()
	{
		var (integration, metrics, stale) = Create();
		metrics.Disks.AddRange([_system, _backup, _stick]);
		await integration.RefreshDisksAsync();
		metrics.Disks.Remove(_backup);
		await integration.RefreshDisksAsync();
		var staleBefore = stale.Count;

		var camera = Disk("/Volumes/CAMERA", "CAMERA", 32, 8, null);
		metrics.Disks.Add(camera);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(await Read(integration, "system_disk_1_name"), Is.EqualTo("CAMERA"));
			Assert.That(await Read(integration, "system_disk_2_name"), Is.EqualTo("STICK"));
			Assert.That(integration.Variables.Select(v => v.Name), Has.None.StartsWith("system_disk_3_"));
			Assert.That(stale.Count, Is.EqualTo(staleBefore), "reusing a declared slot republishes nothing");
		});
	}

	[Test]
	public async Task A_disk_taking_a_freed_number_is_read_again_at_once()
	{
		var (integration, metrics, _) = Create();
		var refresh = new VariableRefreshSignal();
		integration.UseVariableRefreshSignal(refresh);
		metrics.Disks.AddRange([_system, _backup]);
		await integration.RefreshDisksAsync();
		refresh.DrainDefinitionsFor(SystemIntegration.IntegrationId);

		metrics.Disks.Remove(_backup);
		metrics.Disks.Add(_stick);
		await integration.RefreshDisksAsync();

		var requested = refresh.DrainDefinitionsFor(SystemIntegration.IntegrationId);
		Assert.Multiple(() =>
		{
			Assert.That(requested, Does.Contain("system-disk-1-name").And.Contain("system-disk-1-free-bytes"));
			Assert.That(requested, Has.None.StartsWith("system-disk-0-"));
		});
	}

	[Test]
	public async Task A_read_that_finds_no_disks_keeps_the_numbering()
	{
		var (integration, metrics, _) = Create();
		metrics.Disks.AddRange([_backup, _stick]);
		await integration.RefreshDisksAsync();
		metrics.Disks.Add(_system);
		await integration.RefreshDisksAsync();

		metrics.Disks.Clear();
		await integration.RefreshDisksAsync();
		metrics.Disks.AddRange([_backup, _stick, _system]);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(await Read(integration, "system_disk_0_name"), Is.EqualTo("Backup"));
			Assert.That(await Read(integration, "system_disk_1_name"), Is.EqualTo("STICK"));
			Assert.That(await Read(integration, "system_disk_2_name"), Is.EqualTo("Macintosh HD"));
		});
	}

	[Test]
	public async Task A_disk_appearing_while_running_is_declared_and_republished()
	{
		var (integration, metrics, stale) = Create();
		metrics.Disks.Add(_system);
		await integration.RefreshDisksAsync();
		var staleBefore = stale.Count;

		metrics.Disks.Add(_stick);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_disk_1_name"));
			Assert.That(await Read(integration, "system_disk_1_name"), Is.EqualTo("STICK"));
			Assert.That(stale.Count, Is.GreaterThan(staleBefore));
		});
	}

	[Test]
	public async Task Disks_beyond_eight_wait_for_a_free_number()
	{
		var (integration, metrics, _) = Create();
		var disks = Enumerable.Range(0, 9)
			.Select(i => Disk($"/Volumes/D{i}", $"D{i}", 10, 5, null))
			.ToList();
		metrics.Disks.AddRange(disks);
		await integration.RefreshDisksAsync();
		var withoutNinth = integration.Variables.Select(v => v.Name).ToList();

		metrics.Disks.Remove(disks[3]);
		await integration.RefreshDisksAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(withoutNinth, Has.None.StartsWith("system_disk_8_"));
			Assert.That(await Read(integration, "system_disk_3_name"), Is.EqualTo("D8"));
		});
	}

	[Test]
	public async Task Refreshing_audio_devices_and_disks_keeps_both_sets_of_variables()
	{
		var volume = new FakeVolumeService();
		volume.Devices.Add(new AudioDevice("speakers-uid", "Speakers", AudioFlow.Output, true));
		var (integration, metrics, _) = Create(volume);
		metrics.Disks.Add(_system);

		await integration.RefreshDisksAsync();
		await integration.RefreshAudioDevicesAsync();
		volume.Devices.Add(new AudioDevice("headset-uid", "Headset", AudioFlow.Output, false));
		await integration.RefreshAudioDevicesAsync();
		metrics.Disks.Add(_stick);
		await integration.RefreshDisksAsync();

		var names = integration.Variables.Select(v => v.Name).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("system_disk_0_name").And.Contain("system_disk_1_name"));
			Assert.That(names, Does.Contain("system_audio_output_speakers_volume_percent")
				.And.Contain("system_audio_output_headset_volume_percent"));
			Assert.That(names, Does.Contain("system_cpu_usage_percent"));
		});
	}

	[Test]
	public async Task The_largest_possible_surface_fits_the_eager_variable_limit()
	{
		var volume = new FakeVolumeService();
		volume.Devices.AddRange(Enumerable.Range(0, AudioDeviceVariables.MaxKnownDevices)
			.Select(i => new AudioDevice($"device-{i}", $"Device {i}", AudioFlow.Output, i == 0)));
		var (integration, metrics, _) = Create(volume, gpuCount: 64);
		metrics.Disks.AddRange(Enumerable.Range(0, 20).Select(i => Disk($"/Volumes/D{i}", $"D{i}", 10, 5, null)));

		await integration.RefreshAudioDevicesAsync();
		await integration.RefreshDisksAsync();

		Assert.That(integration.Variables.Count(v => v.Materialization == VariableMaterialization.Eager),
			Is.LessThanOrEqualTo(VariableLimits.MaxEagerVariablesPerProvider));
	}

	private sealed class SilentNotifications : INotificationService
	{
		public bool IsSupported => true;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class StaleCounter : IVariablePollingInvalidationSignal
	{
		public int Count { get; private set; }

		public void MarkStale(string integrationId) => Count++;

		public IReadOnlyCollection<string> DrainStale() => [];
	}
}
