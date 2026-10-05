namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal enum YouTubeQuotaLevel
{
	Normal,

	Throttled,

	Paused
}

internal sealed class YouTubeQuotaBudget
{
	public const int DefaultDailyLimit = 10_000;

	private const int ThrottlePercent = 80;
	private const int PausePercent = 95;

	private static readonly TimeZoneInfo _pacific = ResolvePacific();

	private readonly TimeProvider _time;
	private readonly Lock _sync = new();

	private int _limit;
	private int _spent;
	private bool _exhausted;
	private DateOnly _day;

	public YouTubeQuotaBudget(int dailyLimit = DefaultDailyLimit, TimeProvider? timeProvider = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dailyLimit);

		_limit = dailyLimit;
		_time = timeProvider ?? TimeProvider.System;
		_day = PacificDay(_time.GetUtcNow());
	}

	public int Limit
	{
		get
		{
			lock (_sync)
			{
				return _limit;
			}
		}
	}

	public int Spent
	{
		get
		{
			lock (_sync)
			{
				RollOver();
				return _spent;
			}
		}
	}

	public int Remaining
	{
		get
		{
			lock (_sync)
			{
				RollOver();
				return _exhausted ? 0 : Math.Max(0, _limit - _spent);
			}
		}
	}

	public bool IsExhausted
	{
		get
		{
			lock (_sync)
			{
				RollOver();
				return _exhausted;
			}
		}
	}

	public YouTubeQuotaLevel Level
	{
		get
		{
			lock (_sync)
			{
				RollOver();

				if (_exhausted || (long)_spent * 100 >= (long)_limit * PausePercent)
				{
					return YouTubeQuotaLevel.Paused;
				}

				return (long)_spent * 100 >= (long)_limit * ThrottlePercent
					? YouTubeQuotaLevel.Throttled
					: YouTubeQuotaLevel.Normal;
			}
		}
	}

	public DateTimeOffset ResetsAt
	{
		get
		{
			lock (_sync)
			{
				RollOver();
				return NextPacificMidnight(_day);
			}
		}
	}

	public void Charge(int units)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(units);

		lock (_sync)
		{
			RollOver();
			_spent = (int)Math.Min(int.MaxValue, (long)_spent + units);
		}
	}

	public void MarkExhausted()
	{
		lock (_sync)
		{
			RollOver();
			_exhausted = true;
		}
	}

	internal void SetLimit(int dailyLimit)
	{
		lock (_sync)
		{
			_limit = dailyLimit;
		}
	}

	private void RollOver()
	{
		var today = PacificDay(_time.GetUtcNow());
		if (today == _day)
		{
			return;
		}

		_day = today;
		_spent = 0;
		_exhausted = false;
	}

	private static DateOnly PacificDay(DateTimeOffset utcNow)
		=> DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, _pacific).DateTime);

	// Pacific DST switches at 02:00 local, so local midnight always exists exactly once.
	private static DateTimeOffset NextPacificMidnight(DateOnly day)
	{
		var midnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
		return new DateTimeOffset(midnight, _pacific.GetUtcOffset(midnight));
	}

	private static TimeZoneInfo ResolvePacific()
	{
		foreach (var id in (string[])["America/Los_Angeles", "Pacific Standard Time"])
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (TimeZoneNotFoundException)
			{
			}
			catch (InvalidTimeZoneException)
			{
			}
		}

		// Google resets the quota at Pacific midnight; without zone data, PST is the closest fixed offset.
		return TimeZoneInfo.CreateCustomTimeZone("Pacific Standard Time", TimeSpan.FromHours(-8), "Pacific", "Pacific");
	}
}
