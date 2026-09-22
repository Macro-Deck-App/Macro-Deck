using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Internal;
using MacroDeck.Sdk.Messaging;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <see cref="MacroDeckTestHost" />'s side of the <c>messaging</c> host api: records what each hosted
/// plugin publishes, sends, requests and subscribes to, and answers sends and requests from stubs set with
/// <see cref="RespondTo" />. Deliver messages to a hosted plugin through <see cref="PluginSessionView.Messaging" />.
/// </summary>
public sealed class TestHostMessaging
{
	private readonly ConcurrentQueue<ChannelMessage> _published = new();
	private readonly ConcurrentQueue<ChannelMessage> _sent = new();
	private readonly ConcurrentQueue<ChannelMessage> _requested = new();
	private readonly ConcurrentDictionary<string, MessagingSubscriptionsArguments> _subscriptions = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Func<ChannelMessage, JsonElement?>> _stubs = new(StringComparer.Ordinal);

	/// <summary>Every event a hosted plugin published, in order. <see cref="ChannelMessage.Sender" /> is its plugin id.</summary>
	public IReadOnlyList<ChannelMessage> Published => [.. _published];

	/// <summary>Every command a hosted plugin sent, in order.</summary>
	public IReadOnlyList<ChannelMessage> Sent => [.. _sent];

	/// <summary>Every request a hosted plugin sent, in order.</summary>
	public IReadOnlyList<ChannelMessage> Requested => [.. _requested];

	/// <summary>The event patterns <paramref name="pluginId" /> last synced; empty when it synced none.</summary>
	public IReadOnlyList<string> SubscriptionsOf(string pluginId)
		=> _subscriptions.TryGetValue(pluginId, out var table) ? table.Events : [];

	/// <summary>The command topics <paramref name="pluginId" /> last said it handles.</summary>
	public IReadOnlyList<string> HandledCommandsOf(string pluginId)
		=> _subscriptions.TryGetValue(pluginId, out var table) ? table.Commands : [];

	/// <summary>The request topics <paramref name="pluginId" /> last said it handles.</summary>
	public IReadOnlyList<string> HandledRequestsOf(string pluginId)
		=> _subscriptions.TryGetValue(pluginId, out var table) ? table.Requests : [];

	/// <summary>
	/// Answers commands and requests a hosted plugin sends to <paramref name="topic" />. Without a stub they
	/// fail with <c>messaging_no_handler</c>. For a command the returned value is ignored.
	/// </summary>
	public void RespondTo(string topic, Func<ChannelMessage, JsonElement?> reply)
	{
		ArgumentNullException.ThrowIfNull(reply);
		_stubs[topic] = reply;
	}

	internal HostInvokeOutcome Dispatch(string pluginId, HostInvokePayload payload)
	{
		if (payload.Operation == HostOperations.Messaging.Subscriptions)
		{
			_subscriptions[pluginId] = payload.Arguments?.Deserialize<MessagingSubscriptionsArguments>(PluginProtocolJson.Options) ??
				new MessagingSubscriptionsArguments();
			return HostInvokeOutcome.Ok(new MessagingSubscriptionsResult());
		}

		var arguments = payload.Arguments?.Deserialize<MessagingMessageArguments>(PluginProtocolJson.Options);
		if (arguments is null || !MessageTopic.IsValidTopic(arguments.Topic))
		{
			return HostInvokeOutcome.Failed(new ProtocolError
			{
				Code = ProtocolErrorCodes.InvalidPayload,
				Message = "The topic is not valid.",
				Retryable = false,
				Details = new Dictionary<string, string> { ["reason"] = ProtocolErrorReasons.MessagingInvalidTopic }
			});
		}

		var kind = payload.Operation switch
		{
			HostOperations.Messaging.Send => ChannelMessageKind.Command,
			HostOperations.Messaging.Request => ChannelMessageKind.Request,
			_ => ChannelMessageKind.Event
		};

		var message = new ChannelMessage
		{
			Kind = kind,
			Topic = arguments.Topic,
			Sender = pluginId,
			MessageId = Guid.NewGuid().ToString(),
			SentAt = DateTimeOffset.UtcNow,
			Payload = arguments.Payload?.Clone()
		};

		switch (kind)
		{
			case ChannelMessageKind.Event:
				_published.Enqueue(message);
				return HostInvokeOutcome.Ok(null);
			case ChannelMessageKind.Command:
				_sent.Enqueue(message);
				break;
			default:
				_requested.Enqueue(message);
				break;
		}

		if (!_stubs.TryGetValue(arguments.Topic, out var stub))
		{
			return HostInvokeOutcome.Failed(new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnavailable,
				Message = $"Nothing handles '{arguments.Topic}'.",
				Retryable = false,
				Details = new Dictionary<string, string> { ["reason"] = ProtocolErrorReasons.MessagingNoHandler }
			});
		}

		var reply = stub(message);
		return kind == ChannelMessageKind.Request
			? HostInvokeOutcome.Ok(new MessagingReplyPayload { Payload = reply })
			: HostInvokeOutcome.Ok(null);
	}
}
