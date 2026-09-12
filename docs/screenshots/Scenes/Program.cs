using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

var outDir = args.Length > 0 ? args[0] : "out/scenes";
Directory.CreateDirectory(outDir);

var surface = new UiSurface { Kind = "widget", SessionMode = "shared" };
foreach (var scene in Scenes.All())
{
	var tree = UiViewBuilder.Build(surface, scene.Root);
	var json = $$"""{"width":{{scene.Width}},"height":{{scene.Height}},"radius":{{scene.Radius}},"root":{{UiCanonicalJson.Serialize(tree.Root)}}}""";
	File.WriteAllText(Path.Combine(outDir, scene.Name + ".json"), json);
}

internal sealed record Scene(string Name, int Width, int Height, int Radius, UiElement Root);

internal static partial class Scenes
{
	// One deck cell drawn at twice the app's 120px reference size, with its corner radius and safe-area padding.
	private const int CellPx = 240;
	private const double Scale = CellPx / UiLength.Cell;
	private const int CornerRadius = 22;
	private const int SafeArea = 12;

	public static UiSize TilePadding => UiSize.Of(UiLength.Capped(SafeArea / UiLength.Cell, SafeArea));

	public static Scene Tile(string name, UiElement root, int columns = 1, int rows = 1)
		=> new(name, CellPx * columns, CellPx * rows, (int)(CornerRadius * Scale), root);

	public static IEnumerable<Scene> All()
	{
		yield return Tile("text", new UiStack
		{
			Key = "weather",
			Direction = UiComponentDirections.Vertical,
			Padding = TilePadding,
			Children =
			[
				new UiTextRun { Key = "temp", Size = 0.3, Weight = UiComponentTextWeights.Bold, Text = "23°" },
				new UiTextRun { Key = "condition", Size = 0.11, Text = "Partly cloudy" },
				new UiTextRun { Key = "location", Size = 0.07, Role = UiComponentTextRoles.Muted, Text = "Berlin" },
			],
		});
	}
}
