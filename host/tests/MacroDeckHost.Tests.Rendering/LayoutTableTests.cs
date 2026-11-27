using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Animation;
using MacroDeckHost.Rendering.Nodes;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using MacroDeckHost.Rendering.Paint;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Tests.Rendering;

public sealed partial class LayoutTableTests
{
	private static readonly DateTimeOffset _specNow = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	private static readonly DateTimeOffset _progressAnchor = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset _referenceInstant = new(2026, 1, 15, 15, 9, 30, TimeSpan.Zero);

	private static readonly ConditionalWeakTable<Element, NodeScope> _scopes = new();

	private static readonly Dictionary<string, string> _alignToItems = new(StringComparer.Ordinal)
	{
		["start"] = "flex-start",
		["center"] = "center",
		["end"] = "flex-end",
		["baseline"] = "baseline",
		["stretch"] = "stretch"
	};

	private static readonly HashSet<string> _proseKeys = new(StringComparer.Ordinal)
	{
		"note", "alignNote", "bandNote", "description", "appliesTo", "rule"
	};

	private static readonly Dictionary<string, string[]> _proseTableKeys = new(StringComparer.Ordinal)
	{
		["conformance-action-button-layout.json"] = ["artwork", "ring", "press", "label"],
		["conformance-slider-layout.json"] = ["interactiveSurface", "fillColor", "thumb"],
		["conformance-music-player-layout.json"] = ["progressResolution", "barFraction", "fillColor"]
	};

	public static IEnumerable<TestCaseData> TableCases()
	{
		foreach (var file in TableFiles().Where(file => file != "conformance-responsive-layout.json"))
		{
			var cases = Fixtures.Profile(file).GetProperty("cases");
			for (var index = 0; index < cases.GetArrayLength(); index++)
			{
				yield return new TestCaseData(file, index).SetName($"{file[..^".json".Length]} case {index}");
			}
		}
	}

	public static IEnumerable<TestCaseData> ResponsiveCases()
	{
		var cases = Fixtures.Profile("conformance-responsive-layout.json").GetProperty("cases");
		for (var index = 0; index < cases.GetArrayLength(); index++)
		{
			yield return new TestCaseData(index).SetName(
				$"conformance-responsive-layout {cases[index].GetProperty("name").GetString()}");
		}
	}

	public static IEnumerable<TestCaseData> FallbackEntries()
	{
		foreach (var file in TableFiles())
		{
			foreach (var entry in Fixtures.Profile(file)
				.EnumerateObject()
				.Where(entry => VersionAsked().IsMatch(entry.Name)))
			{
				yield return new TestCaseData(file, entry.Name).SetName($"{file[..^".json".Length]} {entry.Name}");
			}
		}
	}

	[TestCaseSource(nameof(TableCases))]
	public void Table_case_resolves_as_stated(string file, int index)
	{
		var table = Fixtures.Profile(file);
		var testCase = table.GetProperty("cases")[index];
		var tree = Fixtures.Profile(file.Replace("-layout.json", "-tree.json", StringComparison.Ordinal));
		var tile = testCase.GetProperty("tile");
		var width = tile.GetProperty("width").GetDouble();
		var height = tile.GetProperty("height").GetDouble();
		var basis = testCase.GetProperty("basis").GetDouble();
		var progress = file == "conformance-music-player-layout.json";
		var scene = new Scene(tree, width, height, basis, progress ? _progressAnchor : _specNow, null);
		var later = progress ? new Scene(tree, width, height, basis, _progressAnchor.AddSeconds(30), null) : null;
		var at = $"{file} case {index} ({width}x{height}@{basis})";
		var checks = new Checks(at);
		foreach (var node in testCase.GetProperty("nodes").EnumerateObject())
		{
			CheckNode(scene, later, node.Name, node.Value, checks.For(node.Name));
		}

		Assert.That(checks.Differences, Is.Empty, string.Join("\n  ", checks.Differences.Prepend("")));
	}

	[TestCaseSource(nameof(ResponsiveCases))]
	public void Responsive_case_draws_the_layout_the_table_names(int index)
	{
		var testCase = Fixtures.Profile("conformance-responsive-layout.json").GetProperty("cases")[index];
		var tree = Fixtures.Profile("conformance-responsive-tree.json");
		var tile = testCase.GetProperty("tile");
		BoxSize? box = tile.ValueKind == JsonValueKind.Object
			? new BoxSize(Nullable(tile.GetProperty("width")), Nullable(tile.GetProperty("height")))
			: null;
		var input = Scene.Input(tree, box?.Width ?? 120, box?.Height ?? 120, 120, _specNow);
		var host = new RenderHost(input);
		var surface = new Element("div");
		new TreeBuilder(host).Render(surface,
			UiNode.Parse(tree.GetProperty("root"))!,
			box,
			null,
			120,
			box?.Extent,
			false,
			false,
			true);
		var checks = new Checks($"conformance-responsive-layout.json {testCase.GetProperty("name").GetString()}");
		foreach (var member in testCase.EnumerateObject()
			.Where(member => member.Name is not ("name" or "tile" or "chosen" or "span" or "claimed")))
		{
			checks.Fail(member.Name, "no interpretation of this key");
		}

		foreach (var chosen in testCase.GetProperty("chosen").EnumerateObject())
		{
			var responsive = surface.Descendants().First(element => element.NodeId == chosen.Name);
			var drawn = responsive.Children.Where(child => child.NodeId is not null).Select(child => child.NodeId);
			checks.For(chosen.Name).Text("chosen", chosen.Value.GetString(), string.Join(",", drawn));
		}

		Assert.That(checks.Differences, Is.Empty, string.Join("\n  ", checks.Differences.Prepend("")));
	}

