namespace MacroDeckHost.Application.Plugins.Runtime;

public interface IPluginProcess : IDisposable
{
	int Id { get; }

	DateTimeOffset StartedAt { get; }

	bool HasExited { get; }

	int? ExitCode { get; }

	Task<int> Exited { get; }

	IReadOnlyList<string> BootstrapOutput { get; }

	Task KillTree(CancellationToken cancellationToken = default);
}
