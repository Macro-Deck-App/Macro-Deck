using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk;

public interface IIntegrationContext
{
	/// <summary>Variables owned by this integration.</summary>
	IVariableApi Variables { get; }

	/// <summary>User-owned variables writable by name.</summary>
	IUserVariableApi UserVariables { get; }

	/// <summary>Configuration entries created by this integration's config flow.</summary>
	IIntegrationConfig Config { get; }

	/// <summary>Deck and folder navigation.</summary>
	IDeckNavigator Deck { get; }

	/// <summary>Access to stored scripts.</summary>
	IScriptApi Scripts { get; }

	/// <summary>Runtime and persisted widget appearance updates.</summary>
	IWidgetApi Widgets { get; }

	/// <summary>Publishes events declared by this integration. Safe to retain for the process lifetime.</summary>
	IEventPublisher Events { get; }

	/// <summary>Publishes notifications to the host notification center.</summary>
	IUserNotifier Notifications { get; }

	/// <summary>
	/// Talks to other plugins and integrations by topic. Registrations made here are released when the
	/// integration is shut down or initialized again. A context from a Macro Deck without a message
	/// channel, and any implementation that does not override this member, throws
	/// <see cref="MessageChannelException" /> with <see cref="MessageChannelErrorCode.Unsupported" />.
	/// </summary>
	IMessageChannel Messages => UnsupportedMessageChannel.Instance;

	/// <summary>
	/// Registers images this integration's own trees show. A context from a Macro Deck that cannot register
	/// UI resources, and any implementation that does not override this member, throws
	/// <see cref="UiResourceException" /> with <see cref="UiResourceErrorCode.Unsupported" />.
	/// </summary>
	IUiResourceRegistry UiResources => UnsupportedUiResourceRegistry.Instance;
}
