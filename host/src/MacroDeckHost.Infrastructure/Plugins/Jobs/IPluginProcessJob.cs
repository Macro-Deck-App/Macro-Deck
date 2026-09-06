namespace MacroDeckHost.Infrastructure.Plugins.Jobs;

public interface IPluginProcessJob : IDisposable
{
	bool IsActive { get; }

	bool TryAssign(int processId);

	bool TryTerminate();
}

internal sealed class NullPluginProcessJob : IPluginProcessJob
{
	public bool IsActive => false;

	public bool TryAssign(int processId) => false;

	public bool TryTerminate() => false;

	public void Dispose()
	{
	}
}