	[TestCaseSource(nameof(FallbackEntries))]
	public void Reader_without_the_asked_version_draws_the_fallback(string file, string key)
	{
		var entry = Fixtures.Profile(file).GetProperty(key);
		var match = VersionAsked().Match(key);
		var type = Components.Get($"ui.{match.Groups["type"].Value.ToLowerInvariant()}")!.Type;
		var older = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture) - 1;
		var testCase = Fixtures.Profile(file).GetProperty("cases")[0];
		var tree = Fixtures.Profile(file.Replace("-layout.json", "-tree.json", StringComparison.Ordinal));
		var tile = testCase.GetProperty("tile");
		var current = new Scene(tree,
			tile.GetProperty("width").GetDouble(),
			tile.GetProperty("height").GetDouble(),
			testCase.GetProperty("basis").GetDouble(),
			_specNow,
			null);
		var capped = new Scene(tree,
			tile.GetProperty("width").GetDouble(),
			tile.GetProperty("height").GetDouble(),
			testCase.GetProperty("basis").GetDouble(),
			_specNow,
			new Dictionary<string, int>(StringComparer.Ordinal) { [type] = older });
		var checks = new Checks($"{file} {key}").For(type);
		var notDrawn = entry.GetProperty("notDrawn").GetString()!;
		var draws = entry.GetProperty("draws").GetString()!;
		checks.Flag($"notDrawn {notDrawn} (version {older})", false, capped.Find(notDrawn) is not null);
		checks.Flag($"draws {draws} (version {older})", true, capped.Find(draws) is not null);
		checks.Flag($"notDrawn {notDrawn} (this reader)", true, current.Find(notDrawn) is not null);
		checks.Flag($"draws {draws} (this reader)", false, current.Find(draws) is not null);
		Prose(checks, "note", entry.GetProperty("note"));
		Assert.That(checks.Differences, Is.Empty, string.Join("\n  ", checks.Differences.Prepend("")));
	}

	[Test]
	public void Clock_hands_point_at_the_reference_instant()
	{
		var table = Fixtures.Profile("conformance-clock-layout.json");
		var instant = table.GetProperty("referenceInstant");
		var scene = new Scene(Fixtures.Profile("conformance-clock-tree.json"), 240, 480, 240, _referenceInstant, null);
		var dial = scene.Node("conformance.dial");
		var face = Part(dial, "widget-clock-dial-face");
		var cx = Attribute(face, "cx");
		var cy = Attribute(face, "cy");

		double AngleOf(string hand)
		{
			var line = Part(dial, hand);
			var degrees = Math.Atan2(Attribute(line, "x2") - cx, cy - Attribute(line, "y2")) * 180 / Math.PI;
			return (degrees % 360 + 360) % 360;
		}

		var checks = new Checks("conformance-clock-layout.json referenceInstant").For("conformance.dial");
		checks.Number("hourAngle", instant.GetProperty("hourAngle"), AngleOf("widget-clock-dial-hand-hour"));
		checks.Number("minuteAngle", instant.GetProperty("minuteAngle"), AngleOf("widget-clock-dial-hand-minute"));
		checks.Number("secondAngle", instant.GetProperty("secondAngle"), AngleOf("widget-clock-dial-hand-second"));
		Assert.That(instant.GetProperty("description").GetString(), Does.Contain("10:09:30"));
		Assert.That(checks.Differences, Is.Empty, string.Join("\n  ", checks.Differences.Prepend("")));
	}

	[Test]
	public void Music_player_runs_artwork_and_crossfade_are_the_renderers()
	{
		var table = Fixtures.Profile("conformance-music-player-layout.json");
		var tree = Fixtures.Profile("conformance-music-player-tree.json");
		var checks = new Checks("conformance-music-player-layout.json");
		var runs = table.GetProperty("runs");
		Prose(checks.For("runs"), "note", runs.GetProperty("note"));
		var columns = new[] { ("atAnchor", _progressAnchor), ("at30sAfterAnchor", _progressAnchor.AddSeconds(30)) };
		foreach (var (column, at) in columns)
		{
			var scene = new Scene(tree, 240, 480, 240, at, null);
			foreach (var run in runs.GetProperty(column).EnumerateObject())
			{
				checks.For(run.Name).Text($"runs.{column}", run.Value.GetString(), scene.Node(run.Name).TextContent());
			}
		}

		var adjustment = table.GetProperty("artworkAdjustment");
		var cover = Part(new Scene(tree, 240, 480, 240, _progressAnchor, null).Node("conformance.paused.cover"),
			null,
			"img");
		var filter = cover.Style.GetValueOrDefault("filter");
		var adjusted = checks.For("artworkAdjustment");
		Prose(adjusted, "appliesTo", adjustment.GetProperty("appliesTo"));
		Prose(adjusted, "note", adjustment.GetProperty("note"));
		var brightness = FilterAmount(filter, "brightness");
		var saturation = FilterAmount(filter, "saturate");
		adjusted.Number("brightness", adjustment.GetProperty("brightness"), brightness);
		adjusted.Number("saturation", adjustment.GetProperty("saturation"), saturation);
		adjusted.Flag("order (brightness before saturate)",
			true,
			filter is not null
			&& filter.IndexOf("brightness", StringComparison.Ordinal)
			< filter.IndexOf("saturate", StringComparison.Ordinal));
		CheckSaturation(adjusted, adjustment, filter ?? "");

		var crossfade = checks.For("crossfade");
		var stated = table.GetProperty("crossfade");
		Prose(crossfade, "appliesTo", stated.GetProperty("appliesTo"));
		Prose(crossfade, "note", stated.GetProperty("note"));
		crossfade.Number("durationMs", stated.GetProperty("durationMs"), Motion.CrossfadeMs);
		crossfade.Text("easing", stated.GetProperty("easing").GetString(), Motion.CrossfadeEasing);
		crossfade.Number("fromOpacity", stated.GetProperty("fromOpacity"), Motion.CrossfadeFromOpacity);
		crossfade.Number("fromScale", stated.GetProperty("fromScale"), Motion.CrossfadeFromScale);
		crossfade.Number("toScale", stated.GetProperty("toScale"), Motion.CrossfadeToScale);

		var corner = checks.For("tileCorner");
		var tileCorner = table.GetProperty("tileCorner");
		Prose(corner, "appliesTo", tileCorner.GetProperty("appliesTo"));
		Prose(corner, "rule", tileCorner.GetProperty("rule"));
		Prose(corner, "note", tileCorner.GetProperty("note"));
		var scene240 = new Scene(tree, 240, 480, 240, _progressAnchor, null);
		var playing = scene240.Node("conformance.playing");
		corner.Flag("conformance.playing takes the tile corner", true, playing.Classes.Contains("widget-tile-corner"));
		corner.Flag("conformance.playing is not rounded by the ratio",
			false,
			playing.Style.ContainsKey("border-radius"));
		Assert.That(checks.Differences, Is.Empty, string.Join("\n  ", checks.Differences.Prepend("")));
	}

	[Test]
	public void Every_table_key_has_an_interpretation()
	{
		var unknown = new List<string>();
		foreach (var file in TableFiles())
		{
			var prose = _proseTableKeys.GetValueOrDefault(file) ?? [];
			foreach (var entry in Fixtures.Profile(file).EnumerateObject())
			{
				var handled = entry.Name is "cases"
						or "runs"
						or "artworkAdjustment"
						or "crossfade"
						or "tileCorner"
						or "referenceInstant"
						or "cell"
					|| VersionAsked().IsMatch(entry.Name)
					|| _proseKeys.Contains(entry.Name)
					|| prose.Contains(entry.Name);
				if (!handled)
				{
					unknown.Add($"{file}: {entry.Name}");
				}
			}
		}

		var history = Fixtures.Profile("conformance-history-graph-layout.json");
		Assert.Multiple(() =>
		{
			Assert.That(unknown, Is.Empty, "table keys no test reads");
			Assert.That(history.GetProperty("cell").GetDouble(),
				Is.EqualTo(UiLength.Cell),
				"conformance-history-graph-layout.json cell: the cell maxOfCell is a multiple of");
			foreach (var file in TableFiles())
			{
				foreach (var key in _proseTableKeys.GetValueOrDefault(file) ?? [])
				{
					Assert.That(Fixtures.Profile(file).GetProperty(key).ValueKind,
						Is.EqualTo(JsonValueKind.String),
						$"{file} {key} is prose");
				}
			}
		});
	}

	private static IEnumerable<string> TableFiles()
	{
		return Directory.GetFiles(Fixtures.ProfileDirectory, "conformance*-layout.json")
			.Select(Path.GetFileName)
			.Order(StringComparer.Ordinal)!;
	}

	private static double? Nullable(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
	}

	private static void CheckNode(Scene scene, Scene? later, string id, JsonElement spec, Checks checks)
	{
		if (spec.TryGetProperty("renders", out var renders))
		{
			var mounted = scene.Find(id) is not null;
			checks.Flag($"renders ({renders.GetRawText()}: never mounted itself)", false, mounted);
			foreach (var other in spec.EnumerateObject().Where(member => member.Name != "renders"))
			{
				checks.Fail(other.Name, "stated beside renders, so it has no element to read");
			}

			return;
		}

		var element = scene.Find(id);
		if (element is null)
		{
			checks.Fail("(node)", "not drawn");
			return;
		}

		var node = new NodeView(scene, element);
		foreach (var member in spec.EnumerateObject())
		{
			CheckKey(node, later, member.Name, member.Value, checks);
		}
	}

	private static void CheckKey(NodeView node, Scene? later, string key, JsonElement value, Checks checks)
	{
		var element = node.Element;
		switch (key)
		{
			case "note" or "alignNote" or "bandNote":
				Prose(checks, key, value);
				break;
			case "width" or "height":
				if (value.ValueKind == JsonValueKind.Null)
				{
					checks.Text(key, null, element.Style.GetValueOrDefault(key));
				}
				else
				{
					checks.Number(key, value, Own(element, key));
				}

				break;
			case "padding" or "gap":
				checks.Number(key, value, StylePx(element, key) ?? 0);
				break;
			case "contentWidth":
				checks.Number(key, value, Own(element, "width") - 2 * (StylePx(element, "padding") ?? 0));
				break;
			case "contentHeight":
				checks.Number(key, value, Own(element, "height") - 2 * (StylePx(element, "padding") ?? 0));
				break;
			case "size" or "imageEdge":
				var image = Part(element, null, "img");
				checks.Number($"{key} (width)", value, StylePx(image, "width"));
				checks.Number($"{key} (height)", value, StylePx(image, "height"));
				break;
			case "fontSize":
				checks.Number(key, value, StylePx(node.Text, "font-size"));
				break;
			case "minFontSize":
				checks.Number(key,
					value,
					node.Scene.Pipeline.Host.Fits.FirstOrDefault(fit => fit.Element == node.Text)?.Minimum);
				break;
			case "secondsFontSize":
				checks.Number(key, value, Part(element, "widget-dynamic-text-seconds").Computed.FontSize);
				break;
			case "secondsRole":
				checks.Text(key,
					$"var(--color-text-{value.GetString()})",
					Part(element, "widget-dynamic-text-seconds").Style.GetValueOrDefault("color"));
				break;
			case "role":
				CheckRole(node, value.GetString()!, checks);
				break;
			case "color":
				checks.Text(key, Hex(value.GetString()), Hex(element.Style.GetValueOrDefault("color")));
				break;
			case "mainSize":
				checks.Number(key, value, Own(element, "width"));
				break;
			case "mainSizeOverFontSize":
				checks.Number(key, value, Own(element, "width") / StylePx(element, "font-size"));
				break;
			case "align" or "blockAlign":
				checks.Text(key, _alignToItems[value.GetString()!], element.Style.GetValueOrDefault("align-items"));
				break;
			case "digits":
				checks.Text(key, $"{value.GetRawText()}ch", element.Style.GetValueOrDefault("min-width"));
				break;
			case "minWidth":
				CheckDigitsWidth(node, value, checks);
				break;
			case "tabular":
				checks.Flag(key, value.GetBoolean(), element.Classes.Contains("widget-text-tabular"));
				break;
			case "transition":
				checks.Flag($"{key} (eased)",
					value.GetString() == "crossfade",
					element.Classes.Contains("widget-artwork-eased"));
				break;
			case "background":
				CheckBackground(element, value, checks);
				break;
			case "thickness":
				checks.Number(key, value, StylePx(Part(element, "widget-range-bar-track"), "height"));
				break;
			case "fillStart":
				checks.Number(key, value, StylePx(Part(element, "widget-range-bar-fill"), "left") / 100);
				break;
			case "fillEnd":
				var fill = Part(element, "widget-range-bar-fill");
				checks.Number(key, value, (StylePx(fill, "left") + StylePx(fill, "width")) / 100);
				break;
			case "marker":
				CheckMarker(element, value, checks);
				break;
			case "geometry":
				CheckDial(element, value, checks);
				break;
			case "centre":
				var face = Part(element, "widget-clock-dial-face");
				checks.Number("centre.x", value.GetProperty("x"), Attribute(face, "cx"));
				checks.Number("centre.y", value.GetProperty("y"), Attribute(face, "cy"));
				break;
			case "secondHand":
				checks.Flag(key, value.GetBoolean(), Find(element, "widget-clock-dial-hand-second") is not null);
				break;
			case "handColor":
				foreach (var hand in new[] { "widget-clock-dial-hand-hour", "widget-clock-dial-hand-minute" })
				{
					checks.Text($"{key} ({hand})",
						Hex(value.GetString()),
						Hex(Part(element, hand).Style.GetValueOrDefault("stroke")));
				}

				checks.Text("second hand keeps the accent",
					"var(--color-accent)",
					Find(element, "widget-clock-dial-hand-second")?.Style.GetValueOrDefault("stroke"));
				break;
			case "tickColor":
				foreach (var tick in element.Descendants()
					.Where(part => part.Classes.Contains("widget-clock-dial-tick")))
				{
					checks.Text(key, Hex(value.GetString()), Hex(tick.Style.GetValueOrDefault("stroke")));
				}

				break;
			case "direction":
				checks.Text(key,
					value.GetString(),
					element.Classes.Contains("widget-slider-vertical") ? "vertical" : "horizontal");
				break;
			case "interactiveWidth":
				checks.Number(key, value, element.Box.Width.Value);
				break;
			case "interactiveHeight":
				checks.Number(key, value, element.Box.Height.Value);
				break;
			case "interactive":
				checks.Flag(key, value.GetBoolean(), element.Classes.Contains("widget-pressable"));
				break;
			case "trackLength":
				checks.Number(key, value, SliderLength(element));
				break;
			case "trackThickness":
				checks.Number(key,
					value,
					node.Type == Components.Slider
						? StylePx(Part(element, "widget-slider-track"), Vertical(element) ? "width" : "height")
						: StylePx(Part(element, "widget-range-bar-track"), "height"));
				break;
			case "cornerRadius":
				CheckCornerRadius(node, value, checks);
				break;
			case "fillLength":
				checks.Number(key, value, SliderFill(element));
				break;
			case "fillPainted":
				checks.Flag(key, value.GetBoolean(), SliderFill(element) > 0);
				break;
			case "fillFrom":
				CheckFillFrom(node, value.GetString()!, checks);
				break;
			case "fillColor":
				CheckFillColor(node, value.GetString()!, checks);
				break;
			case "fillLengthAtAnchor":
				checks.Number(key, value, ProgressFill(element));
				break;
			case "fillLengthAt30s":
				checks.Number(key, value, ProgressFill(later!.Node(node.Element.NodeId!)));
				break;
			case "thumbRadius":
				checks.Number(key,
					value,
					node.Type == Components.Dial
						? Attribute(Part(element, "widget-dial-thumb"), "r")
						: StylePx(Part(element, "widget-slider-thumb"), "width") / 2 + ThumbStroke(element) / 2);
				break;
			case "thumbRingWidth":
				checks.Number(key, value, ThumbStroke(element));
				break;
			case "thumbCentre":
				var thumb = Part(element, "widget-slider-thumb");
				checks.Number(key,
					value,
					StylePx(thumb, Vertical(element) ? "bottom" : "left")
					+ StylePx(thumb, "width") / 2
					+ ThumbStroke(element));
				break;
			case "backdropFit" or "artworkFit":
				checks.Text(key,
					value.GetString(),
					Part(element, "widget-button-artwork").Style.GetValueOrDefault("object-fit"));
				break;
			case "backdropOpacity" or "artworkOpacity":
				checks.Number(key, value, StylePx(Part(element, "widget-button-artwork"), "opacity"));
				break;
			case "backdropWidth" or "backdropHeight" or "backdropOffsetX" or "backdropOffsetY":
				CheckBackdrop(element, key, value, checks);
				break;
			case "ringStyle":
				var ring = Find(element, "widget-button-ring");
				checks.Text(key,
					value.ValueKind == JsonValueKind.Null ? null : value.GetString(),
					ring?.Attributes.GetValueOrDefault("data-ring-style"));
				break;
			case "ringColor":
				CheckRingColor(element, value.GetString()!, checks);
				break;
			case "ringWidth":
				checks.Number(key, value, Rings.WidthDevicePx / node.Scene.Pipeline.Input.Scale);
				break;
			case "pressTintAlpha":
				checks.Number(key, value, PressTintAlpha());
				break;
			case "pressFadeInMs":
				checks.Number(key, value, TransitionMs("--transition-press-in"));
				break;
			case "pressFadeOutMs":
				checks.Number(key, value, TransitionMs("--transition-press-out"));
				break;
			case "pressMinVisibleMs":
				checks.Number(key, value, Motion.PressMinVisibleMs);
				break;
			case "pressSurfaceWidth":
				checks.Number(key, value, element.Box.Width.Value);
				break;
			case "pressSurfaceHeight":
				checks.Number(key, value, element.Box.Height.Value);
				break;
			case "lines":
				CheckLines(node, value.GetInt32(), checks);
				break;
			case "textAlign":
				checks.Text(key, value.GetString(), node.Text.Style.GetValueOrDefault("text-align"));
				break;
			case "paintOrder":
				checks.Text(key,
					string.Join(",", value.EnumerateArray().Select(item => item.GetString())),
					string.Join(",",
						element.Children.Where(child => child.NodeId is not null).Select(child => child.NodeId)));
				break;
			case "childBox":
				CheckChildBox(element, value, checks);
				break;
			case "band":
				CheckBand(node, value, checks);
				break;
			case "strokeWidth":
				checks.Number(key,
					value,
					Attribute(node.Type == Components.Chart
							? Part(element, "widget-chart-line")
							: Part(element, null, "path"),
						"stroke-width"));
				break;
			case "lineOpacity":
				checks.Number(key, value, Part(element, "widget-chart-line").Computed.Number("opacity", 1));
				break;
			case "areaOpacity":
				checks.Number(key, value, Part(element, "widget-chart-area").Computed.Number("fill-opacity", 1));
				break;
			case "line":
				CheckLine(element, value, checks);
				break;
			case "area":
				Prose(checks, key, value);
				CheckArea(element, checks);
				break;
			case "draws":
				Prose(checks, key, value);
				checks.Flag("draws (a line)", false, Find(element, "widget-chart-line") is not null);
				checks.Flag("draws (an area)", false, Find(element, "widget-chart-area") is not null);
				break;
			case "offsetY":
				checks.Number(key, value, element.Box.Y.Value);
				break;
			case "binds":
				CheckBinds(node, "size", key, value, checks);
				break;
			case "paddingBinds":
				CheckBinds(node, "padding", key, value, checks);
				break;
			case "gapBinds":
				CheckBinds(node, "gap", key, value, checks);
				break;
			case "strokeWidthBinds":
				CheckBinds(node, "thickness", key, value, checks);
				break;
			case "transform" or "transformOrigin":
				var property = key == "transform" ? "transform" : "transform-origin";
				checks.Text(key,
					value.ValueKind == JsonValueKind.Null ? null : value.GetString(),
					element.Style.GetValueOrDefault(property));
				break;
			case "cell":
				checks.Number("cell.left", value.GetProperty("left"), StylePx(element, "left"));
				checks.Number("cell.top", value.GetProperty("top"), StylePx(element, "top"));
				checks.Number("cell.width", value.GetProperty("width"), Own(element, "width"));
				checks.Number("cell.height", value.GetProperty("height"), Own(element, "height"));
				break;
			case "glyphEdge":
				checks.Number(key, value, StylePx(Part(element, "widget-icon-glyph"), "width"));
				break;
			case "arcRadius":
				checks.Number(key, value, ArcRadius(Attribute(Part(element, "widget-gauge-track"), "d", true)));
				break;
			case "arcStrokeWidth":
				checks.Number(key, value, Attribute(Part(element, "widget-gauge-track"), "stroke-width"));
				break;
			case "toggleTrack":
				CheckRect(Part(element, "widget-toggle-track"), key, value, checks);
				break;
			case "knobLeft":
				checks.Number(key, value, StylePx(Part(element, "widget-toggle-knob"), "left"));
				break;
			case "segmentFace":
				CheckRect(Part(element, "widget-segmented-face"), key, value, checks);
				break;
			case "trackRadius":
				checks.Number(key, value, StylePx(Part(element, "widget-segmented-track"), "border-radius"));
				break;
			case "faceRadius":
				checks.Number(key, value, StylePx(Part(element, "widget-segmented-face"), "border-radius"));
				break;
			case "borderRadius" or "opacity":
				var name = key == "opacity" ? "opacity" : "border-radius";
				if (value.ValueKind == JsonValueKind.Null)
				{
					checks.Text(key, null, element.Style.GetValueOrDefault(name));
				}
				else
				{
					checks.Number(key, value, StylePx(element, name));
				}

				break;
			case "overflow" or "clipPath" or "maskImage" or "touchAction":
				var css = key switch
				{
					"clipPath" => "clip-path",
					"maskImage" => "mask-image",
					"touchAction" => "touch-action",
					_ => "overflow"
				};
				checks.Text(key,
					value.ValueKind == JsonValueKind.Null ? null : Collapse(value.GetString()),
					Collapse(element.Style.GetValueOrDefault(css)));
				break;
			case "ariaLabel" or "ariaDescription" or "ariaRole" or "ariaDisabled":
				var attribute = key switch
				{
					"ariaLabel" => "aria-label",
					"ariaDescription" => "aria-description",
					"ariaRole" => "role",
					_ => "aria-disabled"
				};
				checks.Text(key,
					value.ValueKind == JsonValueKind.Null ? null : value.GetString(),
					element.Attributes.GetValueOrDefault(attribute));
				break;
			case "borderWidth" or "borderStyle" or "borderColor":
				CheckModifierBorder(element, key, value, checks);
				break;
			case "refusesEvents":
				CheckRefusesEvents(node, value.GetBoolean(), checks);
				break;
			case "clipsStart":
				checks.Flag(key, value.GetBoolean(), element.Classes.Contains("widget-stack-clip-start"));
				break;
			case "justifyContent":
				checks.Text(key, value.GetString(), element.Style.GetValueOrDefault("justify-content"));
				break;
			case "runs":
				CheckRuns(element, value, checks);
				break;
			case "followsEnd":
				checks.Flag(key, value.GetBoolean(), element.Classes.Contains("widget-list-anchor-end"));
				break;
			default:
				checks.Fail(key, "no interpretation of this key");
				break;
		}
	}

	private static void CheckRole(NodeView node, string role, Checks checks)
	{
		var colour = node.Text.Style.GetValueOrDefault("color");
		if (node.Text.Node?.Raw("color") is { ValueKind: JsonValueKind.String } && colour?.StartsWith('#') == true)
		{
			checks.Text("role (declared under a literal colour)", role, node.Text.Node.String("role"));
			return;
		}

		checks.Text("role", $"var(--color-text-{role})", colour);
	}

	private static void CheckDigitsWidth(NodeView node, JsonElement value, Checks checks)
	{
		Prose(checks, "minWidth", value);
		var style = node.Element.Computed;
		var digits = node.Element.Node!.Number("digits")!.Value;
		checks.Number("minWidth (px)",
			JsonDocument.Parse(Format(digits * style.ZeroWidth, 6)).RootElement,
			style.Px("min-width"));
		checks.Text("minWidth (centred)", "center", node.Element.Style.GetValueOrDefault("text-align"));
	}

	private static void CheckBackground(Element element, JsonElement value, Checks checks)
	{
		var actual = element.Style.GetValueOrDefault("background")
			?? element.Style.GetValueOrDefault("background-color");
		var expected = value.GetString()!;
		if (expected == "reader-accent")
		{
			checks.Flag("background (reader-accent)",
				true,
				actual?.Contains("--color-accent", StringComparison.Ordinal) == true);
			return;
		}

		checks.Text("background",
			expected.StartsWith('#') ? Hex(expected) : Collapse(expected),
			expected.StartsWith('#') ? Hex(actual) : Collapse(actual));
	}

	private static void CheckMarker(Element element, JsonElement value, Checks checks)
	{
		var marker = Find(element, "widget-range-bar-marker");
		if (value.ValueKind == JsonValueKind.Null)
		{
			checks.Flag("marker (absent)", false, marker is not null);
			return;
		}

		checks.Flag("marker (present)", true, marker is not null);
		if (marker is not null)
		{
			checks.Number("marker", value, StylePx(marker, "left") / Own(element, "width"));
		}
	}

	private static void CheckDial(Element element, JsonElement geometry, Checks checks)
	{
		var face = Part(element, "widget-clock-dial-face");
		var cx = Attribute(face, "cx");
		var cy = Attribute(face, "cy");
		var edge = Math.Min(Attribute(element, "width"), Attribute(element, "height"));
		var ticks = element.Descendants().Where(part => part.Classes.Contains("widget-clock-dial-tick")).ToList();

		double Radius(Element line, string end)
		{
			return Math.Sqrt(Math.Pow(Attribute(line, $"x{end}") - cx, 2)
				+ Math.Pow(Attribute(line, $"y{end}") - cy, 2));
		}

		double Hand(string hand, DialHand stated, string measure)
		{
			if (Find(element, hand) is not { } line)
			{
				return edge * (measure == "tail" ? stated.Tail : measure == "tip" ? stated.Tip : stated.Stroke);
			}

			return measure == "tail" ? Radius(line, "1")
				: measure == "tip" ? Radius(line, "2")
				: Attribute(line, "stroke-width");
		}

		foreach (var member in geometry.EnumerateObject())
		{
			var actual = member.Name switch
			{
				"edge" => edge,
				"faceRadius" => Attribute(face, "r"),
				"tickOuterRadius" => Radius(ticks[0], "2"),
				"majorTickInnerRadius" => Radius(ticks[0], "1"),
				"majorTickStroke" => Attribute(ticks[0], "stroke-width"),
				"minorTickInnerRadius" => Radius(ticks[1], "1"),
				"minorTickStroke" => Attribute(ticks[1], "stroke-width"),
				"hourTail" => Hand("widget-clock-dial-hand-hour", ShapeComponents.HourHand, "tail"),
				"hourTip" => Hand("widget-clock-dial-hand-hour", ShapeComponents.HourHand, "tip"),
				"hourStroke" => Hand("widget-clock-dial-hand-hour", ShapeComponents.HourHand, "stroke"),
				"minuteTail" => Hand("widget-clock-dial-hand-minute", ShapeComponents.MinuteHand, "tail"),
				"minuteTip" => Hand("widget-clock-dial-hand-minute", ShapeComponents.MinuteHand, "tip"),
				"minuteStroke" => Hand("widget-clock-dial-hand-minute", ShapeComponents.MinuteHand, "stroke"),
				"secondTail" => Hand("widget-clock-dial-hand-second", ShapeComponents.SecondHand, "tail"),
				"secondTip" => Hand("widget-clock-dial-hand-second", ShapeComponents.SecondHand, "tip"),
				"secondStroke" => Hand("widget-clock-dial-hand-second", ShapeComponents.SecondHand, "stroke"),
				"hubRadius" => Attribute(Part(element, "widget-clock-dial-hub"), "r"),
				_ => (double?)null
			};
			if (actual is null)
			{
				checks.Fail($"geometry.{member.Name}", "no interpretation of this key");
				continue;
			}

			checks.Number($"geometry.{member.Name}", member.Value, actual);
		}
	}

	private static void CheckCornerRadius(NodeView node, JsonElement value, Checks checks)
	{
		var element = node.Element;
		switch (node.Type)
		{
			case Components.Slider:
				checks.Number("cornerRadius", value, StylePx(Part(element, "widget-slider-track"), "border-radius"));
				break;
			case Components.Shape:
				var arc = ArcCorner().Match(Attribute(Part(element, null, "path"), "d", true));
				checks.Flag("cornerRadius (an arc corner)", true, arc.Success);
				if (arc.Success)
				{
					checks.Number("cornerRadius (rx)", value, Parse(arc.Groups["rx"].Value));
					checks.Number("cornerRadius (ry)", value, Parse(arc.Groups["ry"].Value));
				}

				break;
			case Components.Button:
				checks.Number("cornerRadius", value, StylePx(element, "border-radius"));
				break;
			default:
				checks.Number("cornerRadius", value, StylePx(Part(element, "widget-range-bar-track"), "border-radius"));
				break;
		}
	}

	private static void CheckFillFrom(NodeView node, string from, Checks checks)
	{
		if (node.Type == Components.Slider)
		{
			var fill = Part(node.Element, "widget-slider-fill").Computed;
			var anchored = from switch
			{
				"bottom" => fill.Get("bottom") is "0" or "0px" && fill.Get("top") == "auto",
				"start" => fill.Get("left") is "0" or "0px",
				_ => false
			};
			checks.Flag($"fillFrom ({from})", true, anchored);
			return;
		}

		checks.Text($"fillFrom ({from})",
			from == "start" ? "0%" : from,
			Find(node.Element, "widget-progress-fill")?.Style.GetValueOrDefault("left"));
	}

	private static void CheckFillColor(NodeView node, string colour, Checks checks)
	{
		var fill = node.Type == Components.Slider
			? Part(node.Element, "widget-slider-fill")
			: Part(node.Element, "widget-progress-fill");
		var background = fill.Style.GetValueOrDefault("background") ?? "";
		if (colour == "reader-accent")
		{
			checks.Flag("fillColor (reader-accent)",
				true,
				background.Contains("--color-accent", StringComparison.Ordinal));
			return;
		}

		if (node.Type == Components.Slider)
		{
			checks.Text("fillColor", Hex(colour), Hex(background));
			return;
		}

		checks.Flag($"fillColor ({colour} in {background})",
			true,
			background.Contains(colour, StringComparison.OrdinalIgnoreCase));
	}

	private static void CheckBackdrop(Element element, string key, JsonElement value, Checks checks)
	{
		var image = Part(element, "widget-button-artwork");
		var match = ArtworkTransform().Match(image.Style.GetValueOrDefault("transform") ?? "");
		if (!match.Success)
		{
			checks.Fail(key, $"unreadable artwork transform {image.Style.GetValueOrDefault("transform")}");
			return;
		}

		var width = Own(element, "width");
		var height = Own(element, "height");
		var zoom = Parse(match.Groups["zoom"].Value);
		checks.Number(key,
			value,
			key switch
			{
				"backdropWidth" => width * zoom,
				"backdropHeight" => height * zoom,
				"backdropOffsetX" => Parse(match.Groups["x"].Value) / 100 * width,
				_ => Parse(match.Groups["y"].Value) / 100 * height
			});
	}

	private static void CheckRingColor(Element element, string colour, Checks checks)
	{
		var ring = Find(element, "widget-button-ring");
		if (colour == "reader-cycled")
		{
			var style = ring?.Attributes.GetValueOrDefault("data-ring-style");
			checks.Text("ringColor (reader-cycled: no colour of its own)",
				null,
				ring?.Attributes.GetValueOrDefault("data-ring-color"));
			checks.Flag("ringColor (reader-cycled: a cycling style)",
				true,
				style is not null && Rings.CycleMs.ContainsKey(style));
			return;
		}

		checks.Text("ringColor", Hex(colour), Hex(ring?.Attributes.GetValueOrDefault("data-ring-color")));
	}

	private static void CheckLines(NodeView node, int lines, Checks checks)
	{
		var text = node.Text;
		checks.Flag("lines (clamped)", lines > 1, text.Classes.Contains("widget-text-clamp"));
		checks.Text("lines",
			lines > 1 ? lines.ToString(CultureInfo.InvariantCulture) : null,
			text.Style.GetValueOrDefault("-webkit-line-clamp"));
	}

	private static void CheckChildBox(Element element, JsonElement box, Checks checks)
	{
		var children = element.Children.Where(child => child.NodeId is not null).ToList();
		if (children.Count == 0)
		{
			checks.Fail("childBox", "no child drawn");
		}

		foreach (var child in children)
		{
			checks.Number($"childBox.width ({child.NodeId})", box.GetProperty("width"), Own(child, "width"));
			var height = box.GetProperty("height");
			if (height.ValueKind == JsonValueKind.Null)
			{
				checks.Text($"childBox.height ({child.NodeId})", null, child.Style.GetValueOrDefault("height"));
				checks.Flag($"childBox.height ({child.NodeId} given no height)",
					true,
					_scopes.TryGetValue(child, out var scope) && scope.GivenBox?.Height is null);
			}
			else
			{
				checks.Number($"childBox.height ({child.NodeId})", height, Own(child, "height"));
			}
		}
	}

	private static void CheckBand(NodeView node, JsonElement band, Checks checks)
	{
		var points = Points(Attribute(Part(node.Element, "widget-chart-line"), "d", true));
		var values = node.Element.Node!.Raw("points")!.Value.EnumerateArray().Select(item => item.GetDouble()).ToList();
		var low = values.IndexOf(values.Min());
		var high = values.IndexOf(values.Max());
		var scale = (points[low].Y - points[high].Y) / (values[high] - values[low]);
		var bottom = points[low].Y + values[low] * scale;
		checks.Number("band.top", band.GetProperty("top"), bottom - scale);
		checks.Number("band.bottom", band.GetProperty("bottom"), bottom);
	}

	private static void CheckLine(Element element, JsonElement line, Checks checks)
	{
		var points = Points(Attribute(Part(element, "widget-chart-line"), "d", true));
		checks.Count("line.length", line.GetArrayLength(), points.Count);
		for (var index = 0; index < Math.Min(points.Count, line.GetArrayLength()); index++)
		{
			checks.Number($"line[{index}].x", line[index].GetProperty("x"), points[index].X);
			checks.Number($"line[{index}].y", line[index].GetProperty("y"), points[index].Y);
		}
	}

	private static void CheckArea(Element element, Checks checks)
	{
		var area = Attribute(Part(element, "widget-chart-area"), "d", true);
		var run = Attribute(Part(element, "widget-chart-line"), "d", true)[1..];
		var foot = Format(Attribute(element, "height"), 6);
		var right = Format(Attribute(element, "width"), 6);
		checks.Text("area", $"M0 {foot} L{run} L{right} {foot} Z", area);
	}

	private static void CheckBinds(NodeView node, string lengthKey, string key, JsonElement value, Checks checks)
	{
		var scope = node.Scene.Pipeline.Builder!.Scopes.First(item => item.Element == node.Element);
		var length = UiLength.Of(scope.Node, lengthKey);
		if (length is null)
		{
			checks.Fail(key, $"the node has no {lengthKey} length");
			return;
		}

		var resolved = UiLength.Resolve(length, scope.Scope)!.Value;
		var terms = new List<(string Name, double Value)>();
		terms.Add(length.OfParent is not null && scope.Container is { } container
			? ("ofParent", length.OfParent.Value * container)
			: ("basis", length.Basis * scope.Basis));
		if (length.MaxOfCross is { } cross && scope.CrossExtent is { } extent)
		{
			terms.Add(("maxOfCross", cross * extent));
		}

		if (length.MaxOfCell is { } cell)
		{
			terms.Add(("maxOfCell", cell * UiLength.Cell));
		}

		var binding = terms.Where(term => Math.Abs(term.Value - resolved) < 1e-9)
			.Select(term => term.Name)
			.FirstOrDefault();
		checks.Text(key, value.GetString(), binding);
	}

	private static void CheckRect(Element part, string key, JsonElement rect, Checks checks)
	{
		foreach (var side in rect.EnumerateObject())
		{
			checks.Number($"{key}.{side.Name}", side.Value, StylePx(part, side.Name));
		}
	}

	private static void CheckModifierBorder(Element element, string key, JsonElement value, Checks checks)
	{
		var overlay = element.Children.FirstOrDefault(child => child.Classes.Contains("widget-modifier-border"));
		if (overlay is null)
		{
			checks.Fail(key, "no border overlay inside the edge");
			return;
		}

		checks.Text($"{key} (no outline)", null, element.Style.GetValueOrDefault("outline"));
		switch (key)
		{
			case "borderWidth":
				checks.Number(key, value, StylePx(overlay, "border-width"));
				break;
			case "borderStyle":
				checks.Text(key, value.GetString(), overlay.Style.GetValueOrDefault("border-style"));
				break;
			default:
				checks.Text(key, Hex(value.GetString()), Hex(overlay.Style.GetValueOrDefault("border-color")));
				break;
		}
	}

	private static void CheckRefusesEvents(NodeView node, bool refuses, Checks checks)
	{
		var offering = new[] { node.Element }.Concat(node.Element.Descendants())
			.Any(part => part.Classes.Contains("widget-pressable") || part.Classes.Contains("widget-press-tint"));
		checks.Flag("refusesEvents (offers no press)", refuses, !offering);
		checks.Flag("refusesEvents (aria-disabled)",
			refuses,
			node.Element.Attributes.GetValueOrDefault("aria-disabled") == "true");
	}

	private static void CheckRuns(Element element, JsonElement runs, Checks checks)
	{
		var drawn = element.Children.Where(child => !child.IsText).ToList();
		checks.Count("runs.length", runs.GetArrayLength(), drawn.Count);
		for (var index = 0; index < Math.Min(drawn.Count, runs.GetArrayLength()); index++)
		{
			var run = runs[index];
			var part = drawn[index];
			if (run.TryGetProperty("image", out var image))
			{
				checks.Text($"runs[{index}] tag", "img", part.Tag);
				checks.Text($"runs[{index}] image",
					image.GetString(),
					part.Attributes.GetValueOrDefault("data-resource-id"));
				checks.Text($"runs[{index}] alt",
					run.GetProperty("alt").GetString() ?? "",
					part.Attributes.GetValueOrDefault("alt"));
				continue;
			}

			checks.Text($"runs[{index}] tag", "span", part.Tag);
			checks.Text($"runs[{index}] text", run.GetProperty("text").GetString(), part.TextContent());
			var colour = run.GetProperty("color");
			checks.Text($"runs[{index}] color",
				colour.ValueKind == JsonValueKind.Null ? null : Hex(colour.GetString()),
				Hex(part.Style.GetValueOrDefault("color")));
			var weight = run.GetProperty("weight");
			checks.Text($"runs[{index}] weight",
				weight.ValueKind == JsonValueKind.Null ? null : weight.GetRawText(),
				part.Style.GetValueOrDefault("font-weight"));
		}
	}

	private static void CheckSaturation(Checks checks, JsonElement adjustment, string filter)
	{
		var luma = LumaWeights().Match(adjustment.GetProperty("saturationFormula").GetString()!);
		if (!luma.Success)
		{
			checks.Fail("saturationFormula", "unreadable formula");
			return;
		}

		var weights = new[] { luma.Groups["r"].Value, luma.Groups["g"].Value, luma.Groups["b"].Value }
			.Select(Parse)
			.ToArray();
		var brightness = adjustment.GetProperty("brightness").GetDouble();
		var saturation = adjustment.GetProperty("saturation").GetDouble();
		var matrix = Artwork.FilterMatrix(filter);
		double[] colour = [0.9, 0.3, 0.1];
		var bright = colour.Select(channel => channel * brightness).ToArray();
		var y = bright[0] * weights[0] + bright[1] * weights[1] + bright[2] * weights[2];
		for (var channel = 0; channel < 3; channel++)
		{
			var expected = y + saturation * (bright[channel] - y);
			var actual = matrix[channel * 5] * colour[0]
				+ matrix[channel * 5 + 1] * colour[1]
				+ matrix[channel * 5 + 2] * colour[2]
				+ matrix[channel * 5 + 4];
			checks.Number($"saturationFormula (channel {channel})",
				JsonDocument.Parse(Format(expected, 5)).RootElement,
				actual);
		}
	}

	private static double SliderLength(Element element)
	{
		return Own(element, Vertical(element) ? "height" : "width");
	}

	private static double? SliderFill(Element element)
	{
		var fill = Part(element, "widget-slider-fill");
		return StylePx(fill, Vertical(element) ? "height" : "width") / 100 * SliderLength(element);
	}

	private static double ProgressFill(Element element)
	{
		return Find(element, "widget-progress-fill") is { } fill
			? (StylePx(fill, "width") ?? 0) / 100 * Own(element, "width")
			: 0;
	}

	private static double ThumbStroke(Element element)
	{
		var border = Part(element, "widget-slider-thumb").Style.GetValueOrDefault("border") ?? "";
		return Px(border.Split(' ')[0]) ?? double.NaN;
	}

	private static bool Vertical(Element element)
	{
		return element.Classes.Contains("widget-slider-vertical");
	}

	private static double PressTintAlpha()
	{
		var tint = PressedTint();
		var rule = StyleSheet.Shared.Rules.Last(item => item.Selector.Matches(tint)
			&& item.Declarations.Any(declaration => declaration.Name == "background-color"));
		var colour = rule.Declarations.Last(declaration => declaration.Name == "background-color").Value;
		return CssColor.Parse(colour)!.Value.Alpha / 255.0;
	}

	private static Element PressedTint()
	{
		var button = new Element("div");
		button.SetClassName("widget-button");
		var tint = new Element("div");
		tint.SetClassName("widget-press-tint widget-press-tint-active");
		button.Append(tint);
		return tint;
	}

	private static double TransitionMs(string token)
	{
		var value = StyleSheet.Shared.DarkTokens[token].Trim().Split(' ')[0];
		return value.EndsWith("ms", StringComparison.Ordinal)
			? Parse(value[..^2])
			: Parse(value[..^1]) * 1000;
	}

	private static double ArcRadius(string path)
	{
		var arc = path.Split(" A ")[1];
		return Parse(arc.Split(' ')[0]);
	}

	private static double? FilterAmount(string? filter, string function)
	{
		var match = Regex.Match(filter ?? "", $@"{function}\((?<amount>[-\d.]+)\)");
		return match.Success ? Parse(match.Groups["amount"].Value) : null;
	}

	private static List<(double X, double Y)> Points(string path)
	{
		return path[1..]
			.Split('L')
			.Select(segment => segment.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Select(pair => (Parse(pair[0]), Parse(pair[1])))
			.ToList();
	}

	private static void Prose(Checks checks, string key, JsonElement value)
	{
		checks.Flag($"{key} (prose)", true, value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0);
	}

	private static double Own(Element element, string side)
	{
		var given = _scopes.TryGetValue(element, out var scope) ? scope.GivenBox : null;
		return StylePx(element, side)
			?? (element.Attributes.TryGetValue(side, out var attribute) ? Parse(attribute) : (double?)null)
			?? (side == "width" ? given?.Width : given?.Height)
			?? (side == "width" ? element.Box.Width.Value : element.Box.Height.Value);
	}

	private static double? StylePx(Element element, string name)
	{
		return element.Style.TryGetValue(name, out var value) ? Px(value) : null;
	}

	private static double? Px(string? value)
	{
		var match = LeadingNumber().Match(value ?? "");
		return match.Success ? Parse(match.Value) : null;
	}

	private static double Attribute(Element element, string name)
	{
		return element.Attributes.TryGetValue(name, out var value) ? Parse(value) : double.NaN;
	}

	private static string Attribute(Element element, string name, bool text)
	{
		return text ? element.Attributes.GetValueOrDefault(name) ?? "" : "";
	}

	private static double Parse(string value)
	{
		return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	private static string Format(double value, int decimals)
	{
		return Math.Round(value, decimals).ToString("0.######", CultureInfo.InvariantCulture);
	}

	private static string? Hex(string? colour)
	{
		if (colour is null)
		{
			return null;
		}

		var rgb = RgbNotation().Match(colour);
		return rgb.Success
			? string.Create(CultureInfo.InvariantCulture,
				$"#{int.Parse(rgb.Groups["r"].Value, CultureInfo.InvariantCulture):x2}"
				+ $"{int.Parse(rgb.Groups["g"].Value, CultureInfo.InvariantCulture):x2}"
				+ $"{int.Parse(rgb.Groups["b"].Value, CultureInfo.InvariantCulture):x2}")
			: colour.ToLowerInvariant();
	}

	private static string? Collapse(string? value)
	{
		return value is null ? null : Whitespace().Replace(value.Trim(), " ");
	}

	private static Element? Find(Element element, string className)
	{
		return element.Descendants()
			.FirstOrDefault(part => part.Classes.Contains(className)
				&& part.Closest(candidate => candidate.NodeId is not null) == element);
	}

	private static Element Part(Element element, string className)
	{
		return Find(element, className) ?? new Element("missing");
	}

	private static Element Part(Element element, string? className, string tag)
	{
		return element.Descendants()
				.FirstOrDefault(part => part.Tag == tag
					&& (className is null || part.Classes.Contains(className))
					&& part.Closest(candidate => candidate.NodeId is not null) == element)
			?? new Element("missing");
	}

	[GeneratedRegex(@"^without(?<type>[A-Z][a-z]+)Version(?<version>\d+)$")]
	private static partial Regex VersionAsked();

	[GeneratedRegex(@"^-?\d+(\.\d+)?([eE][-+]?\d+)?")]
	private static partial Regex LeadingNumber();

	[GeneratedRegex(@"A (?<rx>[\d.]+) (?<ry>[\d.]+) 0 0 1")]
	private static partial Regex ArcCorner();

	[GeneratedRegex(@"translate\((?<x>[-\d.]+)%,\s*(?<y>[-\d.]+)%\)\s*scale\((?<zoom>[\d.]+)\)")]
	private static partial Regex ArtworkTransform();

	[GeneratedRegex(@"luma = (?<r>[\d.]+) R \+ (?<g>[\d.]+) G \+ (?<b>[\d.]+) B")]
	private static partial Regex LumaWeights();

	[GeneratedRegex(@"^rgba?\((?<r>\d+),\s*(?<g>\d+),\s*(?<b>\d+)")]
	private static partial Regex RgbNotation();

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	private sealed class NodeView
	{
		public NodeView(Scene scene, Element element)
		{
			Scene = scene;
			Element = element;
			Text = Type == Components.Button
				? element.Children.FirstOrDefault(child =>
					child.Attributes.GetValueOrDefault("data-node-type") == Components.Text)
				?? element
				: element;
		}

		public Scene Scene { get; }

		public Element Element { get; }

		public Element Text { get; }

		public string Type => Element.Attributes.GetValueOrDefault("data-node-type") ?? "";
	}

	private sealed class Scene
	{
		public Scene(
			JsonElement tree,
			double width,
			double height,
			double basis,
			DateTimeOffset now,
			IReadOnlyDictionary<string, int>? versionCaps)
		{
			Pipeline = TilePipeline.Resolve(Input(tree, width, height, basis, now), Fixtures.Fonts, versionCaps);
			foreach (var scope in Pipeline.Builder?.Scopes ?? [])
			{
				_scopes.AddOrUpdate(scope.Element, scope);
			}
		}

		public TilePipeline Pipeline { get; }

		public static TileRenderInput Input(
			JsonElement tree,
			double width,
			double height,
			double basis,
			DateTimeOffset now)
		{
			return Fixtures.Input(tree, width, height, basis) with
			{
				Now = now,
				Culture = "en-US",
				Localization = new KeyNames(),
				Resources = new AnyResource()
			};
		}

		public Element? Find(string id)
		{
			return Pipeline.Surface.Descendants().FirstOrDefault(element => element.NodeId == id);
		}

		public Element Node(string id)
		{
			return Find(id) ?? throw new InvalidOperationException($"no element with data-node-id=\"{id}\"");
		}
	}

	private sealed class Checks
	{
		private readonly string _at;

		private readonly string _node;

		private readonly List<string> _differences;

		public Checks(string at)
			: this(at, "", [])
		{
		}

		private Checks(string at, string node, List<string> differences)
		{
			_at = at;
			_node = node;
			_differences = differences;
		}

		public IReadOnlyList<string> Differences => _differences;

		public Checks For(string node)
		{
			return new Checks(_at, node, _differences);
		}

		public void Number(string key, JsonElement expected, double? actual)
		{
			var raw = expected.GetRawText();
			var point = raw.IndexOf('.', StringComparison.Ordinal);
			var tolerance = 0.5 * Math.Pow(10, point < 0 ? 0 : -(raw.Length - point - 1));
			if (actual is not { } value || !(Math.Abs(value - expected.GetDouble()) <= tolerance))
			{
				var got = actual is null ? "nothing" : actual.Value.ToString("R", CultureInfo.InvariantCulture);
				Fail(key,
					string.Create(CultureInfo.InvariantCulture, $"expected {raw} (within {tolerance}), got {got}"));
			}
		}

		public void Count(string key, int expected, int actual)
		{
			if (expected != actual)
			{
				Fail(key, string.Create(CultureInfo.InvariantCulture, $"expected {expected}, got {actual}"));
			}
		}

		public void Text(string key, string? expected, string? actual)
		{
			if (!string.Equals(expected, actual, StringComparison.Ordinal))
			{
				Fail(key, $"expected {Show(expected)}, got {Show(actual)}");
			}
		}

		public void Flag(string key, bool expected, bool actual)
		{
			if (expected != actual)
			{
				Fail(key, $"expected {expected}, got {actual}");
			}
		}

		public void Fail(string key, string message)
		{
			_differences.Add($"{_at} {_node} {key}: {message}");
		}

		private static string Show(string? value)
		{
			return value is null ? "nothing" : $"\"{value}\"";
		}
	}

	private sealed class KeyNames : IRenderLocalization
	{
		public string Translate(string scope, string key, IReadOnlyDictionary<string, string>? arguments)
		{
			return $"{scope}:{key}";
		}
	}

	private sealed class AnyResource : IRenderResources
	{
		private static readonly byte[] _pixel = Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

		public RenderResource? TryGet(string resourceId, int? displayPx)
		{
			return new RenderResource(_pixel, "image/png");
		}
	}
}
