using System.Text.Json.Nodes;

namespace MacroDeckHost.Domain.Widgets;

/// <summary>
/// A provider-based widget icon reference: <see cref="Type" /> names the provider ("icon-pack" for the
/// built-in catalog), <see cref="Reference" /> is an opaque, provider-specific identifier - never assumed
/// to be a GUID, since a future provider (a plugin asset, an integration-owned image, ...) may key its
/// icons however it likes.
/// </summary>
public readonly record struct WidgetIconReference(string Type, string Reference)
{
	public const string IconPackType = "icon-pack";

	public static WidgetIconReference IconPack(string reference) => new(IconPackType, reference);

	/// <summary>
	/// Reads the typed <c>icon</c> shape first, falling back to a legacy bare <c>iconId</c> string -
	/// tolerant forever, so data an older host wrote keeps resolving without ever needing a one-time
	/// rewrite to be readable. <c>null</c> when neither is present or usable.
	/// </summary>
	public static WidgetIconReference? Read(JsonNode? icon, string? legacyIconId)
	{
		if (icon is JsonObject iconObject &&
			iconObject["type"] is JsonValue typeValue &&
			typeValue.TryGetValue<string>(out var type) &&
			!string.IsNullOrEmpty(type) &&
			iconObject["reference"] is JsonValue referenceValue &&
			referenceValue.TryGetValue<string>(out var reference) &&
			!string.IsNullOrEmpty(reference))
		{
			return new WidgetIconReference(type, reference);
		}

		return string.IsNullOrWhiteSpace(legacyIconId) ? null : IconPack(legacyIconId.Trim());
	}

	public JsonObject ToJson() => new()
		{ ["type"] = JsonValue.Create(Type), ["reference"] = JsonValue.Create(Reference) };

	/// <summary>
	/// Rewrites a legacy bare <c>iconId</c> key on <paramref name="node" /> into the typed <c>icon</c>
	/// shape in place, leaving an already-typed <c>icon</c> object untouched apart from dropping a
	/// redundant legacy mirror beside it. Idempotent, and the one place this specific rewrite happens -
	/// every migration and write path that needs it calls this rather than re-deriving the shape. Returns
	/// whether <paramref name="node" /> changed.
	/// </summary>
	public static bool MigrateNode(JsonObject node)
	{
		if (node["icon"] is JsonObject)
		{
			// Already typed: nothing to convert, but a stale legacy mirror left beside it must still go -
			// writing always emits `icon` and removes `iconId`.
			return node.Remove("iconId");
		}

		if (node["iconId"] is not JsonValue legacy || !legacy.TryGetValue<string>(out var legacyId))
		{
			return false;
		}

		node.Remove("iconId");

		if (string.IsNullOrWhiteSpace(legacyId))
		{
			return true;
		}

		node["icon"] = IconPack(legacyId.Trim()).ToJson();
		return true;
	}
}
