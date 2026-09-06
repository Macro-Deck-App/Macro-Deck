namespace MacroDeckHost.Integrations.System.Application;

internal sealed class NullApplicationService : IApplicationService
{
	public bool IsSupported => false;

	public Task LaunchAsync(
		string path,
		string? arguments,
		string? workingDirectory,
		LaunchMode mode,
		bool runAsAdmin,
		CancellationToken cancellationToken = default) => Task.CompletedTask;

	public void OpenFile(string path)
	{
	}

	public void OpenWebsite(string url)
	{
	}

	public void OpenFolder(string path)
	{
	}

	public void Kill(string processName, bool graceful)
	{
	}
}
