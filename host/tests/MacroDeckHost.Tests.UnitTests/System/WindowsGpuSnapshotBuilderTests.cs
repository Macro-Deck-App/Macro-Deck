using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class WindowsGpuSnapshotBuilderTests
{
	private static WindowsGpuAdapter Adapter(
		string name,
		uint luidLow,
		ulong dedicated = 4UL * 1024 * 1024 * 1024,
		ulong shared = 8UL * 1024 * 1024 * 1024,
		bool software = false)
		=> new(name, 0, luidLow, dedicated, shared, software);

	private static string Instance(uint luidLow, int engine, string engineType, int pid = 1234)
		=> $"pid_{pid}_luid_0x00000000_0x{luidLow:X8}_phys_0_eng_{engine}_engtype_{engineType}";

	private static CounterReadResult.Instances Counters(params CounterEntry[] entries) => new(entries);

	[Test]
	public void Discrete_gpus_come_first()
	{
		var integrated = Adapter("Integrated", 0x1, dedicated: 1024 * 1024 * 1024);
		var discrete = Adapter("Discrete", 0x2, dedicated: 4UL * 1024 * 1024 * 1024);

		var surviving = WindowsGpuSnapshotBuilder.Surviving([integrated, discrete]);

		Assert.That(surviving.Select(a => a.Name), Is.EqualTo(new[] { "Discrete", "Integrated" }));
	}

	[Test]
	public void Software_and_memoryless_adapters_are_not_gpus()
	{
		var real = Adapter("Radeon", 0x1);
		var software = Adapter("Microsoft Basic Render Driver", 0x2, software: true);
		var virtualDisplay = Adapter("Remote Display Adapter", 0x3, dedicated: 0, shared: 0);

		var surviving = WindowsGpuSnapshotBuilder.Surviving([real, software, virtualDisplay]);

		Assert.That(surviving.Select(a => a.Name), Is.EqualTo(new[] { "Radeon" }));
	}

	[Test]
	public void Engine_instances_of_one_adapter_are_combined_by_maximum()
	{
		var adapter = Adapter("Radeon", 0xAB);

		var samples = WindowsGpuSnapshotBuilder.Build(
			[adapter],
			Counters(
				new CounterEntry(Instance(0xAB, 0, "3D"), 40),
				new CounterEntry(Instance(0xAB, 0, "VideoDecode"), 30)),
			null,
			out _);

		Assert.That(samples[0].UsagePercent, Is.EqualTo(40));
	}

	[Test]
	public void Two_engines_of_the_same_type_do_not_add_up()
	{
		var adapter = Adapter("Radeon", 0xAB);

		var samples = WindowsGpuSnapshotBuilder.Build(
			[adapter],
			Counters(
				new CounterEntry(Instance(0xAB, 0, "Copy"), 60),
				new CounterEntry(Instance(0xAB, 1, "Copy"), 50)),
			null,
			out _);

		Assert.That(samples[0].UsagePercent, Is.EqualTo(60));
	}

	[Test]
	public void Processes_on_one_engine_add_up_and_clamp()
	{
		var adapter = Adapter("Radeon", 0xAB);

		var samples = WindowsGpuSnapshotBuilder.Build(
			[adapter],
			Counters(
				new CounterEntry(Instance(0xAB, 0, "3D", pid: 1), 70),
				new CounterEntry(Instance(0xAB, 0, "3D", pid: 2), 60)),
			null,
			out _);

		Assert.That(samples[0].UsagePercent, Is.EqualTo(100));
	}

	[Test]
	public void Each_adapter_gets_its_own_value()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Discrete", 0xAB), Adapter("Integrated", 0xCD, dedicated: 1)],
			Counters(
				new CounterEntry(Instance(0xAB, 0, "3D"), 80),
				new CounterEntry(Instance(0xCD, 0, "3D"), 20)),
			null,
			out var join);

		Assert.Multiple(() =>
		{
			Assert.That(join, Is.EqualTo(WindowsGpuSnapshotBuilder.LuidJoin.Matched));
			Assert.That(samples[0], Is.EqualTo(new GpuSample("Discrete", 80)));
			Assert.That(samples[1], Is.EqualTo(new GpuSample("Integrated", 20)));
		});
	}

	[Test]
	public void An_adapter_without_instances_among_busy_ones_reads_zero()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Discrete", 0xAB), Adapter("Integrated", 0xCD, dedicated: 1)],
			Counters(new CounterEntry(Instance(0xCD, 0, "3D"), 20)),
			null,
			out _);

		Assert.That(samples[0], Is.EqualTo(new GpuSample("Discrete", 0)));
	}

	[Test]
	public void Malformed_instance_names_are_ignored()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Radeon", 0xAB)],
			Counters(
				new CounterEntry("not an instance name", 99),
				new CounterEntry("pid_1_luid_zz_zz_phys_0_eng_0_engtype_3D", 99),
				new CounterEntry(Instance(0xAB, 0, "3D"), 25)),
			null,
			out _);

		Assert.That(samples[0].UsagePercent, Is.EqualTo(25));
	}

	[Test]
	public void A_luid_that_matches_nothing_still_reads_a_single_gpu()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Radeon", 0xAB)],
			Counters(new CounterEntry(Instance(0xFFFF, 0, "3D"), 55)),
			null,
			out var join);

		Assert.Multiple(() =>
		{
			Assert.That(join, Is.EqualTo(WindowsGpuSnapshotBuilder.LuidJoin.NoMatch));
			Assert.That(samples[0], Is.EqualTo(new GpuSample("Radeon", 55)));
		});
	}

	[Test]
	public void A_luid_that_matches_nothing_leaves_several_adapters_unavailable()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Discrete", 0xAB), Adapter("Integrated", 0xCD, dedicated: 1)],
			Counters(new CounterEntry(Instance(0xFFFF, 0, "3D"), 55)),
			null,
			out _);

		Assert.That(samples, Is.EqualTo(new[]
		{
			new GpuSample("Discrete", null),
			new GpuSample("Integrated", null)
		}));
	}

	[Test]
	public void A_failed_counter_read_keeps_the_adapter_names()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Radeon", 0xAB)],
			new CounterReadResult.Failed(),
			null,
			out _);

		Assert.That(samples[0], Is.EqualTo(new GpuSample("Radeon", null)));
	}

	[Test]
	public void The_first_collect_reports_no_usage_yet()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Radeon", 0xAB)],
			new CounterReadResult.FirstCollect(),
			null,
			out _);

		Assert.That(samples[0], Is.EqualTo(new GpuSample("Radeon", null)));
	}

	[Test]
	public void The_first_collect_never_reaches_nvidia_smi()
	{
		Assert.That(
			WindowsGpuSnapshotBuilder.NeedsNvidiaFallback(
				[Adapter("GeForce", 0xAB)], new CounterReadResult.FirstCollect(), hasNvidiaSmi: true),
			Is.False);
	}

	[Test]
	public void A_failed_counter_read_reaches_nvidia_smi()
	{
		Assert.That(
			WindowsGpuSnapshotBuilder.NeedsNvidiaFallback(
				[Adapter("GeForce", 0xAB)], new CounterReadResult.Failed(), hasNvidiaSmi: true),
			Is.True);
	}

	[Test]
	public void An_empty_counter_array_reaches_nvidia_smi()
	{
		Assert.That(
			WindowsGpuSnapshotBuilder.NeedsNvidiaFallback([Adapter("GeForce", 0xAB)], Counters(), hasNvidiaSmi: true),
			Is.True);
	}

	[Test]
	public void An_nvidia_card_that_dxgi_does_not_list_reaches_nvidia_smi()
	{
		Assert.That(
			WindowsGpuSnapshotBuilder.NeedsNvidiaFallback(
				[], Counters(new CounterEntry(Instance(0xAB, 0, "3D"), 10)), hasNvidiaSmi: true),
			Is.True);
	}

	[Test]
	public void Nothing_is_spawned_when_nvidia_smi_is_not_installed()
	{
		Assert.That(
			WindowsGpuSnapshotBuilder.NeedsNvidiaFallback(
				[Adapter("Radeon", 0xAB)], new CounterReadResult.Failed(), hasNvidiaSmi: false),
			Is.False);
	}

	[Test]
	public void The_nvidia_fallback_supplies_usage_without_moving_names_or_changing_the_count()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("NVIDIA GeForce RTX 4070", 0xAB), Adapter("Intel UHD Graphics", 0xCD, dedicated: 1)],
			new CounterReadResult.Failed(),
			"0, NVIDIA GeForce RTX 4070, 42\n",
			out _);

		Assert.That(samples, Is.EqualTo(new[]
		{
			new GpuSample("NVIDIA GeForce RTX 4070", 42),
			new GpuSample("Intel UHD Graphics", null)
		}));
	}

	[Test]
	public void The_nvidia_fallback_is_the_whole_snapshot_when_dxgi_found_nothing()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[],
			new CounterReadResult.Failed(),
			"0, NVIDIA GeForce RTX 4070, 42\n",
			out _);

		Assert.That(samples, Is.EqualTo(new[] { new GpuSample("NVIDIA GeForce RTX 4070", 42) }));
	}

	[Test]
	public void Instances_that_cannot_be_parsed_at_all_leave_a_single_gpu_unavailable()
	{
		var samples = WindowsGpuSnapshotBuilder.Build(
			[Adapter("Radeon", 0xAB)],
			Counters(new CounterEntry("not an instance name", 99)),
			null,
			out _);

		Assert.That(samples[0], Is.EqualTo(new GpuSample("Radeon", null)));
	}
}
