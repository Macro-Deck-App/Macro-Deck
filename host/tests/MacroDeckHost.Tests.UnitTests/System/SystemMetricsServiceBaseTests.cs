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
			GpuUsage = 33.0
		};

		var firstMemory = await service.GetMemoryAsync();
		var secondMemory = await service.GetMemoryAsync();
		var firstGpu = await service.GetGpuUsageAsync();
		var secondGpu = await service.GetGpuUsageAsync();

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
		var gpu = await service.GetGpuUsageAsync();

		Assert.Multiple(() =>
		{
			Assert.That(cpu, Is.Null);
			Assert.That(memory, Is.Null);
			Assert.That(gpu, Is.Null);
		});
	}

	[Test]
	public async Task Gpu_read_is_skipped_when_unsupported()
	{
		var service = new CountingMetricsService { GpuSupported = false, GpuUsage = 33.0 };

		Assert.That(await service.GetGpuUsageAsync(), Is.Null);
		Assert.That(service.GpuReads, Is.Zero);
	}

	[Test]
	public async Task Gpu_name_is_read_once_and_cached()
	{
		var service = new CountingMetricsService { GpuName = "Test GPU" };

		var first = await service.GetGpuNameAsync();
		var second = await service.GetGpuNameAsync();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo("Test GPU"));
			Assert.That(second, Is.EqualTo("Test GPU"));
			Assert.That(service.GpuNameReads, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Gpu_name_is_retried_while_null()
	{
		var service = new CountingMetricsService { GpuName = null };

		await service.GetGpuNameAsync();
		await service.GetGpuNameAsync();

		Assert.That(service.GpuNameReads, Is.EqualTo(2));
	}

	[Test]
	public async Task Gpu_name_read_is_skipped_when_unsupported()
	{
		var service = new CountingMetricsService { GpuSupported = false, GpuName = "Test GPU" };

		Assert.That(await service.GetGpuNameAsync(), Is.Null);
		Assert.That(service.GpuNameReads, Is.Zero);
	}

	private sealed class CountingMetricsService : SystemMetricsServiceBase
	{
		public Queue<CpuTimes> CpuSamples { get; } = new();

		public MemoryInfo? Memory { get; init; }

		public double? GpuUsage { get; init; }

		public string? GpuName { get; init; }

		public bool GpuSupported { get; init; } = true;

		public bool ThrowOnRead { get; init; }

		public int MemoryReads { get; private set; }

		public int GpuReads { get; private set; }

		public int GpuNameReads { get; private set; }

		public override bool IsGpuSupported => GpuSupported;

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

		protected override Task<double?> ReadGpuUsageAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnRead)
			{
				throw new InvalidOperationException("read failed");
			}

			GpuReads++;
			return Task.FromResult(GpuUsage);
		}

		protected override Task<string?> ReadGpuNameAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnRead)
			{
				throw new InvalidOperationException("read failed");
			}

			GpuNameReads++;
			return Task.FromResult(GpuName);
		}
	}
}
