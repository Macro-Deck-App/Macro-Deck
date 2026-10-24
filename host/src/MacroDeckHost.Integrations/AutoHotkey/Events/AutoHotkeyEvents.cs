using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Events;

namespace MacroDeckHost.Integrations.AutoHotkey.Events;

internal static class AutoHotkeyEvents
{
	public const string Started = "script-started";
	public const string Exited = "script-exited";
	public const string Failed = "script-failed";

	public const string ScriptParameter = "script";
	public const string ExitCodeParameter = "exitCode";
	public const string DurationParameter = "durationMs";
	public const string ReasonParameter = "reason";

	private static readonly LocalizedText _category = Strings.Category();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = Started,
			Name = Strings.StartedName(),
			Description = Strings.StartedDescription(),
			Category = _category,
			PayloadParameters = [Script()]
		},
		new()
		{
			Id = Exited,
			Name = Strings.ExitedName(),
			Description = Strings.ExitedDescription(),
			Category = _category,
			PayloadParameters =
			[
				Script(),
				ActionParameter.Number(ExitCodeParameter, Strings.ExitCodeLabel()),
				ActionParameter.Number(DurationParameter, Strings.DurationLabel())
			]
		},
		new()
		{
			Id = Failed,
			Name = Strings.FailedName(),
			Description = Strings.FailedDescription(),
			Category = _category,
			PayloadParameters =
			[
				Script(),
				ActionParameter.Choice(ReasonParameter,
					[
						new ActionParameterOption
						{
							Value = AutoHotkeyFailureReasons.StartFailed, Label = Strings.ReasonStartFailed()
						},
						new ActionParameterOption
						{
							Value = AutoHotkeyFailureReasons.LoadError, Label = Strings.ReasonLoadError()
						},
						new ActionParameterOption
						{
							Value = AutoHotkeyFailureReasons.RuntimeError, Label = Strings.ReasonRuntimeError()
						},
						new ActionParameterOption
						{
							Value = AutoHotkeyFailureReasons.Timeout, Label = Strings.ReasonTimeout()
						}
					],
					Strings.ReasonLabel())
			]
		}
	];

	public static IReadOnlyDictionary<string, object?> StartedPayload(AutoHotkeyRunSnapshot run)
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal) { [ScriptParameter] = run.DisplayName };
	}

	public static IReadOnlyDictionary<string, object?> ExitedPayload(AutoHotkeyRunSnapshot run)
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[ScriptParameter] = run.DisplayName,
			[ExitCodeParameter] = run.ExitCode,
			[DurationParameter] = (long)(run.Duration ?? TimeSpan.Zero).TotalMilliseconds
		};
	}

	public static IReadOnlyDictionary<string, object?> FailedPayload(string script, string reason)
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[ScriptParameter] = script,
			[ReasonParameter] = reason
		};
	}

	private static ActionParameter Script()
	{
		return ActionParameter.Text(ScriptParameter, Strings.ScriptLabel());
	}
}
