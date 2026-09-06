using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakeApplicationService : IApplicationService
{
	public bool IsSupported { get; init; } = true;

	public List<(string Path, string? Arguments, string? WorkingDirectory, LaunchMode Mode, bool RunAsAdmin)>
		Launches { get; } = [];

	public List<string> OpenedFiles { get; } = [];

	public List<string> OpenedWebsites { get; } = [];

	public List<string> OpenedFolders { get; } = [];

	public List<bool> FolderExistedWhenOpened { get; } = [];

	public Exception? OpenFolderException { get; set; }

	public List<(string Process, bool Graceful)> Kills { get; } = [];

	public Task LaunchAsync(
		string path,
		string? arguments,
		string? workingDirectory,
		LaunchMode mode,
		bool runAsAdmin,
		CancellationToken cancellationToken = default)
	{
		Launches.Add((path, arguments, workingDirectory, mode, runAsAdmin));
		return Task.CompletedTask;
	}

	public void OpenFile(string path) => OpenedFiles.Add(path);

	public void OpenWebsite(string url) => OpenedWebsites.Add(url);

	public void OpenFolder(string path)
	{
		FolderExistedWhenOpened.Add(Directory.Exists(path));

		if (OpenFolderException is not null)
		{
			throw OpenFolderException;
		}

		OpenedFolders.Add(path);
	}

	public void Kill(string processName, bool graceful) => Kills.Add((processName, graceful));
}
