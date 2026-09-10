using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableTemplateRendererCostTests
{
	private const int Cards = 84;
	private const string Subtitle = "{{ vars.system_cpu_name }}";

	[Test]
	[CancelAfter(120_000)]
	public void A_grid_of_cards_repaints_its_subtitles_as_fast_with_many_unrelated_variables_as_with_none()
	{
		var withNone = MedianPassMilliseconds(unrelatedVariables: 0);
		var withMany = MedianPassMilliseconds(unrelatedVariables: 2000);

		// A ratio with a floor, not a time budget: the requirement is that the cost does not grow with the
		// registry, and a fixed number of milliseconds would fail on a slow or busy machine instead.
		Assert.That(withMany,
			Is.LessThan(Math.Max(withNone * 4, 5)),
			$"one pass over {Cards} cards took {withNone:F2} ms with no other variables and {withMany:F2} ms with 2000");
	}

	private static double MedianPassMilliseconds(int unrelatedVariables)
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable("system_cpu_name", "AMD Ryzen 5 7535H"));

		for (var index = 0; index < unrelatedVariables; index++)
		{
			registry.Upsert(Variable($"unrelated_{index}", index.ToString(CultureInfo.InvariantCulture)));
		}

		var cards = Enumerable.Range(0, Cards)
			.Select(_ => (Renderer: new VariableTemplateRenderer(registry), WidgetId: Guid.NewGuid().ToString()))
			.ToList();

		var passes = new List<double>();
		var stopwatch = new Stopwatch();

		for (var pass = 0; pass < 9; pass++)
		{
			stopwatch.Restart();

			foreach (var (renderer, widgetId) in cards)
			{
				renderer.Render(Subtitle, VariableScope.Widget, widgetId);
			}

			stopwatch.Stop();
			passes.Add(stopwatch.Elapsed.TotalMilliseconds);
		}

		var measured = passes.Skip(2).Order().ToList();
		return measured[measured.Count / 2];
	}

	private static VariableEntity Variable(string name, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = VariableType.Text,
		Classification = VariableClassification.User,
		Value = value,
		UpdatedAt = DateTime.UtcNow,
	};
}
