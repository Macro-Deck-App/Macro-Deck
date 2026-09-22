using System.Text.Json;

namespace MacroDeckHost.Application.Messaging;

public sealed record MessageDelivery(
	string Topic,
	string Sender,
	string MessageId,
	DateTimeOffset SentAt,
	JsonElement? Payload);

public interface IMessageParticipant
{
	Task DeliverEventAsync(MessageDelivery message, CancellationToken cancellationToken);

	Task HandleCommandAsync(MessageDelivery message, TimeSpan timeout, CancellationToken cancellationToken);

	Task<JsonElement?> HandleRequestAsync(MessageDelivery message, TimeSpan timeout, CancellationToken cancellationToken);
}
