using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Widgets;

[MacroDeckIntegration]
public sealed class WidgetIntegration : IIntegration, ISystemIntegration
{
	public const string IntegrationId = "app.macro-deck.widget";

	private static readonly IReadOnlyList<ActionParameterOption> _borderStyles =
	[
		new() { Value = "off", Label = AppStrings.Integrations.Widgets.Actions.BorderNone() },
		new() { Value = "static", Label = AppStrings.Integrations.Widgets.Actions.BorderStatic() },
		new() { Value = "heartbeat", Label = AppStrings.Integrations.Widgets.Actions.BorderHeartbeat() },
		new() { Value = "breathing", Label = AppStrings.Integrations.Widgets.Actions.BorderBreathing() },
		new() { Value = "blink", Label = AppStrings.Integrations.Widgets.Actions.BorderBlink() },
		new() { Value = "comet", Label = AppStrings.Integrations.Widgets.Actions.BorderComet() },
		new() { Value = "ants", Label = AppStrings.Integrations.Widgets.Actions.BorderMarchingAnts() },
		new() { Value = "hue-shift", Label = AppStrings.Integrations.Widgets.Actions.BorderHueShift() },
		new() { Value = "rgb", Label = AppStrings.Integrations.Widgets.Actions.BorderRgb() }
	];

	private static readonly IReadOnlyList<ActionParameterOption> _iconFits =
	[
		new() { Value = WidgetActionParameters.Unchanged, Label = AppStrings.Integrations.Widgets.Actions.Unchanged() },
		new() { Value = "contain", Label = AppStrings.Integrations.Widgets.Actions.IconFitContain() },
		new() { Value = "cover", Label = AppStrings.Integrations.Widgets.Actions.IconFitCover() }
	];

	private static readonly IReadOnlyList<ActionParameterOption> _textAlignments =
	[
		new() { Value = WidgetActionParameters.Unchanged, Label = AppStrings.Integrations.Widgets.Actions.Unchanged() },
		new() { Value = "left", Label = AppStrings.Integrations.Widgets.Actions.AlignLeft() },
		new() { Value = "center", Label = AppStrings.Integrations.Widgets.Actions.AlignCenter() },
		new() { Value = "right", Label = AppStrings.Integrations.Widgets.Actions.AlignRight() }
	];

	private IWidgetApi? _widgets;

