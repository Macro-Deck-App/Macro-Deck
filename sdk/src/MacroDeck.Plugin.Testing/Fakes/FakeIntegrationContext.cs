using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// An <see cref="IIntegrationContext" /> built entirely from the fakes in this namespace, so a plugin
/// under test can be constructed and exercised with no host on the other end of any of its eight
/// capabilities.
///
/// <para>
/// Every capability is exposed twice: once through the <see cref="IIntegrationContext" /> interface, for
/// wiring into whatever the plugin under test expects, and once through its concrete fake type - e.g.
/// <see cref="Variables" /> is a <see cref="FakeVariableApi" /> - so a test can seed and inspect it
/// directly, with no downcast.
/// </para>
/// </summary>
public sealed class FakeIntegrationContext : IIntegrationContext
{
	/// <summary>Creates a fresh, empty instance of every fake.</summary>
	public FakeIntegrationContext()
	{
		Variables = new FakeVariableApi();
		Config = new FakeIntegrationConfig();
		Deck = new FakeDeckNavigator();
		Scripts = new FakeScriptApi();
		Widgets = new FakeWidgetApi();
		// Deferring the widget check to the widget fake keeps one seeded widget enough for both.
		UserVariables = new FakeUserVariableApi(Widgets.Exists);
		Events = new FakeEventPublisher();
		Notifications = new FakeUserNotifier();
		Interactions = new FakeActionInteractions();
		Devices = new FakeDeviceProviderContext();
		VariableValues = new FakeVariableSink();
		Layouts = new FakeLayoutProviderContext();
		FolderViews = new FakeFolderViewProviderContext();
		WidgetTypes = new FakeWidgetTypeProviderContext();
	}

	/// <summary>The plugin's own variables - see <see cref="FakeVariableApi" />.</summary>
	public FakeVariableApi Variables { get; }

	/// <summary>The user's variables - see <see cref="FakeUserVariableApi" />.</summary>
	public FakeUserVariableApi UserVariables { get; }

	/// <summary>This integration's config entries and secrets - see <see cref="FakeIntegrationConfig" />.</summary>
	public FakeIntegrationConfig Config { get; }

	/// <summary>Deck navigation - see <see cref="FakeDeckNavigator" />.</summary>
	public FakeDeckNavigator Deck { get; }

	/// <summary>The user's stored scripts - see <see cref="FakeScriptApi" />.</summary>
	public FakeScriptApi Scripts { get; }

	/// <summary>Widget appearance changes - see <see cref="FakeWidgetApi" />.</summary>
	public FakeWidgetApi Widgets { get; }

	/// <summary>This integration's raised events - see <see cref="FakeEventPublisher" />.</summary>
	public FakeEventPublisher Events { get; }

	/// <summary>
	/// Notifications raised into the host's notification center - see <see cref="FakeUserNotifier" />.
	/// </summary>
	public FakeUserNotifier Notifications { get; }

	/// <summary>
	/// The action-interactions fake for this context. Not part of <see cref="IIntegrationContext" />
	/// - <c>IActionInteractions</c> reaches an action execution over the wire instead, answered by
	/// <c>Internal.HostInvokeDispatcher</c>'s handling of the action-interactions <c>host.invoke</c> API
	/// - this is just a convenient single source for both, so a test host can be wired up from one context.
	/// </summary>
	public FakeActionInteractions Interactions { get; }

	/// <summary>
	/// The device registry fake for this context. Not part of <see cref="IIntegrationContext" /> either -
	/// a device provider is handed its own <c>IDeviceProviderContext</c>, and an out-of-process provider
	/// reaches the host over the wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the
	/// devices <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeDeviceProviderContext Devices { get; }

	/// <summary>
	/// The variable push sink fake for this context. Not part of <see cref="IIntegrationContext" />
	/// either - a push-capable variable catalog is handed its own <c>IVariableSink</c> through
	/// <c>IVariableProvider.OnAttachedAsync</c>, and an out-of-process provider reaches the host over the
	/// wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the <c>variable-values</c>
	/// <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeVariableSink VariableValues { get; }

	/// <summary>
	/// The layout registry fake for this context. Not part of <see cref="IIntegrationContext" /> either -
	/// a layout provider is handed its own <c>ILayoutProviderContext</c>, and an out-of-process provider
	/// reaches the host over the wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the
	/// layouts <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeLayoutProviderContext Layouts { get; }

	/// <summary>
	/// The folder view catalog fake for this context. Not part of <see cref="IIntegrationContext" /> for
	/// the same reason <see cref="Layouts" /> is not - a folder view provider is handed its own
	/// <c>IFolderViewProviderContext</c>, and an out-of-process provider reaches the host over the wire.
	/// </summary>
	public FakeFolderViewProviderContext FolderViews { get; }

	/// <summary>
	/// The widget type catalog fake for this context. Not part of <see cref="IIntegrationContext" /> for
	/// the same reason <see cref="FolderViews" /> is not - a widget type provider is handed its own
	/// <c>IWidgetTypeProviderContext</c>, and an out-of-process provider reaches the host over the wire.
	/// </summary>
	public FakeWidgetTypeProviderContext WidgetTypes { get; }

	/// <inheritdoc />
	IVariableApi IIntegrationContext.Variables => Variables;

	/// <inheritdoc />
	IUserVariableApi IIntegrationContext.UserVariables => UserVariables;

	/// <inheritdoc />
	IIntegrationConfig IIntegrationContext.Config => Config;

	/// <inheritdoc />
	IDeckNavigator IIntegrationContext.Deck => Deck;

	/// <inheritdoc />
	IScriptApi IIntegrationContext.Scripts => Scripts;

	/// <inheritdoc />
	IWidgetApi IIntegrationContext.Widgets => Widgets;

	/// <inheritdoc />
	IEventPublisher IIntegrationContext.Events => Events;

	/// <inheritdoc />
	IUserNotifier IIntegrationContext.Notifications => Notifications;
}
