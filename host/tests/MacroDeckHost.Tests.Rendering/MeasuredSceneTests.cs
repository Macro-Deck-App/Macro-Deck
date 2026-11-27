using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Rendering;

namespace MacroDeckHost.Tests.Rendering;

public sealed class MeasuredSceneTests
{
	private const double Tolerance = 0.1;

	public static IEnumerable<TestCaseData> Cases()
	{
		foreach (var scene in Fixtures.Manifest.GetProperty("scenes").EnumerateArray())
		{
			var tree = scene.GetProperty("tree").GetString()!;
			var measured = tree.Replace("-tree.json", "-measured.json", StringComparison.Ordinal);
			foreach (var testCase in Fixtures.Profile(measured).GetProperty("cases").EnumerateArray())
			{
				var key = CaseKey(testCase);
				yield return new TestCaseData(tree, measured, key).SetName($"{tree[..^".json".Length]} {key}");
			}
		}
	}

	[TestCaseSource(nameof(Cases))]
	public void Scene_lays_out_as_chrome_measured(string treeFile, string measuredFile, string key)
	{
		var tree = Fixtures.Profile(treeFile);
		var measured = Fixtures.Profile(measuredFile);
		Assert.That(measured.GetProperty("timeZone").GetString(), Is.EqualTo("UTC"));
		var testCase = measured.GetProperty("cases").EnumerateArray().Single(item => CaseKey(item) == key);
		var tile = testCase.GetProperty("tile");
		var input = Fixtures.Input(tree,
			tile.GetProperty("width").GetDouble(),
			tile.GetProperty("height").GetDouble(),
			testCase.GetProperty("basis").GetDouble());

		var drawn = new TileRenderer().Layout(input).Nodes;

		var differences = new List<string>();
		var expectedNodes = testCase.GetProperty("nodes");
		foreach (var id in drawn.Keys.Where(id => !expectedNodes.TryGetProperty(id, out _)))
		{
			differences.Add($"{id}: node is new");
		}

		foreach (var node in expectedNodes.EnumerateObject())
		{
			if (!drawn.TryGetValue(node.Name, out var actual))
			{
				differences.Add($"{node.Name}: node is gone");
				continue;
			}

			Compare(node.Name, node.Value, actual, differences);
		}

		Assert.That(differences, Is.Empty, $"{treeFile} {key}:\n  " + string.Join("\n  ", differences));
	}

	private static void Compare(string id, JsonElement expected, NodeLayout actual, List<string> differences)
	{
		if (expected.GetProperty("type").GetString() != actual.Type)
		{
			differences.Add($"{id}: type {expected.GetProperty("type").GetString()} drawn as {actual.Type}");
		}

		var box = expected.GetProperty("box");
		Near(id, "box.x", box.GetProperty("x").GetDouble(), actual.Box.X, differences);
		Near(id, "box.y", box.GetProperty("y").GetDouble(), actual.Box.Y, differences);
		Near(id, "box.width", box.GetProperty("width").GetDouble(), actual.Box.Width, differences);
		Near(id, "box.height", box.GetProperty("height").GetDouble(), actual.Box.Height, differences);
		var texts = expected.TryGetProperty("texts", out var list) ? list.EnumerateArray().ToList() : [];
		if (texts.Count != actual.Texts.Count)
		{
			differences.Add($"{id}: {texts.Count} texts drawn as {actual.Texts.Count}");
			return;
		}

		for (var index = 0; index < texts.Count; index++)
		{
			var text = texts[index];
			var other = actual.Texts[index];
			var at = $"{id} text {index}";
			if (text.GetProperty("text").GetString() != other.Text)
			{
				differences.Add($"{at}: text \"{text.GetProperty("text").GetString()}\" drawn as \"{other.Text}\"");
			}

			Near(at, "naturalWidth", text.GetProperty("naturalWidth").GetDouble(), other.NaturalWidth, differences);
			Near(at, "givenWidth", text.GetProperty("givenWidth").GetDouble(), other.GivenWidth, differences);
			var truncated = text.GetProperty("truncated");
			if (truncated.GetProperty("x").GetBoolean() != other.TruncatedX)
			{
				differences.Add($"{at}: truncated.x {truncated.GetProperty("x").GetBoolean()}"
					+ $" drawn as {other.TruncatedX}");
			}

			if (truncated.GetProperty("y").GetBoolean() != other.TruncatedY)
			{
				differences.Add($"{at}: truncated.y {truncated.GetProperty("y").GetBoolean()}"
					+ $" drawn as {other.TruncatedY}");
			}
		}
	}

	private static void Near(string at, string field, double expected, double actual, List<string> differences)
	{
		if (!(Math.Abs(expected - actual) <= Tolerance))
		{
			differences.Add(string.Create(CultureInfo.InvariantCulture,
				$"{at}: {field} {expected} drawn as {Math.Round(actual, 3)}"));
		}
	}

	private static string CaseKey(JsonElement testCase)
	{
		var tile = testCase.GetProperty("tile");
		return string.Create(CultureInfo.InvariantCulture,
			$"{tile.GetProperty("width").GetDouble()}x{tile.GetProperty("height").GetDouble()}"
			+ $"@{testCase.GetProperty("basis").GetDouble()}");
	}
}
