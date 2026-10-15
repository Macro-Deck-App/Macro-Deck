using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Steam.State;
using Serilog;

namespace MacroDeckHost.Integrations.Steam.Native;

internal sealed class SteamGameCloser
{
	public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _killGracePeriod = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);

	private static readonly ILogger _logger = IntegrationLog.For<SteamGameCloser>(SteamIntegration.IntegrationId);

	private readonly ISteamPlatform _platform;
	private readonly TimeProvider _time;
	private readonly TimeSpan _gracePeriod;

	public SteamGameCloser(ISteamPlatform platform, TimeProvider time, TimeSpan? gracePeriod = null)
	{
		_platform = platform;
		_time = time;
		_gracePeriod = gracePeriod ?? DefaultGracePeriod;
	}

	public async Task<SteamCommandOutcome> CloseAsync(
		SteamSnapshot snapshot,
		bool force,
		CancellationToken cancellationToken)
	{
		if (!_platform.CanCloseGames)
		{
			return SteamCommandOutcome.NotAvailable;
		}

		if (snapshot.CurrentGame is not { } game)
		{
			return SteamCommandOutcome.NotRunning;
		}

		var installDirectory
			= snapshot.Games.FirstOrDefault(candidate => candidate.AppId == game.AppId)?.InstallDirectory;
		var processes = _platform.FindGameProcesses(game.AppId, installDirectory);
		if (processes.Count == 0)
		{
			_logger.Information("No process of the running Steam game {AppId} was found", game.AppId);
			return SteamCommandOutcome.Failed;
		}

		var asked = processes.Where(_platform.RequestClose).ToList();
		if (asked.Count > 0 && await WaitAsync(asked, _gracePeriod, cancellationToken))
		{
			return SteamCommandOutcome.Succeeded;
		}

		if (!force)
		{
			return SteamCommandOutcome.Failed;
		}

		foreach (var process in processes.Where(_platform.IsRunning))
		{
			_platform.Kill(process);
		}

		return await WaitAsync(processes, _killGracePeriod, cancellationToken)
			? SteamCommandOutcome.Succeeded
			: SteamCommandOutcome.Failed;
	}

	private async Task<bool> WaitAsync(
		IReadOnlyList<SteamGameProcess> processes,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var deadline = _time.GetUtcNow() + timeout;
		while (true)
		{
			if (!processes.Any(_platform.IsRunning))
			{
				return true;
			}

			if (_time.GetUtcNow() >= deadline)
			{
				return false;
			}

			await Task.Delay(_pollInterval, _time, cancellationToken);
		}
	}
}
