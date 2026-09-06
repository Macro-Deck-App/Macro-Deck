namespace MacroDeckHost.Integrations.System.Metrics;

internal abstract class SystemMetricsServiceBase : ISystemMetricsService, IDisposable
{
	private static readonly TimeSpan _memoryCacheTtl = TimeSpan.FromMilliseconds(900);
	private static readonly TimeSpan _gpuCacheTtl = TimeSpan.FromMilliseconds(2500);

	private readonly SemaphoreSlim _lock = new(1, 1);

	private CpuTimes? _previousCpuTimes;
	private MemoryInfo? _cachedMemory;
	private long? _memoryReadAt;
	private double? _cachedGpuUsage;
	private long? _gpuReadAt;
	private string? _cachedGpuName;

	public virtual bool IsSupported => true;

	public abstract bool IsGpuSupported { get; }

	public async Task<double?> GetCpuUsageAsync(CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var current = await ReadCpuTimesAsync(cancellationToken);
			if (current is not { } sample)
			{
				_previousCpuTimes = null;
				return null;
			}

			var usage = CpuUsageCalculator.Calculate(_previousCpuTimes, sample);
			_previousCpuTimes = sample;
			return usage;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			_previousCpuTimes = null;
			return null;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<MemoryInfo?> GetMemoryAsync(CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var now = Environment.TickCount64;
			if (_memoryReadAt is { } readAt && now - readAt < _memoryCacheTtl.TotalMilliseconds)
			{
				return _cachedMemory;
			}

			MemoryInfo? memory;
			try
			{
				memory = await ReadMemoryAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				memory = null;
			}

			_cachedMemory = memory;
			_memoryReadAt = now;
			return memory;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<double?> GetGpuUsageAsync(CancellationToken cancellationToken = default)
	{
		if (!IsGpuSupported)
		{
			return null;
		}

		await _lock.WaitAsync(cancellationToken);
		try
		{
			var now = Environment.TickCount64;
			if (_gpuReadAt is { } readAt && now - readAt < _gpuCacheTtl.TotalMilliseconds)
			{
				return _cachedGpuUsage;
			}

			double? usage;
			try
			{
				usage = await ReadGpuUsageAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				usage = null;
			}

			_cachedGpuUsage = usage;
			_gpuReadAt = now;
			return usage;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<string?> GetGpuNameAsync(CancellationToken cancellationToken = default)
	{
		if (!IsGpuSupported)
		{
			return null;
		}

		await _lock.WaitAsync(cancellationToken);
		try
		{
			if (_cachedGpuName is not null)
			{
				return _cachedGpuName;
			}

			try
			{
				_cachedGpuName = await ReadGpuNameAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				_cachedGpuName = null;
			}

			return _cachedGpuName;
		}
		finally
		{
			_lock.Release();
		}
	}

	public void Dispose()
	{
		_lock.Dispose();
		GC.SuppressFinalize(this);
	}

	protected abstract Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken);

	protected abstract Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken);

	protected abstract Task<double?> ReadGpuUsageAsync(CancellationToken cancellationToken);

	protected abstract Task<string?> ReadGpuNameAsync(CancellationToken cancellationToken);
}
