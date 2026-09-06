namespace MacroDeckHost.Application.Connect;

/// <summary>
/// The persisted earliest time a suspended account may be retried at. It has to survive a restart:
/// suspension state itself is in-memory, so without a durable floor a crash loop would re-attempt the
/// token endpoint on every start.
/// </summary>
public interface IConnectSuspensionFloor
{
	Task<DateTimeOffset?> Read(CancellationToken cancellationToken = default);

	Task Write(DateTimeOffset notBefore, CancellationToken cancellationToken = default);
}
