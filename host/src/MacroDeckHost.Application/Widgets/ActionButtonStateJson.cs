using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

/// <summary>
/// Structural sanitization and mutation of an Action Button's stored JSON. <see cref="Normalize" /> is
/// called on every host-controlled write, so legacy data gets upgraded (via
/// <see cref="ActionButtonStateModel.Upgrade" />, the single place that logic lives) and the
/// <c>states</c> array is kept structurally sound, the moment anything saves it - not only when a
/// client happens to send already-clean data.
/// </summary>
public static partial class ActionButtonStateJson
{
	[GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
	private static partial Regex StateIdPattern();

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

	/// <summary>
	/// Upgrades legacy data in place, then enforces the structural invariants on <c>states</c> (id
	/// charset, uniqueness, dropping non-object entries) - mirroring the Angular client's
	/// <c>normalizeStates</c> exactly, so the two never drift. Mutates and returns <paramref name="data" />
	/// itself rather than building a fresh object, so unknown keys neither side understands survive
	/// untouched. Idempotent: running it twice never appends a second mapping rule or re-adds a
	/// dropped key, because it only ever inspects and rewrites the known keys it owns.
	/// </summary>
	public static JsonObject Normalize(JsonObject data)
	{
		ActionButtonStateModel.Upgrade(data);
		NormalizeStatesArray(data);

		// A leftover manualStateBackup with no active provider is never written by anything except
		// provider adoption, so its presence unambiguously means a provider was removed and the
		// manual configuration is owed back - restoring it here can never destroy hand-authored data.
		if (data["stateProvider"] is null && data["manualStateBackup"] is JsonObject)
		{
			RestoreManualBackup(data);
			NormalizeStatesArray(data);
		}

		PruneDegenerateMapping(data);
		ActionButtonIconJson.Normalize(data);

		return data;
	}

	/// <summary>
	/// Drops a <c>stateMapping</c> that can never be authoritative: a JSON object with no rule carrying
	/// a non-empty <c>stateId</c>. Removing a mapping is purely additive to begin with - configuring one
	/// never rewrites <c>states</c> or <c>activeStateId</c> - so deleting a degenerate leftover key IS
	/// the full restoration to the pre-mapping widget, and is exactly what heals a button that "Cycle
	/// Button State" permanently refused after a mapping was added then removed. A <c>stateMapping</c> of
	/// any other shape (string, array, ...) is left alone so schema validation keeps rejecting it as malformed -
	/// including a <c>rules</c> field that itself is not an array, which must surface the same way.
	/// </summary>
	internal static void PruneDegenerateMapping(JsonObject data)
	{
		if (data["stateMapping"] is not JsonObject mapping)
		{
			return;
		}

		var rulesNode = mapping["rules"];
		if (rulesNode is not null && rulesNode is not JsonArray)
		{
			return;
		}

		var hasUsableRule = rulesNode is JsonArray rules &&
			rules.OfType<JsonObject>()
				.Any(rule => rule["stateId"] is JsonValue stateId &&
					stateId.TryGetValue<string>(out var id) &&
					!string.IsNullOrEmpty(id));

		if (!hasUsableRule)
		{
			data.Remove("stateMapping");
		}
	}

	internal static void NormalizeStatesArray(JsonObject data)
	{
		if (data["states"] is not JsonArray states)
		{
			return;
		}

		var seenIds = new HashSet<string>(StringComparer.Ordinal);
		var normalized = new JsonArray();
		foreach (var entry in states)
		{
			if (entry is not JsonObject stateObject)
			{
				continue;
			}

			var candidate = stateObject["id"] is JsonValue idValue &&
				idValue.TryGetValue<string>(out var raw) &&
				IsValidId(raw)
					? raw
					: null;
			var id = candidate is not null && seenIds.Add(candidate) ? candidate : GenerateUniqueId(seenIds);

			var clone = (JsonObject)stateObject.DeepClone();
			clone["id"] = JsonValue.Create(id);
			normalized.Add(clone);
		}

		data["states"] = normalized;
	}

	public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && StateIdPattern().IsMatch(id);

	/// <summary>Sets the active state unconditionally - callers must already have checked no provider or mapping is authoritative.</summary>
	public static void SetActiveState(JsonObject data, string stateId) =>
		data["activeStateId"] = JsonValue.Create(stateId);

	/// <summary>Advances to the next state in <c>states</c> order, wrapping past the last. Returns the new id, or null if there are no states.</summary>
	public static string? AdvanceState(JsonObject data)
	{
		if (data["states"] is not JsonArray states || states.Count == 0)
		{
			return null;
		}

		var ids = new List<string>();
		foreach (var node in states)
		{
			if (node is JsonObject entry && entry["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var id))
			{
				ids.Add(id);
			}
		}

		if (ids.Count == 0)
		{
			return null;
		}

		var current = data["activeStateId"] is JsonValue currentValue &&
			currentValue.TryGetValue<string>(out var currentId)
				? currentId
				: null;
		var next = NextStateId(ids, current);
		data["activeStateId"] = JsonValue.Create(next);
		return next;
	}

	/// <summary>
	/// The deterministic wrap-around advance rule itself, shared by <see cref="AdvanceState" /> and
	/// <c>ActionButtonWidgetSession</c>'s own optimistic advance so the two can never disagree on what
	/// "next" means for the same inputs. <paramref name="current" /> is <c>null</c> exactly when no state
	/// has ever been explicitly chosen (never defaulted to the first state - that fallback belongs to
	/// rendering, e.g. <c>ActionButtonWidgetData.InitialStateId</c>, not to this rule), which is what
	/// makes a freshly placed button's first advance land on <paramref name="ids" />[0] rather than [1].
	/// </summary>
	public static string? NextStateId(IReadOnlyList<string> ids, string? current)
	{
		if (ids.Count == 0)
		{
			return null;
		}

		var index = current is not null ? IndexOfOrdinal(ids, current) : -1;

		return ids[(index + 1) % ids.Count];
	}

	private static int IndexOfOrdinal(IReadOnlyList<string> ids, string value)
	{
		for (var i = 0; i < ids.Count; i++)
		{
			if (string.Equals(ids[i], value, StringComparison.Ordinal))
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// Reconciles the states a provider now declares with what is currently configured, carrying each
	/// state's authored appearance forward by matching id and refreshing the <c>stateProvider.states</c>
	/// cache. The result is exactly the provided set: a provider defines the complete set of states, so
	/// keeping an id it no longer offers would show a state nothing can ever report. Manual
	/// configuration is unaffected - it lives in <c>manualStateBackup</c> until the provider is removed.
	///
	/// The labels land in stored widget data, so <paramref name="localization" /> resolves each one into
	/// finished text: the stored shape must stay a plain string, never a localization reference.
	/// </summary>
	public static bool AdoptProviderStates(
		JsonObject data,
		IReadOnlyList<ActionStateDefinition> provided,
		ILocalizationResolver localization,
		string? culture)
	{
		var currentEntries = new List<(string Id, string Label, JsonNode? Appearance)>();
		if (data["states"] is JsonArray current)
		{
			foreach (var node in current)
			{
				if (node is not JsonObject entry)
				{
					continue;
				}

				var id = entry["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var s) ? s : null;
				if (id is null)
				{
					continue;
				}

				var label = entry["label"] is JsonValue labelValue && labelValue.TryGetValue<string>(out var l)
					? l
					: id;
				currentEntries.Add((id, label, entry["appearance"]?.DeepClone()));
			}
		}

		var currentById = new Dictionary<string, (string Label, JsonNode? Appearance)>(StringComparer.Ordinal);
		foreach (var entry in currentEntries)
		{
			currentById[entry.Id] = (entry.Label, entry.Appearance);
		}

		var providedIds = new HashSet<string>(provided.Select(p => p.Id), StringComparer.Ordinal);

		var adopted = new JsonArray();
		foreach (var state in provided)
		{
			// The provider's defaults only ever apply to a state this button has not seen before -
			// carrying an existing appearance forward untouched is what keeps a reconfiguration from
			// restyling states the user has already made their own.
			var appearance = currentById.TryGetValue(state.Id, out var existing)
				? existing.Appearance
				: DefaultAppearanceOf(state, localization, culture);
			adopted.Add(new JsonObject
			{
				["id"] = JsonValue.Create(state.Id),
				["label"] = JsonValue.Create(localization.Resolve(state.Label, culture)),
				["appearance"] = appearance ?? new JsonObject()
			});
		}

		var statesChanged = data["states"] is not JsonArray existingStates ||
			!JsonNode.DeepEquals(existingStates, adopted);
		data["states"] = adopted;

		var changed = statesChanged;
		if (data["stateProvider"] is JsonObject providerObject)
		{
			var cache = new JsonArray();
			foreach (var state in provided)
			{
				cache.Add(new JsonObject
				{
					["id"] = JsonValue.Create(state.Id),
					["label"] = JsonValue.Create(localization.Resolve(state.Label, culture))
				});
			}

			changed |= providerObject["states"] is not JsonArray existingCache ||
				!JsonNode.DeepEquals(existingCache, cache);
			providerObject["states"] = cache;
		}

		return changed;
	}

	/// <summary>
	/// A provider's suggested starting appearance for a state, as stored appearance JSON. The button's
	/// text falls back to the state's own label, so a provider gets readable buttons without repeating
	/// every name twice; a provider that wants an icon-only state says so with an empty label.
	/// </summary>
	private static JsonObject? DefaultAppearanceOf(
		ActionStateDefinition state,
		ILocalizationResolver localization,
		string? culture)
	{
		var appearance = state.DefaultAppearance;
		var label = appearance?.Label ?? localization.Resolve(state.Label, culture);
		var result = new JsonObject { ["label"] = JsonValue.Create(label) };

		if (!string.IsNullOrWhiteSpace(appearance?.BackgroundColor))
		{
			result["backgroundColor"] = JsonValue.Create(appearance.BackgroundColor);
		}

		if (!string.IsNullOrWhiteSpace(appearance?.LabelColor))
		{
			result["labelColor"] = JsonValue.Create(appearance.LabelColor);
		}

		if (!string.IsNullOrWhiteSpace(appearance?.IconId))
		{
			result["icon"] = WidgetIconReference.IconPack(appearance.IconId).ToJson();
		}

		return result;
	}

	/// <summary>
	/// Restores the stashed manual configuration (states, mapping, active state) and drops both
	/// <c>stateProvider</c> and the stash itself. A no-op when nothing is stashed.
	/// </summary>
	public static bool RestoreManualBackup(JsonObject data)
	{
		if (data["manualStateBackup"] is not JsonObject backup)
		{
			return false;
		}

		// An empty stash restores the default pair rather than zero states, matching what the editor
		// does: leaving a state-mode button with no states at all makes it unresolvable, so it would
		// silently drop out of state mode entirely.
		if (backup["states"] is JsonArray states && states.Count > 0)
		{
			data["states"] = states.DeepClone();
		}
		else
		{
			data["states"] = ActionButtonStateModel.BuildStatePair(null, null);
		}

		if (backup["stateMapping"] is JsonObject mapping)
		{
			data["stateMapping"] = mapping.DeepClone();
		}
		else
		{
			data.Remove("stateMapping");
		}

		if (backup["activeStateId"] is JsonValue activeId)
		{
			data["activeStateId"] = activeId.DeepClone();
		}
		else
		{
			data.Remove("activeStateId");
		}

		data.Remove("stateProvider");
		data.Remove("manualStateBackup");
		return true;
	}

	/// <summary>
	/// A stored <c>stateMapping</c> must resolve <c>fallbackStateId</c> to a real state, or the save is
	/// rejected outright rather than silently coerced - a mapping with no usable fallback is a save-time
	/// authoring error, not something to guess at.
	/// </summary>
	public static bool HasResolvableFallback(JsonObject data)
	{
		if (data["stateMapping"] is not JsonObject mapping)
		{
			return true;
		}

		var fallbackStateId = mapping["fallbackStateId"] is JsonValue value && value.TryGetValue<string>(out var id)
			? id
			: null;
		if (string.IsNullOrEmpty(fallbackStateId))
		{
			return false;
		}

		if (data["states"] is not JsonArray states)
		{
			return false;
		}

		foreach (var node in states)
		{
			if (node is JsonObject entry &&
				entry["id"] is JsonValue idValue &&
				idValue.TryGetValue<string>(out var stateId) &&
				string.Equals(stateId, fallbackStateId, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static string GenerateUniqueId(HashSet<string> seenIds)
	{
		string id;
		do
		{
			id = $"state-{Guid.NewGuid():N}"[..20];
		} while (!seenIds.Add(id));

		return id;
	}
}
