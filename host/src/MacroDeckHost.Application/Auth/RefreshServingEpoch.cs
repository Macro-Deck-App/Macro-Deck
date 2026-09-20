namespace MacroDeckHost.Application.Auth;

// Set once per process, before the first request: the rotation grace measures from the later of this and
// the rotation, so time the host spent down or locked does not spend a client's one retry.
public sealed class RefreshServingEpoch
{
	private long _ticks;

	public DateTime StartedAt => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

	public void Begin(DateTime utcNow) => Interlocked.Exchange(ref _ticks, utcNow.Ticks);
}
