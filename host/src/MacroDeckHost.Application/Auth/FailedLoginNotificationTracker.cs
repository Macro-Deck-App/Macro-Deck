namespace MacroDeckHost.Application.Auth;

public class FailedLoginNotificationTracker
{
	private readonly object _lock = new();
	private int _count;

	public int RegisterFailure()
	{
		lock (_lock)
		{
			return ++_count;
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			_count = 0;
		}
	}
}
