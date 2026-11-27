using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal readonly record struct DialHand(double Tail, double Tip, double Stroke);

internal static partial class ShapeComponents
{
	private static readonly string[][] _iconVersions =
	[
		[
			"action-button-type", "alert-triangle", "align-bottom", "align-center", "align-left", "align-middle",
			"align-right", "align-top", "arrow-down", "arrow-left", "arrow-right", "arrow-up", "bell", "braces-x",
			"bug", "chart", "check", "chevron-right", "clipboard", "clock-type", "code", "copy", "crosshair",
			"device-desktop", "device-floppy", "device-phone", "device-tablet", "disc", "discord", "dots-vertical",
			"download", "external-link", "file-text", "folder", "folder-plus", "globe", "grid", "heart",
			"history-graph-type", "image", "info", "layers", "list-play", "lock", "log-out", "message-square",
			"minus", "moon", "music-note", "music-player-type", "pause", "pencil", "pin", "pin-off", "play", "plus",
			"power", "puzzle", "refresh", "scissors", "search", "settings", "sidebar", "sliders", "star", "store",
			"sun", "trash", "undo", "unlock", "upload", "user", "weather-type", "wifi", "x", "zap"
		],
		[
			"device-laptop", "device-watch", "device-headphones", "device-gamepad", "device-mouse", "device-keyboard",
			"device-speaker", "battery", "battery-charging"
		]
	];

	public static readonly DialHand HourHand = new(0.05, 0.21, 0.04);

	public static readonly DialHand MinuteHand = new(0.05, 0.33, 0.025);

	public static readonly DialHand SecondHand = new(0.08, 0.37, 0.0125);

	public static readonly ComponentDefinition Chart = new(Components.Chart, "svg", PaintChart);

	public static readonly ComponentDefinition Shape = new(Components.Shape, "svg", PaintShape);

	public static readonly ComponentDefinition Gauge = new(Components.Gauge,
		"svg",
		(node, scope) =>
		{
			scope.Element.SetClassName("widget-gauge");
			PaintArc(node, scope, Layouts.Level(node), UiLength.HexColor(node, "trackColor"));
		});

	public static readonly ComponentDefinition Dial = new(Components.Dial, "svg", PaintDial);

	public static readonly ComponentDefinition Icon = new(Components.Icon,
		"div",
		PaintIcon,
		2,
		(node, metrics) => UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0);

	public static readonly ComponentDefinition ClockDial = new(Components.ClockDial,
		"svg",
		PaintClockDial,
		1,
		(node, metrics) => UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0,
		false,
		null,
		node => TimeFormats.HasReference(node, "value") ? node.Boolean("seconds") == true ? 1000 : 60_000 : null);

	private static string Fixed(double value)
	{
		return NodeScope.Num(Math.Round(value * 1000, MidpointRounding.AwayFromZero) / 1000);
	}

	private static string Round2(double value)
	{
		return NodeScope.Num(Math.Round(value * 100, MidpointRounding.AwayFromZero) / 100);
	}

	private static Element SvgPart(NodeScope scope, string name, string tag)
	{
		return scope.Part(name, tag);
	}

	private static (double Width, double Height) SvgSize(NodeScope scope)
	{
		var width = scope.Box.Width ?? scope.Basis;
		var height = scope.Box.Height ?? scope.Basis;
		scope.Element.SetAttribute("width", NodeScope.Num(width));
		scope.Element.SetAttribute("height", NodeScope.Num(height));
		return (width, height);
	}

	private static void PaintChart(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-chart");
		var (width, height) = SvgSize(scope);
		var points = new List<double>();
		if (node.Raw("points") is { ValueKind: System.Text.Json.JsonValueKind.Array } raw)
		{
			foreach (var value in raw.EnumerateArray())
			{
				if (value.ValueKind == System.Text.Json.JsonValueKind.Number && double.IsFinite(value.GetDouble()))
				{
					points.Add(Layouts.ClampUnit(value.GetDouble()));
				}
			}
		}

		if (points.Count == 0)
		{
			return;
		}

		var right = Math.Round(width * 100, MidpointRounding.AwayFromZero) / 100;
		var bottom = Math.Round(height * 100, MidpointRounding.AwayFromZero) / 100;
		var top = Layouts.ClampUnit(node.Number("plotTop") ?? 0) * bottom;

		string Y(double value)
		{
			return Round2(bottom - value * (bottom - top));
		}

		var run = new StringBuilder();
		if (points.Count == 1)
		{
			run.Append(CultureInfo.InvariantCulture, $"0 {Y(points[0])} L{NodeScope.Num(right)} {Y(points[0])}");
		}
		else
		{
			for (var index = 0; index < points.Count; index++)
			{
				var x = Round2(index / (double)(points.Count - 1) * right);
				run.Append(index == 0 ? $"{x} {Y(points[index])}" : $" L{x} {Y(points[index])}");
			}
		}

		var colour = UiLength.HexColor(node, "color") ?? "var(--color-accent)";
		var area = SvgPart(scope, "chartArea", "path");
		area.SetClassName("widget-chart-area");
		area.SetAttribute("d", $"M0 {NodeScope.Num(bottom)} L{run} L{NodeScope.Num(right)} {NodeScope.Num(bottom)} Z");
		area.SetStyle("fill", colour);
		var line = SvgPart(scope, "chartLine", "path");
		line.SetClassName("widget-chart-line");
		line.SetAttribute("d", $"M{run}");
		line.SetAttribute("stroke-width", NodeScope.Num(Layouts.Length(node, "thickness", scope.Scope)));
		line.SetStyle("stroke", colour);
	}

