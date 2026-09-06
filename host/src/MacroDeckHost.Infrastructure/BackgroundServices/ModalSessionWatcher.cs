using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Sessions;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Keeps the promise that awaiting a modal cannot hang: cancels a modal whose session has ended for any
/// reason other than the user answering it, and forgets modals nobody could still be waiting on.
/// </summary>
/// <remarks>
/// The session hook covers the ordinary failures - the client disconnecting, the provider faulting, the
/// session being closed from elsewhere. The sweep covers the one case with no session to hang the hook
/// on: a modal a client was told about but never opened.
/// </remarks>
public sealed class ModalSessionWatcher : HostReadyBackgroundService
{
	private static readonly TimeSpan _sweepInterval = TimeSpan.FromMinutes(1);

	private readonly UiSessionRegistry _sessions;
	private readonly IModalInteractionCoordinator _coordinator;

	public ModalSessionWatcher(
		IHostApplicationLifetime lifetime,
		UiSessionRegistry sessions,
		IModalInteractionCoordinator coordinator)
		: base(lifetime)
	{
		_sessions = sessions;
		_coordinator = coordinator;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_sessions.Ended += OnSessionEnded;

		try
		{
			using var timer = new PeriodicTimer(_sweepInterval);

			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				_coordinator.SweepExpired();
			}
		}
		finally
		{
			_sessions.Ended -= OnSessionEnded;
		}
	}

	private void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
		=> _coordinator.CancelForSession(e.Session.SessionId);
}
