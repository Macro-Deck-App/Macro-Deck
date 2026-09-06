using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Transport;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Ui.Modals;

/// <summary>
/// Builds the modal surface handed to one running action. Per-integration rather than a singleton,
/// because a modal is served by the calling integration's own UI provider - the id has to be bound at the
/// call site, not injected once.
/// </summary>
public interface IUiInteractionsFactory
{
	IUiInteractions ForIntegration(string integrationId);
}

public sealed class UiInteractionsFactory : IUiInteractionsFactory
{
	private readonly IModalInteractionCoordinator _coordinator;
	private readonly IUiTransport _transport;
	private readonly ILogger _logger;

	public UiInteractionsFactory(
		IModalInteractionCoordinator coordinator,
		IUiTransport transport,
		ILogger logger)
	{
		_coordinator = coordinator;
		_transport = transport;
		_logger = logger;
	}

	public IUiInteractions ForIntegration(string integrationId)
		=> new UiInteractions(_coordinator, _transport, integrationId, _logger);
}