	[GeneratedRegex(@"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")]
	private static partial Regex PathNumber();

	private static bool IsPathData(string value)
	{
		var arity = new Dictionary<char, int>
		{
			['M'] = 2,
			['L'] = 2,
			['H'] = 1,
			['V'] = 1,
			['C'] = 6,
			['Q'] = 4,
			['A'] = 7,
			['Z'] = 0
		};
		var rest = value.Trim();
		if (rest.Length == 0 || rest[0] != 'M')
		{
			return false;
		}

		while (rest.Length > 0)
		{
			if (!arity.TryGetValue(rest[0], out var count))
			{
				return false;
			}

			rest = rest[1..];
			var numbers = 0;
			while (true)
			{
				rest = rest.TrimStart(' ', ',', '\t', '\n');
				var match = PathNumber().Match(rest);
				if (!match.Success)
				{
					break;
				}

				rest = rest[match.Length..];
				numbers++;
			}

			if (count == 0 ? numbers != 0 : numbers == 0 || numbers % count != 0)
			{
				return false;
			}
		}

		return true;
	}

	private static string RoundedRect(double width, double height, double radius)
	{
		var r = Math.Max(0, Math.Min(radius, Math.Min(width / 2, height / 2)));

		string N(double value)
		{
			return NodeScope.Num(value);
		}

		if (r == 0)
		{
			return $"M 0 0 H {N(width)} V {N(height)} H 0 Z";
		}

		return $"M {N(r)} 0 H {N(width - r)} A {N(r)} {N(r)} 0 0 1 {N(width)} {N(r)} V {N(height - r)} "
			+ $"A {N(r)} {N(r)} 0 0 1 {N(width - r)} {N(height)} H {N(r)} "
			+ $"A {N(r)} {N(r)} 0 0 1 0 {N(height - r)} V {N(r)} "
			+ $"A {N(r)} {N(r)} 0 0 1 {N(r)} 0 Z";
	}

	private static void PaintShape(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-shape");
		var (width, height) = SvgSize(scope);
		var shape = node.String("shape") ?? "rectangle";
		var isPath = shape == "path";
		string? d;
		if (isPath)
		{
			d = node.String("path") is { } data && IsPathData(data) ? data : null;
		}
		else
		{
			var r = Math.Min(width, height) / 2;
			d = shape switch
			{
				"rectangle" => RoundedRect(width, height, 0),
				"rounded-rectangle" => RoundedRect(width,
					height,
					UiLength.Resolve(UiLength.Of(node, "cornerRadius"), scope.Scope.WithoutCross()) ?? 0),
				"capsule" => RoundedRect(width, height, r),
				"circle" => $"M {NodeScope.Num(width / 2 - r)} {NodeScope.Num(height / 2)} "
					+ $"A {NodeScope.Num(r)} {NodeScope.Num(r)} 0 1 1 "
					+ $"{NodeScope.Num(width / 2 + r)} {NodeScope.Num(height / 2)} "
					+ $"A {NodeScope.Num(r)} {NodeScope.Num(r)} 0 1 1 "
					+ $"{NodeScope.Num(width / 2 - r)} {NodeScope.Num(height / 2)} Z",
				_ => null
			};
		}

		if (d is null)
		{
			return;
		}

		var path = SvgPart(scope, "shapePath", "path");
		path.SetAttribute("d", d);
		path.SetAttribute("transform", isPath ? $"scale({NodeScope.Num(width)} {NodeScope.Num(height)})" : null);
		path.SetAttribute("vector-effect", isPath ? "non-scaling-stroke" : null);
		path.SetAttribute("fill", UiLength.HexColor(node, "color") ?? "none");
		var stroke = UiLength.HexColor(node, "strokeColor");
		var strokeWidth = UiLength.Resolve(UiLength.Of(node, "strokeWidth"), scope.Scope.WithoutCross());
		var stroked = stroke is not null && strokeWidth is > 0;
		path.SetAttribute("stroke", stroked ? stroke : null);
		path.SetAttribute("stroke-width", stroked ? NodeScope.Num(strokeWidth!.Value) : null);
	}