	public WidgetIntegration()
	{
		Actions =
		[
			SetLabel(),
			SetBackgroundColor(),
			SetLabelColor(),
			SetIcon(),
			SetIconDisplay(),
			SetFont(),
			SetBorder(),
			new SetButtonStateActionDefinition(() => _widgets),
			new CycleButtonStateActionDefinition(() => _widgets)
		];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Widgets.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_widgets = context.Widgets;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	private WidgetAppearanceActionDefinition SetLabel()
		=> new("set-label",
			AppStrings.Integrations.Widgets.Actions.SetLabelName(),
			AppStrings.Integrations.Widgets.Actions.SetLabelDescription(),
			[
				ActionParameter.MultilineText("label",
					label: AppStrings.Integrations.Widgets.Actions.LabelLabel(),
					description: AppStrings.Integrations.Widgets.Actions.LabelDescription(),
					placeholder: AppStrings.Integrations.Widgets.Actions.LabelPlaceholder())
			],
			() => _widgets,
			// Empty is meaningful here: a label is text, and clearing it is a thing to want. Every
			// other property reads empty as "unchanged", because there is no useful empty color.
			context => new WidgetAppearancePatch { Label = ReadText(context, "label") });

	private WidgetAppearanceActionDefinition SetBackgroundColor()
		=> new("set-background-color",
			AppStrings.Integrations.Widgets.Actions.SetBackgroundColorName(),
			AppStrings.Integrations.Widgets.Actions.SetBackgroundColorDescription(),
			[
				ActionParameter.Color("color",
					label: AppStrings.Integrations.Widgets.Actions.ColorLabel(),
					supportsReset: true)
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				BackgroundColor = WidgetActionParameters.IsReset(context, "color")
					? null
					: WidgetActionParameters.ReadOptional(context, "color")
			},
			context => WidgetActionParameters.IsReset(context, "color")
				? [WidgetAppearanceProperty.BackgroundColor]
				: []);

	private WidgetAppearanceActionDefinition SetLabelColor()
		=> new("set-label-color",
			AppStrings.Integrations.Widgets.Actions.SetLabelColorName(),
			AppStrings.Integrations.Widgets.Actions.SetLabelColorDescription(),
			[
				ActionParameter.Color("color",
					label: AppStrings.Integrations.Widgets.Actions.ColorLabel(),
					supportsReset: true)
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				LabelColor = WidgetActionParameters.IsReset(context, "color")
					? null
					: WidgetActionParameters.ReadOptional(context, "color")
			},
			context => WidgetActionParameters.IsReset(context, "color")
				? [WidgetAppearanceProperty.LabelColor]
				: []);

	private WidgetAppearanceActionDefinition SetIcon()
		=> new("set-icon",
			AppStrings.Integrations.Widgets.Actions.SetIconName(),
			AppStrings.Integrations.Widgets.Actions.SetIconDescription(),
			[
				ActionParameter.Icon("iconId",
					label: AppStrings.Integrations.Widgets.Actions.IconLabel(),
					description: AppStrings.Integrations.Widgets.Actions.IconDescription())
			],
			() => _widgets,
			context => new WidgetAppearancePatch { IconId = ReadText(context, "iconId") },
			onApplyFailed: (widgets, widgetId) =>
				widgets.GetWidgets().FirstOrDefault(w => w.Id == widgetId) is { HasActiveIconProvider: true }
					? ActionResult.Failed(ActionErrorCodes.PermissionDenied,
						AppStrings.Integrations.Widgets.Errors.IconControlledByProvider())
					: ActionResult.Failed(ActionErrorCodes.NotFound,
						AppStrings.Integrations.Widgets.Errors.WidgetNotFound(widgetId: widgetId)));

	private WidgetAppearanceActionDefinition SetIconDisplay()
		=> new("set-icon-display",
			AppStrings.Integrations.Widgets.Actions.SetIconDisplayName(),
			AppStrings.Integrations.Widgets.Actions.SetIconDisplayDescription(),
			[
				ActionParameter.Choice("iconFit",
					_iconFits,
					label: AppStrings.Integrations.Widgets.Actions.IconFitDisplayLabel(),
					description: AppStrings.Integrations.Widgets.Actions.IconFitDisplayDescription(),
					defaultValue: WidgetActionParameters.Unchanged),
				UnfilledNumber("iconZoom",
					AppStrings.Integrations.Widgets.Actions.IconZoomLabel(),
					AppStrings.Integrations.Widgets.Actions.IconZoomDescription(),
					min: 10,
					max: 400),
				UnfilledNumber("iconOffsetX",
					AppStrings.Integrations.Widgets.Actions.IconOffsetXLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOffsetXDescription(),
					min: -100,
					max: 100),
				UnfilledNumber("iconOffsetY",
					AppStrings.Integrations.Widgets.Actions.IconOffsetYLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOffsetYDescription(),
					min: -100,
					max: 100),
				UnfilledNumber("iconOpacity",
					AppStrings.Integrations.Widgets.Actions.IconOpacityLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOpacityDescription(),
					min: 0,
					max: 100)
			],
			() => _widgets,
			context => WidgetActionParameters.IsReset(context, "iconFit")
				? new WidgetAppearancePatch()
				: new WidgetAppearancePatch
				{
					IconFit = WidgetActionParameters.ReadOptional(context, "iconFit"),
					IconZoom = WidgetActionParameters.ReadNumber(context, "iconZoom"),
					IconOffsetX = WidgetActionParameters.ReadNumber(context, "iconOffsetX"),
					IconOffsetY = WidgetActionParameters.ReadNumber(context, "iconOffsetY"),
					IconOpacity = WidgetActionParameters.ReadNumber(context, "iconOpacity")
				},
			context => WidgetActionParameters.IsReset(context, "iconFit")
				? [WidgetAppearanceProperty.IconDisplay]
				: []);

	private static ActionParameter UnfilledNumber(string name,
		LocalizedText label,
		LocalizedText description,
		double min,
		double max)
		=> new()
		{
			Name = name,
			Type = ActionParameterType.Number,
			Label = label,
			Description = description,
			Min = min,
			Max = max,
			DefaultValue = WidgetActionParameters.Unchanged
		};

	private WidgetAppearanceActionDefinition SetFont()
		=> new("set-font",
			AppStrings.Integrations.Widgets.Actions.SetFontName(),
			AppStrings.Integrations.Widgets.Actions.SetFontDescription(),
			[
				ActionParameter.Autocomplete("fontFaceId",
					label: AppStrings.Integrations.Widgets.Actions.FontLabel(),
					optionsSourceId: WidgetOptionsSources.Fonts,
					placeholder: AppStrings.Integrations.Widgets.Actions.Unchanged()),
				ActionParameter.Number("fontSize",
					label: AppStrings.Integrations.Widgets.Actions.FontSizeLabel(),
					description: AppStrings.Integrations.Widgets.Actions.FontSizeDescription(),
					// 0 is the historical, persisted Unchanged sentinel. The dedicated action UI
					// deliberately only offers 1..100 as concrete values, but schemas must keep
					// old/default flows valid.
					min: 0,
					max: 100,
					defaultValue: 0),
				ActionParameter.Choice("textAlign",
					_textAlignments,
					label: AppStrings.Integrations.Widgets.Actions.AlignmentLabel(),
					defaultValue: WidgetActionParameters.Unchanged),
				ActionParameter.Choice("labelPosition",
					[
						new ActionParameterOption
						{
							Value = WidgetActionParameters.Unchanged,
							Label = AppStrings.Integrations.Widgets.Actions.Unchanged()
						},
						new ActionParameterOption
							{ Value = "top", Label = AppStrings.Integrations.Widgets.Actions.PositionTop() },
						new ActionParameterOption
							{ Value = "center", Label = AppStrings.Integrations.Widgets.Actions.PositionCenter() },
						new ActionParameterOption
							{ Value = "bottom", Label = AppStrings.Integrations.Widgets.Actions.PositionBottom() }
					],
					label: AppStrings.Integrations.Widgets.Actions.PositionLabel(),
					defaultValue: WidgetActionParameters.Unchanged)
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				FontFaceId = WidgetActionParameters.ReadOptional(context, "fontFaceId"),
				FontSize = WidgetActionParameters.ReadNumber(context, "fontSize") is > 0 and var size ? size : null,
				TextAlign = WidgetActionParameters.ReadOptional(context, "textAlign"),
				LabelPosition = WidgetActionParameters.ReadOptional(context, "labelPosition")
			});

	private WidgetAppearanceActionDefinition SetBorder()
		=> new("set-border",
			AppStrings.Integrations.Widgets.Actions.SetBorderName(),
			AppStrings.Integrations.Widgets.Actions.SetBorderDescription(),
			[
				ActionParameter.Choice("style",
					[
						new ActionParameterOption
						{
							Value = WidgetActionParameters.Unchanged,
							Label = AppStrings.Integrations.Widgets.Actions.Unchanged()
						},
						.. _borderStyles
					],
					label: AppStrings.Integrations.Widgets.Actions.StyleLabel(),
					defaultValue: WidgetActionParameters.Unchanged),
				ActionParameter.Color("color",
					label: AppStrings.Integrations.Widgets.Actions.ColorLabel(),
					description: AppStrings.Integrations.Widgets.Actions.BorderColorDescription(),
					supportsReset: true)
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				BorderStyle = WidgetActionParameters.ReadOptional(context, "style"),
				BorderColor = WidgetActionParameters.IsReset(context, "color")
					? null
					: WidgetActionParameters.ReadOptional(context, "color")
			},
			context => WidgetActionParameters.IsReset(context, "color")
				? [WidgetAppearanceProperty.BorderColor]
				: []);

	private static string? ReadText(ActionExecutionContext context, string name)
		=> context.Parameters.TryGetValue(name, out var value) ? value.ToString() ?? string.Empty : null;
}
