namespace MacroDeck.Sdk.FolderViews;

/// <summary>
/// Implemented by integrations that offer a complete rendering of a folder - a dashboard, a control
/// surface, a mixer - in place of Macro Deck's built-in widget grid. A provider says which views it
/// <i>offers</i>; the views themselves are served through the same <see cref="Ui.IUiProvider" /> a widget
/// or a configuration flow uses, when Macro Deck opens a <c>folder</c> surface for a folder that selected
/// one of them.
/// </summary>
/// <remarks>
/// <para>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized, and
/// withdraws whatever the provider registered when the integration stops. There is deliberately no
/// shutdown member of its own: <c>IDeviceProvider.ShutdownAsync</c> and <c>ILayoutProvider</c> face the
/// same problem, and an integration providing both folder views and either of those would have had one
/// method body serving two interfaces and run twice. Release whatever <see cref="InitializeAsync" />
/// acquired in the integration's own <c>ShutdownAsync</c> instead.
/// </para>
/// <para>
/// Withdrawing a view does not reset the folders that selected it. Macro Deck keeps the folder's stored
/// view id and its configuration untouched and renders a placeholder instead, so reinstalling or
/// re-enabling the provider restores the folder exactly as it was rather than silently turning it back
/// into an empty grid.
/// </para>
/// <para>
/// <b>Navigation is the host's, not the provider's.</b> A view that declares
/// <see cref="FolderViewNavigation.Hidden" /> is stating a preference. Macro Deck shows its own back button
/// anyway when the view is the only thing on screen and there is a valid target to go back to, so an
/// incomplete or broken view can never trap the user inside it.
/// </para>
/// </remarks>
public interface IFolderViewProvider
{
	/// <summary>Human-readable provider name, e.g. "Home Assistant".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// Registers whatever folder views the provider offers, and keeps registering and withdrawing them as
	/// its configuration changes - a provider whose views depend on a connection it has not made yet
	/// registers nothing here and registers later.
	/// </summary>
	Task InitializeAsync(IFolderViewProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>
	/// The folder views this provider currently offers. The host reads this to recover its view after a
	/// reconnect; a provider that keeps no catalog of its own may return an empty list.
	/// </summary>
	IReadOnlyList<FolderViewDescriptor> GetFolderViews() => [];
}
