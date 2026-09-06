namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// Implemented by integrations that offer their own deck widgets - a gauge, a camera tile, a queue - beside
/// Macro Deck's built-in ones. A provider says which types it <i>offers</i>; a widget of one of those types
/// is drawn by the same <see cref="Ui.IUiProvider" /> a folder view or a configuration flow is served
/// through, when Macro Deck opens a <c>widget</c>, <c>preview</c> or <c>config</c> surface for it. There is
/// no separate rendering path: a plugin widget is a tree in the same component vocabulary a built-in widget
/// is built from.
/// </summary>
/// <remarks>
/// <para>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized, before any
/// other provider hook, and withdraws whatever the provider registered when the integration is uninstalled
/// or stops. There is deliberately no shutdown member of its own, for the reason
/// <see cref="FolderViews.IFolderViewProvider" /> and <c>ILayoutProvider</c> give: an integration providing
/// two of them would have had one method body serving two interfaces and run twice. Release whatever
/// <see cref="InitializeAsync" /> acquired in the integration's own <c>ShutdownAsync</c> instead.
/// </para>
/// <para>
/// Withdrawing a type does not delete the widgets using it, and neither does the provider going away. A
/// widget keeps its stored type and data whether or not anything provides them; Macro Deck simply has
/// nothing to draw it with until the type returns, at which point every widget of it comes back exactly as
/// it was. That is also why the local id must never be reused for a different widget.
/// </para>
/// <para>
/// A widget's stored configuration reaches the provider on the surface rather than being looked up, because
/// a provider outside the host cannot read the host's widget store. A provider is therefore stateless with
/// respect to which widget it is drawing: everything it needs is in
/// <c>UiWidgetSurfaceAttributes</c>.
/// </para>
/// </remarks>
public interface IWidgetTypeProvider
{
	/// <summary>Human-readable provider name, e.g. "Home Assistant".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// Registers whatever widget types the provider offers, and keeps registering and withdrawing them as
	/// its configuration changes - a provider whose types depend on a connection it has not made yet
	/// registers nothing here and registers later.
	/// </summary>
	Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>
	/// The widget types this provider currently offers. The host reads this to recover its catalog after a
	/// reconnect; a provider that keeps no catalog of its own may return an empty list.
	/// </summary>
	IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [];
}
