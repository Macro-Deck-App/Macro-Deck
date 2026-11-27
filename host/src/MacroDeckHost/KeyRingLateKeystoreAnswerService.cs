using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost;

public sealed class KeyRingLateKeystoreAnswerService : BackgroundService
{
	private readonly Task<KeyRingKeystoreReadOutcome>? _lateRead;
	private readonly string _markerPath;
	private readonly bool _alreadyRestarted;
	private readonly IApplicationRestartService _restart;
	private readonly ILogger _logger;

	public KeyRingLateKeystoreAnswerService(
		Task<KeyRingKeystoreReadOutcome>? lateRead,
		string markerPath,
		bool alreadyRestarted,
		IApplicationRestartService restart,
		ILogger logger)
	{
		_lateRead = lateRead;
		_markerPath = markerPath;
		_alreadyRestarted = alreadyRestarted;
		_restart = restart;
		_logger = logger.ForContext<KeyRingLateKeystoreAnswerService>();
	}

	// In the temporary directory rather than the data root, which backups copy wholesale: the marker only
	// has to reach the host the bootstrapper starts right after this one.
	public static string MarkerPath(Guid installationId)
	{
		return Path.Combine(Path.GetTempPath(), $"macro-deck-keystore-answered-late-{installationId:N}");
	}

	public static bool ConsumeMarker(string markerPath)
	{
		if (!File.Exists(markerPath))
		{
			return false;
		}

		try
		{
			File.Delete(markerPath);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}

		return true;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_lateRead is null)
		{
			return;
		}

		KeyRingKeystoreReadOutcome read;
		try
		{
			read = await _lateRead.WaitAsync(stoppingToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (read.Outcome != KeyRingKeystoreRetryOutcome.Recovered)
		{
			_logger.Information("The keystore answered late ({Outcome}: {Reason}) without the key ring's key; "
				+ "staying locked",
				read.Outcome,
				read.Reason ?? "no detail");
			return;
		}

		if (_alreadyRestarted)
		{
			_logger.Warning("The keystore answered late again after the longer wait; restart Macro Deck to "
				+ "unlock the key ring");
			return;
		}

		// A keystore that is merely slow, not waiting on a prompt, would miss the deadline again on every
		// start; the marker gives the next start a longer wait so this restart cannot repeat forever.
		WriteMarker();
		var restart = _restart.Request("key-ring-keystore-answered");
		if (restart.Success)
		{
			_logger.Information("The keystore answered after startup stopped waiting; restarting to unlock");
		}
		else
		{
			_logger.Warning("The keystore answered after startup stopped waiting, but Macro Deck cannot restart "
				+ "itself ({Error}); restart it to unlock the key ring",
				restart.Error);
		}
	}

	private void WriteMarker()
	{
		try
		{
			using var _ = new FileStream(_markerPath, FileMode.CreateNew, FileAccess.Write);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(e, "Could not note that the keystore answered late");
		}
	}
}
