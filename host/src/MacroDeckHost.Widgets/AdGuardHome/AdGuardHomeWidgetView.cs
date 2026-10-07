using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.AdGuardHome;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.AdGuardHome;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal sealed record AdGuardHomeWidgetCommands(
	Func<string, UiEventOutcome> Pause,
	Func<UiEventOutcome> Enable)
{
	public static readonly AdGuardHomeWidgetCommands None =
		new(_ => UiEventOutcome.Accepted, () => UiEventOutcome.Accepted);
}

internal static class AdGuardHomeWidgetView
{
	public const string Root = "adguardHome";

	private const string EnabledColor = "#68bc71";
	private const string PausedColor = "#e0a03a";
	private const string DisabledColor = "#e5534b";
	private const string UnknownColor = "#8b8b8b";
	private const string PauseButtonColor = "#3a3a42";
	private const string EnableButtonColor = "#2f855a";
	private const string ButtonTextColor = "#ffffff";
	private const string TileTint = "#8b8b8b";
	private const double TileOpacity = 0.14;

	private static readonly UiLength _titleText = UiLength.Capped(0.12, 14);
	private static readonly UiLength _text = UiLength.Capped(0.095, 11.5);
	private static readonly UiLength _smallText = UiLength.Capped(0.08, 9.5);
	private static readonly UiLength _valueText = UiLength.Capped(0.16, 19);
	private static readonly UiLength _logo = UiLength.Capped(0.16, 19);
	private static readonly UiLength _shield = UiLength.Capped(0.3, 36);
	private static readonly UiLength _tileIcon = UiLength.Capped(0.12, 14);
	private static readonly UiLength _gap = UiLength.Capped(0.035, 4);
	private static readonly UiLength _buttonHeight = UiLength.Capped(0.24, 28);
	private static readonly UiLength _radius = UiLength.Capped(0.07, 8);

