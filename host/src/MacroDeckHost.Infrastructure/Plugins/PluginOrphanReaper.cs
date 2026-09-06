using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginOrphanReaper : IPluginOrphanReaper
{
	// Process.StartTime is reconstructed from /proc on Linux and is not bit stable between reads, so
	// the owner and entry comparisons below need slack rather than equality.
	private static readonly TimeSpan _startTimeTolerance = TimeSpan.FromSeconds(2);

	private readonly IPluginProcessJournal _journal;
	private readonly IProcessTable _processTable;
	private readonly ILogger _logger;

	public PluginOrphanReaper(IPluginProcessJournal journal, IProcessTable processTable, ILogger logger)
	{
		_journal = journal;
		_processTable = processTable;
		_logger = logger.ForContext<PluginOrphanReaper>();
	}

	public async Task ReapAsync(CancellationToken cancellationToken = default)
	{
		var snapshot = _journal.Load();
		if (snapshot.Entries.Count == 0)
		{
			return;
		}

		if (snapshot.Owner is { } owner && IsSameProcess(owner.ProcessId, owner.StartedAt))
		{
			PluginInfrastructureLog.OrphanSweepSkipped(_logger, owner.ProcessId);
			return;
		}

		var swept = new List<string>(snapshot.Entries.Count);

		foreach (var entry in snapshot.Entries)
		{
			swept.Add(entry.LaunchId);

			var running = _processTable.TryGet(entry.ProcessId);
			if (running is null)
			{
				continue;
			}

			if (!WithinTolerance(running.Value.StartedAt, entry.StartedAt))
			{
				PluginInfrastructureLog.OrphanPidReused(_logger, entry.PluginId, entry.ProcessId);
				continue;
			}

			try
			{
				await _processTable.KillTree(entry.ProcessId, cancellationToken);
				PluginInfrastructureLog.OrphanKilled(_logger, entry.PluginId, entry.ProcessId);
			}
			catch (Exception ex)
			{
				PluginInfrastructureLog.OrphanKillFailed(_logger, entry.PluginId, entry.ProcessId, ex);
			}
		}

		await _journal.Remove(swept);
	}

	private bool IsSameProcess(int processId, DateTimeOffset startedAt)
	{
		var running = _processTable.TryGet(processId);
		return running is not null && WithinTolerance(running.Value.StartedAt, startedAt);
	}

	private static bool WithinTolerance(DateTimeOffset left, DateTimeOffset right)
		=> (left - right).Duration() <= _startTimeTolerance;
}
