using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemMetricsServiceBaseTests
{
	[Test]
	public async Task First_memory_and_gpu_reads_hit_the_reader_and_get_cached()
	{
		var service = new CountingMetricsService
		{
			Memory = new MemoryInfo(100, 40),
			Snapshot = [new GpuSample("Test GPU", 33.0)]
		};

		var firstMemory = await service.GetMemoryAsync();
		var secondMemory = await service.GetMemoryAsync();
		var firstGpu = await service.GetGpuUsageAsync(0);
		var secondGpu = await service.GetGpuUsageAsync(0);

		Assert.Multiple(() =>
		{
			Assert.That(firstMemory, Is.EqualTo(service.Memory));
			Assert.That(secondMemory, Is.EqualTo(service.Memory));
			Assert.That(firstGpu, Is.EqualTo(33.0));
			Assert.That(secondGpu, Is.EqualTo(33.0));
			Assert.That(service.MemoryReads, Is.EqualTo(1));
			Assert.That(service.GpuReads, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task One_snapshot_answers_every_gpu_and_both_readings()
	{
		var service = new CountingMetricsService
		{
			GpuCountValue = 2,
			Snapshot = [new GpuSample("First", 10.0), new GpuSample("Second", 70.0)]
		};

		var firstUsage = await service.GetGpuUsageAsync(0);
		var secondUsage = await service.GetGpuUsageAsync(1);
		var firstName = await service.GetGpuNameAsync(0);
		var secondName = await service.GetGpuNameAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(firstUsage, Is.EqualTo(10.0));
			Assert.That(secondUsage, Is.EqualTo(70.0));
			Assert.That(firstName, Is.EqualTo("First"));
			Assert.That(secondName, Is.EqualTo("Second"));
			Assert.That(service.GpuReads, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Cpu_usage_needs_two_samples_for_a_delta()
	{
		var service = new CountingMetricsService();
		service.CpuSamples.Enqueue(new CpuTimes(100, 200));
		service.CpuSamples.Enqueue(new CpuTimes(150, 300));

		var first = await service.GetCpuUsageAsync();
		var second = await service.GetCpuUsageAsync();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Null);
			Assert.That(second, Is.EqualTo(50.0));
		});
	}

	[Test]
	public async Task Reader_exceptions_surface_as_null()
	{
		var service = new CountingMetricsService { ThrowOnRead = true };
		service.CpuSamples.Enqueue(new CpuTimes(100, 200));

		var cpu = await service.GetCpuUsageAsync();
		var memory = await service.GetMemoryAsync();
		var gpu = await service.GetGpuUsageAsync(0);

		Assert.Multiple(() =>
		{
			Assert.That(cpu, Is.Null);
			Assert.That(memory, Is.Null);
			Assert.That(gpu, Is.Null);
		});
	}

	[Test]
	public async Task Gpu_read_is_skipped_when_no_gpu_was_detected()
	{
		var service = new CountingMetricsService
		{
			GpuCountValue = 0,
			Snapshot = [new GpuSample("Test GPU", 33.0)]
		};

		Assert.That(service.IsGpuSupported, Is.False);
		Assert.That(await service.GetGpuUsageAsync(0), Is.Null);
		Assert.That(await service.GetGpuNameAsync(0), Is.Null);
		Assert.That(service.GpuReads, Is.Zero);
	}

	[Test]
	public async Task Reads_outside_the_detected_gpu_range_are_unavailable()
	{
		var service = new CountingMetricsService { Snapshot = [new GpuSample("Test GPU", 33.0)] };

		Assert.Multiple(async () =>
		{
			Assert.That(await service.GetGpuUsageAsync(1), Is.Null);
			Assert.That(await service.GetGpuNameAsync(1), Is.Null);
			Assert.That(await service.GetGpuUsageAsync(-1), Is.Null);
		});
	}

	[Test]
	public async Task Gpu_name_survives_a_snapshot_that_lost_it()
	{
		var clock = new TestClock();
		var service = new CountingMetricsService(clock) { Snapshot = [new GpuSample("Test GPU", 10.0)] };

		var named = await service.GetGpuNameAsync(0);
		service.Snapshot = [new GpuSample(null, 20.0)];
		clock.Advance(TimeSpan.FromSeconds(3));

		Assert.Multiple(async () =>
		{
			Assert.That(named, Is.EqualTo("Test GPU"));
			Assert.That(await service.GetGpuNameAsync(0), Is.EqualTo("Test GPU"));
			Assert.That(await service.GetGpuUsageAsync(0), Is.EqualTo(20.0));
		});
	}

	[Test]
	public async Task The_snapshot_is_read_again_once_its_lifetime_has_passed()
	{
		var clock = new TestClock();
		var service = new CountingMetricsService(clock) { Snapshot = [new GpuSample("Test GPU", 10.0)] };

		await service.GetGpuUsageAsync(0);
		clock.Advance(TimeSpan.FromSeconds(3));
		var second = await service.GetGpuUsageAsync(0);

		Assert.That(second, Is.EqualTo(10.0));
		Assert.That(service.GpuReads, Is.EqualTo(2));
	}

	[Test]
	public async Task Gpu_name_is_null_until_one_arrives()
	{
		var service = new CountingMetricsService { Snapshot = [new GpuSample(null, 10.0)] };

		Assert.That(await service.GetGpuNameAsync(0), Is.Null);
	}

	private sealed class TestClock : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan by) => _now += by;
	}

	private sealed class CountingMetricsService : SystemMetricsServiceBase
	{
		public CountingMetricsService(TimeProvider? time = null)
			: base(time)
		{
		}

		public Queue<CpuTimes> CpuSamples { get; } = new();

		public MemoryInfo? Memory { get; init; }

		public IReadOnlyList<GpuSample> Snapshot { get; set; } = [];

		public int GpuCountValue { get; init; } = 1;

		public bool ThrowOnRead { get; init; }

		public int MemoryReads { get; private set; }

		public int GpuReads { get; private set; }

		public override int GpuCount => GpuCountValue;

		protected override Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnRead)
			{
				throw new InvalidOperationException("read failed");
			}

			return Task.FromResult<CpuTimes?>(CpuSamples.Count > 0 ? CpuSamples.Dequeue() : null);
		}

		protected override Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnRead)
			{
				throw new InvalidOperationException("read failed");
			}

			MemoryReads++;
			return Task.FromResult(Memory);
		}

		protected override Task<IReadOnlyList<GpuSample>> ReadGpuSnapshotAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnRead)
			{
				throw new InvalidOperationException("read failed");
			}

			GpuReads++;
			return Task.FromResult(Snapshot);
		}
	}
}
