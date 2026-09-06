using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Ui.Modals;

/// <summary>
/// The host end of <see cref="IUiInteractions" /> for an in-process integration. Registers the modal,
/// tells the originating client it is there, and - when the caller wants an answer - waits for one.
/// </summary>
public sealed class UiInteractions : IUiInteractions
{
	private static readonly JsonSerializerOptions _valueOptions = new(JsonSerializerDefaults.Web);

	private readonly IModalInteractionCoordinator _coordinator;
	private readonly IUiTransport _transport;
	private readonly string _integrationId;
	private readonly ILogger _logger;

	public UiInteractions(
		IModalInteractionCoordinator coordinator,
		IUiTransport transport,
		string integrationId,
		ILogger logger)
	{
		_coordinator = coordinator;
		_transport = transport;
		_integrationId = integrationId;
		_logger = logger.ForContext<UiInteractions>();
	}

	public async Task<bool> ShowModalAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
		=> await OpenAsync(originClientId, modal, cancellationToken).ConfigureAwait(false) is not null;

	public async Task<ModalResult<T>> ShowModalAsync<T>(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default)
	{
		var modalId = await OpenAsync(originClientId, modal, cancellationToken).ConfigureAwait(false);
		if (modalId is null)
		{
			return ModalResult.FromCancellation<T>();
		}

		var outcome = await _coordinator.AwaitAsync(modalId, cancellationToken).ConfigureAwait(false);
		if (outcome.Cancelled)
		{
			return ModalResult.FromCancellation<T>();
		}

		try
		{
			return ModalResult.FromValue(outcome.Value.Deserialize<T>(_valueOptions));
		}
		catch (JsonException exception)
		{
			// The tree that produced this value is the integration's own, so a payload that does not fit
			// the type it asked for is its bug - but it must not become an exception thrown out of a
			// running flow, which would fail the whole action rather than the one question.
			_logger.Error(exception,
				"Integration '{IntegrationId}' completed a modal with a value that does not fit {Type}",
				_integrationId,
				typeof(T));

			return ModalResult.FromCancellation<T>();
		}
	}

	private async Task<string?> OpenAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(modal);

		var modalId = _coordinator.Register(_integrationId, originClientId, modal);
		if (modalId is null)
		{
			return null;
		}

		await _transport.SendToGroup(UiClientGroups.For(originClientId!),
				new UiModalOpenedEvent { ModalId = modalId, Title = modal.Title ?? default },
				cancellationToken)
			.ConfigureAwait(false);

		return modalId;
	}
}
