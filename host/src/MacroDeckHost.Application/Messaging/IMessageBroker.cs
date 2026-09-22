using System.Text.Json;

namespace MacroDeckHost.Application.Messaging;

public interface IMessageBroker
{
	IReadOnlyList<MessageRegistrationRejection> Replace(string participantId,
		string registrationKey,
		IMessageParticipant participant,
		MessageRegistrations registrations);

	void Remove(string participantId, string registrationKey);

	string? CurrentRegistrationKey(string participantId);

	void Publish(string senderId, string topic, JsonElement? payload);

	Task SendAsync(string senderId,
		string topic,
		JsonElement? payload,
		TimeSpan timeout,
		CancellationToken cancellationToken);

	Task<JsonElement?> RequestAsync(string senderId,
		string topic,
		JsonElement? payload,
		TimeSpan timeout,
		CancellationToken cancellationToken);
}
