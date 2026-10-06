namespace MacroDeckHost.Integrations.Obs;

internal sealed class ObsCustomEventLimiter
{
	private readonly TimeProvider _time;
	private readonly double _perSecond;
	private readonly double _burst;
	private readonly Lock _gate = new();
	private double _tokens;
	private DateTimeOffset _last;
	private int _dropped;

	public ObsCustomEventLimiter(TimeProvider? time = null, double perSecond = 20, double burst = 50)
	{
		_time = time ?? TimeProvider.System;
		_perSecond = perSecond;
		_burst = burst;
		_tokens = burst;
		_last = _time.GetUtcNow();
	}

	public bool TryAcquire(out int droppedBefore)
	{
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			_tokens = Math.Min(_burst, _tokens + Math.Max(0, (now - _last).TotalSeconds) * _perSecond);
			_last = now;
			if (_tokens < 1)
			{
				_dropped++;
				droppedBefore = 0;
				return false;
			}

			_tokens--;
			droppedBefore = _dropped;
			_dropped = 0;
			return true;
		}
	}
}
