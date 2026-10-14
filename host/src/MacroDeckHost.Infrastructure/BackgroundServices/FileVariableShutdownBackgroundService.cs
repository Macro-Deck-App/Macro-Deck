using MacroDeckHost.Application.Variables.Files;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class FileVariableShutdownBackgroundService : IHostedService
{
	private static readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(2);

	private readonly FileVariableSynchronizer _files;

	public FileVariableShutdownBackgroundService(FileVariableSynchronizer files)
	{
		_files = files;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _files.DrainAsync(_drainTimeout);
		_files.Dispose();
	}
}
