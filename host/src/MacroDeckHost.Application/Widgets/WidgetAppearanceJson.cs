using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Widgets;

public static class WidgetAppearanceJson
{
	/// <summary>Fixed key used for every widget type that has exactly one appearance (not a State-Mode action button).</summary>
	private const string SingleAppearanceStateId = "off";

	public static bool Apply(
		JsonObject data,
		string type,
		WidgetAppearancePatch patch,
		IReadOnlyCollection<string> stateIds)
	{
		if (patch.IsEmpty || stateIds.Count == 0)
		{
			return false;
		}

		var changed = false;
		foreach (var stateId in stateIds)
		{
			changed |= type switch
			{
				WidgetTypeIds.ActionButton => ApplyToActionButton(data, patch, stateId),
				WidgetTypeIds.Slider => ApplyToSlider(data, patch),
				WidgetTypeIds.HistoryGraph => ApplyToHistoryGraph(data, patch),
				_ => ApplyBorder(data, patch)
			};
		}

		return changed;
	}

	public static bool ClearProperty(
		JsonObject data,
		string type,
		WidgetAppearanceProperty property,
		string stateId)
	{
		if (!SupportedProperties(type).Contains(property))
		{
			return false;
		}

		return type switch
		{
			WidgetTypeIds.ActionButton => ClearOnActionButton(data, property, stateId),
			WidgetTypeIds.HistoryGraph => ClearOn(data, property, labelKey: "title"),
			_ => ClearOn(data, property, labelKey: "label")
		};
	}

	/// <summary>True for any action button with two or more states, whatever their ids - an old plugin keeps the On/Off/Both capability it has today.</summary>
	public static bool HasOnOffStates(JsonObject data, string type) => GetLiveStateIds(data, type).Count >= 2;

	public static IReadOnlyCollection<WidgetAppearanceProperty> SupportedProperties(string type)
		=> type switch
		{
			WidgetTypeIds.ActionButton =>
			[
				WidgetAppearanceProperty.Label,
				WidgetAppearanceProperty.BackgroundColor,
				WidgetAppearanceProperty.LabelColor,
				WidgetAppearanceProperty.Icon,
				WidgetAppearanceProperty.IconDisplay,
				WidgetAppearanceProperty.Font,
				WidgetAppearanceProperty.Border,
				WidgetAppearanceProperty.BorderColor
			],
			WidgetTypeIds.Slider =>
			[
				WidgetAppearanceProperty.Label,
				WidgetAppearanceProperty.BackgroundColor,
				WidgetAppearanceProperty.LabelColor,
				WidgetAppearanceProperty.Icon,
				WidgetAppearanceProperty.Border,
				WidgetAppearanceProperty.BorderColor
			],
			WidgetTypeIds.HistoryGraph =>
			[
				WidgetAppearanceProperty.Label, WidgetAppearanceProperty.Border,
				WidgetAppearanceProperty.BorderColor
			],
			_ => [WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor]
		};

	/// <summary>
	/// Expands a request's state ids (real ids, or the <see cref="WidgetStates" /> sentinels) against
	/// the widget's live state set. <paramref name="liveActiveStateId" /> is the derived-store value for
	/// a mapping/provider-bound button (issue #312) - its displayed state does not live in its data.
	/// </summary>
	public static IReadOnlyList<string> ResolveStates(
		JsonObject data,
		string type,
		IReadOnlyCollection<string> requestedStateIds,
		string? liveActiveStateId = null)
	{
		var liveIds = GetLiveStateIds(data, type);
		if (liveIds.Count == 0)
		{
			return [SingleAppearanceStateId];
		}

		var result = new List<string>();
		foreach (var requested in requestedStateIds)
		{
			if (WidgetStates.IsAll(requested))
			{
				// Every state, including a third: the headline old-plugin compatibility case.
				return liveIds;
			}

			if (WidgetStates.IsCurrent(requested))
			{
				var current = liveActiveStateId is not null && liveIds.Contains(liveActiveStateId)
					? liveActiveStateId
					: ResolveStoredActiveStateId(data, liveIds);
				AddOnce(result, current);
				continue;
			}

			if (liveIds.Contains(requested))
			{
				AddOnce(result, requested);
				continue;
			}

			// Literal "on"/"off" is what an old WidgetStateSelector.On/Off request resolves to before
			// reaching here. A real match already returned above; a button whose real ids are not
			// literally "on"/"off" instead falls back positionally - off to the first state, on to
			// the second - the order the two faces always had when every button was exactly
			// [off, on]. Must stay identical to WidgetStateWireCompatibility's mapping for remote
			// plugins, or the same selector would land on different states depending on the caller.
			// The fallback only makes sense when the button has two states to distinguish; a
			// single-state button leaves "on" unresolved rather than aliasing it to its only face.
			if (requested == "off" && liveIds.Count >= 2)
			{
				AddOnce(result, liveIds[0]);
			}
			else if (requested == "on" && liveIds.Count >= 2)
			{
				AddOnce(result, liveIds[1]);
			}

			// Anything else (an id the button does not have) is a no-op rather than a create.
		}

		return result;
	}