	private sealed record ArcMetrics(double Cx, double Cy, double Radius);

	private static (double Start, double Sweep) Sweep(UiNode node)
	{
		var start = node.Number("startAngle") ?? -135;
		var end = node.Number("endAngle") ?? 135;
		return (start, Math.Max(-360, Math.Min(360, end - start)));
	}

	private static (double X, double Y) ArcPoint(ArcMetrics metrics, double angle)
	{
		var radians = angle * Math.PI / 180;
		return (metrics.Cx + metrics.Radius * Math.Sin(radians), metrics.Cy - metrics.Radius * Math.Cos(radians));
	}

	private static string ArcPath(ArcMetrics metrics, double start, double sweep)
	{
		var from = ArcPoint(metrics, start);
		var head = $"M {Fixed(from.X)} {Fixed(from.Y)}";
		if (sweep == 0)
		{
			return $"{head} L {Fixed(from.X)} {Fixed(from.Y)}";
		}

		double[] halves = Math.Abs(sweep) >= 360 ? [sweep / 2, sweep / 2] : [sweep];
		var flag = sweep > 0 ? 1 : 0;
		var at = start;
		var d = head;
		foreach (var part in halves)
		{
			at += part;
			var to = ArcPoint(metrics, at);
			var large = Math.Abs(part) > 180 ? 1 : 0;
			d += $" A {Fixed(metrics.Radius)} {Fixed(metrics.Radius)} 0 {large} {flag} {Fixed(to.X)} {Fixed(to.Y)}";
		}

		return d;
	}

	private static ArcMetrics PaintArc(UiNode node, NodeScope scope, double level, string? trackColor)
	{
		var (width, height) = SvgSize(scope);
		var thickness = Layouts.Length(node, "thickness", scope.Scope);
		var metrics = new ArcMetrics(width / 2, height / 2, Math.Max(0, (Math.Min(width, height) - thickness) / 2));
		var (start, sweep) = Sweep(node);
		var track = SvgPart(scope, "gaugeTrack", "path");
		track.SetClassName("widget-gauge-track");
		track.SetAttribute("d", ArcPath(metrics, start, sweep));
		track.SetAttribute("stroke-width", NodeScope.Num(thickness));
		track.SetStyle("stroke", trackColor);
		if (level > 0)
		{
			var fill = SvgPart(scope, "gaugeFill", "path");
			fill.SetClassName("widget-gauge-fill");
			fill.SetAttribute("d", ArcPath(metrics, start, sweep * level));
			fill.SetAttribute("stroke-width", NodeScope.Num(thickness));
			fill.SetStyle("stroke", Layouts.LevelColor(node));
		}

		return metrics;
	}

	private static void PaintDial(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-dial");
		scope.Element.SetClass("widget-pressable",
			Modifiers.ClaimsValue(node) && !scope.IsDisabled && Sweep(node).Sweep != 0);
		var level = Layouts.Level(node);
		var metrics = PaintArc(node, scope, level, null);
		var thickness = Layouts.Length(node, "thickness", scope.Scope);
		var (start, sweep) = Sweep(node);
		var at = ArcPoint(metrics, start + sweep * level);
		var thumb = SvgPart(scope, "dialThumb", "circle");
		thumb.SetClassName("widget-dial-thumb");
		thumb.SetAttribute("cx", NodeScope.Num(at.X));
		thumb.SetAttribute("cy", NodeScope.Num(at.Y));
		thumb.SetAttribute("r", NodeScope.Num(1.25 * thickness));
		thumb.SetAttribute("stroke-width", NodeScope.Num(0.28 * thickness));
	}

