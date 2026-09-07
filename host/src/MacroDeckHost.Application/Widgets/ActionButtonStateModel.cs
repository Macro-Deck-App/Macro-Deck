using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

public sealed record ActionButtonStateEntry(string Id, string Label, JsonObject? Appearance);

public sealed record ActionButtonStateMappingRule(string Id, string StateId, JsonElement When);

public sealed record ActionButtonStateMapping(
	IReadOnlyList<ActionButtonStateMappingRule> Rules,
	string? FallbackStateId);

public sealed record ActionButtonStateProviderOption(string Id, string Label);

public sealed record ActionButtonStateProvider(
	string BlockId,
	string? IntegrationId,
	string? ActionId,
	string? ActionLabel,
	IReadOnlyList<ActionButtonStateProviderOption> States);

/// <summary>
/// Marks one of this button's own action-flow blocks as the authority for its rendered icon (issue #425).
/// Independent of <see cref="ActionButtonStateProvider" /> - a block may be either, both or neither - and
/// carries no cached value of its own: unlike a state provider's declared state set, there is nothing
/// useful to cache for offline fallback, since an icon provider that cannot answer falls back to the
/// configured appearance icon rather than holding a stale image.
/// </summary>
public sealed record ActionButtonIconProvider(
	string BlockId,
	string? IntegrationId,
	string? ActionId,
	string? ActionLabel);

public sealed record ActionButtonManualStateBackup(
	IReadOnlyList<ActionButtonStateEntry> States,
	ActionButtonStateMapping? StateMapping,
	string? ActiveStateId);

/// <summary>
/// The one read model every host consumer of action-button state uses instead of independently
/// re-deriving <c>mode == "toggle"</c>. Also performs the upgrade of pre-#612 stored data (a boolean
/// toggle with an off/on face pair) into the current N-state shape, entirely in memory.
/// </summary>
public sealed class ActionButtonStateModel
{
	/// <summary>
	/// The literal ids a legacy toggle button's two faces are migrated onto. They must stay exactly
	/// "off"/"on": a saved flow parameter (e.g. a "Set Button State" action's `state: "off"`) and a
	/// saved appearance-action `state` selector both already reference these strings, so the upgrade
	/// has to be an identity mapping for them to keep resolving.
	/// </summary>
	public const string DefaultOffStateId = "off";

	public const string DefaultOnStateId = "on";

	/// <summary>
	/// The most states one button may be configured with (issue #673). A button past this size stops
	/// being editable at all: its configuration tree grows until the editor can no longer be opened.
	/// Enforced where states are created - the editor's own add control and the widget save gate - and
	/// deliberately not on the read path, so an existing button that already holds more still loads.
	/// </summary>
	public const int MaxStates = 25;

	public required bool StateMode { get; init; }

	public required IReadOnlyList<ActionButtonStateEntry> States { get; init; }

	public ActionButtonStateMapping? StateMapping { get; init; }

	/// <summary>
	/// Whether a short press on the button advances it to its next state on its own. On unless the
	/// button has explicitly turned it off, so an absent key - every button saved before the flag
	/// existed included - keeps stepping through its states on tap the way it always did. Only ever
	/// consulted while nothing else is authoritative for the state - a provider or a mapping refuses
	/// the write regardless.
	/// </summary>
	public required bool CycleStatesOnPress { get; init; }

	public ActionButtonStateProvider? StateProvider { get; init; }

	public ActionButtonIconProvider? IconProvider { get; init; }

	public string? ActiveStateId { get; init; }

	public ActionButtonManualStateBackup? ManualStateBackup { get; init; }

	/// <summary>The fully upgraded and structurally-normalized data bag this model was read from, for callers that also need a root-level appearance field.</summary>
	public required JsonObject Data { get; init; }

	public ActionButtonStateEntry? FindState(string? id)
		=> id is null ? null : States.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

	public static ActionButtonStateModel Read(string? data) => Read(ActionButtonStateJson.ParseDataBag(data));

	public static ActionButtonStateModel Read(JsonObject data)
	{
		var normalized = (JsonObject)data.DeepClone();
		Upgrade(normalized);
		ActionButtonStateJson.NormalizeStatesArray(normalized);
		return Build(normalized);
	}

