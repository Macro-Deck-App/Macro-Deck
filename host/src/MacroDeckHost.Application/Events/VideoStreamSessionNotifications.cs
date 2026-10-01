using MacroDeck.Localization;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.VideoStreams;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record VideoStreamSessionChangedNotification(
	string ConnectionId,
	string SessionId,
	long Revision,
	VideoStreamSessionState State,
	VideoStreamRelayDescription? Description,
	VideoStreamSessionReason Reason,
	LocalizedText? Message) : INotification;

public sealed record VideoStreamSessionClosedNotification(
	string ConnectionId,
	string SessionId,
	VideoStreamSessionReason Reason,
	LocalizedText? Message,
	VideoStreamError? Error) : INotification;
