using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Sessions;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using ILogger = Serilog.ILogger;

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
	private readonly ILogger _logger;

	public ModalSessionWatcher(
		IHostApplicationLifetime lifetime,
		UiSessionRegistry sessions,
		IModalInteractionCoordinator coordinator,
		ILogger logger)
		: base(lifetime)
	{
		_sessions = sessions;
		_coordinator = coordinator;
		_logger = logger.ForContext<ModalSessionWatcher>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_sessions.Ended += OnSessionEnded;

		try
		{
			using var timer = new PeriodicTimer(_sweepInterval);

			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				foreach (var modal in _coordinator.SweepExpired())
				{
					_logger.Information(
						"Cancelled modal {ModalId} of integration {IntegrationId} (view {ViewId}): it expired unanswered",
						modal.ModalId,
						modal.IntegrationId,
						modal.ViewId);
				}
			}
		}
		finally
		{
			_sessions.Ended -= OnSessionEnded;
		}
	}

	internal void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
	{
		// A client closes its dialog session before it settles the modal, so a plain close is routine.
		var level = e.Reason == UiSessionEndReason.Closed ? LogEventLevel.Debug : LogEventLevel.Information;

		foreach (var modal in _coordinator.CancelForSession(e.Session.SessionId))
		{
			_logger.Write(level,
				"Cancelled modal {ModalId} of integration {IntegrationId} (view {ViewId}): its session {SessionId} " +
				"ended ({Reason}, code {Code}, retryable {Retryable})",
				modal.ModalId,
				modal.IntegrationId,
				modal.ViewId,
				e.Session.SessionId,
				e.Reason,
				e.Code,
				e.Retryable);
		}
	}
}
