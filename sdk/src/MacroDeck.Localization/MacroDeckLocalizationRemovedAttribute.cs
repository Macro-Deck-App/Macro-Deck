namespace MacroDeck.Localization;

/// <summary>
/// Marks a generated localization member whose key Macro Deck has retired. The member is still generated
/// and still resolves, so code written against it keeps compiling and keeps rendering; MDLOC006 reports
/// the call site with the replacement to move to.
/// </summary>
/// <remarks>
/// A retired key is recorded rather than deleted because the catalog is published SDK surface: deleting
/// it outright would turn every call site into a bare "member does not exist" compiler error with nothing
/// to migrate to. A resource declares its retirement by prefixing its comment with
/// <c>[removed:&lt;guidance&gt;]</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MacroDeckLocalizationRemovedAttribute : Attribute
{
	/// <summary>Creates the marker.</summary>
	/// <param name="guidance">What to use instead, shown verbatim in the diagnostic.</param>
	public MacroDeckLocalizationRemovedAttribute(string guidance) => Guidance = guidance;

	/// <summary>What to use instead.</summary>
	public string Guidance { get; }
}
