namespace MacroDeck.Sdk.Actions;

/// <summary>
/// A host-resolvable icon reference a provider hands back instead of raw bytes - most commonly one
/// already in the icon pack catalog. <paramref name="Reference" /> is opaque to the provider: it is
/// never a URL, because the host never fetches one on a provider's say-so.
/// </summary>
/// <param name="Type">Which host-side source resolves <paramref name="Reference" />.</param>
/// <param name="Reference">Source-specific identity, e.g. an icon pack entry's id.</param>
public sealed record ActionIconReference(string Type, string Reference)
{
	public const string IconPackType = "icon-pack";

	/// <summary>An icon already in the host's icon pack catalog, named by its existing id.</summary>
	public static ActionIconReference IconPack(string reference) => new(IconPackType, reference);
}

/// <summary>The bytes behind one <see cref="ActionIconSnapshot.Version" />, mirroring
/// <c>MacroDeck.Sdk.MusicPlayer.MusicPlayerArtwork</c>.</summary>
public sealed record ActionIconContent(byte[] Data, string MediaType);

/// <summary>
/// The icon a configured action instance currently provides for the widget it is attached to. Three
/// distinguishable answers, matching the three questions a widget rendering an icon needs answered:
/// carries a <see cref="Reference" /> or (via <see cref="IIconProviderActionDefinition.GetActionIconContentAsync" />)
/// bytes, is deliberately blank (<see cref="NoIcon" />), or - by the whole snapshot being <c>null</c> -
/// cannot be answered at all right now.
/// </summary>
public sealed record ActionIconSnapshot
{
	/// <summary>
	/// Stable identity of the image behind this result. The host refetches bytes only when it changes,
	/// which is what makes polling affordable for a provider whose image rarely changes. Empty when
	/// <see cref="NoIcon" /> is set.
	/// </summary>
	public string Version { get; init; } = string.Empty;

	/// <summary>A host icon reference to render. Null when this provider supplies bytes instead, or
	/// when <see cref="NoIcon" /> is set.</summary>
	public ActionIconReference? Reference { get; init; }

	/// <summary>The media type bytes at <see cref="Version" /> would carry, when <see cref="Reference" />
	/// is null. Unused when a <see cref="Reference" /> is given, or when <see cref="NoIcon" /> is set.</summary>
	public string? MediaType { get; init; }

	/// <summary>
	/// The provider is working and is deliberately showing no icon at all - distinct from the provider
	/// returning <c>null</c> from <see cref="IIconProviderActionDefinition.GetActionIconAsync" />, which
	/// means "I cannot answer" and falls back to the widget's configured icon instead of rendering blank.
	/// </summary>
	public bool NoIcon { get; init; }
}

/// <summary>
/// Implemented by actions whose configured instance can drive a widget's icon: the instance owns the
/// image the widget currently renders, independently of whether it also implements
/// <see cref="IStateProviderActionDefinition" />. At most one icon-provider instance is authoritative
/// for any one widget, and it owns the <em>currently rendered</em> icon - not one icon per state.
///
/// <para>
/// <see cref="GetActionIconAsync" /> answers for the <em>configured instance</em>, not the action type -
/// exactly like <see cref="IStateProviderActionDefinition.GetActionStateAsync" />. Everything is
/// answered from the <c>parameters</c> that instance was configured with, so the same action may appear
/// on several widgets with different configurations and each answers for itself. Which instance is a
/// given widget's provider is the host's decision alone and is never asserted by the action.
/// </para>
/// </summary>
public interface IIconProviderActionDefinition
{
	/// <summary>
	/// Returns the current icon identity for the given configured parameters, or <c>null</c> when no
	/// icon can be reported (the target is unconfigured, disconnected, or gone) - the host then falls
	/// back to the widget's own configured icon and never holds onto a stale image.
	///
	/// <para>
	/// Called on two different cadences and must be safe on both: once per settled editor draft while
	/// the user is still configuring the action - with a partially filled, possibly half-typed parameter
	/// set, so this must not throw on a missing or incomplete value - and repeatedly at
	/// <see cref="IconPollInterval" /> while a widget follows the instance.
	/// </para>
	///
	/// <para>
	/// Must be side-effect free: answer from state already held, honour <paramref name="cancellationToken" />,
	/// never connect or authenticate to produce an answer, and never be routed through
	/// <see cref="IActionExecutor" />.
	/// </para>
	/// </summary>
	Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken);

	/// <summary>
	/// Returns the bytes behind the <see cref="ActionIconSnapshot.Version" /> last reported. Null when
	/// that version has already moved on - the host then keeps the resource it already holds rather than
	/// blanking the widget. Never called for a snapshot that already carried a
	/// <see cref="ActionIconSnapshot.Reference" /> or set <see cref="ActionIconSnapshot.NoIcon" />.
	/// </summary>
	Task<ActionIconContent?> GetActionIconContentAsync(
		IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionIconContent?>(null); // reference-only providers need no override

	/// <summary>
	/// How often a widget following this instance should poll while it is on screen. A request, not a
	/// guarantee: the host clamps it and may read less often - or not at all - while nothing displays
	/// the widget.
	/// </summary>
	TimeSpan IconPollInterval => TimeSpan.FromSeconds(5);
}