	/// <summary>
	/// The only place the legacy-data upgrade is implemented. <see cref="ActionButtonStateJson.Normalize" />
	/// (run on every host-controlled write) calls this same routine rather than re-deriving the rules,
	/// so a read and a save can never disagree about what old data means.
	/// </summary>
	internal static void Upgrade(JsonObject data)
	{
		var wasToggleMode = string.Equals(ReadString(data, "mode"), "toggle", StringComparison.OrdinalIgnoreCase);
		var hasExplicitStateMode = data["stateMode"] is JsonValue explicitMode && explicitMode.TryGetValue<bool>(out _);
		if (!hasExplicitStateMode && wasToggleMode)
		{
			data["stateMode"] = JsonValue.Create(true);
		}

		UpgradeStatesArray(data, wasToggleMode);

		if (data["activeStateId"] is null &&
			data["isToggled"] is JsonValue toggledValue &&
			toggledValue.TryGetValue<bool>(out var isToggled))
		{
			data["activeStateId"] = JsonValue.Create(isToggled ? DefaultOnStateId : DefaultOffStateId);
		}

		if (data["stateMapping"] is null && data["stateBinding"] is JsonObject stateBindingNode)
		{
			// Behaviour-exact translation of the old binding: the same condition, true selects "on",
			// false falls back to "off" - synced buttons keep working across the upgrade.
			data["stateMapping"] = new JsonObject
			{
				["rules"] = new JsonArray
				{
					new JsonObject
					{
						["id"] = JsonValue.Create(Guid.NewGuid().ToString("N")),
						["stateId"] = JsonValue.Create(DefaultOnStateId),
						["when"] = stateBindingNode.DeepClone()
					}
				},
				["fallbackStateId"] = JsonValue.Create(DefaultOffStateId)
			};
		}

		data.Remove("mode");
		data.Remove("isToggled");
		data.Remove("stateBinding");
		data.Remove("offState");
		data.Remove("onState");
	}

	private static void UpgradeStatesArray(JsonObject data, bool wasToggleMode)
	{
		if (data["states"] is JsonObject legacyPairObject)
		{
			data["states"]
				= BuildStatePair(legacyPairObject["off"] as JsonObject, legacyPairObject["on"] as JsonObject);
			return;
		}

		if (data["states"] is not null)
		{
			return;
		}

		var offAlias = data["offState"] as JsonObject;
		var onAlias = data["onState"] as JsonObject;
		if (offAlias is not null || onAlias is not null)
		{
			data["states"] = BuildStatePair(offAlias, onAlias);
			return;
		}

		if (wasToggleMode)
		{
			// A bare "mode": "toggle" with no explicit states object and no off/on aliases still
			// means exactly two states in the old model - neither face ever had appearance of its
			// own, so both simply fell back to the root fields the way toggle mode always did. A
			// stateBinding on data shaped exactly like this depends on "on" resolving to a real
			// state to mean anything at all.
			data["states"] = BuildStatePair(null, null);
		}
	}

	/// <summary>The default Off/On pair, optionally carrying appearance across from the legacy faces. A
	/// legacy face's own bare <c>iconId</c> is migrated to the typed <c>icon</c> shape as it is carried
	/// across, so this stays the one place a copied face can reintroduce the retired key.</summary>
	internal static JsonArray BuildStatePair(JsonObject? off, JsonObject? on)
		=> new()
		{
			new JsonObject
			{
				["id"] = JsonValue.Create(DefaultOffStateId),
				["label"] = JsonValue.Create("Off"),
				["appearance"] = MigratedAppearance(off)
			},
			new JsonObject
			{
				["id"] = JsonValue.Create(DefaultOnStateId),
				["label"] = JsonValue.Create("On"),
				["appearance"] = MigratedAppearance(on)
			}
		};

	private static JsonObject MigratedAppearance(JsonObject? face)
	{
		var appearance = (JsonObject?)face?.DeepClone() ?? [];
		WidgetIconReference.MigrateNode(appearance);
		return appearance;
	}

