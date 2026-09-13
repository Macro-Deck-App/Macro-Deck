using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Connect;

/// <summary>Bridges session transitions onto the UI transport. Clients re-fetch the session themselves,
/// so the notification stays empty.</summary>
public sealed class ConnectSessionNotifier : IHostedService, IDisposable
{
	internal static readonly TimeSpan AvatarRevalidationInterval = TimeSpan.FromHours(1);

	private readonly IConnectSessionService _sessionService;
	private readonly IConnectAvatarCache _avatarCache;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _stopping = new();

	private Task? _revalidationLoop;

	public ConnectSessionNotifier(
		IConnectSessionService sessionService,
		IConnectAvatarCache avatarCache,
		IUiTransport transport,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_sessionService = sessionService;
		_avatarCache = avatarCache;
		_transport = transport;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ConnectSessionNotifier>();
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_sessionService.SessionChanged += OnSessionChanged;
		_avatarCache.VersionChanged += OnAvatarChanged;
		_revalidationLoop = Task.Run(() => RevalidatePeriodically(_stopping.Token), CancellationToken.None);

		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_sessionService.SessionChanged -= OnSessionChanged;
		_avatarCache.VersionChanged -= OnAvatarChanged;
		await _stopping.CancelAsync();

		if (_revalidationLoop is { } loop)
		{
			await loop;
		}
	}

	public void Dispose() => _stopping.Dispose();

	private void OnSessionChanged(object? sender, ConnectSessionSnapshot snapshot)
	{
		_ = _transport.Send(new ConnectSessionChangedNotification());

		// Raised from inside the session's Publish, so the revalidation must never run on this call.
		if (snapshot.Account is not null)
		{
			_ = Task.Run(RevalidateAvatar, CancellationToken.None);
		}
	}

	private void OnAvatarChanged(object? sender, EventArgs e)
		=> _ = _transport.Send(new ConnectSessionChangedNotification());

	private async Task RevalidatePeriodically(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(AvatarRevalidationInterval, _timeProvider, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (_sessionService.Current.Account is not null)
			{
				await RevalidateAvatar();
			}
		}
	}

	private async Task RevalidateAvatar()
	{
		try
		{
			await _avatarCache.Revalidate(_stopping.Token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "The Macro Deck Connect avatar could not be revalidated");
		}
	}
}
