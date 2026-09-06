using System.Text.Json.Nodes;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Profiles;

public static class WidgetFontFaceMigration
{
	public static bool Normalize(IReadOnlyCollection<FolderEntity> folders, IFontCatalog fontCatalog)
	{
		var changed = false;
		foreach (var widget in folders.SelectMany(folder => folder.Widgets))
		{
			changed |= Normalize(widget, fontCatalog);
		}

		return changed;
	}

	public static bool Normalize(WidgetEntity widget, IFontCatalog fontCatalog)
	{
		if (widget.Type != WidgetTypeIds.ActionButton)
		{
			return false;
		}

		var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
		var changed = MigrateNode(data, fontCatalog);

		if (data["states"] is JsonObject states)
		{
			changed |= states["off"] is JsonObject off && MigrateNode(off, fontCatalog);
			changed |= states["on"] is JsonObject on && MigrateNode(on, fontCatalog);
		}

		if (!changed)
		{
			return false;
		}

		widget.Data = data.ToJsonString();
		return true;
	}

	private static bool MigrateNode(JsonObject node, IFontCatalog fontCatalog)
	{
		if (!node.ContainsKey("fontFamily") && !node.ContainsKey("fontBold") && !node.ContainsKey("fontItalic"))
		{
			return false;
		}

		var family = ReadString(node, "fontFamily");
		var bold = ReadBool(node, "fontBold");
		var italic = ReadBool(node, "fontItalic");

		node.Remove("fontFamily");
		node.Remove("fontBold");
		node.Remove("fontItalic");

		if (!string.IsNullOrWhiteSpace(family))
		{
			node["fontFaceId"] = JsonValue.Create(ResolveFaceId(family, bold, italic, fontCatalog));
		}

		return true;
	}

	private static string ResolveFaceId(string family, bool bold, bool italic, IFontCatalog fontCatalog)
	{
		var faces = fontCatalog.GetFaces()
			.Where(face => string.Equals(face.Family, family, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (faces.Count == 0)
		{
			// The family does not resolve on this machine. Re-pointing it at a catalog face - even the
			// closest one - would silently swap the user's chosen family for whatever happens to be
			// installed, so the id is built deterministically instead and rendered with the platform
			// fallback. It stays stable and recognisable if the profile later reaches a machine that
			// does have the font.
			return FontFaceIdentity.BuildLegacy(family, bold, italic);
		}

		var targetWeight = bold ? FontFaceIdentity.BoldWeight : FontFaceIdentity.RegularWeight;
		var targetSlant = italic ? FontFaceIdentity.ItalicSlant : FontFaceIdentity.UprightSlant;

		// Nearest match is scoped to this one family's real faces, so the stored id always points at an
		// actual face whose catalog metadata reports its true weight/slant - never a stamped lie about a
		// style the family does not have.
		return faces
			.OrderBy(face => string.Equals(face.Slant, targetSlant, StringComparison.Ordinal) ? 0 : 1)
			.ThenBy(face => Math.Abs(face.Weight - targetWeight))
			.First()
			.FaceId;
	}

	private static string? ReadString(JsonObject node, string key)
		=> node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private static bool ReadBool(JsonObject node, string key)
		=> node[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
