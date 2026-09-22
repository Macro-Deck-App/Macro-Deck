using MacroDeck.Sdk.Messaging;

namespace MacroDeckHost.Application.Messaging;

public sealed record MessageRegistrations(
	IReadOnlyList<string> Events,
	IReadOnlyList<string> Commands,
	IReadOnlyList<string> Requests)
{
	public static readonly MessageRegistrations Empty = new([], [], []);
}

public sealed record MessageRegistrationRejection(
	ChannelMessageKind Kind,
	string Topic,
	string Reason,
	string? Owner);