	private static void PaintIcon(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-icon");
		var known = new[] { scope.Box.Width, scope.Box.Height }
			.Where(value => value is not null)
			.Select(value => value!.Value)
			.ToList();
		var edge = scope.Resolve("size") ?? (known.Count > 0 ? known.Min() : scope.Basis);
		var width = scope.Box.Width ?? edge;
		var height = scope.Box.Height ?? edge;
		NodeScope.SizeTo(element, new BoxSize(width, height));
		var name = node.String("icon");
		if (name is null || !_iconVersions.Any(group => group.Contains(name)))
		{
			return;
		}

		var glyph = scope.Part("glyph", "span");
		glyph.SetClassName($"widget-icon-glyph icon icon-{name}");
		glyph.SetStyle("width", NodeScope.Px(edge));
		glyph.SetStyle("height", NodeScope.Px(edge));
		glyph.SetStyle("left", NodeScope.Px((width - edge) / 2));
		glyph.SetStyle("top", NodeScope.Px((height - edge) / 2));
		glyph.SetStyle("color", Layouts.FillColor(node));
		glyph.Paint = name;
	}

	private static void PaintClockDial(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-clock-dial");
		var (width, height) = SvgSize(scope);
		var d = Math.Min(width, height);
		var cx = width / 2;
		var cy = height / 2;
		var face = SvgPart(scope, "dialFace", "circle");
		face.SetClassName("widget-clock-dial-face");
		face.SetAttribute("cx", NodeScope.Num(cx));
		face.SetAttribute("cy", NodeScope.Num(cy));
		face.SetAttribute("r", NodeScope.Num(d * 0.48));
		face.SetStyle("fill", "var(--color-bg-tertiary)");
		var tint = UiLength.HexColor(node, "color");
		for (var index = 0; index < 12; index++)
		{
			var major = index % 3 == 0;
			var angle = index * Math.PI / 6;
			var inner = d * (major ? 0.37 : 0.405);
			var outer = d * 0.44;
			var mark = SvgPart(scope, $"dialTick{index}", "line");
			mark.SetClassName("widget-clock-dial-tick");
			mark.SetAttribute("x1", NodeScope.Num(cx + inner * Math.Sin(angle)));
			mark.SetAttribute("y1", NodeScope.Num(cy - inner * Math.Cos(angle)));
			mark.SetAttribute("x2", NodeScope.Num(cx + outer * Math.Sin(angle)));
			mark.SetAttribute("y2", NodeScope.Num(cy - outer * Math.Cos(angle)));
			mark.SetAttribute("stroke-width", NodeScope.Num(d * (major ? 0.025 : 0.015)));
			mark.SetStyle("stroke", tint ?? (major ? "var(--color-text-secondary)" : "var(--color-text-muted)"));
		}

		var local = TimeFormats.Local(scope.Host.Now, TimeFormats.Zone(node, "value"), scope.Host.TimeZone);
		var hours = local.Hour;
		var minutes = local.Minute;
		var seconds = local.Second;
		var hands = new List<(string Key, string Class, double Angle, DialHand Hand, string Color)>
		{
			("dialHour",
				"widget-clock-dial-hand-hour",
				hours % 12 * 30 + minutes * 0.5,
				HourHand,
				tint ?? "var(--color-text-primary)"),
			("dialMinute",
				"widget-clock-dial-hand-minute",
				minutes * 6 + seconds * 0.1,
				MinuteHand,
				tint ?? "var(--color-text-primary)")
		};
		if (node.Boolean("seconds") == true)
		{
			hands.Add(("dialSecond", "widget-clock-dial-hand-second", seconds * 6, SecondHand, "var(--color-accent)"));
		}

		foreach (var hand in hands)
		{
			var radians = hand.Angle * Math.PI / 180;
			var line = SvgPart(scope, hand.Key, "line");
			line.SetClassName(hand.Class);
			line.SetAttribute("x1", NodeScope.Num(cx - d * hand.Hand.Tail * Math.Sin(radians)));
			line.SetAttribute("y1", NodeScope.Num(cy + d * hand.Hand.Tail * Math.Cos(radians)));
			line.SetAttribute("x2", NodeScope.Num(cx + d * hand.Hand.Tip * Math.Sin(radians)));
			line.SetAttribute("y2", NodeScope.Num(cy - d * hand.Hand.Tip * Math.Cos(radians)));
			line.SetAttribute("stroke-width", NodeScope.Num(d * hand.Hand.Stroke));
			line.SetStyle("stroke", hand.Color);
		}

		var hub = SvgPart(scope, "dialHub", "circle");
		hub.SetClassName("widget-clock-dial-hub");
		hub.SetAttribute("cx", NodeScope.Num(cx));
		hub.SetAttribute("cy", NodeScope.Num(cy));
		hub.SetAttribute("r", NodeScope.Num(d * 0.026));
		hub.SetStyle("fill", "var(--color-accent)");
	}
}
