---
title: Messaging between plugins
description: Let plugins and integrations talk to each other by topic with IMessageChannel - events, commands and requests, topic ids, lifecycle, delivery guarantees, limits, older hosts and testing.
---

A plugin can talk to other plugins and to Macro Deck's built-in integrations without referencing
them. Everyone addresses a **topic**, a stable id such as `obs.scene.changed`, and Macro Deck
delivers the message to whoever listens to that topic. The OBS plugin publishes `obs.scene.changed`; a
lighting plugin subscribes to it and never needs to know which plugin sent it or whether it is
installed.

Macro Deck brokers every message. There is no direct connection between plugins, and every message
it delivers carries the sender's id, stamped by Macro Deck.

## Quick start

`IIntegrationContext.Messages` is the channel. The integration below answers requests for the current
scene and tells everyone when the scene changes:

```csharp
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Messaging;

public sealed class ObsIntegration : IPluginIntegration
{
	private IMessageChannel? _messages;
	private string _scene = "Starting";

	public IReadOnlyList<IActionDefinition> Actions => [];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_messages = context.Messages;

		await _messages.HandleRequestsAsync<object, SceneInfo>("obs.scene.current",
			(_, _, _) => Task.FromResult(new SceneInfo(_scene)));
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	private async Task OnSceneChangedAsync(string scene)
	{
		_scene = scene;
		await _messages!.PublishAsync("obs.scene.changed", new SceneInfo(scene));
	}
}

public sealed record SceneInfo(string Scene);
```

Another plugin, which has no reference to the OBS plugin, reacts to it:

```csharp
public async Task InitializeAsync(IIntegrationContext context)
{
	await context.Messages.SubscribeAsync<SceneInfo>("obs.scene.changed",
		(scene, message, _) => UpdateLightsAsync(scene!.Scene));

	var current = await context.Messages.RequestAsync<object?, SceneInfo>("obs.scene.current", null);
}
```

The SDK's typed helpers serialize payloads with System.Text.Json using the web defaults (camelCase).
Pass your own `JsonSerializerOptions` when both sides agree on different ones, or work with
`JsonElement` directly through the untyped members of `IMessageChannel`.

## Events, commands and requests

| Kind | Send with | Handle with | Reaches | Answer |
| --- | --- | --- | --- | --- |
| Event | `PublishAsync` | `SubscribeAsync` | every matching subscription | none; `PublishAsync` completes once Macro Deck accepted the event |
| Command | `SendAsync` | `HandleCommandsAsync` | the topic's one handler | completes when the handler finished |
| Request | `RequestAsync` | `HandleRequestsAsync` | the topic's one handler | the handler's return value |

- **Events fan out.** Every subscription whose pattern matches receives the event, including the
  publisher's own. `ChannelMessage.Sender` is your own plugin id for an event you published, so a
  handler can ignore it. Delivery is at most once and in publish order per receiver. A receiver that
  cannot keep up loses events rather than slowing the publisher down.
- **Commands and requests have exactly one handler.** Whoever registers a handler for a topic first
  keeps it until they dispose the registration or go away. A second participant that tries to handle the
  same topic gets `TopicAlreadyHandled`, and `MessageChannelException.HandlerOwner` names who has it.
  Nothing reserves a topic for its "real" owner, so prefix your topics with something only you use, such
  as your product name.
- **Handlers run on thread-pool threads.** An exception thrown by a command or request handler fails the
  call with `HandlerFailed`; its message is logged in your plugin and never sent to the caller. Put an
  error the caller should see into the reply payload instead. A subscription that throws is logged and
  does not affect other subscriptions.

## Topics

A topic is two or more dot-separated segments of lowercase letters, digits, `-` and `_`, each starting
and ending with a letter or digit, at most 128 characters: `obs.scene.changed`,
`home-assistant.light_1.state`. `MessageTopic.IsValidTopic` checks one.

A subscription takes a topic or a prefix wildcard. `obs.*` receives every topic below `obs`, such as
`obs.scene.changed` and `obs.stream.started`, but not `obs` itself and not `obsidian.note`. There are no
other wildcards, and command and request handlers always name one exact topic.

Topics are part of your plugin's public contract once other plugins use them. Keep them stable, and
keep the payload shapes compatible the way you would for a public API: add fields rather than rename
them.

## Lifecycle

Registrations made through `IIntegrationContext.Messages` belong to the integration's current
initialization. Macro Deck releases them when it shuts the integration down, and when it initializes
it again, which also happens after the user changed its configuration or the plugin reconnected to a
restarted Macro Deck. Register everything in `InitializeAsync` and you never have to dispose anything
yourself. While your integrations shut down and initialize again, Macro Deck keeps your command and
request topics for you, so re-registering them in the next `InitializeAsync` cannot fail because
another plugin took them in between.

