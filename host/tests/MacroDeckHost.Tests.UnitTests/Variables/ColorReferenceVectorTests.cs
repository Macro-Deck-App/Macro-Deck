using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ColorReferenceVectorTests
{
	private static readonly JsonElement _fixture = Load();

	private static JsonElement Load()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ui-model", "fixtures")))
		{
			directory = directory.Parent;
		}

		var path = Path.Combine(directory!.FullName, "ui-model", "fixtures", "colors", "color-references.json");
		return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
	}

	private static IEnumerable<TestCaseData> Cases(string section)
		=> _fixture.GetProperty(section)
			.EnumerateArray()
			.Select((vector, index) => new TestCaseData(vector).SetName($"{section}[{index}] {vector.GetRawText()}"));

	private static IEnumerable<TestCaseData> ParseCases() => Cases("parse");

	private static IEnumerable<TestCaseData> ModifierCases() => Cases("modifiers");

	private static IEnumerable<TestCaseData> ChainCases() => Cases("chains");

	private static IEnumerable<TestCaseData> ReferenceCases() => Cases("references");

	[TestCaseSource(nameof(ParseCases))]
	public void Parses_to_the_canonical_form(JsonElement vector)
	{
		var expected = vector.GetProperty("canonical") is { ValueKind: JsonValueKind.String } canonical
			? canonical.GetString()
			: null;

		Assert.That(RgbaColor.Canonicalize(vector.GetProperty("input").GetString()), Is.EqualTo(expected));
	}

	[TestCaseSource(nameof(ModifierCases))]
	public void Applies_a_single_modifier(JsonElement vector)
	{
		var color = RgbaColor.Parse(vector.GetProperty("color").GetString())!.Value;

		Assert.That(Apply(color, vector).ToString(), Is.EqualTo(vector.GetProperty("expected").GetString()));
	}

	[TestCaseSource(nameof(ChainCases))]
	public void Applies_modifiers_in_order(JsonElement vector)
	{
		var color = RgbaColor.Parse(vector.GetProperty("color").GetString())!.Value;
		foreach (var step in vector.GetProperty("steps").EnumerateArray())
		{
			color = Apply(color, step);
		}

		Assert.That(color.ToString(), Is.EqualTo(vector.GetProperty("expected").GetString()));
	}

	[TestCaseSource(nameof(ReferenceCases))]
	public void Recognizes_only_the_reference_grammar(JsonElement vector)
	{
		var recognized = ColorReference.TryParse(vector.GetProperty("text").GetString(), out var reference);

		Assert.That(recognized, Is.EqualTo(vector.GetProperty("isReference").GetBoolean()));
		if (!recognized)
		{
			return;
		}

		var steps = vector.GetProperty("steps").EnumerateArray().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(reference.Variable, Is.EqualTo(vector.GetProperty("variable").GetString()));
			Assert.That(reference.Steps.Select(Describe), Is.EqualTo(steps.Select(Describe)));
		});
	}

	private static RgbaColor Apply(RgbaColor color, JsonElement step)
	{
		var args = step.GetProperty("args");
		return step.GetProperty("op").GetString() switch
		{
			"lighten" => color.Lighten(args[0].GetDouble()),
			"darken" => color.Darken(args[0].GetDouble()),
			"saturate" => color.Saturate(args[0].GetDouble()),
			"desaturate" => color.Desaturate(args[0].GetDouble()),
			"hue" => color.ShiftHue(args[0].GetDouble()),
			"opacity" => color.WithOpacity(args[0].GetDouble()),
			"increase_opacity" => color.IncreaseOpacity(args[0].GetDouble()),
			"reduce_opacity" => color.ReduceOpacity(args[0].GetDouble()),
			"mix" => color.Mix(RgbaColor.Parse(args[0].GetString())!.Value, args[1].GetDouble()),
			var op => throw new InvalidOperationException($"Unknown op {op}")
		};
	}

	private static string Describe(ColorStep step)
		=> step.Modifier == ColorModifier.Mix
			? string.Create(CultureInfo.InvariantCulture,
				$"mix {step.MixVariable ?? step.MixColor?.ToString()} {step.Amount}")
			: string.Create(CultureInfo.InvariantCulture, $"{OpName(step.Modifier)} {step.Amount}");

	private static string OpName(ColorModifier modifier) => modifier switch
	{
		ColorModifier.IncreaseOpacity => "increase_opacity",
		ColorModifier.ReduceOpacity => "reduce_opacity",
		_ => modifier.ToString().ToLowerInvariant()
	};

	private static string Describe(JsonElement step)
	{
		var op = step.GetProperty("op").GetString()!;
		var args = step.GetProperty("args");
		if (op != "mix")
		{
			return string.Create(CultureInfo.InvariantCulture, $"{op} {args[0].GetDouble()}");
		}

		var other = args[0].ValueKind == JsonValueKind.Object
			? args[0].GetProperty("variable").GetString()
			: RgbaColor.Canonicalize(args[0].GetString());
		return string.Create(CultureInfo.InvariantCulture, $"mix {other} {args[1].GetDouble()}");
	}
}
