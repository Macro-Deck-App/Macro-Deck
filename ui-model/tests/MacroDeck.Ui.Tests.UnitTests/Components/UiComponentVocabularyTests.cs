using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
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
		"ui.chart", "ui.text-field", "ui.list",
	];

	private static readonly string[] _expectedMacroDeckComponents =
	[
		"macrodeck.dynamic-text", "macrodeck.clock-dial", "macrodeck.progress-bar",
		"macrodeck.progress-text",
	];

	private static readonly string[] _expectedProperties =
	[
		"events", "mainSize", "fill", "direction", "justify", "align", "gap", "padding", "background",
		"text", "size", "minSize", "weight", "role", "color", "maxLines", "wrap", "fontFace", "source",
		"transition", "fit", "zoom", "offsetX", "offsetY", "opacity", "brightness", "saturation", "start",
		"end", "startColor", "endColor", "marker", "thickness", "value", "format", "seconds", "level",
		"step", "levelColor", "borderStyle", "borderColor", "corner", "points", "plotTop", "digits",
		"answer", "placeholder",
	];

	private static readonly string[] _expectedTimeFormats =
	[
		"time", "date", "zone-name", "zone-offset",
		"time-12h", "time-12h-padded", "time-24h", "time-24h-unpadded",
		"date-day-first", "date-month-first", "date-iso", "date-long",
	];

	private static readonly string[] _expectedProgressFormats = ["elapsed", "remaining", "duration"];

	private static readonly string[] _expectedImageTransitions = ["crossfade"];

	private static readonly string[] _expectedImageFits = ["contain", "cover"];

	private static readonly string[] _expectedButtonCorners = ["tile"];

	private static readonly string[] _expectedBorderStyles =
	[
		"static", "heartbeat", "breathing", "blink", "comet", "ants", "hue-shift", "rgb",
	];

	private static readonly string[] _expectedEvents =
		["change", "adjust", "press", "long-press", "press-start", "press-end", "reveal"];

	private static UiSurface WidgetSurface()
		=> new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	[Test]
	public void The_core_component_set_is_the_ten_ui_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiComponents.WellKnown, Is.EqualTo(_expectedCoreComponents).AsCollection);
			Assert.That(UiComponents.WellKnown.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(10));

			foreach (var type in UiComponents.WellKnown)
			{
				Assert.That(type,
					Does.StartWith("ui."),
					"a type string shares one flat vocabulary with every other profile, so it stays namespaced");
			}
		});
	}

	[Test]
	public void The_macro_deck_component_set_is_the_four_reference_resolving_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiMacroDeckComponents.WellKnown,
				Is.EqualTo(_expectedMacroDeckComponents).AsCollection);
			Assert.That(UiMacroDeckComponents.WellKnown.Distinct(StringComparer.Ordinal).Count(),
				Is.EqualTo(4));

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

	/// <summary>One element of each of the ten types, with every property the DSL exposes populated.</summary>
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
			MainSize = 0.42,
			Fill = true,
			Children =
			[
				new UiTextRun
				{
					Key = "text",
					Text = "Partly cloudy",
					Size = 0.11,
					MinSize = 0.078,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Secondary,
					Color = "#f0f0f0",
					Align = UiComponentAlignments.Center,
					MaxLines = 2,
					Wrap = true,
					FontFace = "app.macro-deck.font.condensed",
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
