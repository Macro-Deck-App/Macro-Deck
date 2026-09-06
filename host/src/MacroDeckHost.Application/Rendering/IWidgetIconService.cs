using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// What an active icon provider currently contributes to a widget's rendered icon. <see cref="IsActive" />
/// is false whenever there is nothing to override with - no assignment, or the guard chain / the provider
/// itself could not answer (issue #425 decision 1) - in which case a caller applies the widget's own
/// configured icon exactly as it would with no provider at all. <see cref="IsActive" /> true with a null
/// <see cref="Resource" /> means the provider is deliberately showing no icon, or answered with an image
/// this host cannot render (an unregistered reference type, an unlisted media type, oversized bytes) -
/// both render blank rather than falling back to the configured icon, because the provider did answer.
/// </summary>
public sealed record WidgetIconResolution(bool IsActive, UiResource? Resource)
{
	public static readonly WidgetIconResolution Inactive = new(false, null);

	public static readonly WidgetIconResolution Blank = new(true, null);

	public static WidgetIconResolution Active(UiResource resource) => new(true, resource);
}

/// <summary>
/// Resolves what an action-provided icon currently contributes to an Action Button, mirroring
/// <see cref="IWidgetStateService" />. Kept deliberately independent of the widget's configured
/// per-state/root icon: the provider owns the whole button's rendered icon, not one icon per state, so it
/// is resolved once per widget rather than once per state (see <see cref="WidgetIconResolution" />).
/// </summary>
public interface IWidgetIconService
{
	/// <summary>
	/// Resolves the icon an active icon-provider action currently supplies for a widget. Never throws -
	/// an unreachable provider, a timeout (3 s, matching <c>WidgetStateService</c>'s own provider read
	/// timeout), an unresolvable reference, or bytes this host cannot register all resolve to a result
	/// rather than an exception.
	/// </summary>
	Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default);

	/// <summary>
	/// How often the widget's icon-provider action asked to be polled, or null when the widget has no
	/// usable provider right now. Synchronous and side-effect free - it never reads the provider itself,
	/// only the declared interval, so the background poller can call it on every tick without cost. Shares
	/// the same guard chain <see cref="Resolve" /> uses, so the two can never disagree about whether a
	/// block is currently a usable provider.
	/// </summary>
	TimeSpan? GetProviderPollInterval(Guid widgetId);
}
