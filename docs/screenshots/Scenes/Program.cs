using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

var outDir = args.Length > 0 ? args[0] : "out/scenes";
Directory.CreateDirectory(outDir);

var surface = new UiSurface { Kind = "widget", SessionMode = "shared" };
foreach (var scene in Scenes.All())
{
	var tree = UiViewBuilder.Build(surface, scene.Root);
	var resources = JsonSerializer.Serialize(Scenes.Resources);
	var json
		= $$"""{"width":{{scene.Width}},"height":{{scene.Height}},"radius":{{scene.Radius}},"basis":{{scene.Basis}},"resources":{{resources}},"root":{{UiCanonicalJson.Serialize(tree.Root)}}}""";
	File.WriteAllText(Path.Combine(outDir, scene.Name + ".json"), json);
}

internal sealed record Scene(string Name, int Width, int Height, int Radius, int Basis, UiElement Root);

internal static partial class Scenes
{
	// One deck cell drawn at twice the app's 120px reference size, with its corner radius and safe-area padding.
	private const int CellPx = 240;
	private const double Scale = CellPx / UiLength.Cell;
	private const int CornerRadius = 22;
	private const int SafeArea = 12;
	private const string IconDir = "../../ui/runtime/styles/icons";

	public static Dictionary<string, string> Resources { get; } = [];

	public static UiSize TilePadding => UiSize.Of(UiLength.Capped(SafeArea / UiLength.Cell, SafeArea));

	public static Scene Tile(string name, UiElement root, int columns = 1, int rows = 1)
		=> new(name,
			CellPx * columns,
			CellPx * rows,
			(int)(CornerRadius * Scale),
			Math.Min(columns, rows) * CellPx,
			root);

	// A dialog's lengths follow the box Macro Deck hands it, which is larger than the part a scene shows.
	public static Scene Dialog(string name, UiElement root, int width, int height, int basis = 600)
		=> new(name, width, height, 24, basis, root);

	public static UiResource Icon(string name, string color = "#ffffff")
	{
		var svg = File.ReadAllText(Path.Combine(IconDir, name + ".svg"))
			.Replace("#000", color, StringComparison.Ordinal);
		return Svg(name + color, svg);
	}

	public static UiResource Svg(string id, string svg)
	{
		Resources[id] = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
		return new UiResource { ResourceId = id };
	}

	public static UiResource Cover(string id, string from, string to)
		=> Svg(id,
			$"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{from}"/><stop offset="1" stop-color="{to}"/></linearGradient></defs><rect width="100" height="100" fill="url(#g)"/><circle cx="50" cy="50" r="22" fill="none" stroke="#ffffff" stroke-opacity="0.35" stroke-width="3"/><circle cx="50" cy="50" r="5" fill="#ffffff" fill-opacity="0.5"/></svg>""");

	public static IEnumerable<Scene> All() =>
	[
		.. TextScenes(), .. TextFieldScenes(), .. ImageScenes(), .. ButtonScenes(), .. SliderScenes(),
		.. RangeBarScenes(), .. StackScenes(), .. ListScenes(), .. TransformScenes(), .. ModifierScenes(), .. ChartScenes(),
		.. TimeScenes(), .. ProgressScenes(), .. ShapeScenes(), .. IconScenes(), .. GridScenes(),
		.. GaugeScenes(), .. ToggleScenes(), .. SegmentedScenes(), .. DialScenes(), .. ViewScenes(),
		.. ConceptScenes(),
	];
}
