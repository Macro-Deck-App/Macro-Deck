using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Parameters;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal static class AutoHotkeyParameters
{
	public const string Script = "script";
	public const string File = "file";
	public const string Code = "code";
	public const string Arguments = "arguments";
	public const string WorkingDirectory = "workingDirectory";
	public const string IncludeHelper = "includeHelper";
	public const string Parameters = "parameters";
	public const string WhenRunning = "whenRunning";
	public const string Wait = "wait";
	public const string Timeout = "timeout";
	public const string OutputVariable = "outputVariable";
	public const string Target = "target";
	public const string StopAll = "stopAll";

	public const string StartAnother = "start-another";
	public const string Ignore = "ignore";
	public const string Restart = "restart";
	public const string Fail = "fail";

	public const string ManagedTarget = "managed";
	public const string FileTarget = "file";

	public const string Language = "autohotkey";
	public const string VariableInsertFormat = "MacroDeck.GetVar(\"{name}\")";

	public static ActionParameter ScriptChoice()
	{
		return ActionParameter.DynamicChoice(Script,
			Strings.ScriptLabel(),
			placeholder: Strings.ScriptPlaceholder(),
			required: true);
	}

	public static ActionParameter FilePicker()
	{
		return ActionParameter.File(File, Strings.FileLabel(), fileExtensions: ["ahk"], required: true);
	}

	public static ActionParameter KeyValues()
	{
		return ActionParameter.KeyValue(Parameters, Strings.ParametersLabel(), Strings.ParametersDescription());
	}

	public static IReadOnlyList<ActionParameter> Targeted()
	{
		return
		[
			ActionParameter.Choice(Target,
				[
					new ActionParameterOption { Value = ManagedTarget, Label = Strings.TargetManaged() },
					new ActionParameterOption { Value = FileTarget, Label = Strings.TargetFile() }
				],
				Strings.TargetLabel(),
				defaultValue: ManagedTarget),
			ScriptChoice().OnlyWhen(Target, ManagedTarget),
			FilePicker().OnlyWhen(Target, FileTarget)
		];
	}

	public static IReadOnlyList<ActionParameter> RunOptions()
	{
		return
		[
			KeyValues(),
			ActionParameter.Choice(WhenRunning,
				[
					new ActionParameterOption { Value = Ignore, Label = Strings.WhenRunningIgnore() },
					new ActionParameterOption { Value = StartAnother, Label = Strings.WhenRunningStartAnother() },
					new ActionParameterOption { Value = Restart, Label = Strings.WhenRunningRestart() },
					new ActionParameterOption { Value = Fail, Label = Strings.WhenRunningFail() }
				],
				Strings.WhenRunningLabel(),
				defaultValue: Ignore),
			ActionParameter.Toggle(Wait, Strings.WaitLabel(), Strings.WaitDescription(), literalOnly: true),
			ActionParameter.Duration(Timeout,
					Strings.TimeoutLabel(),
					Strings.TimeoutDescription(),
					1000,
					AutoHotkeyRunRequest.MaxTimeout.TotalMilliseconds,
					AutoHotkeyRunRequest.DefaultTimeout.TotalMilliseconds)
				.OnlyWhen(Wait, "true"),
			ActionParameter.Autocomplete(OutputVariable,
					Strings.OutputVariableLabel(),
					Strings.OutputVariableDescription(),
					optionsSourceId: HostOptionsSources.UserVariables)
				.OnlyWhen(Wait, "true")
		];
	}

	public static ActionParameter CodeEditor()
	{
		return new ActionParameter
		{
			Name = Code,
			Type = ActionParameterType.Code,
			Language = Language,
			VariableInsertFormat = VariableInsertFormat,
			Label = Strings.CodeLabel(),
			Description = Strings.CodeDescription(),
			Required = true
		};
	}

	public static ActionParameter ArgumentList()
	{
		return ActionParameter.Array(Arguments,
				ActionParameter.Text("argument", Strings.ArgumentLabel()),
				Strings.ArgumentsLabel(),
				Strings.ArgumentsDescription())
			.WithTemplatedItems();
	}
}
