using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.AutoHotkey.Actions;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Migration;

namespace MacroDeckHost.Integrations.AutoHotkey.Migration;

internal sealed class AutoHotkeyMacroDeck2Migration : IIntegrationMigration
{
	private const string ActionNamespace = "jbcarreon123.AHKPlugin.Actions.";

	public MigrationSource Source => MigrationSource.MacroDeck2;

	// The community plugin's AssemblyName in every released version (1.0 to 3.0.0).
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["AutoHotkey Plugin"];

	public IReadOnlyList<string> ClaimedSettingsSources { get; } = [];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
	{
		return Task.FromResult(Migrate(action));
	}

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);
	}

	internal static ActionMigrationResult? Migrate(ForeignAction action)
	{
		return action.TypeName switch
		{
			ActionNamespace + "RunScriptv2Action" => MigrateRunFile(action),
			ActionNamespace + "WriteScriptv2Action" => MigrateInlineCode(action),
			ActionNamespace + "WindowSpyAction" => Result(action, AutoHotkeyActions.OpenWindowSpyId, [], []),
			_ => null
		};
	}

	private static ActionMigrationResult? MigrateRunFile(ForeignAction action)
	{
		if (ReadString(action.Configuration, "ahkScriptPath") is not { } path || string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		return Result(action,
			AutoHotkeyActions.RunFileId,
			[
				(AutoHotkeyParameters.File, path.Trim().Trim('"')),
				(AutoHotkeyParameters.WhenRunning, AutoHotkeyParameters.StartAnother)
			],
			[]);
	}

	private static ActionMigrationResult? MigrateInlineCode(ForeignAction action)
	{
		if (ReadString(action.Configuration, "script") is not { } code || string.IsNullOrWhiteSpace(code))
		{
			return null;
		}

		IReadOnlyList<LocalizedText> warnings = code.Contains("{{", StringComparison.Ordinal)
			? [Strings.TemplatePlaceholders()]
			: [];
		return Result(action,
			AutoHotkeyActions.RunInlineCodeId,
			[
				(AutoHotkeyParameters.Code, code),
				(AutoHotkeyParameters.WhenRunning, AutoHotkeyParameters.StartAnother)
			],
			warnings);
	}

	private static ActionMigrationResult Result(
		ForeignAction action,
		string actionId,
		IReadOnlyList<(string Name, string Value)> parameters,
		IReadOnlyList<LocalizedText> warnings)
	{
		return new ActionMigrationResult(AutoHotkeyIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? actionId,
			parameters.ToDictionary(parameter => parameter.Name,
				parameter => JsonSerializer.SerializeToElement(parameter.Value),
				StringComparer.Ordinal),
			[Strings.EnableIntegration(), .. warnings]);
	}

	private static string? ReadString(string? configuration, string name)
	{
		if (string.IsNullOrWhiteSpace(configuration))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(configuration);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind == JsonValueKind.String)
				{
					return property.Value.GetString();
				}
			}

			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
