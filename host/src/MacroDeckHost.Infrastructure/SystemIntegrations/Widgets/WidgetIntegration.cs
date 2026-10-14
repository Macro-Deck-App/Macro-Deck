using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Infrastructure.SystemIntegrations.Widgets;

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
			SetAccentColor(),
			SetIcon(),
			SetIconDisplay(),
			SetIconColor(),
			SetIconAppearance(),
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

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	private WidgetAppearanceActionDefinition SetLabel()
	{
		return new WidgetAppearanceActionDefinition("set-label",
			AppStrings.Integrations.Widgets.Actions.SetLabelName(),
			AppStrings.Integrations.Widgets.Actions.SetLabelDescription(),
			WidgetAppearanceProperty.Label,
			[
				ActionParameter.MultilineText("label",
					AppStrings.Integrations.Widgets.Actions.LabelLabel(),
					AppStrings.Integrations.Widgets.Actions.LabelDescription(),
					AppStrings.Integrations.Widgets.Actions.LabelPlaceholder())
			],
			() => _widgets,
			// Empty is meaningful here: a label is text, and clearing it is a thing to want. Every
			// other property reads empty as "unchanged", because there is no useful empty color.
			context => new WidgetAppearancePatch { Label = ReadText(context, "label") });
	}

	private WidgetAppearanceActionDefinition SetBackgroundColor()
	{
		return new WidgetAppearanceActionDefinition("set-background-color",
			AppStrings.Integrations.Widgets.Actions.SetBackgroundColorName(),
			AppStrings.Integrations.Widgets.Actions.SetBackgroundColorDescription(),
			WidgetAppearanceProperty.BackgroundColor,
			[
				AlphaColor(AppStrings.Integrations.Widgets.Actions.ColorLabel())
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
	}

	private WidgetAppearanceActionDefinition SetLabelColor()
	{
		return new WidgetAppearanceActionDefinition("set-label-color",
			AppStrings.Integrations.Widgets.Actions.SetLabelColorName(),
			AppStrings.Integrations.Widgets.Actions.SetLabelColorDescription(),
			WidgetAppearanceProperty.LabelColor,
			[
				AlphaColor(AppStrings.Integrations.Widgets.Actions.ColorLabel())
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
	}

	private WidgetAppearanceActionDefinition SetAccentColor()
	{
		return new WidgetAppearanceActionDefinition("set-accent-color",
			AppStrings.Integrations.Widgets.Actions.SetAccentColorName(),
			AppStrings.Integrations.Widgets.Actions.SetAccentColorDescription(),
			WidgetAppearanceProperty.AccentColor,
			[
				AlphaColor(AppStrings.Integrations.Widgets.Actions.ColorLabel())
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				AccentColor = WidgetActionParameters.IsReset(context, "color")
					? null
					: WidgetActionParameters.ReadOptional(context, "color")
			},
			context => WidgetActionParameters.IsReset(context, "color")
				? [WidgetAppearanceProperty.AccentColor]
				: []);
	}

	private WidgetAppearanceActionDefinition SetIconColor()
	{
		return new WidgetAppearanceActionDefinition("set-icon-color",
			AppStrings.Integrations.Widgets.Actions.SetIconColorName(),
			AppStrings.Integrations.Widgets.Actions.SetIconColorDescription(),
			WidgetAppearanceProperty.IconColor,
			[
				AlphaColor(AppStrings.Integrations.Widgets.Actions.ColorLabel())
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				IconColor = WidgetActionParameters.IsReset(context, "color")
					? null
					: WidgetActionParameters.ReadOptional(context, "color")
			},
			context => WidgetActionParameters.IsReset(context, "color")
				? [WidgetAppearanceProperty.IconColor]
				: []);
	}

	private WidgetAppearanceActionDefinition SetIconAppearance()
	{
		return new WidgetAppearanceActionDefinition("set-icon-appearance",
			AppStrings.Integrations.Widgets.Actions.SetIconAppearanceName(),
			AppStrings.Integrations.Widgets.Actions.SetIconAppearanceDescription(),
			WidgetAppearanceProperty.IconAppearance,
			[
				new ActionParameter
				{
					Name = "iconAppearance",
					Type = ActionParameterType.DynamicChoice,
					OptionsSourceId = IconOptionsSourceIds.Appearances,
					Options = IconAppearanceOptions.BuiltIn(),
					Label = AppStrings.Integrations.Widgets.Actions.IconAppearanceLabel(),
					Description = AppStrings.Integrations.Widgets.Actions.IconAppearanceDescription(),
					Placeholder = AppStrings.Widgets.Appearance.Icon.AppearanceAutomatic(),
					DefaultValue = WidgetAppearanceValues.Reset
				}
			],
			() => _widgets,
			context => new WidgetAppearancePatch
			{
				IconAppearance = WidgetActionParameters.IsReset(context, "iconAppearance")
					? null
					: WidgetActionParameters.ReadOptional(context, "iconAppearance")
			},
			context => WidgetActionParameters.IsReset(context, "iconAppearance")
				? [WidgetAppearanceProperty.IconAppearance]
				: [],
			IconControlledByProvider);
	}

	private static ActionResult? IconControlledByProvider(IWidgetApi widgets, string widgetId)
	{
		return widgets.GetWidgets().FirstOrDefault(w => w.Id == widgetId) is { HasActiveIconProvider: true }
			? ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				AppStrings.Integrations.Widgets.Errors.IconControlledByProvider())
			: null;
	}

	private WidgetAppearanceActionDefinition SetIcon()
	{
		return new WidgetAppearanceActionDefinition("set-icon",
			AppStrings.Integrations.Widgets.Actions.SetIconName(),
			AppStrings.Integrations.Widgets.Actions.SetIconDescription(),
			WidgetAppearanceProperty.Icon,
			[
				ActionParameter.Icon("iconId",
					AppStrings.Integrations.Widgets.Actions.IconLabel(),
					AppStrings.Integrations.Widgets.Actions.IconDescription())
			],
			() => _widgets,
			context => new WidgetAppearancePatch { IconId = ReadText(context, "iconId") },
			onApplyFailed: IconControlledByProvider);
	}

	private WidgetAppearanceActionDefinition SetIconDisplay()
	{
		return new WidgetAppearanceActionDefinition("set-icon-display",
			AppStrings.Integrations.Widgets.Actions.SetIconDisplayName(),
			AppStrings.Integrations.Widgets.Actions.SetIconDisplayDescription(),
			WidgetAppearanceProperty.IconDisplay,
			[
				ActionParameter.Choice("iconFit",
					_iconFits,
					AppStrings.Integrations.Widgets.Actions.IconFitDisplayLabel(),
					AppStrings.Integrations.Widgets.Actions.IconFitDisplayDescription(),
					WidgetActionParameters.Unchanged),
				UnfilledNumber("iconZoom",
					AppStrings.Integrations.Widgets.Actions.IconZoomLabel(),
					AppStrings.Integrations.Widgets.Actions.IconZoomDescription(),
					10,
					400),
				UnfilledNumber("iconOffsetX",
					AppStrings.Integrations.Widgets.Actions.IconOffsetXLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOffsetXDescription(),
					-100,
					100),
				UnfilledNumber("iconOffsetY",
					AppStrings.Integrations.Widgets.Actions.IconOffsetYLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOffsetYDescription(),
					-100,
					100),
				UnfilledNumber("iconOpacity",
					AppStrings.Integrations.Widgets.Actions.IconOpacityLabel(),
					AppStrings.Integrations.Widgets.Actions.IconOpacityDescription(),
					0,
					100)
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
	}

	private static ActionParameter UnfilledNumber(
		string name,
		LocalizedText label,
		LocalizedText description,
		double min,
		double max)
	{
		return new ActionParameter
		{
			Name = name,
			Type = ActionParameterType.Number,
			Label = label,
			Description = description,
			Min = min,
			Max = max,
			DefaultValue = WidgetActionParameters.Unchanged
		};
	}

	private WidgetAppearanceActionDefinition SetFont()
	{
		return new WidgetAppearanceActionDefinition("set-font",
			AppStrings.Integrations.Widgets.Actions.SetFontName(),
			AppStrings.Integrations.Widgets.Actions.SetFontDescription(),
			WidgetAppearanceProperty.Font,
			[
				ActionParameter.Autocomplete("fontFaceId",
					AppStrings.Integrations.Widgets.Actions.FontLabel(),
					optionsSourceId: WidgetOptionsSources.Fonts,
					placeholder: AppStrings.Integrations.Widgets.Actions.Unchanged()),
				ActionParameter.Number("fontSize",
					AppStrings.Integrations.Widgets.Actions.FontSizeLabel(),
					AppStrings.Integrations.Widgets.Actions.FontSizeDescription(),
					// 0 is the historical, persisted Unchanged sentinel. The dedicated action UI
					// deliberately only offers 1..100 as concrete values, but schemas must keep
					// old/default flows valid.
					0,
					100,
					defaultValue: 0),
				ActionParameter.Choice("textAlign",
					_textAlignments,
					AppStrings.Integrations.Widgets.Actions.AlignmentLabel(),
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
					AppStrings.Integrations.Widgets.Actions.PositionLabel(),
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
	}

	private WidgetAppearanceActionDefinition SetBorder()
	{
		return new WidgetAppearanceActionDefinition("set-border",
			AppStrings.Integrations.Widgets.Actions.SetBorderName(),
			AppStrings.Integrations.Widgets.Actions.SetBorderDescription(),
			WidgetAppearanceProperty.Border,
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
					AppStrings.Integrations.Widgets.Actions.StyleLabel(),
					defaultValue: WidgetActionParameters.Unchanged),
				AlphaColor(AppStrings.Integrations.Widgets.Actions.ColorLabel(),
					AppStrings.Integrations.Widgets.Actions.BorderColorDescription())
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
	}

	private static ActionParameter AlphaColor(LocalizedText label, LocalizedText description = default)
	{
		return new ActionParameter
		{
			Name = "color",
			Type = ActionParameterType.Color,
			Label = label,
			Description = description,
			SupportsReset = true,
			AllowAlpha = true
		};
	}

	private static string? ReadText(ActionExecutionContext context, string name)
	{
		return context.Parameters.TryGetValue(name, out var value) ? value.ToString() ?? string.Empty : null;
	}
}
