using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Connect;

/// <summary>Bridges session transitions onto the UI transport. Clients re-fetch the session themselves,
/// so the notification stays empty.</summary>
public sealed class ConnectSessionNotifier : IHostedService
{
	private readonly IConnectSessionService _sessionService;
	private readonly IUiTransport _transport;

	public ConnectSessionNotifier(IConnectSessionService sessionService, IUiTransport transport)
	{
		_sessionService = sessionService;
		_transport = transport;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_sessionService.SessionChanged += OnSessionChanged;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_sessionService.SessionChanged -= OnSessionChanged;
		return Task.CompletedTask;
	}

	private void OnSessionChanged(object? sender, ConnectSessionSnapshot snapshot)
		=> _ = _transport.Send(new ConnectSessionChangedNotification());
}
