namespace MacroDeckHost.Integrations.System.Application;

public enum LaunchMode
{
	Start,

	StartStop,

	StartFocus
}

public interface IApplicationService
{
	bool IsSupported { get; }

	Task LaunchAsync(
		string path,
		string? arguments,
		string? workingDirectory,
		LaunchMode mode,
		bool runAsAdmin,
		CancellationToken cancellationToken = default);

	void OpenFile(string path);

	void OpenWebsite(string url);

	void OpenFolder(string path);

	void Kill(string processName, bool graceful);
}
