using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

/// <summary>
/// Regression coverage for the component vocabularies. A type string is what a renderer switches on; a
/// rename makes a component unrenderable with no negotiation signal, and a property constant nobody
/// emits is a public string with no meaning that can never be removed.
///
/// <para>
/// The expected members below are <b>hard-coded literals</b>, never read from the constants under test.
/// Deriving them would make the test agree with any rename, which is the one thing it exists to catch.
/// </para>
/// </summary>
[TestFixture]
public class UiComponentVocabularyTests
{
	private static readonly string[] _expectedCoreComponents =
	[
		"ui.stack", "ui.text", "ui.image", "ui.range-bar", "ui.slider", "ui.button", "ui.layer",
		"ui.chart", "ui.text-field", "ui.list", "ui.transform", "ui.shape", "ui.icon", "ui.grid", "ui.gauge",
		"ui.toggle", "ui.segmented", "ui.dial",
		"ui.modifier", "ui.responsive", "ui.first-fit",
	];

	private static readonly string[] _expectedModifierMembers =
	[
		"background", "radius", "borderWidth", "borderColor", "borderLine", "accessibilityLabel",
		"accessibilityHint", "disabled",
	];

	private static readonly string[] _expectedClips = ["bounds", "circle", "capsule"];

	private static readonly string[] _expectedBorderLines = ["solid", "dashed", "dotted"];

	private static readonly string[] _expectedOverflows = ["shrink", "clip-start"];

	private static readonly string[] _expectedListAnchors = ["start", "end"];

	private static readonly string[] _expectedMacroDeckComponents =
	[
		"macrodeck.dynamic-text", "macrodeck.clock-dial", "macrodeck.progress-bar",
		"macrodeck.progress-text", "macrodeck.video-stream",
	];

	private static readonly string[] _expectedProperties =
	[
		"events", "mainSize", "fill", "direction", "justify", "align", "gap", "padding", "background",
		"text", "size", "minSize", "weight", "role", "color", "maxLines", "wrap", "fontFace", "source",
		"transition", "fit", "zoom", "offsetX", "offsetY", "opacity", "brightness", "saturation", "tint", "start",
		"end", "startColor", "endColor", "marker", "thickness", "value", "format", "seconds", "level",
		"step", "levelColor", "interaction", "borderStyle", "borderColor", "corner", "points", "plotTop", "digits",
		"answer", "placeholder", "rotation", "originX", "originY", "shape", "cornerRadius", "strokeColor",
		"strokeWidth", "path", "icon", "columns", "rows", "columnSpan", "rowSpan", "startAngle", "endAngle",
		"on", "selected",
		"modifiers", "frame", "clip", "mask", "variants", "spans", "overflow", "anchor", "stream", "shadow",
		"trackColor",
	];

	private static readonly string[] _expectedIconsVersion1 =
	[
		"action-button-type", "alert-triangle", "align-bottom", "align-center", "align-left",
		"align-middle", "align-right", "align-top", "arrow-down", "arrow-left", "arrow-right",
		"arrow-up", "bell", "braces-x", "bug", "chart", "check", "chevron-right", "clipboard",
		"clock-type", "code", "copy", "crosshair", "device-desktop", "device-floppy", "device-phone",
		"device-tablet", "disc", "discord", "dots-vertical", "download", "external-link", "file-text",
		"folder", "folder-plus", "globe", "grid", "heart", "history-graph-type", "image", "info",
		"layers", "list-play", "lock", "log-out", "message-square", "minus", "moon", "music-note",
		"music-player-type", "pause", "pencil", "pin", "pin-off", "play", "plus", "power", "puzzle",
		"refresh", "scissors", "search", "settings", "sidebar", "sliders", "star", "store", "sun",
		"trash", "undo", "unlock", "upload", "user", "weather-type", "wifi", "x", "zap",
	];

	private static readonly string[] _expectedShapes = ["rectangle", "rounded-rectangle", "circle", "capsule", "path"];

