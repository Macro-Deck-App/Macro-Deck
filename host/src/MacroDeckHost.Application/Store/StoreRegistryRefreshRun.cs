using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreRegistryRefreshTrigger
{
	Manual,
	Scheduled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreRegistryRefreshRunState
{
	Running,
	Succeeded,
	Failed,
	Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreRegistryRefreshStep
{
	Started,
	FetchingManifest,
	FetchingSignature,
	UpToDate,
	DownloadingFiles,
	Verifying,
	ReadingCatalog,
	Applied,
	Failed,
	Cancelled
}

public sealed record StoreRegistryRefreshLogEntry
{
	public DateTimeOffset At { get; init; }

	public StoreRegistryRefreshStep Step { get; init; }

	public int? Count { get; init; }

	public long? Sequence { get; init; }

	public RegistryRefreshError? Error { get; init; }

	public string? Detail { get; init; }
}

public sealed record StoreRegistryRefreshRun
{
	public Guid HostInstanceId { get; init; }

	public Guid Id { get; init; }

	public long Revision { get; init; }

	public StoreRegistryRefreshTrigger Trigger { get; init; }

	public StoreRegistryRefreshRunState State { get; init; }

	public DateTimeOffset StartedAt { get; init; }

	public DateTimeOffset? FinishedAt { get; init; }

	public int FilesCompleted { get; init; }

	public int FilesTotal { get; init; }

	public IReadOnlyList<StoreRegistryRefreshLogEntry> Entries { get; init; } = [];

	public StoreRegistryStatus? Status { get; init; }

	public bool IsTerminal => State is not StoreRegistryRefreshRunState.Running;
}
