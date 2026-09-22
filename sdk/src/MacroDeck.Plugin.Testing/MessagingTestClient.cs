using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Messaging;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>messaging</c> capability: messages from other participants, delivered to the plugin over the wire.</summary>
public sealed class MessagingTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal MessagingTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>Delivers an event to the plugin's subscriptions matching <paramref name="topic" />.</summary>
	public Task<CapabilityInvocationOutcome> DeliverEventAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CapabilityInvokeOptions? options = null)
		=> DeliverAsync(CapabilityOperations.Messaging.Event, topic, payload, sender, options);

	/// <summary>Delivers a command to the plugin's handler of <paramref name="topic" />.</summary>
	public Task<CapabilityInvocationOutcome> DeliverCommandAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CapabilityInvokeOptions? options = null)
		=> DeliverAsync(CapabilityOperations.Messaging.Command, topic, payload, sender, options);

	/// <summary>Delivers a request to the plugin's handler of <paramref name="topic" />; the reply is the outcome's <c>payload</c>.</summary>
	public Task<CapabilityInvocationOutcome> DeliverRequestAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CapabilityInvokeOptions? options = null)
		=> DeliverAsync(CapabilityOperations.Messaging.Request, topic, payload, sender, options);

	private Task<CapabilityInvocationOutcome> DeliverAsync(string operation,
		string topic,
		JsonElement? payload,
		string sender,
		CapabilityInvokeOptions? options)
		=> _invoker.InvokeAsync(CapabilityKinds.Messaging,
			ProviderCapabilityId.LocalId,
			operation,
			new MessagingDeliveryArguments
			{
				Topic = topic,
				Sender = sender,
				MessageId = Guid.NewGuid().ToString(),
				SentAt = DateTimeOffset.UtcNow,
				Payload = payload
			},
			options);
}
