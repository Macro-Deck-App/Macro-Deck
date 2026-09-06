using System.Globalization;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Builds one Action Button's stored JSON from a Macro Deck 2 button: its appearance, its two faces and
/// its flows.
/// </summary>
/// <remarks>
/// Macro Deck 2 always stored an off and an on face, and in practice only one of them was ever edited -
/// the untouched one keeps the plugin's defaults, so "the two faces differ" is true of almost every
/// button and says nothing about whether the button was ever meant to have two states.
///
/// What decides it is whether anything in Macro Deck 2 <em>drove</em> the state: a bound variable, or an
/// action that sets or toggles it. Without one of those the button showed a single face forever, and
/// migrating it into state mode would actively change its behaviour - Macro Deck 3 advances a state-mode
/// button's state on every short press unless that button turns cycling off, or a mapping or provider is
/// authoritative for it (<c>WidgetTriggerService</c>), so the face would start flipping on a button that
/// never had two.
/// </remarks>
internal sealed class MacroDeck2ButtonTranslator
{
	private const string OffStateId = "off";

	private const string OnStateId = "on";

	private readonly Func<string?, Guid?> _resolveIcon;

	private readonly Func<string, string> _resolveVariableName;

	public MacroDeck2ButtonTranslator(Func<string?, Guid?> resolveIcon, Func<string, string> resolveVariableName)
	{
		_resolveIcon = resolveIcon;
		_resolveVariableName = resolveVariableName;
	}

	public JsonObject Build(MacroDeck2Button button, JsonArray flows)
	{
		var off = Face(button.IconOff, button.BackColorOff, button.LabelOff);
		var on = Face(button.IconOn, button.BackColorOn, button.LabelOn);

		var stateBinding = NormalizeBinding(button.StateBindingVariable);
		var data = RequiresStateMode(button, stateBinding)
			? WithStates(off, on, button, stateBinding)
			: Merge([], button.State ? on : off);

		data["flows"] = JsonValue.Create(flows.ToJsonString());
		return data;
	}

	private static bool RequiresStateMode(MacroDeck2Button button, string? stateBinding)
		=> stateBinding is not null || DrivesItsOwnState(button);

	/// <summary>
	/// Whether one of Macro Deck 2's own state actions sits on this button. Matched on the type name
	/// rather than on the translation, because the answer is needed before the flows are built.
	/// </summary>
	private static bool DrivesItsOwnState(MacroDeck2Button button)
		=> new[] { button.Actions, button.ActionsRelease, button.ActionsLongPress, button.ActionsLongPressRelease }
			.SelectMany(actions => actions)
			.Any(action => action.Type is { } type &&
				(type.Contains("ActionButtonSetState", StringComparison.Ordinal) ||
					type.Contains("ActionButtonToggleState", StringComparison.Ordinal)));

	private static JsonObject WithStates(JsonObject off, JsonObject on, MacroDeck2Button button, string? stateBinding)
	{
		var data = new JsonObject
		{
			["stateMode"] = JsonValue.Create(true),
			["activeStateId"] = JsonValue.Create(button.State ? OnStateId : OffStateId),
			["states"] = new JsonArray
			{
				new JsonObject
				{
					["id"] = JsonValue.Create(OffStateId),
					["label"] = JsonValue.Create("Off"),
					["appearance"] = off
				},
				new JsonObject
				{
					["id"] = JsonValue.Create(OnStateId),
					["label"] = JsonValue.Create("On"),
					["appearance"] = on
				}
			}
		};

		if (stateBinding is not null)
		{
			// Behaviour-exact restatement of Macro Deck 2's binding: the variable being true selects the
			// on face, anything else falls back to off.
			data["stateMapping"] = new JsonObject
			{
				["rules"] = new JsonArray
				{
					new JsonObject
					{
						["id"] = JsonValue.Create(Guid.NewGuid().ToString("N")),
						["stateId"] = JsonValue.Create(OnStateId),
						["when"] = new JsonObject
						{
							["kind"] = JsonValue.Create("compare"),
							["id"] = JsonValue.Create(Guid.NewGuid().ToString("N")),
							["operator"] = JsonValue.Create("=="),
							["left"] = new JsonObject { ["$var"] = JsonValue.Create(stateBinding) },
							["right"] = JsonValue.Create(true)
						}
					}
				},
				["fallbackStateId"] = JsonValue.Create(OffStateId)
			};
		}

		return data;
	}

	private JsonObject Face(string? icon, string? backColor, MacroDeck2Label? label)
	{
		var face = new JsonObject();

		var iconId = _resolveIcon(icon);
		if (iconId is { } resolved)
		{
			face["icon"] = WidgetIconReference.IconPack(resolved.ToString()).ToJson();
		}

		var background = MacroDeck2Color.ToHex(backColor);
		if (background is not null)
		{
			face["backgroundColor"] = JsonValue.Create(background);
		}

		if (label is null)
		{
			return face;
		}

		if (!string.IsNullOrEmpty(label.LabelText))
		{
			face["label"] = JsonValue.Create(RewriteVariables(label.LabelText));
		}

		var labelColor = MacroDeck2Color.ToHex(label.LabelColor);
		if (labelColor is not null)
		{
			face["labelColor"] = JsonValue.Create(labelColor);
		}

		face["labelPosition"] = JsonValue.Create(MapLabelPosition(label.LabelPosition));

		if (label.Size > 0)
		{
			face["fontSize"] = JsonValue.Create(MapFontSize(label.Size));
		}

		return face;
	}

	/// <summary>
	/// Macro Deck 2 sizes a label in points against a fixed button bitmap; Macro Deck 3 sizes it as a
	/// percentage of the cell so it survives resizing. The reference button was 100 px tall, which makes
	/// a point size the same number of percent, clamped to what the editor itself allows.
	/// </summary>
	private static double MapFontSize(float size) => Math.Clamp(Math.Round(size, 1), 4, 60);

	private static string MapLabelPosition(int position)
		=> position switch
		{
			0 => "top",
			1 => "center",
			_ => "bottom"
		};

	private static JsonObject Merge(JsonObject target, JsonObject source)
	{
		foreach (var (key, value) in source)
		{
			target[key] = value?.DeepClone();
		}

		return target;
	}

	private string? NormalizeBinding(string? variable)
		=> string.IsNullOrWhiteSpace(variable) ? null : _resolveVariableName(variable.Trim());

	/// <summary>Rewrites <c>{name}</c> placeholders onto the names the variables were created under.</summary>
	private string RewriteVariables(string text)
		=> MacroDeck2VariablePlaceholder.Rewrite(text, _resolveVariableName);
}

internal static class MacroDeck2VariablePlaceholder
{
	public static string Rewrite(string text, Func<string, string> resolve)
	{
		if (string.IsNullOrEmpty(text) || !text.Contains('{', StringComparison.Ordinal))
		{
			return text;
		}

		var builder = new System.Text.StringBuilder(text.Length);
		var index = 0;
		while (index < text.Length)
		{
			var open = text.IndexOf('{', index);
			if (open < 0)
			{
				builder.Append(text, index, text.Length - index);
				break;
			}

			var close = text.IndexOf('}', open + 1);
			if (close < 0)
			{
				builder.Append(text, index, text.Length - index);
				break;
			}

			builder.Append(text, index, open - index);
			var name = text[(open + 1)..close];
			builder.Append(CultureInfo.InvariantCulture, $"{{{resolve(name)}}}");
			index = close + 1;
		}

		return builder.ToString();
	}
}
