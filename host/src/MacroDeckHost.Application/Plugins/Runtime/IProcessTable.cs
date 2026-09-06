namespace MacroDeckHost.Application.Plugins.Runtime;

public readonly record struct RunningProcess(int ProcessId, DateTimeOffset StartedAt);

public interface IProcessTable
{
	RunningProcess? TryGet(int processId);

	Task KillTree(int processId, CancellationToken cancellationToken = default);
}