	public static JsonObject ParseDataBag(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		try
		{
			return JsonNode.Parse(json) as JsonObject ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public static string? ReadLabel(JsonObject data, string type, string? stateId)
	{
		if (type != WidgetTypeIds.ActionButton)
		{
			return type is WidgetTypeIds.Slider ? ReadString(data, "label") : null;
		}

		var model = ActionButtonStateModel.Read(data);
		if (model.StateMode &&
			stateId is not null &&
			model.FindState(stateId)?.Appearance is { } appearance)
		{
			var stateLabel = ReadString(appearance, "label");
			if (!string.IsNullOrEmpty(stateLabel))
			{
				return stateLabel;
			}
		}

		return ReadString(model.Data, "label");
	}

	private static void AddOnce(List<string> target, string value)
	{
		if (!target.Contains(value))
		{
			target.Add(value);
		}
	}

	private static List<string> GetLiveStateIds(JsonObject data, string type)
	{
		if (type != WidgetTypeIds.ActionButton)
		{
			return [];
		}

		var model = ActionButtonStateModel.Read(data);
		return model.StateMode ? model.States.Select(s => s.Id).ToList() : [];
	}

	private static string ResolveStoredActiveStateId(JsonObject data, List<string> liveIds)
	{
		// Reads through the same upgraded model GetLiveStateIds used to build liveIds - data itself
		// is never mutated by the read (ActionButtonStateModel.Read operates on a clone), so a raw
		// data["activeStateId"] lookup here would silently miss an id the legacy upgrade only just
		// derived (e.g. from isToggled) and always fall back to the first state instead.
		var stored = ActionButtonStateModel.Read(data).ActiveStateId;
		return stored is not null && liveIds.Contains(stored) ? stored : liveIds[0];
	}

	private static bool ApplyToActionButton(JsonObject data, WidgetAppearancePatch patch, string stateId)
	{
		if (IsStateMode(data))
		{
			if (!TryGetState(data, stateId, create: true, out var stateObject))
			{
				return false;
			}

			var changed = ApplyLabelProperties(stateObject, patch);
			changed |= SetIcon(stateObject, patch.IconId);
			changed |= ApplyIconDisplay(stateObject, patch);
			changed |= ApplyBorder(stateObject, patch);
			return changed;
		}

		var flatChanged = ApplyLabelProperties(data, patch);
		flatChanged |= ApplyBorder(data, patch);

		if (patch.IconId is not null || HasIconDisplay(patch))
		{
			flatChanged |= HoistLegacyMomentaryIcon(data);
		}

		flatChanged |= SetIcon(data, patch.IconId);
		flatChanged |= ApplyIconDisplay(data, patch);

		return flatChanged;
	}

	/// <summary>
	/// Writes the typed <c>icon</c> shape through the unchanged string patch API: <c>null</c> leaves the
	/// icon as it is, an empty string removes the <c>icon</c> key outright (never a stub
	/// <c>{"type":"icon-pack","reference":""}</c>), and any other value sets an icon-pack reference. A
	/// stray legacy <c>iconId</c> key is always dropped alongside, whichever branch runs - writing always
	/// emits <c>icon</c> and removes <c>iconId</c>.
	/// </summary>
	private static bool SetIcon(JsonObject target, string? iconId)
	{
		if (iconId is null)
		{
			return false;
		}

		var legacyRemoved = target.Remove("iconId");

		if (iconId.Length == 0)
		{
			return target.Remove("icon") | legacyRemoved;
		}

		var next = WidgetIconReference.IconPack(iconId).ToJson();
		if (target["icon"] is JsonObject existing && JsonNode.DeepEquals(existing, next))
		{
			return legacyRemoved;
		}

		target["icon"] = next;
		return true;
	}

	private static bool HoistLegacyMomentaryIcon(JsonObject data)
	{
		if (data["states"] is not JsonArray states)
		{
			return false;
		}

		var firstAppearance = states.OfType<JsonObject>().FirstOrDefault()?["appearance"] as JsonObject;
		if (firstAppearance is null)
		{
			return false;
		}

		var migrated = WidgetIconReference.MigrateNode(firstAppearance);
		var rootHoldsIcon = data["icon"] is not null || data["iconId"] is not null || data["iconDisplay"] is not null;
		var changed = migrated;

		foreach (var key in new[] { "icon", "iconDisplay" })
		{
			if (!firstAppearance.ContainsKey(key))
			{
				continue;
			}

			var legacy = firstAppearance[key];
			firstAppearance.Remove(key);
			changed = true;
			if (!rootHoldsIcon)
			{
				data[key] = legacy?.DeepClone();
			}
		}

		return changed;
	}

	private static bool ClearOnActionButton(JsonObject data, WidgetAppearanceProperty property, string stateId)
	{
		if (IsStateMode(data))
		{
			if (!TryGetState(data, stateId, create: true, out var stateObject))
			{
				return false;
			}

			return ClearOn(stateObject, property, labelKey: "label");
		}

		var changed = ClearOn(data, property, labelKey: "label");

		if (property is WidgetAppearanceProperty.Icon or WidgetAppearanceProperty.IconDisplay &&
			data["states"] is JsonArray states &&
			states.OfType<JsonObject>().FirstOrDefault()?["appearance"] is JsonObject firstAppearance)
		{
			changed |= ClearOn(firstAppearance, property, labelKey: "label");
		}

		return changed;
	}

	private static bool ClearOn(JsonObject target, WidgetAppearanceProperty property, string labelKey)
		=> property switch
		{
			WidgetAppearanceProperty.Label => Remove(target, labelKey),
			WidgetAppearanceProperty.BackgroundColor => Remove(target, "backgroundColor"),
			WidgetAppearanceProperty.LabelColor => Remove(target, "labelColor"),
			WidgetAppearanceProperty.Icon => Remove(target, "icon", "iconId"),
			WidgetAppearanceProperty.IconDisplay => Remove(target, "iconDisplay"),
			WidgetAppearanceProperty.Font => Remove(target,
				"fontFaceId",
				"fontSize",
				"textAlign",
				"labelPosition"),
			WidgetAppearanceProperty.Border => Remove(target, "border"),
			WidgetAppearanceProperty.BorderColor => target["border"] is JsonObject border && Remove(border, "color"),
			_ => false
		};

	private static bool Remove(JsonObject target, params string[] keys)
	{
		var changed = false;
		foreach (var key in keys)
		{
			changed |= target.Remove(key);
		}

		return changed;
	}

	private static bool ApplyToSlider(JsonObject data, WidgetAppearancePatch patch)
	{
		var changed = SetIfPresent(data, "label", patch.Label);
		changed |= SetIfPresent(data, "labelColor", patch.LabelColor);
		changed |= SetIfPresent(data, "backgroundColor", patch.BackgroundColor);
		changed |= SetIcon(data, patch.IconId);
		return changed | ApplyBorder(data, patch);
	}

	private static bool ApplyToHistoryGraph(JsonObject data, WidgetAppearancePatch patch)
		=> SetIfPresent(data, "title", patch.Label) | ApplyBorder(data, patch);

	private static bool ApplyLabelProperties(JsonObject target, WidgetAppearancePatch patch)
	{
		var changed = SetIfPresent(target, "label", patch.Label);
		changed |= SetIfPresent(target, "backgroundColor", patch.BackgroundColor);
		changed |= SetIfPresent(target, "labelColor", patch.LabelColor);
		changed |= SetIfPresent(target, "fontFaceId", patch.FontFaceId);
		changed |= SetIfPresent(target, "fontSize", patch.FontSize);
		changed |= SetIfPresent(target, "textAlign", patch.TextAlign);
		changed |= SetIfPresent(target, "labelPosition", patch.LabelPosition);
		return changed;
	}

	private static bool ApplyBorder(JsonObject target, WidgetAppearancePatch patch)
	{
		if (patch.BorderStyle is null && patch.BorderColor is null)
		{
			return false;
		}

		if (target["border"] is not JsonObject border)
		{
			border = [];
			target["border"] = border;
		}

		var changed = SetIfPresent(border, "style", patch.BorderStyle);
		return changed | SetIfPresent(border, "color", patch.BorderColor);
	}

	private static bool ApplyIconDisplay(JsonObject target, WidgetAppearancePatch patch)
	{
		if (!HasIconDisplay(patch))
		{
			return false;
		}

		if (target["iconDisplay"] is not JsonObject display)
		{
			display = [];
			target["iconDisplay"] = display;
		}

		var changed = SetIfPresent(display, "fit", patch.IconFit);
		changed |= SetIfPresent(display, "zoom", patch.IconZoom);
		changed |= SetIfPresent(display, "offsetX", patch.IconOffsetX);
		changed |= SetIfPresent(display, "offsetY", patch.IconOffsetY);
		return changed | SetIfPresent(display, "opacity", patch.IconOpacity);
	}

	private static bool HasIconDisplay(WidgetAppearancePatch patch)
		=> patch.IconFit is not null ||
			patch.IconZoom is not null ||
			patch.IconOffsetX is not null ||
			patch.IconOffsetY is not null ||
			patch.IconOpacity is not null;

	private static bool TryGetState(JsonObject data, string stateId, bool create, out JsonObject stateObject)
	{
		if (data["states"] is not JsonArray states)
		{
			if (!create)
			{
				stateObject = [];
				return false;
			}

			states = [];
			data["states"] = states;
		}

		foreach (var node in states)
		{
			if (node is not JsonObject entry ||
				entry["id"] is not JsonValue idValue ||
				!idValue.TryGetValue<string>(out var id) ||
				!string.Equals(id, stateId, StringComparison.Ordinal))
			{
				continue;
			}

			if (entry["appearance"] is JsonObject existingAppearance)
			{
				stateObject = existingAppearance;
				return true;
			}

			if (!create)
			{
				stateObject = [];
				return false;
			}

			var appearance = new JsonObject();
			entry["appearance"] = appearance;
			stateObject = appearance;
			return true;
		}

		if (!create)
		{
			stateObject = [];
			return false;
		}

		// The target state id is not in the array yet - should not normally happen, since
		// ResolveStates only ever returns live ids, but creating it defensively is safer than
		// silently dropping an appearance change.
		var newAppearance = new JsonObject();
		states.Add(new JsonObject
		{
			["id"] = JsonValue.Create(stateId),
			["label"] = JsonValue.Create(stateId),
			["appearance"] = newAppearance
		});
		stateObject = newAppearance;
		return true;
	}

	private static bool SetIfPresent(JsonObject target, string key, string? value)
	{
		if (value is null)
		{
			return false;
		}

		var node = value.Length == 0 ? null : JsonValue.Create(value);
		if (NodesEqual(target[key], node))
		{
			return false;
		}

		target[key] = node;
		return true;
	}

	private static bool SetIfPresent(JsonObject target, string key, double? value)
	{
		if (value is null)
		{
			return false;
		}

		var node = JsonValue.Create(value.Value);
		if (NodesEqual(target[key], node))
		{
			return false;
		}

		target[key] = node;
		return true;
	}

	private static bool SetIfPresent(JsonObject target, string key, bool? value)
	{
		if (value is null)
		{
			return false;
		}

		var node = JsonValue.Create(value.Value);
		if (NodesEqual(target[key], node))
		{
			return false;
		}

		target[key] = node;
		return true;
	}

	private static bool NodesEqual(JsonNode? left, JsonNode? right)
	{
		if (left is null || right is null)
		{
			return left is null && right is null;
		}

		return string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
	}

	private static string? ReadString(JsonObject data, string key)
		=> data[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private static bool IsStateMode(JsonObject data)
		=> data["stateMode"] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
