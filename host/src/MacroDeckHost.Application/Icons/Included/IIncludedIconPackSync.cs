namespace MacroDeckHost.Application.Icons.Included;

public interface IIncludedIconPackSync
{
	Task SyncAsync(CancellationToken cancellationToken);
}
