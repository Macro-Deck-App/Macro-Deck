using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record ModifierInputs(double? Opacity, string? Radius);

internal sealed record IntrinsicMetrics(double Basis, double? CrossExtent, double? Container, bool Horizontal)
{
	public LengthScope Scope => new(Basis, CrossExtent, Container);

	public IntrinsicMetrics Nested()
	{
		return this with { Container = null };
	}

	public double OfChild(UiNode child)
	{
		return Layouts.IntrinsicMainPx(child, this);
	}
}

internal sealed record ComponentDefinition(
	string Type,
	string Tag,
	Action<UiNode, NodeScope> Paint,
	int MaxVersion = 1,
	Func<UiNode, IntrinsicMetrics, double>? Intrinsic = null,
	bool TransparentRoot = false,
	Func<UiNode, ModifierInputs>? ModifierInputs = null,
	Func<UiNode, long?>? TickPeriodMs = null);

internal static class Components
{
	public const string Stack = "ui.stack";
	public const string Text = "ui.text";
	public const string Image = "ui.image";
	public const string RangeBar = "ui.range-bar";
	public const string Slider = "ui.slider";
	public const string Button = "ui.button";
	public const string Layer = "ui.layer";
	public const string Chart = "ui.chart";
	public const string TextField = "ui.text-field";
	public const string List = "ui.list";
	public const string Transform = "ui.transform";
	public const string Shape = "ui.shape";
	public const string Icon = "ui.icon";
	public const string Grid = "ui.grid";
	public const string Gauge = "ui.gauge";
	public const string Toggle = "ui.toggle";
	public const string Segmented = "ui.segmented";
	public const string Dial = "ui.dial";
	public const string Modifier = "ui.modifier";
	public const string Responsive = "ui.responsive";
	public const string FirstFit = "ui.first-fit";
	public const string DynamicText = "macrodeck.dynamic-text";
	public const string ClockDial = "macrodeck.clock-dial";
	public const string ProgressBar = "macrodeck.progress-bar";
	public const string ProgressText = "macrodeck.progress-text";
	public const string VideoStream = "macrodeck.video-stream";

	private static readonly Lazy<Dictionary<string, ComponentDefinition>> _registry = new(Build);

	public static ComponentDefinition? Get(string type)
	{
		return _registry.Value.GetValueOrDefault(type);
	}

	public static (UiNode Node, string? Unsupported) Resolve(
		UiNode node,
		IReadOnlyDictionary<string, int>? versionCaps = null)
	{
		var current = node;
		while (true)
		{
			if (Get(current.Type) is { } definition
				&& current.RequiredComponentVersion >= 1
				&& current.RequiredComponentVersion <= MaxVersion(definition, versionCaps))
			{
				return (current, null);
			}

			if (current.Fallback is null)
			{
				return (current, current.Type);
			}

			current = current.Fallback;
		}
	}

	private static int MaxVersion(ComponentDefinition definition, IReadOnlyDictionary<string, int>? versionCaps)
	{
		return versionCaps is not null && versionCaps.TryGetValue(definition.Type, out var cap)
			? Math.Min(cap, definition.MaxVersion)
			: definition.MaxVersion;
	}

	private static Dictionary<string, ComponentDefinition> Build()
	{
		ComponentDefinition[] definitions =
		[
			StackComponents.Stack, TextComponents.Text, ImageComponent.Image, BarComponents.RangeBar,
			BarComponents.Slider, StackComponents.Button, ContainerComponents.Layer, ShapeComponents.Chart,
			TextComponents.TextField, ListComponent.List, ContainerComponents.Transform, ShapeComponents.Shape,
			ShapeComponents.Icon, GridComponent.Grid, ShapeComponents.Gauge, ControlComponents.Toggle,
			ControlComponents.Segmented, ShapeComponents.Dial, ContainerComponents.Modifier,
			ContainerComponents.Responsive, ContainerComponents.FirstFit, TextComponents.DynamicText,
			ShapeComponents.ClockDial, BarComponents.ProgressBar, TextComponents.ProgressText,
			VideoStreamComponent.VideoStream
		];
		return definitions.ToDictionary(definition => definition.Type, StringComparer.Ordinal);
	}
}
