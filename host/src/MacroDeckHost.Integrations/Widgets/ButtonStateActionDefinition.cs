using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Widgets;

/// <summary>
/// "Set Button State" - jumps a State-Mode action button straight to a named state. Fails, rather than
/// silently doing nothing, whenever the target's state is not this call's to set: a state provider or a
/// state mapping is authoritative, the target is not a State-Mode button, or the requested id is not one
/// of its states.
/// </summary>
internal sealed class SetButtonStateActionDefinition : IDynamicOptionsActionDefinition
{
	private const string StateParameterName = "state";

	private readonly Func<IWidgetApi?> _widgets;

	public SetButtonStateActionDefinition(Func<IWidgetApi?> widgets)
	{
		_widgets = widgets;
		Parameters =
		[
			WidgetActionParameters.TargetParameter(),
			new ActionParameter
			{
				Name = StateParameterName,
				Type = ActionParameterType.DynamicChoice,
				DynamicOptions = true,
				Label = AppStrings.Integrations.Widgets.Actions.SetStateLabel(),
				Description = AppStrings.Integrations.Widgets.Actions.SetStateDescription(),
				Required = true
			}
		];
	}

	public string Id => "set-state";
	public LocalizedText Name => AppStrings.Integrations.Widgets.Actions.SetButtonStateName();
	public LocalizedText Description => AppStrings.Integrations.Widgets.Actions.SetButtonStateDescription();
	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new ButtonStateExecutor(_widgets, isSet: true);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(ResolveStateOptions(_widgets(), context));

	private static DynamicOptionsResult ResolveStateOptions(IWidgetApi? widgets, DynamicOptionsContext context)
	{
		var target = context.CurrentParameters.GetValueOrDefault(WidgetActionParameters.Target) as string;
		if (widgets is null || string.IsNullOrWhiteSpace(target) || WidgetTargets.IsSelf(target))
		{
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}

		// Unlike the appearance actions' picker, this one names exactly one concrete state to set - no
		// "current"/"both" sentinel makes sense for Set Button State, so only the shared
		// states -> options projection is reused, not WidgetActionParameters.ResolveStateOptions itself.
		var widget = widgets.GetWidgets().FirstOrDefault(w => w.Id == target);
		var options = widget is null ? [] : WidgetActionParameters.ProjectStateOptions(widget.States).ToList();
		return new DynamicOptionsResult { Options = options };
	}
}

/// <summary>"Cycle Button State" - advances to the next state in declared order, wrapping past the last.</summary>
internal sealed class CycleButtonStateActionDefinition : IActionDefinition
{
	private readonly Func<IWidgetApi?> _widgets;

	public CycleButtonStateActionDefinition(Func<IWidgetApi?> widgets)
	{
		_widgets = widgets;
		Parameters = [WidgetActionParameters.TargetParameter()];
	}

	// Renamed to "Cycle Button State" in the UI only: the stored id stays "toggle-state" so every saved
	// flow keeps resolving this action.
	public string Id => "toggle-state";
	public LocalizedText Name => AppStrings.Integrations.Widgets.Actions.CycleButtonStateName();
	public LocalizedText Description => AppStrings.Integrations.Widgets.Actions.CycleButtonStateDescription();
	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new ButtonStateExecutor(_widgets, isSet: false);
}

internal sealed class ButtonStateExecutor : IActionExecutor
{
	private readonly Func<IWidgetApi?> _widgets;
	private readonly bool _isSet;

	public ButtonStateExecutor(Func<IWidgetApi?> widgets, bool isSet)
	{
		_widgets = widgets;
		_isSet = isSet;
	}

	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
	{
		var widgets = _widgets();
		if (widgets is null)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.Widgets.Errors.WidgetsUnavailable());
		}

		var widgetId = WidgetActionParameters.TargetOf(context);
		if (string.IsNullOrEmpty(widgetId))
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Widgets.Errors.NoOwnerWidget());
		}

		var result = _isSet
			? await widgets.SetStateAsync(widgetId, ReadState(context), context.CancellationToken)
			: await widgets.AdvanceStateAsync(widgetId, context.CancellationToken);

		return result.Success
			? ActionResult.Success()
			: ActionResult.Failed(MapErrorCode(result.Error), Describe(result.Error, widgetId));
	}

	private static string ReadState(ActionExecutionContext context)
		=> context.Parameters.TryGetValue("state", out var value) ? value.ToString() ?? string.Empty : string.Empty;

	private static string MapErrorCode(WidgetStateWriteError? error) => error switch
	{
		WidgetStateWriteError.ProviderActive or WidgetStateWriteError.MappingActive =>
			ActionErrorCodes.PermissionDenied,
		WidgetStateWriteError.UnknownState => ActionErrorCodes.InvalidParameter,
		_ => ActionErrorCodes.NotFound
	};

	private static LocalizedText Describe(WidgetStateWriteError? error, string widgetId) => error switch
	{
		WidgetStateWriteError.ProviderActive =>
			AppStrings.Integrations.Widgets.Errors.StateControlledByProvider(),
		WidgetStateWriteError.MappingActive =>
			AppStrings.Integrations.Widgets.Errors.StateControlledByMapping(),
		WidgetStateWriteError.UnknownState => AppStrings.Integrations.Widgets.Errors.UnknownState(),
		_ => AppStrings.Integrations.Widgets.Errors.NotStateModeButton(widgetId: widgetId)
	};
}
