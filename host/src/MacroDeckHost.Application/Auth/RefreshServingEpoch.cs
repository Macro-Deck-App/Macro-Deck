using System.Globalization;

namespace MacroDeckHost.Application.Auth;

// Both values feed AuthService.WithinGrace; see AuthDefaults.RefreshTokenReuseGraceAfterRestart.
public sealed class RefreshServingEpoch
{
	private long _startedTicks;
	private long _previousEndedTicks;

	public DateTime StartedAt => new(Interlocked.Read(ref _startedTicks), DateTimeKind.Utc);

	// MinValue when there is no record, which refuses the post-restart grace outright.
	public DateTime PreviousRotationAt => new(Interlocked.Read(ref _previousEndedTicks), DateTimeKind.Utc);

	// Only the first caller wins, so a second cannot replace the predecessor's record with one this host
	// has already written.
	public bool Begin(DateTime utcNow, DateTime previousEndedAt)
	{
		if (Interlocked.CompareExchange(ref _startedTicks, utcNow.Ticks, 0) != 0)
		{
			return false;
		}

		Interlocked.Exchange(ref _previousEndedTicks, previousEndedAt.Ticks);

		return true;
	}

	public static string Format(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

	// Anything unreadable reads as MinValue, which refuses the post-restart grace rather than widening it.
	public static DateTime Parse(string? value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
			? parsed.ToUniversalTime()
			: DateTime.MinValue;
}
