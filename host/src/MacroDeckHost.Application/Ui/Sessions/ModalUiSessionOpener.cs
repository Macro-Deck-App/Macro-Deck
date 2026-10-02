using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IModalUiSessionOpener
{
	OpenModalUiSessionResponse Open(OpenModalUiSessionRequest request, string ownerPrincipal);

	CompleteUiModalResponse Complete(CompleteUiModalRequest request, string ownerPrincipal);
}

/// <summary>
/// Opens the <c>dialog</c> surface for a modal an action registered, on the request of the client the
/// modal was addressed to. Client-initiated for the reason the ownership binding requires: a session's
/// owning principal is what keeps one device's half-filled dialog off another's screen, and the host
/// cannot infer a principal from the client id an action carries.
/// </summary>
public sealed class ModalUiSessionOpener : IModalUiSessionOpener
{
	private readonly IModalInteractionCoordinator _coordinator;
	private readonly IUiSessionBroker _broker;
	private readonly ILogger _logger;

	public ModalUiSessionOpener(IModalInteractionCoordinator coordinator, IUiSessionBroker broker, ILogger logger)
	{
		_coordinator = coordinator;
		_broker = broker;
		_logger = logger.ForContext<ModalUiSessionOpener>();
	}

	public OpenModalUiSessionResponse Open(OpenModalUiSessionRequest request, string ownerPrincipal)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		if (!_coordinator.TryClaim(request.ModalId, ownerPrincipal, out var modal))
		{
			// One answer for "no such modal" and "not yours", so this cannot be used to discover that a
			// modal exists on another device.
			if (_coordinator.Find(request.ModalId) is { } foreign)
			{
				_logger.Information(
					"Refused to open modal {ModalId} of integration {IntegrationId} (view {ViewId}): {Code}, " +
					"another client already claimed it",
					foreign.ModalId,
					foreign.IntegrationId,
					foreign.ViewId,
					UiSessionErrorCodes.SessionNotFound);
			}
			else
			{
				_logger.Information("Refused to open modal {ModalId}: {Code}, it is no longer registered",
					request.ModalId,
					UiSessionErrorCodes.SessionNotFound);
			}

			return new OpenModalUiSessionResponse
			{
				Accepted = false,
				SessionId = string.Empty,
				Code = UiSessionErrorCodes.SessionNotFound,
				Message = "There is no modal with that id."
			};
		}

		if (_coordinator.SessionFor(request.ModalId) is { Length: > 0 } existing)
		{
			return new OpenModalUiSessionResponse { Accepted = true, SessionId = existing };
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = BuildAttributes(modal)
		};

		var ticket = _broker.Open(modal.IntegrationId, surface, ownerPrincipal);

		if (!ticket.Accepted)
		{
			// The provider could not serve the dialog, so nobody will ever answer it. Cancelling here is
			// what keeps the waiting action from sitting until its run budget runs out.
			_logger.Warning(
				"Refused to open modal {ModalId} of integration {IntegrationId} (view {ViewId}): {Code} {Message}",
				modal.ModalId,
				modal.IntegrationId,
				modal.ViewId,
				ticket.Code,
				ticket.Message);
			_coordinator.CancelInternal(request.ModalId);

			return new OpenModalUiSessionResponse
			{
				Accepted = false,
				SessionId = string.Empty,
				Code = ticket.Code,
				Message = ticket.Message
			};
		}

		_coordinator.BindSession(request.ModalId, ticket.SessionId);

		return new OpenModalUiSessionResponse { Accepted = true, SessionId = ticket.SessionId };
	}

	public CompleteUiModalResponse Complete(CompleteUiModalRequest request, string ownerPrincipal)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		var sessionId = _coordinator.SessionFor(request.ModalId);
		var settled = _coordinator.Settle(request.ModalId, ownerPrincipal, request.Cancelled, request.Value);

		if (settled && sessionId is { Length: > 0 })
		{
			_broker.CloseOwned(sessionId, ownerPrincipal, "The modal was closed.");
		}

		return new CompleteUiModalResponse { Accepted = settled };
	}

	private static Dictionary<string, JsonElement> BuildAttributes(PendingModal modal)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiDialogSurfaceAttributes.ModalId] = JsonSerializer.SerializeToElement(modal.ModalId),
			[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(modal.ViewId)
		};

		attributes[UiDialogSurfaceAttributes.Data] = modal.Data is null
			? JsonDocument.Parse("{}").RootElement
			: JsonSerializer.SerializeToElement(modal.Data);

		return attributes;
	}
}
