namespace MacroDeck.Sdk.ScreenSavers;

/// <summary>
/// Implemented by integrations that offer screensavers: what a device shows in place of the deck after it
/// has sat idle for the time its settings name. A provider says which screensavers it <i>offers</i>; the
/// screensavers themselves are served through the same <see cref="Ui.IUiProvider" /> a widget or a folder
/// view uses, when Macro Deck opens a <c>screensaver</c> surface for a device that selected one of them.
/// </summary>
/// <remarks>
/// <para>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized, and
/// withdraws whatever the provider registered when the integration stops. There is deliberately no
/// shutdown member of its own, for the reason <see cref="FolderViews.IFolderViewProvider" /> gives:
/// release whatever <see cref="InitializeAsync" /> acquired in the integration's own <c>ShutdownAsync</c>.
/// </para>
/// <para>
/// Withdrawing a screensaver does not reset the devices that selected it. Macro Deck keeps the device's
/// stored screensaver id and its configuration untouched and shows its built-in clock instead, so
/// reinstalling or re-enabling the provider restores the device exactly as it was, and a device is never
/// left on a blank screen.
/// </para>
/// <para>
/// The idle timer runs on the device, never on the host: the host only stores the settings and serves the
/// surface, so a network hiccup never delays waking up. A screensaver is inert by default: the first touch or
/// click dismisses it and is swallowed, never reaching the tree or the deck underneath. A descriptor that
/// declares <see cref="ScreenSaverDescriptor.Interactive" /> receives input on the nodes that claim it.
/// </para>
/// </remarks>
public interface IScreenSaverProvider
{
	/// <summary>Human-readable provider name, e.g. "Home Assistant".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>
	/// Registers whatever screensavers the provider offers, and keeps registering and withdrawing them as
	/// its configuration changes - a provider whose screensavers depend on a connection it has not made yet
	/// registers nothing here and registers later.
	/// </summary>
	Task InitializeAsync(IScreenSaverProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>
	/// The screensavers this provider currently offers. The host reads this to recover its catalog after a
	/// reconnect; a provider that keeps no catalog of its own may return an empty list.
	/// </summary>
	IReadOnlyList<ScreenSaverDescriptor> GetScreenSavers() => [];
}
