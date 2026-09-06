namespace MacroDeck.Sdk.Logging;

/// <summary>How a just-recorded failure should be logged.</summary>
public enum FailureEpisodeSignalKind
{
	/// <summary>First failure after a healthy stretch - log it loudly, with the exception attached.</summary>
	Onset,

	/// <summary>The periodic summary is due - log the episode's duration and counters.</summary>
	SummaryDue,

	/// <summary>Between summaries - keep the per-attempt noise at debug level.</summary>
	Quiet
}

/// <summary>A recorded failure plus the episode context the log line needs.</summary>
public sealed record FailureEpisodeSignal(
	FailureEpisodeSignalKind Kind,
	DateTimeOffset StartedAt,
	TimeSpan Duration,
	int ConsecutiveFailures,
	string LastError);

/// <summary>A finished episode, returned by <see cref="FailureEpisodeTracker.RecordSuccess"/>.</summary>
public sealed record FailureEpisodeEnd(DateTimeOffset StartedAt, TimeSpan Duration, int Failures);

/// <summary>
/// Turns a stream of per-attempt failures into loggable episodes - one onset, a summary per interval,
/// one recovery - so a poll loop failing every second or two stays diagnosable in a log that must not
/// receive a line per tick. Owns only the counting and timing; callers own the message templates.
/// </summary>
/// <remarks>
/// There is no timer: summaries fire from the recorded attempts themselves, so a loop that stops
/// polling also stops summarising (whatever supervises the loop is expected to log that separately).
/// </remarks>
public sealed class FailureEpisodeTracker
{
	private static readonly TimeSpan _defaultSummaryInterval = TimeSpan.FromMinutes(5);

	private readonly Lock _sync = new();
	private readonly TimeProvider _time;
	private readonly TimeSpan _summaryInterval;

	private DateTimeOffset? _startedAt;
	private DateTimeOffset _nextSummaryDue;
	private int _failures;

	public FailureEpisodeTracker(TimeSpan? summaryInterval = null, TimeProvider? time = null)
	{
		_summaryInterval = summaryInterval ?? _defaultSummaryInterval;
		_time = time ?? TimeProvider.System;
	}

	/// <summary>Whether a failure episode is currently open.</summary>
	public bool IsActive
	{
		get
		{
			lock (_sync)
			{
				return _startedAt is not null;
			}
		}
	}

	/// <summary>When the current episode began, or null outside an episode.</summary>
	public DateTimeOffset? StartedAt
	{
		get
		{
			lock (_sync)
			{
				return _startedAt;
			}
		}
	}

	/// <summary>Records one failed attempt and says how it should be logged.</summary>
	public FailureEpisodeSignal RecordFailure(string lastError)
	{
		var now = _time.GetUtcNow();
		lock (_sync)
		{
			_failures++;
			if (_startedAt is not { } startedAt)
			{
				_startedAt = now;
				_nextSummaryDue = now + _summaryInterval;
				return new FailureEpisodeSignal(FailureEpisodeSignalKind.Onset,
					now,
					TimeSpan.Zero,
					_failures,
					lastError);
			}

			if (now >= _nextSummaryDue)
			{
				_nextSummaryDue = now + _summaryInterval;
				return new FailureEpisodeSignal(FailureEpisodeSignalKind.SummaryDue,
					startedAt,
					now - startedAt,
					_failures,
					lastError);
			}

			return new FailureEpisodeSignal(FailureEpisodeSignalKind.Quiet,
				startedAt,
				now - startedAt,
				_failures,
				lastError);
		}
	}

	/// <summary>
	/// Records a successful attempt. Returns the episode it closed - the caller's cue for a recovery
	/// log line - or null when nothing was failing.
	/// </summary>
	public FailureEpisodeEnd? RecordSuccess()
	{
		lock (_sync)
		{
			if (_startedAt is not { } startedAt)
			{
				return null;
			}

			var end = new FailureEpisodeEnd(startedAt, _time.GetUtcNow() - startedAt, _failures);
			_startedAt = null;
			_failures = 0;
			return end;
		}
	}
}