	private static ActionButtonStateModel Build(JsonObject data)
	{
		var stateMode = data["stateMode"] is JsonValue modeValue &&
			modeValue.TryGetValue<bool>(out var modeFlag) &&
			modeFlag;

		return new ActionButtonStateModel
		{
			StateMode = stateMode,
			States = ReadStates(data["states"] as JsonArray),
			CycleStatesOnPress = ReadBool(data, "cycleStatesOnPress", whenAbsent: true),
			StateMapping = ReadMapping(data["stateMapping"] as JsonObject),
			StateProvider = ReadProvider(data["stateProvider"] as JsonObject),
			IconProvider = ReadIconProvider(data["iconProvider"] as JsonObject),
			ActiveStateId = ReadString(data, "activeStateId"),
			ManualStateBackup = ReadBackup(data["manualStateBackup"] as JsonObject),
			Data = data
		};
	}

	private static List<ActionButtonStateEntry> ReadStates(JsonArray? array)
	{
		if (array is null || array.Count == 0)
		{
			return [];
		}

		var result = new List<ActionButtonStateEntry>(array.Count);
		foreach (var node in array)
		{
			if (node is not JsonObject entry)
			{
				continue;
			}

			var id = ReadString(entry, "id");
			if (id is null)
			{
				continue;
			}

			var label = ReadString(entry, "label") ?? id;
			result.Add(new ActionButtonStateEntry(id, label, entry["appearance"] as JsonObject));
		}

		return result;
	}

	private static ActionButtonStateMapping? ReadMapping(JsonObject? mapping)
	{
		if (mapping is null)
		{
			return null;
		}

		var rules = new List<ActionButtonStateMappingRule>();
		if (mapping["rules"] is JsonArray ruleArray)
		{
			foreach (var node in ruleArray)
			{
				if (node is not JsonObject rule)
				{
					continue;
				}

				var stateId = ReadString(rule, "stateId");
				if (string.IsNullOrEmpty(stateId))
				{
					continue;
				}

				var id = ReadString(rule, "id") ?? Guid.NewGuid().ToString("N");
				var when = rule["when"] is { } whenNode ? whenNode.Deserialize<JsonElement>() : default;
				rules.Add(new ActionButtonStateMappingRule(id, stateId, when));
			}
		}

		// A mapping with no usable rule can only ever answer its own fallback - exactly what
		// activeStateId already says - so it is not authoritative and is read as absent rather than as
		// a mapping that permanently locks out Set/Cycle State.
		if (rules.Count == 0)
		{
			return null;
		}

		return new ActionButtonStateMapping(rules, ReadString(mapping, "fallbackStateId"));
	}

	private static ActionButtonStateProvider? ReadProvider(JsonObject? provider)
	{
		if (provider is null)
		{
			return null;
		}

		var blockId = ReadString(provider, "blockId");
		if (string.IsNullOrEmpty(blockId))
		{
			return null;
		}

		var states = new List<ActionButtonStateProviderOption>();
		if (provider["states"] is JsonArray stateArray)
		{
			foreach (var node in stateArray)
			{
				if (node is not JsonObject entry)
				{
					continue;
				}

				var id = ReadString(entry, "id");
				if (id is null)
				{
					continue;
				}

				states.Add(new ActionButtonStateProviderOption(id, ReadString(entry, "label") ?? id));
			}
		}

		return new ActionButtonStateProvider(blockId,
			ReadString(provider, "integrationId"),
			ReadString(provider, "actionId"),
			ReadString(provider, "actionLabel"),
			states);
	}

	private static ActionButtonIconProvider? ReadIconProvider(JsonObject? provider)
	{
		if (provider is null)
		{
			return null;
		}

		var blockId = ReadString(provider, "blockId");
		if (string.IsNullOrEmpty(blockId))
		{
			return null;
		}

		return new ActionButtonIconProvider(blockId,
			ReadString(provider, "integrationId"),
			ReadString(provider, "actionId"),
			ReadString(provider, "actionLabel"));
	}

	private static ActionButtonManualStateBackup? ReadBackup(JsonObject? backup)
	{
		if (backup is null)
		{
			return null;
		}

		return new ActionButtonManualStateBackup(ReadStates(backup["states"] as JsonArray),
			ReadMapping(backup["stateMapping"] as JsonObject),
			ReadString(backup, "activeStateId"));
	}

	private static string? ReadString(JsonObject data, string key)
		=> data[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private static bool ReadBool(JsonObject data, string key, bool whenAbsent)
		=> data[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : whenAbsent;
}
