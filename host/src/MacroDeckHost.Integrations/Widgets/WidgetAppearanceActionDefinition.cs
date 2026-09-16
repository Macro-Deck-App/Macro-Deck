using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Widgets;

internal sealed class WidgetAppearanceActionDefinition : IDynamicOptionsActionDefinition
{
	private static readonly ILogger _logger =
		IntegrationLog.For<WidgetAppearanceActionDefinition>(WidgetIntegration.IntegrationId);

	private readonly WidgetAppearanceProperty _property;
	private readonly Func<IWidgetApi?> _widgets;
	private readonly Func<ActionExecutionContext, WidgetAppearancePatch> _buildPatch;
	private readonly Func<ActionExecutionContext, IReadOnlyCollection<WidgetAppearanceProperty>>? _buildClears;
	private readonly Func<IWidgetApi, string, ActionResult?>? _onApplyFailed;

	public WidgetAppearanceActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		WidgetAppearanceProperty property,
		IReadOnlyList<ActionParameter> parameters,
		Func<IWidgetApi?> widgets,
		Func<ActionExecutionContext, WidgetAppearancePatch> buildPatch,
		Func<ActionExecutionContext, IReadOnlyCollection<WidgetAppearanceProperty>>? buildClears = null,
		Func<IWidgetApi, string, ActionResult?>? onApplyFailed = null)
	{
		Id = id;
		Name = name;
		Description = description;
		_property = property;
		_widgets = widgets;
		_buildPatch = buildPatch;
		_buildClears = buildClears;
		_onApplyFailed = onApplyFailed;

		Parameters =
		[
			WidgetActionParameters.TargetParameter(),
			WidgetActionParameters.StateParameter(),
			.. parameters
		];
	}

	public string Id { get; }
	public LocalizedText Name { get; }
	public LocalizedText Description { get; }
	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor()
		=> new Executor(_property, _widgets, _buildPatch, _buildClears, _onApplyFailed);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(WidgetActionParameters.ResolveStateOptions(_widgets(), context));

	private sealed class Executor : IActionExecutor
	{
		private readonly WidgetAppearanceProperty _property;
		private readonly Func<IWidgetApi?> _widgets;
		private readonly Func<ActionExecutionContext, WidgetAppearancePatch> _buildPatch;
		private readonly Func<ActionExecutionContext, IReadOnlyCollection<WidgetAppearanceProperty>>? _buildClears;
		private readonly Func<IWidgetApi, string, ActionResult?>? _onApplyFailed;

		public Executor(
			WidgetAppearanceProperty property,
			Func<IWidgetApi?> widgets,
			Func<ActionExecutionContext, WidgetAppearancePatch> buildPatch,
			Func<ActionExecutionContext, IReadOnlyCollection<WidgetAppearanceProperty>>? buildClears,
			Func<IWidgetApi, string, ActionResult?>? onApplyFailed)
		{
			_property = property;
			_widgets = widgets;
			_buildPatch = buildPatch;
			_buildClears = buildClears;
			_onApplyFailed = onApplyFailed;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var widgets = _widgets();
			var widgetId = WidgetActionParameters.TargetOf(context);

			if (string.IsNullOrEmpty(widgetId))
			{
				_logger.Warning("Widget action skipped: 'This widget' resolved to nothing");
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Widgets.Errors.NoOwnerWidget());
			}

			if (widgets is null)
			{
				_logger.Warning("Widget action skipped: the widget API is unavailable");
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Widgets.Errors.WidgetsUnavailable());
			}

			var applied = await widgets.ApplyAsync(new WidgetAppearanceRequest
				{
					WidgetId = widgetId,
					Patch = _buildPatch(context),
					ClearProperties = _buildClears?.Invoke(context) ?? [],
					StateIds = WidgetActionParameters.ResolveStateIds(context)
				},
				context.CancellationToken);

			if (applied)
			{
				return ActionResult.Success();
			}

			if (_onApplyFailed?.Invoke(widgets, widgetId) is { } classified)
			{
				return classified;
			}

			// ApplyAsync also answers false when the widget already shows this value, so a supported
			// property on an existing widget is a repeat run with nothing to change, not a failure.
			return widgets.GetWidgets().FirstOrDefault(w => w.Id == widgetId) is { } target &&
				target.AppearanceProperties.Contains(_property)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound,
						AppStrings.Integrations.Widgets.Errors.WidgetNotFound(widgetId: widgetId));
		}
	}
}
