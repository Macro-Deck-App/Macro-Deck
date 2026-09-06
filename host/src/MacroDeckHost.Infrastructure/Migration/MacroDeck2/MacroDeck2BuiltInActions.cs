using System.Text.Json;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Sdk.Migration;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>Where a built-in action's own profile is, so folder references can be rewritten.</summary>
internal sealed record MacroDeck2ProfileContext(IReadOnlyDictionary<string, Guid> FolderIds, Guid? RootFolderId);

/// <summary>
/// Translates the actions Macro Deck 2 shipped inside its own executable. Their assembly is "Macro Deck 2"
/// itself, so no integration can claim them - without this every migrated deck's folder navigation, the
/// most common thing on a button, would arrive as a placeholder.
/// </summary>
internal static class MacroDeck2BuiltInActions
{
	private const string DeckIntegrationId = "app.macro-deck.deck";

	private const string WidgetIntegrationId = "app.macro-deck.widget";

	private const string VariablesIntegrationId = "app.macro-deck.variables";

	public static ActionMigrationResult? Migrate(
		string typeName,
		MacroDeck2Action action,
		MacroDeck2ProfileContext context)
		=> typeName switch
		{
			"SuchByte.MacroDeck.Folders.Plugin.FolderSwitcher" => ChangeFolder(action, context),
			"SuchByte.MacroDeck.Folders.Plugin.GoToParentFolder"
				=> new ActionMigrationResult(DeckIntegrationId,
					"go-to-parent",
					action.Name ?? "Go to parent folder",
					Parameters()),
			"SuchByte.MacroDeck.Folders.Plugin.GoToRootFolder" => GoToRoot(action, context),
			"SuchByte.MacroDeck.ActionButton.ActionButtonSetStateOnAction" => SetState(action, "on"),
			"SuchByte.MacroDeck.ActionButton.ActionButtonSetStateOffAction" => SetState(action, "off"),
			"SuchByte.MacroDeck.ActionButton.ActionButtonToggleStateAction" => ToggleState(action),
			"SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Actions.ActionButtonSetBackgroundColorAction"
				=> SetBackgroundColor(action),
			"SuchByte.MacroDeck.Variables.Plugin.ChangeVariableValueAction" => ChangeVariable(action),
			_ => null
		};

	/// <summary>
	/// The folder switcher stores a bare Macro Deck 2 folder id rather than JSON, and that id has to become
	/// the migrated folder's new identity or the button would navigate nowhere.
	/// </summary>
	private static ActionMigrationResult? ChangeFolder(MacroDeck2Action action, MacroDeck2ProfileContext context)
	{
		var sourceId = action.Configuration?.Trim();
		if (string.IsNullOrEmpty(sourceId) || !context.FolderIds.TryGetValue(sourceId, out var folderId))
		{
			return null;
		}

		return new ActionMigrationResult(DeckIntegrationId,
			"change-folder",
			action.Name ?? "Change folder",
			Parameters(("folderId", JsonString(folderId.ToString()))));
	}

	/// <summary>
	/// Macro Deck 3 has no "go to root" action, so the root folder is named outright. That is exact rather
	/// than approximate: a Macro Deck 2 profile has precisely one root.
	/// </summary>
	private static ActionMigrationResult? GoToRoot(MacroDeck2Action action, MacroDeck2ProfileContext context)
		=> context.RootFolderId is not { } rootId
			? null
			: new ActionMigrationResult(DeckIntegrationId,
				"change-folder",
				action.Name ?? "Go to root folder",
				Parameters(("folderId", JsonString(rootId.ToString()))));

	private static ActionMigrationResult SetState(MacroDeck2Action action, string state)
		=> new(WidgetIntegrationId,
			"set-state",
			action.Name ?? $"Set state {state}",
			Parameters(("widget", JsonString(WidgetTargets.Self)), ("state", JsonString(state))));

	private static ActionMigrationResult ToggleState(MacroDeck2Action action)
		=> new(WidgetIntegrationId,
			"toggle-state",
			action.Name ?? "Toggle state",
			Parameters(("widget", JsonString(WidgetTargets.Self))));

	private static ActionMigrationResult? SetBackgroundColor(MacroDeck2Action action)
	{
		if (!TryParse(action.Configuration, out var config))
		{
			return null;
		}

		var color = MacroDeck2Color.ToHex(ReadString(config, "BackgroundColor") ?? ReadString(config, "color"));
		if (color is null)
		{
			return null;
		}

		return new ActionMigrationResult(WidgetIntegrationId,
			"set-background-color",
			action.Name ?? "Set background colour",
			Parameters(("widget", JsonString(WidgetTargets.Self)),
				("state", JsonString("current")),
				("color", JsonString(color))));
	}

	private static ActionMigrationResult? ChangeVariable(MacroDeck2Action action)
	{
		if (!TryParse(action.Configuration, out var config))
		{
			return null;
		}

		var variable = ReadString(config, "variable");
		if (string.IsNullOrWhiteSpace(variable))
		{
			return null;
		}

		var method = ReadString(config, "method");
		var operation = method switch
		{
			"countUp" or "countDown" => "add",
			"toggle" => "toggle",
			_ => "set"
		};

		// Macro Deck 3 has one additive operation rather than a pair, so counting down is the same
		// operation with a negated amount.
		var value = ReadString(config, "value") ?? string.Empty;
		if (method == "countDown" && value.Length > 0 && !value.StartsWith('-'))
		{
			value = "-" + value;
		}

		var parameters = operation == "toggle"
			? Parameters(("variable", JsonString(variable)), ("operation", JsonString(operation)))
			: Parameters(("variable", JsonString(variable)),
				("operation", JsonString(operation)),
				("value", JsonString(value)));

		return new ActionMigrationResult(VariablesIntegrationId,
			"set-variable",
			action.Name ?? "Set variable",
			parameters);
	}

	private static bool TryParse(string? configuration, out JsonElement config)
	{
		config = default;
		if (string.IsNullOrWhiteSpace(configuration))
		{
			return false;
		}

		try
		{
			config = JsonDocument.Parse(configuration).RootElement.Clone();
			return config.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static string? ReadString(JsonElement config, string name)
		=> config.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
