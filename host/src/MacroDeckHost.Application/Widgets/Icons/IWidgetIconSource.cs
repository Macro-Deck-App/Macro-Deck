namespace MacroDeckHost.Application.Widgets.Icons;

/// <summary>The bytes behind one resolved widget icon.</summary>
public sealed record WidgetIconImage(Stream Content, string MediaType);

/// <summary>
/// Resolves a <see cref="MacroDeckHost.Domain.Widgets.WidgetIconReference" />'s <c>Reference</c> to an
/// image for one provider <see cref="Type" />. The icon-pack catalog is the first and, for now, only
/// implementation - <see cref="IWidgetIconSourceRegistry" /> is what lets a future provider (a plugin
/// asset, an integration-owned image, ...) plug in without every call site learning a new reference
/// shape.
/// </summary>
public interface IWidgetIconSource
{
	/// <summary>The provider type this source answers for, e.g. <c>"icon-pack"</c>.</summary>
	string Type { get; }

	/// <summary>
	/// A cheap, stable identity for <paramref name="reference" />'s current bytes - a content hash, not
	/// necessarily the bytes themselves - so a caller can tell whether it needs to re-fetch. <c>null</c>
	/// when the reference does not resolve to anything.
	/// </summary>
	string? GetVersion(string reference);

	/// <summary>
	/// Reads <paramref name="reference" />'s image at (up to) <paramref name="size" />, or its first
	/// frame alone when <paramref name="staticFrame" /> is set and the icon is animated. <c>null</c> when
	/// the reference does not resolve to anything - never a thrown exception, since an unresolvable icon
	/// must leave a usable widget behind.
	/// </summary>
	Task<WidgetIconImage?> GetImageAsync(string reference,
		int size,
		bool acceptWebp,
		bool staticFrame,
		CancellationToken cancellationToken);
}