A registration made through an `IMessageChannel` taken from dependency injection lasts until you dispose
it, across re-initializations. Use it from code that lives outside an integration, such as a hosted
service.

When your plugin disconnects, its handlers stay registered for the one minute Macro Deck waits for it to
resume; a command or request sent to it in that time fails with `HandlerUnavailable`, which is worth
retrying. Once that window has passed, or the plugin stopped, Macro Deck removes everything it
registered the next time it tidies up its sessions, and the topics are free; until then the answer
stays `HandlerUnavailable`. When your plugin reconnects without resuming, it registers everything
again, and another participant may have taken a topic in the gap.

## Failures and limits

Every operation throws `MessageChannelException`. `ErrorCode` says why:

| Code | Meaning |
| --- | --- |
| `Unsupported` | This Macro Deck has no message channel. |
| `NotConnected` | Your plugin is not connected to Macro Deck right now. Registrations are kept and sent once it is. |
| `InvalidTopic` | The topic or pattern does not follow the topic grammar. |
| `PayloadTooLarge` | The payload or reply is larger than 64 KiB once serialized. |
| `NoHandler` | Nothing handles the topic. |
| `HandlerUnavailable` | The handler is temporarily unreachable. Retry later. |
| `TopicAlreadyHandled` | Another participant already handles the topic; see `HandlerOwner`. |
| `HandlerFailed` | The handler threw or failed. |
| `Timeout` | The handler did not answer in time. |
| `RateLimited` | Too many messages in quick succession. Retry later. |

- A command or request waits at most 30 seconds for its handler, which is also the default. Pass a
  shorter `timeout` when you have a better idea of how long an answer is worth waiting for.
- A plugin can publish, send and request about 50 messages a second, with bursts of up to 100.
- A plugin can wait on at most 16 commands and requests at once. A handler receives at most 8 at once;
  more are refused with `RateLimited` rather than queued.
- A plugin can register at most 256 subscriptions, 256 command topics and 256 request topics.

## Permission

Declare `host:messaging` in `manifest.json` so that people installing your plugin can see it talks to
other plugins:

```json
"permissions": ["host:messaging"]
```

Macro Deck does not enforce it today. What you receive over the channel comes from other plugins: check
`Sender` when it matters who asked, and validate payloads like any other input.

## Built-in integrations

Macro Deck's own integrations get the same `IIntegrationContext.Messages` and share the same topics
with plugins. A plugin cannot tell whether a topic is handled by a plugin or by Macro Deck itself. One
difference: a built-in integration's topics are released as soon as it shuts down, so another participant
can take them before it initializes again.

## Older versions of Macro Deck

A Macro Deck without the message channel does not offer it, and your plugin then does not declare it.
`IMessageChannel` calls throw `MessageChannelException` with `Unsupported`; catch it where messaging is
optional for your plugin. The same applies to a custom `IIntegrationContext` implementation written
before the channel existed: its `Messages` member reports `Unsupported`.

On a Macro Deck that offers the channel, a plugin built on this SDK declares one more capability,
`messaging`, even when it never uses it. Tests that assert your plugin's exact list of declared
capabilities see it; see [Testing](/features/testing/).

## Testing

`FakeIntegrationContext.Messages` is a `FakeMessageChannel`. It routes your plugin's own messages the
way Macro Deck would, records everything your plugin publishes, sends and requests, and lets a test play
the other participants:

```csharp
var context = new FakeIntegrationContext();
context.Messages.RespondTo("obs.scene.current", _ => JsonSerializer.SerializeToElement(new { scene = "Live" }));

await integration.InitializeAsync(context);
var reply = await context.Messages.DeliverRequestAsync("lights.state", sender: "com.example.deck");

Assert.That(context.Messages.Published.Select(message => message.Topic), Does.Contain("lights.changed"));
```

In `PluginTestHarness` the same fake is `harness.Context.Messages` and is also what an injected
`IMessageChannel` resolves to; deliver through it rather than through raw capability invocations. Against
`MacroDeckTestHost`, `host.Messaging` records what a hosted plugin sends and subscribes to and answers
its commands and requests from `RespondTo` stubs, and `session.Messaging` delivers events, commands and
requests to it over the wire.

## On the wire

The channel uses the `messaging` host API and the `messaging` capability kind; see the
[WebSocket reference](/reference/websocket/#messaging).
