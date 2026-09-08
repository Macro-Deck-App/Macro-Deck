namespace MacroDeckHost.Integrations.System.Metrics;

internal abstract class SystemMetricsServiceBase : ISystemMetricsService, IDisposable
{
	private static readonly TimeSpan _memoryCacheTtl = TimeSpan.FromMilliseconds(900);
	private static readonly TimeSpan _gpuCacheTtl = TimeSpan.FromMilliseconds(2500);

	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private readonly Dictionary<int, string> _lastKnownGpuNames = new();

	private CpuTimes? _previousCpuTimes;
	private MemoryInfo? _cachedMemory;
	private DateTimeOffset? _memoryReadAt;
	private IReadOnlyList<GpuSample> _cachedGpuSnapshot = [];
	private DateTimeOffset? _gpuReadAt;

	protected SystemMetricsServiceBase(TimeProvider? time = null)
	{
		_time = time ?? TimeProvider.System;
	}

	public virtual bool IsSupported => true;

	public abstract int GpuCount { get; }

	public bool IsGpuSupported => GpuCount > 0;

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
			var now = _time.GetUtcNow();
			if (_memoryReadAt is { } readAt && now - readAt < _memoryCacheTtl)
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

	public async Task<double?> GetGpuUsageAsync(int gpuIndex, CancellationToken cancellationToken = default)
		=> (await GetGpuAsync(gpuIndex, cancellationToken))?.UsagePercent;

	public async Task<string?> GetGpuNameAsync(int gpuIndex, CancellationToken cancellationToken = default)
	{
		var sample = await GetGpuAsync(gpuIndex, cancellationToken);
		if (sample is null)
		{
			return null;
		}

		if (sample.Name is { Length: > 0 } name)
		{
			return name;
		}

		// A GPU that momentarily reports no name keeps the last one it had: a single failed read
		// would otherwise blank the GPU name and the history graph's subtitle until the next TTL.
		await _lock.WaitAsync(cancellationToken);
		try
		{
			return _lastKnownGpuNames.GetValueOrDefault(gpuIndex);
		}
		finally
		{
			_lock.Release();
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_lock.Dispose();
		}
	}

	protected abstract Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken);

	protected abstract Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken);

	protected abstract Task<IReadOnlyList<GpuSample>> ReadGpuSnapshotAsync(CancellationToken cancellationToken);

	private async Task<GpuSample?> GetGpuAsync(int gpuIndex, CancellationToken cancellationToken)
	{
		if (gpuIndex < 0 || gpuIndex >= GpuCount)
		{
			return null;
		}

		await _lock.WaitAsync(cancellationToken);
		try
		{
			var now = _time.GetUtcNow();
			if (_gpuReadAt is not { } readAt || now - readAt >= _gpuCacheTtl)
			{
				try
				{
					_cachedGpuSnapshot = await ReadGpuSnapshotAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
					_cachedGpuSnapshot = [];
				}

				_gpuReadAt = now;
				RememberNames(_cachedGpuSnapshot);
			}

			return gpuIndex < _cachedGpuSnapshot.Count ? _cachedGpuSnapshot[gpuIndex] : null;
		}
		finally
		{
			_lock.Release();
		}
	}

	private void RememberNames(IReadOnlyList<GpuSample> snapshot)
	{
		for (var index = 0; index < snapshot.Count; index++)
		{
			if (snapshot[index].Name is { Length: > 0 } name)
			{
				_lastKnownGpuNames[index] = name;
			}
		}
	}
}