	private static readonly string[] _expectedTimeFormats =
	[
		"time", "date", "zone-name", "zone-offset",
		"time-12h", "time-12h-padded", "time-24h", "time-24h-unpadded",
		"date-day-first", "date-month-first", "date-iso", "date-long",
	];

	private static readonly string[] _expectedProgressFormats = ["elapsed", "remaining", "duration"];

	private static readonly string[] _expectedImageTransitions = ["crossfade"];

	private static readonly string[] _expectedImageFits = ["contain", "cover"];

	private static readonly string[] _expectedVideoStreamKeys = ["stream", "fit", "fill"];

	private static readonly string[] _expectedButtonCorners = ["tile"];

	private static readonly string[] _expectedSliderInteractions = ["relative"];

	private static readonly string[] _expectedBorderStyles =
	[
		"static", "heartbeat", "breathing", "blink", "comet", "ants", "hue-shift", "rgb",
	];

	private static readonly string[] _expectedEvents =
	[
		"change", "adjust", "press", "long-press", "press-start", "press-end", "reveal", "double-press", "drag",
		"drag-end", "swipe", "pinch", "pinch-end", "pointer-down", "pointer-move", "pointer-up", "tap",
	];

	private static UiSurface WidgetSurface()
		=> new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	[Test]
	public void The_core_component_set_is_the_twenty_one_ui_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiComponents.WellKnown, Is.EqualTo(_expectedCoreComponents).AsCollection);
			Assert.That(UiComponents.WellKnown.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(21));

			foreach (var type in UiComponents.WellKnown)
			{
				Assert.That(type,
					Does.StartWith("ui."),
					"a type string shares one flat vocabulary with every other profile, so it stays namespaced");
			}
		});
	}

	[Test]
	public void The_macro_deck_component_set_is_the_five_reader_resolved_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiMacroDeckComponents.WellKnown,
				Is.EqualTo(_expectedMacroDeckComponents).AsCollection);
			Assert.That(UiMacroDeckComponents.WellKnown.Distinct(StringComparer.Ordinal).Count(),
				Is.EqualTo(5));

			foreach (var type in UiMacroDeckComponents.WellKnown)
			{
				Assert.That(type, Does.StartWith("macrodeck."));
			}
		});
	}

	[Test]
	public void The_two_component_namespaces_are_disjoint()
	{
		// The split is what lets a reader tell "the framework ships this" from "Macro Deck does". One
		// string in both would make that question unanswerable from the type alone.
		Assert.That(UiComponents.WellKnown.Intersect(UiMacroDeckComponents.WellKnown, StringComparer.Ordinal),
			Is.Empty);
	}

	[Test]
	public void The_time_format_set_is_frozen()
	{
		Assert.That(UiTimeFormats.WellKnown, Is.EqualTo(_expectedTimeFormats).AsCollection);
	}

	[Test]
	public void The_progress_format_set_is_frozen()
	{
		Assert.That(UiProgressFormats.WellKnown, Is.EqualTo(_expectedProgressFormats).AsCollection);
	}

	[Test]
	public void No_progress_format_collides_with_a_time_format()
	{
		// The two format vocabularies share one `format` property key, told apart only by the node type
		// that carries it. A name in both would read as one thing on a clock and another on a timeline.
		Assert.That(UiProgressFormats.WellKnown
				.Intersect(UiTimeFormats.WellKnown, StringComparer.Ordinal),
			Is.Empty);
	}

	[Test]
	public void The_image_transition_set_is_frozen()
	{
		Assert.That(UiComponentImageTransitions.WellKnown, Is.EqualTo(_expectedImageTransitions).AsCollection);
	}

	[Test]
	public void The_image_fit_set_is_frozen()
	{
		Assert.That(UiComponentImageFits.WellKnown, Is.EqualTo(_expectedImageFits).AsCollection);
	}

	[Test]
	public void The_button_corner_set_is_frozen()
	{
		Assert.That(UiComponentButtonCorners.WellKnown, Is.EqualTo(_expectedButtonCorners).AsCollection);
	}

	[Test]
	public void The_slider_interaction_set_is_frozen()
	{
		Assert.That(UiComponentSliderInteractions.WellKnown, Is.EqualTo(_expectedSliderInteractions).AsCollection);
	}

	[Test]
	public void The_stack_overflow_set_is_frozen()
	{
		Assert.That(UiComponentOverflows.WellKnown, Is.EqualTo(_expectedOverflows).AsCollection);
	}

	[Test]
	public void The_list_anchor_set_is_frozen()
	{
		Assert.That(UiComponentListAnchors.WellKnown, Is.EqualTo(_expectedListAnchors).AsCollection);
	}

	[Test]
	public void The_border_style_set_is_frozen()
	{
		Assert.That(UiComponentBorderStyles.WellKnown, Is.EqualTo(_expectedBorderStyles).AsCollection);
	}

	[Test]
	public void The_widget_event_set_is_frozen()
	{
		Assert.That(UiComponentEvents.WellKnown, Is.EqualTo(_expectedEvents).AsCollection);
	}

	[Test]
	public void The_modifier_member_clip_and_border_line_sets_and_the_normative_constants_are_frozen()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiComponentModifiers.WellKnown, Is.EqualTo(_expectedModifierMembers).AsCollection);
			Assert.That(UiComponentClips.WellKnown, Is.EqualTo(_expectedClips).AsCollection);
			Assert.That(UiComponentBorderLines.WellKnown, Is.EqualTo(_expectedBorderLines).AsCollection);
			Assert.That(UiComponentModifiers.DimOpacity, Is.EqualTo(0.4));
			Assert.That(UiComponentModifiers.GestureSlop, Is.EqualTo(0.04));
			Assert.That(UiComponentModifiers.SwipeMinDistance, Is.EqualTo(0.2));
			Assert.That(UiComponentModifiers.SwipeMaxDurationMs, Is.EqualTo(500));
			Assert.That(UiComponentModifiers.GestureThrottleMs, Is.EqualTo(100));
			Assert.That(UiComponentModifiers.PointerMoveIntervalMs, Is.EqualTo(16));
			Assert.That(UiComponentModifiers.PointerMoveMaxSamples, Is.EqualTo(256));
			Assert.That(UiComponentModifiers.TapMaxDurationMs, Is.EqualTo(400));
		});
	}

	[Test]
	public void No_component_type_collides_with_a_configuration_type()
	{
		// The configuration profile spells "stack" unprefixed, with entirely different properties. A
		// collision would make a renderer's switch on type alone ambiguous, which is the whole reason
		// component types are prefixed - and it is what keeps leaving the configuration vocabulary
		// unprefixed safe.
		Assert.That(UiComponents.WellKnown.Concat(UiMacroDeckComponents.WellKnown)
				.Intersect(UiConfigPrimitives.WellKnown, StringComparer.Ordinal),
			Is.Empty);
	}

	[Test]
	public void A_video_stream_puts_only_the_stream_reference_on_the_wire()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(), new UiVideoStream
		{
			Key = "camera",
			Stream = UiValue.Of(new UiVideoStreamReference { Provider = "com.example.obs::studio", Id = "Preview scene" }),
			Fit = UiComponentImageFits.Cover,
			Fill = true,
		});

		Assert.Multiple(() =>
		{
			Assert.That(tree.Root.Type, Is.EqualTo("macrodeck.video-stream"));
			Assert.That(tree.Root.Properties.Keys, Is.EquivalentTo(_expectedVideoStreamKeys));
			Assert.That(tree.Root.Properties["stream"].GetRawText(),
				Is.EqualTo("""{"provider":"com.example.obs::studio","id":"Preview scene"}"""));
			Assert.That(tree.Root.Properties["fit"].GetString(), Is.EqualTo("cover"));
		});
	}

	[Test]
	public void Every_declared_property_key_is_reachable_from_at_least_one_rendered_primitive()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(), BuildEveryWidgetPrimitive());

		var renderedTypes = new HashSet<string>(StringComparer.Ordinal);
		var renderedKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var node in Walk(tree.Root))
		{
			renderedTypes.Add(node.Type);

			foreach (var key in node.Properties.Keys)
			{
				renderedKeys.Add(key);
			}
		}

		Assert.Multiple(() =>
		{
			// Both directions: an unemitted declared key and an undeclared emitted key each fail.
			Assert.That(renderedKeys, Is.EquivalentTo(_expectedProperties));
			Assert.That(renderedKeys, Is.EquivalentTo(UiComponentProperties.WellKnown));
			Assert.That(renderedTypes,
				Is.EquivalentTo(UiComponents.WellKnown.Concat(UiMacroDeckComponents.WellKnown)),
				"the fixture has to author one element of every type for the key union to be complete");
		});
	}

	[Test]
	public void A_length_carries_its_basis_fraction_and_omits_an_absent_cross_clamp()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Children =
				[
					new UiTextRun { Key = "unclamped", Size = 0.17 },
					new UiTextRun { Key = "clamped", Size = UiSize.FromBasis(0.06, 0.5) },
				],
			});

		var unclamped = tree.Root.Children[0].Properties["size"].GetRawText();
		var clamped = tree.Root.Children[1].Properties["size"].GetRawText();

		Assert.Multiple(() =>
		{
			Assert.That(unclamped, Is.EqualTo("""{"basis":0.17}"""));
			Assert.That(clamped, Is.EqualTo("""{"basis":0.06,"maxOfCross":0.5}"""));
		});
	}

	/// <summary>One element of each of the eleven types, with every property the DSL exposes populated.</summary>
	private static UiStack BuildEveryWidgetPrimitive()
		=> new()
		{
			Key = "root",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.SpaceBetween,
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Padding = 0.06,
			Background = "#101014",
			Overflow = UiComponentOverflows.ClipStart,
			MainSize = 0.42,
			Fill = true,
			Children =
			[
				new UiTextRun
				{
					Key = "text",
					Text = "Partly cloudy",
					Spans = UiValue.Of<IReadOnlyList<UiTextSpan>>(
					[
						UiTextSpan.FromText("Partly ", "#ffcc00", UiComponentTextWeights.Bold),
						UiTextSpan.FromText("cloudy"),
					]),
					Size = 0.11,
					MinSize = 0.078,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Secondary,
					Color = "#f0f0f0",
					Align = UiComponentAlignments.Center,
					MaxLines = 2,
					Wrap = true,
					FontFace = "app.macro-deck.font.condensed",
					Shadow = false,
					StrokeColor = "#000000",
					StrokeWidth = 0.01,
					MainSize = UiSize.FromBasis(0.144, 1.2),
					Fill = false,
				},
				new UiImage
				{
					Key = "image",
					Size = 0.2,
					Transition = UiComponentImageTransitions.Crossfade,
					Opacity = 0.6,
					Brightness = 0.6,
					Saturation = 0.55,
					Tint = "#f5c542",
					Source = UiValue.Of(new UiResource
					{
						ResourceId = "app.macro-deck.weather.clear-day",
						ContentHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
						MediaType = "image/svg+xml",
						ByteLength = 1024,
					}),
				},
				new UiDynamicText
				{
					Key = "dynamicText",
					Value = UiValue.Of(UiTimeReference.InZone("America/New_York")),
					Format = UiTimeFormats.Time,
					Seconds = true,
					Size = 0.24,
					MinSize = 0.13,
					Weight = UiComponentTextWeights.Bold,
					Role = UiComponentTextRoles.Primary,
					Align = UiComponentAlignments.Center,
					MainSize = 0.3,
					Fill = false,
				},
				new UiClockDial
				{
					Key = "clockDial",
					Value = UiValue.Of(UiTimeReference.Now()),
					Seconds = true,
					Fill = true,
				},
				new UiVideoStream
				{
					Key = "videoStream",
					Stream = UiValue.Of(new UiVideoStreamReference { Provider = "com.example.obs::studio", Id = "Program" }),
					Fit = UiComponentImageFits.Cover,
					Size = 0.5,
				},
				new UiProgressBar
				{
					Key = "progressBar",
					Value = UiValue.Of(UiProgressReference.Advancing(42_000,
						new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
						215_000)),
					StartColor = "#3aa0ff",
					EndColor = "#ff5a3a",
					Thickness = UiSize.FromBasis(0.03, 0.35),
					Fill = true,
				},
				new UiProgressText
				{
					Key = "progressText",
					Value = UiValue.Of(UiProgressReference.Halted(42_000,
						new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
						215_000)),
					Format = UiProgressFormats.Elapsed,
					Size = 0.09,
					MinSize = 0.07,
					Weight = UiComponentTextWeights.Medium,
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.End,
					MainSize = 0.12,
					Fill = false,
				},
				new UiRangeBar
				{
					Key = "bar",
					Start = 0.1,
					End = 0.75,
					StartColor = "#3aa0ff",
					EndColor = "#ff5a3a",
					Marker = 0.4,
					Thickness = UiSize.FromBasis(0.03, 0.35),
					Fill = true,
				},
				new UiSlider
				{
					Key = "slider",
					Level = 0.42,
					Step = 0.01,
					LevelColor = "#3aa0ff",
					Direction = UiComponentDirections.Vertical,
					Interaction = UiComponentSliderInteractions.Relative,
					Thickness = UiSize.FromBasis(0.12, 0.55),
					Fill = true,
					Events = [UiEventHandler.On(UiComponentEvents.Change, static () => { })],
				},
				new UiButton
				{
					Key = "button",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = 0.02,
					Padding = 0.05,
					Background = "#1c2430",
					Source = UiValue.Of(new UiResource
					{
						ResourceId = "app.macro-deck.button.sunrise",
						ContentHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
						MediaType = "image/svg+xml",
						ByteLength = 2048,
					}),
					Fit = UiComponentImageFits.Cover,
					Zoom = 1.35,
					OffsetX = 0.08,
					OffsetY = -0.05,
					Opacity = 0.85,
					Tint = "#f5c542",
					BorderStyle = UiComponentBorderStyles.Static,
					BorderColor = "#4f8cff",
					Corner = UiComponentButtonCorners.Tile,
					MainSize = 0.3,
					Fill = false,
					Events =
					[
						UiEventHandler.On(UiComponentEvents.Press, static () => { }),
						UiEventHandler.On(UiComponentEvents.LongPress, static () => { }),
						UiEventHandler.On(UiComponentEvents.PressStart, static () => { }),
						UiEventHandler.On(UiComponentEvents.PressEnd, static () => { }),
					],
					Children =
					[
						new UiTextRun
						{
							Key = "buttonLabel",
							Text = "Run scene",
							Size = 0.11,
						},
					],
				},
				new UiLayer
				{
					Key = "layer",
					Children =
					[
						new UiChart
						{
							Key = "chart",
							Points = UiValue.Of<IReadOnlyList<double>>([0.1, 0.4, 0.9]),
							Color = "#4f9dff",
							PlotTop = 0.66,
							Thickness = UiSize.Capped(0.0167, 2),
						},
						new UiTextRun
						{
							Key = "reading",
							Text = "73.4",
							Size = 0.26,
							Digits = 3.5,
						},
					],
				},
				new UiTransform
				{
					Key = "needle",
					Rotation = 42,
					OriginX = 0.5,
					OriginY = 0.9,
					Zoom = 1.2,
					OffsetX = 0.1,
					OffsetY = -0.05,
					Children =
					[
						new UiTextRun
						{
							Key = "needleGlyph",
							Text = "|",
							Size = 0.2,
						},
					],
				},
				new UiGrid
				{
					Key = "grid",
					Columns = 3,
					Rows = 2,
					Gap = 0.02,
					Padding = 0.03,
					Children =
					[
						new UiShape
						{
							Key = "shape",
							Shape = UiComponentShapes.Path,
							CornerRadius = 0.05,
							Color = "#2b6cee",
							StrokeColor = "#ffffff",
							StrokeWidth = UiSize.Capped(0.01, 2),
							Path = "M0 0 L1 0 L0.5 1 Z",
							ColumnSpan = 2,
							RowSpan = 2,
						},
						new UiIcon
						{
							Key = "icon",
							Icon = UiIcons.Play,
							Size = 0.2,
							Role = UiComponentTextRoles.Secondary,
							Color = "#ffcc00",
						},
						new UiSegmented
						{
							Key = "segmented",
							Selected = 1,
							LevelColor = "#34c759",
							Events = [UiEventHandler.On(UiComponentEvents.Change, static () => { })],
							Children =
							[
								new UiTextRun { Key = "day", Text = "Day" },
								new UiTextRun { Key = "night", Text = "Night" },
							],
						},
					],
				},
				new UiGauge
				{
					Key = "gauge",
					Level = 0.7,
					StartAngle = -120,
					EndAngle = 120,
					LevelColor = "#ff9500",
					TrackColor = "#3A3A3C",
					Thickness = 0.06,
				},
				new UiToggle
				{
					Key = "toggle",
					On = true,
					LevelColor = "#34c759",
					Size = 0.12,
					Events = [UiEventHandler.On(UiComponentEvents.Change, static () => { })],
				},
				new UiDial
				{
					Key = "dial",
					Level = 0.3,
					Step = 0.05,
					StartAngle = 0,
					EndAngle = 360,
					LevelColor = "#5856d6",
					Thickness = 0.05,
					Events = [UiEventHandler.On(UiComponentEvents.Adjust, static () => { })],
				},
				new UiModifier
				{
					Key = "framed",
					Padding = 0.03,
					Clip = UiComponentClips.Bounds,
					Mask = UiMask.Linear(180,
						new UiMaskStop { Offset = 0, Opacity = 1 },
						new UiMaskStop { Offset = 1, Opacity = 0 }),
					Frame = new UiFrame { AspectRatio = 1 },
					Radius = 0.05,
					Child = new UiTextRun { Key = "framedLabel", Text = "Framed" },
				},
				new UiResponsive
				{
					Key = "layouts",
					Default = new UiTextRun { Key = "compactLabel", Text = "21°" },
					Variants =
					[
						new UiResponsiveVariant
						{
							MinWidth = 1.5,
							Content = new UiTextRun { Key = "wideLabel", Text = "21° Sunny" },
						},
					],
				},
				new UiFirstFit
				{
					Key = "fitting",
					Children =
					[
						new UiTextRun { Key = "longLabel", Text = "21° Sunny" },
						new UiTextRun { Key = "shortLabel", Text = "21°" },
					],
				},
				new UiTextField
				{
					Key = "search",
					Text = "anthem",
					Placeholder = "Search...",
					Size = 0.045,
				},
				new UiList
				{
					Key = "results",
					Gap = 0.015,
					Padding = 0.02,
					Background = "#101014",
					Anchor = UiComponentListAnchors.End,
					Children =
					[
						new UiStack
						{
							Key = "row",
							Answer = "track-7",
							Children = [new UiTextRun { Key = "title", Text = "A track" }],
						},
					],
				},
			],
		};

	[Test]
	public void The_shape_set_is_frozen()
	{
		Assert.That(UiComponentShapes.WellKnown, Is.EqualTo(_expectedShapes).AsCollection);
	}

	[Test]
	public void The_first_icon_group_is_frozen_and_names_only_glyphs()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiIcons.Version1, Is.EqualTo(_expectedIconsVersion1).AsCollection);
			Assert.That(UiIcons.WellKnown, Is.SupersetOf(UiIcons.Version1));
			Assert.That(UiIcons.WellKnown, Has.None.AnyOf("xs", "sm", "md", "lg", "xl", "2xl", "wifi-solid-full"));
		});
	}

	[Test]
	public void An_icon_name_reports_the_component_version_that_draws_it()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiIcons.VersionOf("play"), Is.EqualTo(1));
			Assert.That(UiIcons.VersionOf("wifi"), Is.EqualTo(1));
			Assert.That(UiIcons.VersionOf("xs"), Is.Null);
			Assert.That(UiIcons.VersionOf("not-an-icon"), Is.Null);
		});
	}

	[Test]
	public void Grid_spans_reach_the_wire_only_when_declared()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiGrid
			{
				Key = "root",
				Columns = 2,
				Children =
				[
					new UiTextRun { Key = "plain", Text = "a" },
					new UiStack { Key = "wide", ColumnSpan = 2, RowSpan = 3 },
				],
			});

		var plain = tree.Root.Children[0].Properties;
		var wide = tree.Root.Children[1].Properties;

		Assert.Multiple(() =>
		{
			Assert.That(plain.ContainsKey("columnSpan"), Is.False);
			Assert.That(plain.ContainsKey("rowSpan"), Is.False);
			Assert.That(wide["columnSpan"].GetInt32(), Is.EqualTo(2));
			Assert.That(wide["rowSpan"].GetInt32(), Is.EqualTo(3));
		});
	}

	[Test]
	public void A_list_emits_its_direction_only_when_one_is_declared()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Children =
				[
					new UiList { Key = "plain", Children = [new UiTextRun { Key = "a", Text = "a" }] },
					new UiList
					{
						Key = "row",
						Direction = UiComponentDirections.Horizontal,
						Children = [new UiTextRun { Key = "b", Text = "b" }],
					},
				],
			});

		var lists = Walk(tree.Root).Where(node => node.Type == "ui.list")
			.ToDictionary(node => node.Id.Split('.')[^1], StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(lists["plain"].Properties.ContainsKey("direction"), Is.False);
			Assert.That(lists["row"].Properties["direction"].GetString(), Is.EqualTo("horizontal"));
		});
	}

	[Test]
	public void A_gauge_emits_its_track_colour_only_when_one_is_declared()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Children =
				[
					new UiGauge { Key = "themed", Level = 0.85, LevelColor = "#FFFFFF" },
					new UiGauge { Key = "custom", Level = 0.85, LevelColor = "#FFFFFF", TrackColor = "#3A3A3C" },
				],
			});

		var gauges = Walk(tree.Root).Where(node => node.Type == "ui.gauge")
			.ToDictionary(node => node.Id.Split('.')[^1], StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(gauges["themed"].Properties.ContainsKey("trackColor"), Is.False);
			Assert.That(gauges["custom"].Properties["trackColor"].GetString(), Is.EqualTo("#3A3A3C"));
		});
	}

	[Test]
	public void A_list_emits_its_anchor_only_when_one_is_declared()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Children =
				[
					new UiList { Key = "plain", Children = [new UiTextRun { Key = "a", Text = "a" }] },
					new UiList
					{
						Key = "chat",
						Anchor = UiComponentListAnchors.End,
						RequiredComponentVersion = 3,
						Fallback = new UiList { Key = "newestFirst", Children = [new UiTextRun { Key = "c", Text = "c" }] },
						Children = [new UiTextRun { Key = "b", Text = "b" }],
					},
				],
			});

		var plain = tree.Root.Children[0];
		var chat = tree.Root.Children[1];

		Assert.Multiple(() =>
		{
			Assert.That(plain.Properties.ContainsKey("anchor"), Is.False);
			Assert.That(chat.Properties["anchor"].GetString(), Is.EqualTo("end"));
			Assert.That(chat.RequiredComponentVersion, Is.EqualTo(3));
			Assert.That(chat.Fallback!.Properties.ContainsKey("anchor"), Is.False);
		});
	}

	[Test]
	public void A_reader_whose_list_stops_at_version_2_draws_the_fallback_of_an_anchored_list()
	{
		var chat = UiViewBuilder.Build(WidgetSurface(),
			new UiList
			{
				Key = "chat",
				Anchor = UiComponentListAnchors.End,
				RequiredComponentVersion = 3,
				Fallback = new UiList { Key = "newestFirst", Children = [new UiTextRun { Key = "b", Text = "b" }] },
				Children = [new UiTextRun { Key = "a", Text = "a" }],
			}).Root;
		var older = new UiCapabilities
		{
			UiProtocol = new UiVersionRange { Minimum = 3, Maximum = 4 },
			SupportsAllComponents = false,
			Components = new Dictionary<string, UiVersionRange>
			{
				["ui.list"] = new() { Minimum = 1, Maximum = 2 },
				["ui.text"] = new() { Minimum = 1, Maximum = 1 },
			},
		};

		Assert.Multiple(() =>
		{
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(chat, older).IsSupported, Is.False,
				"a version 2 list would ignore the anchor and leave a feed scrolled away from its newest row");
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(chat.Fallback!, older).IsSupported, Is.True);
		});
	}

	[Test]
	public void Text_spans_and_stack_overflow_reach_the_wire_only_when_declared()
	{
		var emote = new UiResource
		{
			ResourceId = "acme.emote-25",
			ContentHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
		};
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Overflow = UiComponentOverflows.ClipStart,
				Children =
				[
					new UiStack { Key = "plainStack", Children = [new UiTextRun { Key = "plain", Text = "a" }] },
					new UiTextRun
					{
						Key = "rich",
						Text = "hi Kappa",
						Spans = UiValue.Of<IReadOnlyList<UiTextSpan>>(
						[
							UiTextSpan.FromText("hi ", "#9146ff", UiComponentTextWeights.Bold),
							UiTextSpan.FromImage(emote, "Kappa"),
							UiTextSpan.FromImage(emote),
						]),
					},
				],
			});

		var plainStack = tree.Root.Children[0];
		var plain = plainStack.Children[0];
		var rich = tree.Root.Children[1];

		Assert.Multiple(() =>
		{
			Assert.That(tree.Root.Properties["overflow"].GetString(), Is.EqualTo("clip-start"));
			Assert.That(plainStack.Properties.ContainsKey("overflow"), Is.False);
			Assert.That(plain.Properties.ContainsKey("spans"), Is.False);
			Assert.That(rich.Properties["text"].GetString(), Is.EqualTo("hi Kappa"));
			Assert.That(rich.Properties["spans"].GetRawText(), Is.EqualTo(
				"""[{"text":"hi ","color":"#9146ff","weight":"bold"},""" +
				"""{"image":{"resourceId":"acme.emote-25","contentHash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"},"alt":"Kappa"},""" +
				"""{"image":{"resourceId":"acme.emote-25","contentHash":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}}]"""));
		});
	}

	[Test]
	public void Text_shadow_and_outline_reach_the_wire_only_when_declared()
	{
		var tree = UiViewBuilder.Build(WidgetSurface(),
			new UiStack
			{
				Key = "root",
				Children =
				[
					new UiTextRun { Key = "plain", Text = "a" },
					new UiTextRun
					{
						Key = "styled",
						Text = "b",
						Shadow = false,
						StrokeColor = "#112233",
						StrokeWidth = 0.02,
					},
				],
			});

		var plain = tree.Root.Children[0];
		var styled = tree.Root.Children[1];

		Assert.Multiple(() =>
		{
			Assert.That(plain.Properties.ContainsKey("shadow"), Is.False);
			Assert.That(plain.Properties.ContainsKey("strokeColor"), Is.False);
			Assert.That(plain.Properties.ContainsKey("strokeWidth"), Is.False);
			Assert.That(styled.Properties["shadow"].GetBoolean(), Is.False);
			Assert.That(styled.Properties["strokeColor"].GetString(), Is.EqualTo("#112233"));
			Assert.That(styled.Properties["strokeWidth"].GetRawText(), Is.EqualTo("""{"basis":0.02}"""));
		});
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}
	}
}
