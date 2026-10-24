using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Variables;

namespace MacroDeckHost.Integrations.AutoHotkey.Variables;

internal static class AutoHotkeyVariables
{
	public const string Installed = "autohotkey_installed";
	public const string Version = "autohotkey_version";
	public const string RunningScripts = "autohotkey_running_scripts";
	public const string LastScript = "autohotkey_last_script";
	public const string LastExitCode = "autohotkey_last_exit_code";
	public const string LastResult = "autohotkey_last_result";
	public const string LastError = "autohotkey_last_error";

	private const string ScriptPrefix = "autohotkey_script_";
	private const string ScriptSuffix = "_running";

	private static readonly TimeSpan _refresh = TimeSpan.FromSeconds(30);

	public static IReadOnlyList<VariableDefinition> Status { get; } =
	[
		Define(Installed, VariableType.Boolean) with { DisplayName = Strings.Installed() },
		Define(Version, VariableType.Text) with { DisplayName = Strings.Version() },
		Define(RunningScripts, VariableType.Numeric, 0) with { DisplayName = Strings.RunningScripts() },
		Define(LastScript, VariableType.Text) with { DisplayName = Strings.LastScript() },
		Define(LastExitCode, VariableType.Numeric, 0) with { DisplayName = Strings.LastExitCode() },
		Define(LastResult, VariableType.Text) with { DisplayName = Strings.LastResult() },
		Define(LastError, VariableType.Text) with { DisplayName = Strings.LastError() }
	];

	public static IReadOnlyList<string> StatusIds { get; } = [.. Status.Select(variable => variable.ResolvedId!)];

	public static VariableDefinition Template { get; } =
		Define(ScriptRunningName(VariableNameTemplate.Placeholder("script")), VariableType.Boolean) with
		{
			DisplayName = Strings.ScriptRunning(VariableNameTemplate.Placeholder("script"))
		};

	public static string ScriptRunningName(string slug)
	{
		return ScriptPrefix + slug + ScriptSuffix;
	}

	public static VariableDefinition ForScript(AutoHotkeyScript script)
	{
		return Define(ScriptRunningName(script.Slug), VariableType.Boolean) with
		{
			DisplayName = Strings.ScriptRunning(script.Name)
		};
	}

	public static string? SlugOf(string name)
	{
		return name.StartsWith(ScriptPrefix, StringComparison.Ordinal)
			&& name.EndsWith(ScriptSuffix, StringComparison.Ordinal)
			&& name.Length > ScriptPrefix.Length + ScriptSuffix.Length
				? name[ScriptPrefix.Length..^ScriptSuffix.Length]
				: null;
	}

	private static VariableDefinition Define(string name, VariableType type, int? decimalPlaces = null)
	{
		return VariableDefinition.Eager(name, type, decimalPlaces, _refresh);
	}
}
