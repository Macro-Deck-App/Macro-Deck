using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UiThresholdsTests
{
	private static readonly UiThresholds _fourBands = new([
		new UiThresholdBand("green", "#34c759"),
		new UiThresholdBand("yellow", "#ffcc00", 26),
		new UiThresholdBand("orange", "#ff9500", 63),
		new UiThresholdBand("red", "#ff3b30", 90),
	]);

	private static JsonElement Fixture()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

		while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ui-model", "fixtures")))
		{
			directory = directory.Parent;
		}

		Assert.That(directory, Is.Not.Null, "the repository root carrying ui-model/fixtures was not found");

		var path = Path.Combine(directory!.FullName, "ui-model", "fixtures", "thresholds", "threshold-values.json");

		return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
	}

	private static IEnumerable<TestCaseData> Cases(string kind)
		=> Fixture()
			.GetProperty(kind)
			.EnumerateArray()
			.Select(item => new TestCaseData(item).SetName($"{kind}: {item.GetProperty("name").GetString()}"));

	private static IEnumerable<TestCaseData> ValidCases() => Cases("valid");

	private static IEnumerable<TestCaseData> InvalidCases() => Cases("invalid");

	[TestCaseSource(nameof(ValidCases))]
	public void A_shared_valid_case_parses_and_writes_its_normalised_form(JsonElement testCase)
	{
		var parsed = UiThresholds.TryParse(testCase.GetProperty("value"), out var thresholds);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(JsonElement.DeepEquals(UiCanonicalJson.ToElement(thresholds), testCase.GetProperty("normalized")),
				Is.True,
				UiCanonicalJson.Serialize(thresholds));
		});
	}

	[TestCaseSource(nameof(InvalidCases))]
	public void A_shared_invalid_case_is_refused_without_throwing(JsonElement testCase)
	{
		Assert.That(UiThresholds.TryParse(testCase.GetProperty("value"), out var thresholds), Is.False);
		Assert.That(thresholds, Is.Null);
	}

	[Test]
	public void More_than_sixty_four_bands_are_refused()
	{
		var bands = Enumerable.Range(0, 65)
			.Select(i => new UiThresholdBand($"b{i}", "#000000", i == 0 ? null : i))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(() => new UiThresholds(bands), Throws.ArgumentException);
			Assert.That(UiThresholds.TryParse(JsonSerializer.SerializeToElement(new { bands }, UiCanonicalJson.Options),
				out _), Is.False);
		});
	}

	[Test]
	public void An_invalid_list_built_in_code_throws_at_construction()
		=> Assert.That(() => new UiThresholds([new UiThresholdBand("a", "#000", 5)]), Throws.ArgumentException);

	[TestCase(-1000, "green")]
	[TestCase(0, "green")]
	[TestCase(25.99, "green")]
	[TestCase(26, "yellow")]
	[TestCase(89.9, "orange")]
	[TestCase(90, "red")]
	[TestCase(1e9, "red")]
	public void A_value_falls_in_the_band_that_starts_at_or_below_it(double value, string expected)
		=> Assert.That(_fourBands.BandAt(value)?.Id, Is.EqualTo(expected));

	[Test]
	public void A_band_without_a_colour_keeps_its_place_and_answers_no_colour()
	{
		var thresholds = new UiThresholds([
			new UiThresholdBand("ok", "#34c759"),
			new UiThresholdBand("gone", "", 50),
			new UiThresholdBand("bad", "#ff3b30", 90),
		]);

		Assert.Multiple(() =>
		{
			Assert.That(thresholds.BandAt(60)?.Id, Is.EqualTo("gone"));
			Assert.That(thresholds.ColorAt(60), Is.Null);
			Assert.That(thresholds.ColorAt(95), Is.EqualTo("#ff3b30"));
		});
	}

	[TestCase("{{ vars.primary | color | color_reduce_opacity: 20 | color_increase_opacity: 5 }}", true)]
	[TestCase("{{ vars.primary | color | color_mix: \"#fff\", 50 }}", true)]
	[TestCase("{{ vars.primary | color | upcase }}", false)]
	[TestCase("{{ vars.primary | color | color_darken }}", false)]
	[TestCase("{{ vars.Primary | color }}", false)]
	[TestCase("{{ vars.primary }}", false)]
	public void A_band_colour_may_be_a_colour_reference_in_the_hosts_grammar_only(string color, bool valid)
		=> Assert.That(() => new UiThresholds([new UiThresholdBand("a", color)]),
			valid ? Throws.Nothing : Throws.ArgumentException);

	[Test]
	public void NaN_falls_in_no_band()
		=> Assert.That(_fourBands.ColorAt(double.NaN), Is.Null);

	[Test]
	public void Two_values_with_the_same_bands_are_equal()
	{
		var copy = new UiThresholds([.. _fourBands.Bands]);

		Assert.Multiple(() =>
		{
			Assert.That(copy, Is.EqualTo(_fourBands));
			Assert.That(copy.GetHashCode(), Is.EqualTo(_fourBands.GetHashCode()));
			Assert.That(copy, Is.Not.EqualTo(new UiThresholds([new UiThresholdBand("green", "#34c759")])));
		});
	}

	private static UiView View(UiThresholdsInput input)
		=> new(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			new UiFlow { Key = "setup", Children = [input] });

	private static UiDispatchResult Change(UiView view, UiThresholds? value)
		=> view.Dispatch(new UiEvent
		{
			NodeId = "thresholds",
			Name = UiConfigEvents.Change,
			Data = value is null ? JsonDocument.Parse("null").RootElement : UiCanonicalJson.ToElement(value),
		});

	private static UiThresholds Without(UiThresholds value, string id)
	{
		var bands = value.Bands.Where(band => band.Id != id).ToList();

		return new UiThresholds(bands);
	}

	private static UiThresholds Recoloured(UiThresholds value, string id, string color)
		=> new([.. value.Bands.Select(band => band.Id == id ? band with { Color = color } : band)]);

	private static UiThresholds Added(UiThresholds value)
		=> new([.. value.Bands, new UiThresholdBand("purple", "#af52de", 95)]);

	[Test]
	public void A_change_writes_the_typed_value_into_the_binding()
	{
		var state = new UiState<UiThresholds>(null!);
		var view = View(new UiThresholdsInput { Key = "thresholds", Binding = Bind.To(state) });

		var result = Change(view, _fourBands);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True, result.Reason);
			Assert.That(state.Peek(), Is.EqualTo(_fourBands));
		});
	}

	[Test]
	public void A_change_whose_bands_cross_is_rejected_before_it_reaches_the_binding()
	{
		var state = new UiState<UiThresholds>(_fourBands);
		var view = View(new UiThresholdsInput { Key = "thresholds", Binding = Bind.To(state) });

		var result = view.Dispatch(new UiEvent
		{
			NodeId = "thresholds",
			Name = UiConfigEvents.Change,
			Data = JsonDocument.Parse(
					"""{"bands":[{"id":"a","color":"#000000"},{"id":"b","color":"#ffffff","from":50},{"id":"c","color":"#ff0000","from":20}]}""")
				.RootElement,
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(state.Peek(), Is.EqualTo(_fourBands));
		});
	}

	[Test]
	public void A_reset_writes_null_so_the_defaults_apply_again()
	{
		var state = new UiState<UiThresholds>(_fourBands);
		var view = View(new UiThresholdsInput
		{
			Key = "thresholds", Binding = Bind.To(state), DefaultValue = _fourBands, SupportsReset = true,
		});

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, null).IsAccepted, Is.True);
			Assert.That(state.Peek(), Is.Null);
		});
	}

	[Test]
	public void A_disabled_editor_accepts_no_change_not_even_a_reset()
	{
		var state = new UiState<UiThresholds>(_fourBands);
		var view = View(new UiThresholdsInput { Key = "thresholds", Binding = Bind.To(state), Disabled = true });

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, Recoloured(_fourBands, "red", "#000000")).Outcome,
				Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, null).Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(state.Peek(), Is.EqualTo(_fourBands));
		});
	}

	[Test]
	public void A_fixed_count_keeps_the_default_number_of_bands_but_allows_moves_and_recolours()
	{
		var state = new UiState<UiThresholds>(null!);
		var view = View(new UiThresholdsInput
		{
			Key = "thresholds", Binding = Bind.To(state), DefaultValue = _fourBands, FixedCount = true,
		});
		var moved = new UiThresholds([.. _fourBands.Bands.Select(band => band.Id == "red" ? band with { From = 80 } : band)]);

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, Added(_fourBands)).Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, Without(_fourBands, "orange")).Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, moved).IsAccepted, Is.True);
			Assert.That(Change(view, Recoloured(moved, "red", "#000000")).IsAccepted, Is.True);
		});
	}

	[Test]
	public void A_fixed_count_is_the_developer_default_even_when_the_stored_value_has_another()
	{
		var state = new UiState<UiThresholds>(Added(_fourBands));
		var view = View(new UiThresholdsInput
		{
			Key = "thresholds", Binding = Bind.To(state), DefaultValue = _fourBands, FixedCount = true,
		});

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, Recoloured(Added(_fourBands), "red", "#000000")).Outcome,
				Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, _fourBands).IsAccepted, Is.True);
		});
	}

	[Test]
	public void Fixed_colours_allow_moves_and_removals_but_no_recolour_and_no_new_band()
	{
		var state = new UiState<UiThresholds>(null!);
		var view = View(new UiThresholdsInput
		{
			Key = "thresholds", Binding = Bind.To(state), DefaultValue = _fourBands, FixedColors = true,
		});

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, Recoloured(_fourBands, "red", "#000000")).Outcome,
				Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, Added(_fourBands)).Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(Change(view, Without(_fourBands, "orange")).IsAccepted, Is.True);
		});
	}

	[Test]
	public void A_count_cap_refuses_only_a_change_that_adds_beyond_it()
	{
		var overCap = Added(_fourBands);
		var state = new UiState<UiThresholds>(overCap);
		var view = View(new UiThresholdsInput { Key = "thresholds", Binding = Bind.To(state), MaxCount = 4 });
		var overCapMoved = new UiThresholds([.. overCap.Bands.Select(band => band.Id == "purple" ? band with { From = 97 } : band)]);

		Assert.Multiple(() =>
		{
			Assert.That(Change(view, overCapMoved).IsAccepted, Is.True, "an existing over-cap value stays editable");
			Assert.That(Change(view, _fourBands).IsAccepted, Is.True);
			Assert.That(Change(view, Added(_fourBands)).Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
		});
	}
}
