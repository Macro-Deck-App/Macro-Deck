namespace MacroDeck.Ui.Model.Versioning;

/// <summary>
/// A single monotonically increasing integer major for the UI model itself, independent of the plugin
/// protocol's own major. Unknown members are ignored and unknown node types, surface kinds and patch
/// operations are non-fatal, so every additive change to this model is backward-compatible by
/// construction and a bump only ever means "breaking".
///
/// <para>
/// This is a second, independent major from the plugin protocol on purpose: a break in this model
/// would otherwise force a protocol major on every plugin that never renders a tree.
/// </para>
/// </summary>
public static class UiModelVersions
{
	/// <summary>The oldest UI model major this package still speaks.</summary>
	/// <remarks>
	/// 3 since #842, in step with <see cref="Current" />: the component vocabulary was renamed outright,
	/// with no spelling of the old one accepted. A package that no longer understands a single
	/// <c>widget.*</c> node type must not advertise that it speaks the majors in which those were the
	/// only spelling - a session negotiated down to 1 or 2 would open successfully and then draw every
	/// node as the unsupported placeholder, which is precisely the silent failure a negotiated major
	/// exists to turn into a refusal.
	/// </remarks>
	public const int Minimum = 3;

	/// <summary>The newest UI model major this package speaks.</summary>
	/// <remarks>
	/// 2 since #326: a text-bearing configuration property may carry a localization reference
	/// (<c>{"$localized":{…}}</c>) where it previously always carried a JSON string. The core model is
	/// unchanged - <see cref="Nodes.UiNode.Properties" /> was always arbitrary, unvalidated JSON - but a
	/// producer built against version 1 emits only strings, and one built against version 2 may emit
	/// either, so the two are not interchangeable and the version has to say which a session speaks.
	/// The host opens every session at <see cref="Current" /> and honours whatever a provider negotiates
	/// down to.
	///
	/// <para>
	/// 3 since #842: every node type in the component vocabulary was respelled, from one flat
	/// <c>widget.*</c> namespace into <c>ui.*</c> and <c>macrodeck.*</c>. Node types were always
	/// open and an unknown one was always non-fatal, so nothing about the <i>model</i> changed - but a
	/// tree authored at 2 names nothing a reader at 3 can draw, which is a break rather than an
	/// addition. <see cref="Minimum" /> moved with it; see its remarks.
	/// </para>
	///
	/// <para>
	/// 4 since #791: an icon-bearing configuration property may carry a typed
	/// <c>{"type":…,"reference":…}</c> provider reference where it previously always carried a bare
	/// icon-pack reference string. The same shape as the move to 2, and bumped for the same reason - a
	/// producer built against 3 emits only strings and one built against 4 may emit either, so the two
	/// are not interchangeable. <see cref="Minimum" /> deliberately does <b>not</b> move: this widens a
	/// value rather than renaming one, so a reader at 4 still reads every tree a producer at 3 emits.
	/// </para>
	/// </remarks>
	public const int Current = 4;

	/// <summary>The contiguous range between <see cref="Minimum" /> and <see cref="Current" />.</summary>
	public static readonly IReadOnlyList<int> Supported = BuildSupported();

	private static List<int> BuildSupported()
	{
		var versions = new List<int>(Current - Minimum + 1);
		for (var version = Minimum; version <= Current; version++)
		{
			versions.Add(version);
		}

		return versions;
	}
}
