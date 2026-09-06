namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed record PluginProcessJournalEntry
{
	public required string LaunchId { get; init; }

	public required string PluginId { get; init; }

	public required int ProcessId { get; init; }

	public required DateTimeOffset StartedAt { get; init; }
}

public sealed record PluginProcessJournalOwner
{
	public required int ProcessId { get; init; }

	public required DateTimeOffset StartedAt { get; init; }
}

public sealed record PluginProcessJournalSnapshot
{
	public PluginProcessJournalOwner? Owner { get; init; }

	public IReadOnlyList<PluginProcessJournalEntry> Entries { get; init; } = [];
}

public interface IPluginProcessJournal
{
	PluginProcessJournalSnapshot Load();

	Task Record(PluginProcessJournalEntry entry);

	Task Remove(string launchId);

	Task Remove(IReadOnlyCollection<string> launchIds);
}
