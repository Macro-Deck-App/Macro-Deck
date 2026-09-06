using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Widgets;

internal static class WidgetActionParameters
{
	public const string Target = "widget";
	public const string State = "state";

	public const string Unchanged = "";

	private static readonly ActionParameterOption _currentStateOption =
		new() { Value = "current", Label = AppStrings.Integrations.Widgets.Actions.CurrentStateLabel() };

	/// <summary>
	/// Shared instance for the "every state" sentinel, so the permissive branch below and the resolved
	/// branch in <see cref="ResolveStateOptions" /> never drift on its value or label.
	/// </summary>
	private static readonly ActionParameterOption _bothStatesOption =
		new() { Value = "both", Label = AppStrings.Integrations.Widgets.Actions.EveryStateLabel() };

	/// <summary>
	/// Offered when the target cannot be asked what states it has ($self or an empty target resolve
	/// only at run time). No longer names the legacy "on"/"off" literals: those named a fixed two-state
	/// vocabulary a widget under an arbitrary state model may not have. Every already-stored "on"/"off"
	/// value keeps working regardless - it still resolves through <see cref="ResolveStateIds" />'s
	/// positional path, <c>WidgetAppearanceJson.ResolveStates</c>, and the plugin wire-compat path - this
	/// only changes what a picker newly offers an author.
	/// </summary>
	public static readonly IReadOnlyList<ActionParameterOption> PermissiveStateOptions =
		[_currentStateOption, _bothStatesOption];

	public static readonly IReadOnlyList<ActionParameterOption> SingleStateOptions = [_currentStateOption];

	public static ActionParameter TargetParameter()
		=> ActionParameter.WidgetTarget(Target,
			label: AppStrings.Integrations.Widgets.Actions.TargetWidgetLabel(),
			description: AppStrings.Integrations.Widgets.Actions.TargetWidgetDescription());

	public static ActionParameter StateParameter(string defaultValue = "current")
		=> new()
		{
			Name = State,
			Type = ActionParameterType.DynamicChoice,
			DynamicOptions = true,
			Label = AppStrings.Integrations.Widgets.Actions.StateLabel(),
			Description = AppStrings.Integrations.Widgets.Actions.StateDescription(),
			DefaultValue = defaultValue
		};

	public static DynamicOptionsResult ResolveStateOptions(IWidgetApi? widgets, DynamicOptionsContext context)
	{
		var target = context.CurrentParameters.GetValueOrDefault(Target) as string;
		if (widgets is null || string.IsNullOrWhiteSpace(target) || WidgetTargets.IsSelf(target))
		{
			return new DynamicOptionsResult { Options = PermissiveStateOptions };
		}

		var widget = widgets.GetWidgets().FirstOrDefault(w => w.Id == target);
		if (widget is null || widget.States.Count == 0)
		{
			return new DynamicOptionsResult { Options = SingleStateOptions };
		}

		var options = new List<ActionParameterOption> { _currentStateOption };
		if (widget.States.Count > 1)
		{
			options.Add(_bothStatesOption);
		}

		options.AddRange(ProjectStateOptions(widget.States));
		return new DynamicOptionsResult { Options = options };
	}

	/// <summary>
	/// The shared <c>widget.States -&gt; ActionParameterOption</c> projection, used by this resolver and
	/// by <see cref="ButtonStateActionDefinition" />'s own resolver. Kept as the only thing the two share:
	/// set-state must name exactly one concrete state and has no use for the "current"/"both" sentinels,
	/// while the appearance actions here legitimately offer both, so the resolvers themselves stay
	/// separate rather than being merged.
	/// </summary>
	internal static IEnumerable<ActionParameterOption> ProjectStateOptions(IReadOnlyList<WidgetStateInfo> states)
		=> states.Select(state => new ActionParameterOption { Value = state.Id, Label = state.Label });

	public static string TargetOf(ActionExecutionContext context)
	{
		var value = ReadString(context, Target);

		return string.IsNullOrWhiteSpace(value) || WidgetTargets.IsSelf(value)
			? context.OwnerWidgetId ?? string.Empty
			: value;
	}


	/// <summary>
	/// The "state" parameter as <see cref="WidgetAppearanceRequest.StateIds" /> expects it: the stored
	/// value is either one of the old fixed words ("current"/"both") a saved flow may still carry, or a
	/// real state id chosen from <see cref="ResolveStateOptions" /> - both pass straight through the
	/// host's state resolution, which already treats a literal "on"/"off"/real id and the sentinels alike.
	/// </summary>
	public static IReadOnlyCollection<string> ResolveStateIds(ActionExecutionContext context)
	{
		var raw = ReadString(context, State);
		return raw switch
		{
			"" or "current" => [WidgetStates.Current],
			"both" => [WidgetStates.All],
			_ => [raw]
		};
	}

	public static string? ReadOptional(ActionExecutionContext context, string name)
	{
		var value = ReadString(context, name);
		return string.IsNullOrEmpty(value) ? null : value;
	}

	public static bool IsReset(ActionExecutionContext context, string name)
		=> WidgetAppearanceValues.IsReset(ReadString(context, name));

	public static double? ReadNumber(ActionExecutionContext context, string name)
	{
		if (!context.Parameters.TryGetValue(name, out var value))
		{
			return null;
		}

		return value switch
		{
			double number => number,
			long integer => integer,
			int integer => integer,
			string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => null
		};
	}

	private static string ReadString(ActionExecutionContext context, string name)
		=> context.Parameters.TryGetValue(name, out var value) ? value.ToString() ?? string.Empty : string.Empty;
}