	public static UiElement Build(
		UiState<AdGuardHomeViewState> state,
		AdGuardHomeWidgetOptions options,
		AdGuardHomeIconResources icons,
		AdGuardHomeWidgetCommands commands,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(icons);
		ArgumentNullException.ThrowIfNull(commands);

		var builder = new Builder(state, options, icons, commands);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Gap = _gap,
			Children =
			[
				builder.Header(),
				new UiWhen
				{
					Key = "unavailableGate",
					Condition = () => !state.Value.Available,
					Content = builder.Unavailable,
				},
				new UiWhen
				{
					Key = "availableGate",
					Condition = () => state.Value.Available,
					Content = builder.Body,
				},
			],
		};
	}

	internal static string ProtectionColor(AdGuardHomeProtection protection)
		=> protection switch
		{
			AdGuardHomeProtection.Enabled => EnabledColor,
			AdGuardHomeProtection.Paused => PausedColor,
			AdGuardHomeProtection.Disabled => DisabledColor,
			_ => UnknownColor,
		};

	private sealed class Builder(
		UiState<AdGuardHomeViewState> state,
		AdGuardHomeWidgetOptions options,
		AdGuardHomeIconResources icons,
		AdGuardHomeWidgetCommands commands)
	{
		private readonly IReadOnlyList<string> _durations =
		[
			.. options.Durations.Where(id => AdGuardHomeWidgetType.Duration(id) is not null),
		];

		public UiStack Header()
		{
			var children = new List<UiElement>();
			if (icons.Logo is { } logo)
			{
				children.Add(new UiImage { Key = "logo", Source = logo, Size = _logo, MainSize = _logo });
			}

			children.Add(new UiStack
			{
				Key = "titles",
				Direction = UiComponentDirections.Vertical,
				Fill = true,
				Children =
				[
					new UiTextRun
					{
						Key = "title",
						Text = UiText.From(() => state.Value.Title),
						Size = _titleText,
						MinSize = _text,
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Primary,
						MaxLines = 1,
					},
					new UiWhen
					{
						Key = "versionGate",
						Condition = () => options.ShowVersion && state.Value.Version is not null,
						Content = () => new UiTextRun
						{
							Key = "version",
							Text = UiText.From(() => state.Value.Version),
							Size = _smallText,
							Role = UiComponentTextRoles.Muted,
							MaxLines = 1,
						},
					},
				],
			});

			if (options.ShowStatus)
			{
				children.Add(StatusDot());
			}

			return new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = _gap,
				Children = children,
			};
		}

		public UiStack Unavailable()
			=> new()
			{
				Key = "unavailable",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Fill = true,
				Gap = _gap,
				Children =
				[
					new UiIcon
					{
						Key = "icon",
						Icon = UiValue.From(() => state.Value.Connecting ? UiIcons.Refresh : UiIcons.AlertTriangle),
						Size = _tileIcon,
						MainSize = _tileIcon,
						Role = UiComponentTextRoles.Muted,
					},
					new UiTextRun
					{
						Key = "message",
						Text = UiText.Optional(() => state.Value.Message),
						Size = _text,
						MinSize = _smallText,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						MaxLines = 3,
					},
				],
			};

		public UiResponsive Body()
			=> options.View switch
			{
				AdGuardHomeWidgetType.StatisticsView => Statistics(),
				AdGuardHomeWidgetType.OverviewView => Overview(),
				_ => Control(),
			};

		private UiResponsive Control()
			=> new()
			{
				Key = "control",
				Fill = true,
				Default = ControlBody("small", buttonsPerRow: 2, rows: 1, shield: false),
				Variants =
				[
					new UiResponsiveVariant { MinWidth = 1.5, MinHeight = 1.5, Content = ControlBody("large", 4, 2, true) },
					new UiResponsiveVariant { MinWidth = 2.5, Content = ControlBody("wide", buttonsPerRow: 6, 1, true) },
					new UiResponsiveVariant { MinWidth = 1.5, Content = ControlBody("medium", buttonsPerRow: 4, 1, false) },
					new UiResponsiveVariant { MinHeight = 1.5, Content = ControlBody("tall", buttonsPerRow: 2, 2, true) },
				],
			};

		private UiResponsive Statistics()
			=> new()
			{
				Key = "statistics",
				Fill = true,
				Default = CompactStatistics("small", maxRows: 2),
				Variants =
				[
					new UiResponsiveVariant { MinWidth = 2.5, MinHeight = 1.5, Content = Tiles("large", 8, 4) },
					new UiResponsiveVariant { MinWidth = 1.5, MinHeight = 1.5, Content = Tiles("square", 4, 2) },
					new UiResponsiveVariant { MinWidth = 2.5, Content = Tiles("wide", maxTiles: 6, columns: 6) },
					new UiResponsiveVariant { MinWidth = 1.5, Content = Tiles("medium", maxTiles: 4, columns: 4) },
					new UiResponsiveVariant { MinHeight = 1.5, Content = CompactStatistics("tall", maxRows: 5) },
				],
			};

		private UiResponsive Overview()
			=> new()
			{
				Key = "overview",
				Fill = true,
				Default = ControlBody("small", buttonsPerRow: 2, rows: 1, shield: false),
				Variants =
				[
					new UiResponsiveVariant
					{
						MinWidth = 2.5,
						MinHeight = 1.5,
						Content = Combined("large", buttonsPerRow: 4, maxTiles: 4, tileColumns: 4),
					},
					new UiResponsiveVariant
					{
						MinWidth = 1.5,
						MinHeight = 1.5,
						Content = Combined("square", buttonsPerRow: 4, maxTiles: 4, tileColumns: 2),
					},
					new UiResponsiveVariant { MinWidth = 1.5, Content = ControlBody("medium", 4, 1, false) },
					new UiResponsiveVariant { MinHeight = 1.5, Content = ControlBody("tall", 2, 2, true) },
				],
			};

		private UiStack Combined(string key, int buttonsPerRow, int maxTiles, int tileColumns)
			=> new()
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Children =
				[
					ProtectionLine(shield: false),
					Failure(),
					ButtonArea(buttonsPerRow, rows: 1),
					Tiles("tiles", maxTiles, tileColumns, fill: true),
				],
			};

		private UiStack ControlBody(string key, int buttonsPerRow, int rows, bool shield)
			=> new()
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Children =
				[
					ProtectionLine(shield),
					Failure(),
					new UiStack { Key = "spacer", Fill = true },
					ButtonArea(buttonsPerRow, rows),
				],
			};

		private UiWhen Failure()
			=> new()
			{
				Key = "failureGate",
				Condition = () => state.Value.HasFailure,
				Content = () => new UiTextRun
				{
					Key = "failure",
					Text = UiText.Optional(() => state.Value.Failure),
					Size = _smallText,
					Color = DisabledColor,
					MaxLines = 2,
				},
			};

		private UiStack ButtonArea(int buttonsPerRow, int rows)
			=> new()
			{
				Key = "buttons",
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Children =
				[
					new UiWhen
					{
						Key = "pauseGate",
						Condition = () => state.Value.Protection == AdGuardHomeProtection.Enabled && _durations.Count > 0,
						Content = () => PauseButtons(buttonsPerRow, rows),
					},
					new UiWhen
					{
						Key = "enableGate",
						Condition = () => state.Value.Protection is AdGuardHomeProtection.Paused or
							AdGuardHomeProtection.Disabled,
						Content = () => ButtonRow("enableRow",
							[Button("enable", Strings.EnableProtection(), EnableButtonColor, _ => commands.Enable())]),
					},
				],
			};

		private UiStack ProtectionLine(bool shield)
			=> new()
			{
				Key = "protection",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = _gap,
				Children =
				[
					new UiImage
					{
						Key = "shield",
						Source = UiValue.From(() => state.Value.Protection == AdGuardHomeProtection.Enabled
							? icons.Protected
							: icons.Unprotected),
						Size = shield ? _shield : _text,
						MainSize = shield ? _shield : _text,
					},
					new UiTextRun
					{
						Key = "status",
						Text = UiText.Optional(() => state.Value.Message),
						Size = _text,
						MinSize = _smallText,
						Weight = UiComponentTextWeights.Medium,
						Color = UiValue.From(() => ProtectionColor(state.Value.Protection)),
						MaxLines = 2,
					},
				],
			};

		private UiStack PauseButtons(int buttonsPerRow, int rows)
		{
			var shown = _durations.Take(buttonsPerRow * rows).ToList();
			return new UiStack
			{
				Key = "pauseButtons",
				Direction = UiComponentDirections.Vertical,
				Gap = _gap,
				Children =
				[
					.. shown.Chunk(buttonsPerRow).Select((chunk, index) => ButtonRow("row" + index,
					[
						.. chunk.Select(id => Button("pause-" + id, ShortLabel(id), PauseButtonColor, _ => commands.Pause(id))),
					])),
				],
			};
		}

		private static UiStack ButtonRow(string key, IReadOnlyList<UiElement> buttons)
			=> new()
			{
				Key = key,
				Direction = UiComponentDirections.Horizontal,
				Gap = _gap,
				MainSize = _buttonHeight,
				Children = buttons,
			};

		private static LocalizedText ShortLabel(string durationId)
			=> AdGuardHomeWidgetType.Duration(durationId)?.Length is { } length
				? length.TotalMinutes < 60
					? Strings.ShortMinutes(count: ((int)length.TotalMinutes).ToString(CultureInfo.CurrentCulture))
					: Strings.ShortHours(count: ((int)length.TotalHours).ToString(CultureInfo.CurrentCulture))
				: Strings.Off();

		private static UiButton Button(
			string key,
			LocalizedText label,
			string background,
			Func<UiEventData, UiEventOutcome> press)
			=> new()
			{
				Key = key,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Fill = true,
				Background = background,
				Events = [UiEventHandler.On(UiComponentEvents.Press, press)],
				Children =
				[
					new UiTextRun
					{
						Key = "label",
						Text = label,
						Size = _text,
						MinSize = _smallText,
						Weight = UiComponentTextWeights.Medium,
						Color = ButtonTextColor,
						Align = UiComponentAlignments.Center,
						MaxLines = 1,
					},
				],
			};

		private UiStack CompactStatistics(string key, int maxRows)
			=> new()
			{
				Key = key,
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Gap = _gap,
				Children =
				[
					.. options.Statistics.Take(maxRows).Select(statistic => new UiStack
					{
						Key = statistic,
						Direction = UiComponentDirections.Horizontal,
						Align = UiComponentAlignments.Center,
						Gap = _gap,
						Children =
						[
							new UiImage
							{
								Key = "icon",
								Source = icons.Statistics[statistic],
								Size = _tileIcon,
								MainSize = _tileIcon,
							},
							new UiTextRun
							{
								Key = "value",
								Text = UiText.From(() => state.Value.Value(statistic)),
								Size = _text,
								MinSize = _smallText,
								Weight = UiComponentTextWeights.Bold,
								Role = UiComponentTextRoles.Primary,
								MaxLines = 1,
							},
						],
					}),
				],
			};

		private UiGrid Tiles(string key, int maxTiles, int columns, bool fill = false)
		{
			var shown = options.Statistics.Take(maxTiles).ToList();
			var grid = new UiGrid
			{
				Key = key,
				Columns = Math.Max(1, Math.Min(columns, shown.Count)),
				Gap = _gap,
				Children = [.. shown.Select(Tile)],
			};
			return fill ? grid with { Fill = true } : grid;
		}

		private UiLayer Tile(string statistic)
			=> new()
			{
				Key = statistic,
				Fill = true,
				Children =
				[
					new UiModifier
					{
						Key = "face",
						Opacity = TileOpacity,
						Child = new UiShape
						{
							Key = "shape",
							Shape = UiComponentShapes.RoundedRectangle,
							CornerRadius = _radius,
							Color = TileTint,
						},
					},
					new UiStack
					{
						Key = "content",
						Direction = UiComponentDirections.Vertical,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Center,
						Padding = _gap,
						Gap = UiLength.Capped(0.015, 2),
						Children =
						[
							new UiImage
							{
								Key = "icon",
								Source = icons.Statistics[statistic],
								Size = _tileIcon,
								MainSize = _tileIcon,
							},
							new UiTextRun
							{
								Key = "value",
								Text = UiText.From(() => state.Value.Value(statistic)),
								Size = _valueText,
								MinSize = _text,
								Weight = UiComponentTextWeights.Bold,
								Role = UiComponentTextRoles.Primary,
								Align = UiComponentAlignments.Center,
								MaxLines = 1,
							},
							new UiTextRun
							{
								Key = "label",
								Text = StatisticLabel(statistic),
								Size = _smallText,
								Role = UiComponentTextRoles.Muted,
								Align = UiComponentAlignments.Center,
								MaxLines = 1,
							},
						],
					},
				],
			};

		private UiModifier StatusDot()
			=> new()
			{
				Key = "statusDot",
				Frame = new UiFrame { Width = _smallText, Height = _smallText },
				Child = new UiShape
				{
					Key = "shape",
					Shape = UiComponentShapes.Circle,
					Color = UiValue.From(() => state.Value.Available
						? ProtectionColor(state.Value.Protection)
						: UnknownColor),
				},
			};
	}

	internal static LocalizedText StatisticLabel(string statistic)
		=> statistic switch
		{
			AdGuardHomeWidgetType.DnsQueriesStatistic => Strings.Statistics.DnsQueries(),
			AdGuardHomeWidgetType.BlockedStatistic => Strings.Statistics.Blocked(),
			AdGuardHomeWidgetType.BlockedPercentageStatistic => Strings.Statistics.BlockedPercentage(),
			AdGuardHomeWidgetType.AverageProcessingTimeStatistic => Strings.Statistics.AverageProcessingTime(),
			AdGuardHomeWidgetType.SafeBrowsingStatistic => Strings.Statistics.SafeBrowsing(),
			AdGuardHomeWidgetType.ParentalStatistic => Strings.Statistics.Parental(),
			_ => Strings.Statistics.SafeSearch(),
		};
}
