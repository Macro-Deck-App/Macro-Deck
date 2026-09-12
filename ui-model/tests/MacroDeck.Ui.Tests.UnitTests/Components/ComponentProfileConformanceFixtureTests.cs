using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Components;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

/// <summary>
/// Guards the shared renderer-conformance fixture in <c>ui-model/fixtures/component-profile/</c>.
///
/// <para>
/// The fixture is hand-authored from the profile's rules, so what needs proving here is that it is
/// genuinely canonical - a fixture this model would not itself produce cannot be a conformance target -
/// and that it still covers the whole vocabulary. The coverage check is what stops a fifth primitive from
/// shipping unpinned, and what makes trimming the fixture a deliberate act rather than an accident.
/// </para>
/// </summary>
[TestFixture]
public class ComponentProfileConformanceFixtureTests
{
	private static string FixtureDirectory()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

		while (directory is not null &&
			!Directory.Exists(Path.Combine(directory.FullName, "ui-model", "fixtures")))
		{
			directory = directory.Parent;
		}

		Assert.That(directory, Is.Not.Null, "the repository root carrying ui-model/fixtures was not found");

		return Path.Combine(directory!.FullName, "ui-model", "fixtures", "component-profile");
	}

	/// <summary>
	/// Every tree in the fixture directory. One per widget shape rather than one growing tree: the
	/// weather tree's proportions are themselves the thing under test, and folding a second widget into
	/// it would dilute what a renderer is being checked against.
	/// </summary>
	private static IEnumerable<string> FixtureTrees()
	{
		yield return "conformance-tree.json";
		yield return "conformance-clock-tree.json";
		yield return "conformance-clock-formats-tree.json";
		yield return "conformance-slider-tree.json";
		yield return "conformance-action-button-tree.json";
		yield return "conformance-music-player-tree.json";
		yield return "conformance-history-graph-tree.json";
		yield return "conformance-picker-tree.json";
		yield return "conformance-gauge-tree.json";
		yield return "conformance-building-blocks-tree.json";
	}

	private static readonly string[] _transformKeys = ["rotation", "originX", "originY", "zoom", "offsetX", "offsetY"];

	// Stated once so the assertion that uses it fits a line.
	private const string _playingProgressJson =
		"""{"$progress":{"positionMs":42000,"durationMs":215000,"anchor":"2026-08-25T12:00:00.000Z"}}""";

	private static string ReadTree(string fileName = "conformance-tree.json")
		=> File.ReadAllText(Path.Combine(FixtureDirectory(), fileName)).Trim();

	[TestCaseSource(nameof(FixtureTrees))]
	public void The_fixture_is_already_in_canonical_form(string fileName)
	{
		var text = ReadTree(fileName);

		var tree = JsonSerializer.Deserialize<UiTree>(text, UiCanonicalJson.Options);

		Assert.That(tree, Is.Not.Null);
		Assert.That(UiCanonicalJson.Serialize(tree!),
			Is.EqualTo(text),
			"the fixture must be byte-identical to what this model writes, or it cannot be a conformance target");
	}

	[Test]
	public void The_gauge_fixture_pins_every_transform_key_the_identity_shape_nesting_and_the_fallback()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-gauge-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(nodes["conformance.needle"].Properties.Keys, Is.EquivalentTo(_transformKeys));
			Assert.That(nodes["conformance.needle"].Fallback?.Type,
				Is.EqualTo("ui.text"),
				"a reader without ui.transform must still have a reading to draw");
			Assert.That(nodes["conformance.identity"].Properties,
				Is.Empty,
				"identity is every key absent, never a written 0 or 1");
			Assert.That(nodes["conformance.outer"].Children.Single().Type, Is.EqualTo("ui.transform"));
		});
	}

	[Test]
	public void The_building_blocks_fixture_pins_each_recommended_fallback_and_an_honest_degradation()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-building-blocks-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(nodes["conformance.shape"].Fallback?.Type, Is.EqualTo("ui.stack"));
			Assert.That(nodes["conformance.icon"].Fallback?.Type, Is.EqualTo("ui.text"));
			Assert.That(nodes["conformance.gauge"].Fallback?.Type, Is.EqualTo("ui.range-bar"));
			Assert.That(nodes["conformance.toggle"].Fallback?.Type, Is.EqualTo("ui.button"));
			Assert.That(nodes["conformance.segmented"].Fallback?.Type, Is.EqualTo("ui.stack"));
			Assert.That(nodes["conformance.dial"].Fallback?.Type, Is.EqualTo("ui.slider"));
			Assert.That(nodes["conformance.dial"].Fallback?.Properties["events"].GetRawText(),
				Is.EqualTo("""["adjust","change"]"""),
				"a slider fallback keeps the dial's own events, so the older reader is still the same control");
			Assert.That(nodes["conformance.path"].Fallback,
				Is.Null,
				"drawing nothing is an allowed degradation for a decorative shape");
			Assert.That(nodes["conformance"].Properties.ContainsKey("rows"),
				Is.True,
				"a declared row count is what makes the grid drop children that do not fit");
		});
	}

	[Test]
	public void The_fixtures_cover_every_primitive_and_every_property_key()
	{
		var types = new HashSet<string>(StringComparer.Ordinal);
		var keys = new HashSet<string>(StringComparer.Ordinal);

		// The union across trees: coverage is a property of the fixture set, not of any one file.
		foreach (var fileName in FixtureTrees())
		{
			var tree = JsonSerializer.Deserialize<UiTree>(ReadTree(fileName), UiCanonicalJson.Options)!;

			foreach (var node in Walk(tree.Root))
			{
				types.Add(node.Type);

				foreach (var key in node.Properties.Keys)
				{
					keys.Add(key);
				}
			}
		}

		Assert.Multiple(() =>
		{
			// Both families: the fixtures are what a second implementation is checked against, so a type
			// missing from them is a type nobody has to get right.
			Assert.That(types,
				Is.SupersetOf(UiComponents.WellKnown.Concat(UiMacroDeckComponents.WellKnown)));
			Assert.That(keys, Is.SupersetOf(UiComponentProperties.WellKnown));
		});
	}

	[Test]
	public void The_fixture_pins_both_length_branches_and_the_degradation_path()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree(), UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		var clamped = nodes["conformance.rows.2026-07-20.day"];
		var unclamped = nodes["conformance.rows.2026-07-20.bar"];
		var withoutMarker = nodes["conformance.rows.2026-07-21.bar"];
		var degrading = nodes["conformance.state.future"];

		Assert.Multiple(() =>
		{
			// A column width and a font size on one node, differing by the factor a conflating renderer
			// cannot reproduce.
			Assert.That(clamped.Properties["mainSize"].GetRawText(),
				Is.EqualTo("""{"basis":0.114,"maxOfCross":0.95}"""));
			Assert.That(clamped.Properties["size"].GetRawText(),
				Is.EqualTo("""{"basis":0.06,"maxOfCross":0.5}"""));

			Assert.That(unclamped.Properties.ContainsKey("marker"), Is.True);
			// Absent, never an explicit null - the model defines a null property value as "explicitly
			// null", which is a different thing a renderer would have to draw.
			Assert.That(withoutMarker.Properties.ContainsKey("marker"), Is.False);

			Assert.That(degrading.RequiredComponentVersion, Is.EqualTo(2));
			Assert.That(degrading.Fallback, Is.Not.Null);
			Assert.That(degrading.Fallback!.Type, Is.EqualTo(UiComponents.Text));
		});
	}

	[Test]
	public void The_clock_fixture_pins_both_reference_spellings_and_a_two_step_degradation()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-clock-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		var zoned = nodes["conformance.caption"];
		var dial = nodes["conformance.dial"];
		var plainDial = nodes["conformance.dialPlain"];

		Assert.Multiple(() =>
		{
			Assert.That(zoned.Properties["value"].GetRawText(),
				Is.EqualTo("""{"$time":{"zone":"America/New_York"}}"""));
			// Absent, never an explicit null: a zone-less reference means the reader's own zone, which is
			// a different instruction from "explicitly no zone".
			Assert.That(plainDial.Properties["value"].GetRawText(), Is.EqualTo("""{"$time":{}}"""));

			Assert.That(dial.Properties.ContainsKey("seconds"), Is.True);
			Assert.That(plainDial.Properties.ContainsKey("seconds"), Is.False);

			// Two steps, because the useful degradation is not the last one: a reader that draws no dial
			// but knows a dynamic text still shows the right time.
			Assert.That(dial.Fallback!.Type, Is.EqualTo(UiMacroDeckComponents.DynamicText));
			Assert.That(dial.Fallback!.Fallback!.Type, Is.EqualTo(UiComponents.Text));
		});
	}

	[Test]
	public void The_slider_fixture_pins_the_declared_affordance_and_the_accent_default()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-slider-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		var bound = nodes["conformance.track"];
		var commitOnly = nodes["conformance.column"];
		var plain = nodes["conformance.plain"];
		var empty = nodes["conformance.empty"];

		Assert.Multiple(() =>
		{
			// Three declaration shapes, because the rule under test is "send what the node declares" and a
			// fixture carrying only one of them passes a reader that ignores the declaration entirely.
			Assert.That(bound.Properties["events"].GetRawText(), Is.EqualTo("""["adjust","change"]"""));
			Assert.That(commitOnly.Properties["events"].GetRawText(), Is.EqualTo("""["change"]"""));
			// Absent, never an empty array: a reader offers no affordance at all rather than an inert one.
			Assert.That(plain.Properties.ContainsKey("events"), Is.False);

			// A literal colour and the absence that means the reader's own accent - the second is the case
			// a reader that resolved its accent into the tree would pass without.
			Assert.That(bound.Properties["levelColor"].GetString(), Is.EqualTo("#4f8cff"));
			Assert.That(plain.Properties.ContainsKey("levelColor"), Is.False);

			// Absent, not zero: a snapping reader and a continuous one draw the same picture but land on
			// different values.
			Assert.That(bound.Properties.ContainsKey("step"), Is.True);
			Assert.That(commitOnly.Properties.ContainsKey("step"), Is.False);

			// Both axes, and the empty level, which is where a reader tends to paint a stub one radius wide.
			Assert.That(commitOnly.Properties["direction"].GetString(), Is.EqualTo(UiComponentDirections.Vertical));
			Assert.That(bound.Properties.ContainsKey("direction"), Is.False);
			Assert.That(empty.Properties["level"].GetDouble(), Is.Zero);

			// One step, to a primitive the frozen four already contain - a reader too old for the slider
			// still shows the right level, and honestly cannot drag it.
			Assert.That(bound.Fallback!.Type, Is.EqualTo(UiComponents.RangeBar));
		});
	}

	[Test]
	public void The_action_button_fixture_pins_the_declared_press_names_and_the_backdrop_framing()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-action-button-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		var full = nodes["conformance.full"];
		var press = nodes["conformance.press"];
		var boundary = nodes["conformance.boundary"];
		var silent = nodes["conformance.silent"];

		Assert.Multiple(() =>
		{
			// Four declaration shapes, because the rule under test is "send what the node declares" and
			// a fixture missing any one of them passes a reader that infers or offers more than that.
			Assert.That(full.Properties["events"].GetRawText(),
				Is.EqualTo("""["long-press","press","press-end","press-start"]"""));
			Assert.That(press.Properties["events"].GetRawText(), Is.EqualTo("""["press"]"""));
			Assert.That(boundary.Properties["events"].GetRawText(),
				Is.EqualTo("""["press-end","press-start"]"""));
			// Absent, never an empty array: a reader offers no affordance at all rather than an inert
			// one, and a reader must never infer "press" from the start/end pair on conformance.boundary.
			Assert.That(silent.Properties.ContainsKey("events"), Is.False);

			// A tinted ring and a self-cycling one: borderColor is present on the first and absent on
			// the second, which is the case a reader that always echoed borderColor would fail.
			Assert.That(full.Properties["borderColor"].GetString(), Is.EqualTo("#4f8cff"));
			Assert.That(boundary.Properties.ContainsKey("borderColor"), Is.False);
			Assert.That(boundary.Properties["borderStyle"].GetString(), Is.EqualTo(UiComponentBorderStyles.HueShift));

			// Absent, never "off": there is no such value on the wire, and a reader that defaulted
			// absence to "static" would draw a ring nobody asked for.
			Assert.That(press.Properties.ContainsKey("borderStyle"), Is.False);
			Assert.That(silent.Properties.ContainsKey("borderStyle"), Is.False);

			// Every framing key on one backdrop, none on the other - absent is never zero: a button
			// with a source and no framing keys still resolves to fit "contain", zoom 1, offset 0 and
			// full opacity, never to a collapsed or invisible rect.
			Assert.That(full.Properties["fit"].GetString(), Is.EqualTo(UiComponentImageFits.Cover));
			Assert.That(full.Properties["zoom"].GetDouble(), Is.EqualTo(1.35));
			Assert.That(full.Properties["offsetX"].GetDouble(), Is.EqualTo(0.08));
			Assert.That(full.Properties["offsetY"].GetDouble(), Is.EqualTo(-0.05));
			Assert.That(full.Properties["opacity"].GetDouble(), Is.EqualTo(0.85));

			Assert.That(press.Properties.ContainsKey("fit"), Is.False);
			Assert.That(press.Properties.ContainsKey("zoom"), Is.False);
			Assert.That(press.Properties.ContainsKey("offsetX"), Is.False);
			Assert.That(press.Properties.ContainsKey("offsetY"), Is.False);
			Assert.That(press.Properties.ContainsKey("opacity"), Is.False);
			Assert.That(press.Properties.ContainsKey("source"), Is.True);

			// No source at all - a label-only button, distinct from a backdrop resolved to defaults.
			Assert.That(boundary.Properties.ContainsKey("source"), Is.False);

			// background present on one button, absent - meaning the reader's own accent colour, never
			// black - on another.
			Assert.That(full.Properties["background"].GetString(), Is.EqualTo("#1c2430"));
			Assert.That(press.Properties.ContainsKey("background"), Is.False);

			// wrap present and absent on sibling text children of the same button.
			var title = nodes["conformance.full.title"];
			var caption = nodes["conformance.full.caption"];
			var pressLabel = nodes["conformance.press.label"];

			Assert.That(title.Properties["wrap"].GetBoolean(), Is.True);
			Assert.That(caption.Properties["wrap"].GetBoolean(), Is.True);
			Assert.That(caption.Properties.ContainsKey("maxLines"), Is.True);
			Assert.That(title.Properties.ContainsKey("maxLines"), Is.False);
			Assert.That(pressLabel.Properties.ContainsKey("wrap"), Is.False);

			// fontFace present on one text and absent on another.
			var boundaryLabel = nodes["conformance.boundary.label"];

			Assert.That(boundaryLabel.Properties["fontFace"].GetString(), Is.EqualTo("mono-14"));
			Assert.That(pressLabel.Properties.ContainsKey("fontFace"), Is.False);

			// Label position and alignment in both extremes, so a renderer conflating the main and
			// cross axis fails on at least one of them.
			Assert.That(full.Properties["justify"].GetString(), Is.EqualTo(UiComponentJustify.Start));
			Assert.That(full.Properties["align"].GetString(), Is.EqualTo(UiComponentAlignments.End));
			Assert.That(press.Properties["justify"].GetString(), Is.EqualTo(UiComponentJustify.Center));
			Assert.That(press.Properties["align"].GetString(), Is.EqualTo(UiComponentAlignments.Center));

			// A one-step fallback to ui.stack, carrying the same layout, background and children,
			// and none of the button-only properties.
			Assert.That(full.Fallback!.Type, Is.EqualTo(UiComponents.Stack));
			Assert.That(full.Fallback!.Properties["background"].GetString(), Is.EqualTo("#1c2430"));
			Assert.That(full.Fallback!.Properties["direction"].GetString(),
				Is.EqualTo(full.Properties["direction"].GetString()));
			Assert.That(full.Fallback!.Properties["justify"].GetString(),
				Is.EqualTo(full.Properties["justify"].GetString()));
			Assert.That(full.Fallback!.Properties["align"].GetString(),
				Is.EqualTo(full.Properties["align"].GetString()));
			Assert.That(full.Fallback!.Properties.ContainsKey("events"), Is.False);
			Assert.That(full.Fallback!.Properties.ContainsKey("source"), Is.False);
			Assert.That(full.Fallback!.Properties.ContainsKey("fit"), Is.False);
			Assert.That(full.Fallback!.Properties.ContainsKey("borderStyle"), Is.False);
			Assert.That(full.Fallback!.Children, Has.Count.EqualTo(full.Children.Count));
		});
	}

	[Test]
	public void The_music_player_fixture_pins_the_progress_reference_shapes_and_the_transition()
	{
		var tree = JsonSerializer.Deserialize<UiTree>(ReadTree("conformance-music-player-tree.json"),
			UiCanonicalJson.Options)!;
		var nodes = Walk(tree.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);

		var playingBar = nodes["conformance.playing.progress"];
		var pausedBar = nodes["conformance.paused.progress"];
		var loadingBar = nodes["conformance.loading.progress"];
		var stream = nodes["conformance.stream"];
		var artworkLayer = nodes["conformance.playing"];
		var badge = nodes["conformance.playing.badge"];
		var cover = nodes["conformance.paused.cover"];

		Assert.Multiple(() =>
		{
			// Absent means normal speed - the case whose display changes every second, so it costs no
			// key - and a halted medium writes 0. A fixture with only one of them passes a reader that
			// ignores rate entirely, leaving a paused track counting up or a playing one frozen.
			Assert.That(playingBar.Properties["value"].GetRawText(), Is.EqualTo(_playingProgressJson));
			Assert.That(pausedBar.Properties["value"].GetProperty("$progress").GetProperty("rate").GetDouble(),
				Is.Zero);

			// A length and no length at all: the second has no fraction to draw, so the track stays
			// empty rather than the reader inventing a whole to divide by.
			Assert.That(stream.Properties["value"].GetProperty("$progress").TryGetProperty("durationMs", out _),
				Is.False);

			// Absent, never a zero-position reference: a widget still waiting for its first read draws
			// the same empty track, and the two are different instructions.
			Assert.That(loadingBar.Properties.ContainsKey("value"), Is.False);

			// All three duration formats, each on its own node.
			Assert.That(nodes["conformance.playing.elapsed"].Properties["format"].GetString(),
				Is.EqualTo(UiProgressFormats.Elapsed));
			Assert.That(nodes["conformance.playing.duration"].Properties["format"].GetString(),
				Is.EqualTo(UiProgressFormats.Duration));
			Assert.That(nodes["conformance.paused.remaining"].Properties["format"].GetString(),
				Is.EqualTo(UiProgressFormats.Remaining));

			// Both colours, and neither - the second is the accent case a reader that resolved its own
			// accent into the tree would pass without.
			Assert.That(playingBar.Properties["startColor"].GetString(), Is.EqualTo("#c9a6ff"));
			Assert.That(pausedBar.Properties.ContainsKey("startColor"), Is.False);
			Assert.That(pausedBar.Properties.ContainsKey("endColor"), Is.False);

			// One step each, and both honest: the wrong second, never the wrong picture.
			Assert.That(playingBar.Fallback!.Type, Is.EqualTo(UiComponents.RangeBar));
			Assert.That(nodes["conformance.playing.elapsed"].Fallback!.Type,
				Is.EqualTo(UiComponents.Text));

			// Present on a button's artwork and on an image, absent on a third image - absence is an
			// immediate replacement, not a slower fade, and is what a reader without the key does.
			Assert.That(artworkLayer.Properties["transition"].GetString(),
				Is.EqualTo(UiComponentImageTransitions.Crossfade));
			Assert.That(cover.Properties["transition"].GetString(),
				Is.EqualTo(UiComponentImageTransitions.Crossfade));
			Assert.That(badge.Properties.ContainsKey("transition"), Is.False);

			// An events-less button that still carries artwork: no affordance at all, and the shape the
			// Music Player's full cover style actually ships.
			Assert.That(artworkLayer.Type, Is.EqualTo(UiComponents.Button));
			Assert.That(artworkLayer.Properties.ContainsKey("events"), Is.False);
			Assert.That(artworkLayer.Properties.ContainsKey("source"), Is.True);
		});
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}
	}
}
