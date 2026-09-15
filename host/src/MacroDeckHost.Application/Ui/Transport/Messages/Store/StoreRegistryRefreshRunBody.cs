using MacroDeckHost.Application.Store;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreRegistryRefreshRunBody
{
	public Guid HostInstanceId { get; set; }

	public Guid Id { get; set; }

	public long Revision { get; set; }

	public StoreRegistryRefreshTrigger Trigger { get; set; }

	public StoreRegistryRefreshRunState State { get; set; }

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset? FinishedAt { get; set; }

	public int FilesCompleted { get; set; }

	public int FilesTotal { get; set; }

	public List<StoreRegistryRefreshLogEntryBody> Entries { get; set; } = [];
}

public class StoreRegistryRefreshLogEntryBody
{
	public DateTimeOffset At { get; set; }

	public StoreRegistryRefreshStep Step { get; set; }

	public int? Count { get; set; }

	public long? Sequence { get; set; }

	public string? Error { get; set; }

	public string? Detail { get; set; }
}

public class StoreRegistryRefreshChangedEvent
{
	public StoreRegistryRefreshRunBody Run { get; set; } = new();
}
