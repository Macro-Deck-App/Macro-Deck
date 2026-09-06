using System.Text.Json.Nodes;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Profiles;

/// <summary>
/// Rewrites every legacy bare <c>iconId</c> a widget stores into the typed <c>icon</c> shape
/// (<see cref="WidgetIconReference" />), in place. Reading a legacy <c>iconId</c> stays tolerant forever
/// (see <see cref="WidgetIconReference.Read" />), so this migration is purely cosmetic for rendering - it
/// exists so a save never re-persists the retired shape once a widget has passed through here.
///
/// <para>
/// Broader than <see cref="WidgetFontFaceMigration" />: that one only ever walks the root and the legacy
/// <c>states.off</c>/<c>states.on</c> object form. An icon can additionally live on the current
/// <c>states[]</c> array's <c>appearance.iconId</c>, on the legacy top-level <c>offState</c>/<c>onState</c>
/// aliases, and on a Slider's root - so this migration walks all of those directly, rather than going
/// through <see cref="ActionButtonStateModel.Upgrade" /> first, exactly like the font migration does not.
/// </para>
/// </summary>
public static class WidgetIconReferenceMigration
{
	public static bool Normalize(IReadOnlyCollection<FolderEntity> folders)
	{
		var changed = false;
		foreach (var widget in folders.SelectMany(folder => folder.Widgets))
		{
			changed |= Normalize(widget);
		}

		return changed;
	}

	public static bool Normalize(WidgetEntity widget)
	{
		if (widget.Type != WidgetTypeIds.ActionButton && widget.Type != WidgetTypeIds.Slider)
		{
			return false;
		}

		var data = WidgetAppearanceJson.ParseDataBag(widget.Data);
		var changed = widget.Type == WidgetTypeIds.ActionButton ? NormalizeActionButton(data) : NormalizeSlider(data);

		if (!changed)
		{
			return false;
		}

		widget.Data = data.ToJsonString();
		return true;
	}

	private static bool NormalizeSlider(JsonObject data) => WidgetIconReference.MigrateNode(data);

	private static bool NormalizeActionButton(JsonObject data)
	{
		var changed = WidgetIconReference.MigrateNode(data);

		changed |= data["offState"] is JsonObject offState && WidgetIconReference.MigrateNode(offState);
		changed |= data["onState"] is JsonObject onState && WidgetIconReference.MigrateNode(onState);

		switch (data["states"])
		{
			case JsonObject legacyPair:
				// The pre-#612 toggle shape: an { off, on } object rather than the array of named states.
				changed |= legacyPair["off"] is JsonObject off && WidgetIconReference.MigrateNode(off);
				changed |= legacyPair["on"] is JsonObject on && WidgetIconReference.MigrateNode(on);
				break;

			case JsonArray states:
				foreach (var entry in states.OfType<JsonObject>())
				{
					changed |= entry["appearance"] is JsonObject appearance &&
						WidgetIconReference.MigrateNode(appearance);
				}

				break;
		}

		if (data["manualStateBackup"] is JsonObject backup && backup["states"] is JsonArray backedUpStates)
		{
			foreach (var entry in backedUpStates.OfType<JsonObject>())
			{
				changed |= entry["appearance"] is JsonObject appearance && WidgetIconReference.MigrateNode(appearance);
			}
		}

		return changed;
	}
}
