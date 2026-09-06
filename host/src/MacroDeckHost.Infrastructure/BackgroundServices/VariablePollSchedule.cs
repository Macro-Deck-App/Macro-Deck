namespace MacroDeckHost.Infrastructure.BackgroundServices;

internal sealed class VariablePollSchedule
{
	private readonly TimeSpan _interval;
	private DateTime _nextDue = DateTime.MinValue;

	public VariablePollSchedule(TimeSpan interval)
	{
		_interval = interval;
	}

	// Brings the next read forward to the next tick. A write is only ever "applied by the owner", so the
	// value the host shows still comes from a read - without this the control sits on the pre-write
	// reading until the ordinary cadence comes round.
	public void MarkDueNow() => _nextDue = DateTime.MinValue;

	public bool IsDue(DateTime now)
	{
		if (now < _nextDue)
		{
			return false;
		}

		_nextDue += _interval;
		if (_nextDue <= now)
		{
			_nextDue = now + _interval;
		}

		return true;
	}
}
