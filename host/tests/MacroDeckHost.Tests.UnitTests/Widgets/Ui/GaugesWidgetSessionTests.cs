using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Gauges;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class GaugesWidgetSessionTests
{
	private VariableRegistry _variables = null!;
	private VariableChangeNotifier _notifier = null!;
	private GaugesWidgetUiProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_variables = new VariableRegistry();
		_notifier = new VariableChangeNotifier();
		_provider = new GaugesWidgetUiProvider(_variables,
			_notifier,
			new SliderWidgetConfigTests.FakeWidgetIconResources(),
			TestLocalization.SampleText);

		Set("cpu_metric", 10);
		Set("ram_metric", 20);
	}

	[Test]
	public async Task A_variable_change_moves_only_the_gauges_that_show_it()
	{
		await using var session = await Open();
		session.DrainPatches();

		Set("cpu_metric", 75);
		_notifier.Publish("cpu_metric");

		var levels = Levels(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(session.DrainPatches(), Is.Not.Empty);
			Assert.That(levels["a"], Is.EqualTo(0.75));
			Assert.That(levels["b"], Is.EqualTo(0.2));
		});
	}

	[Test]
	public async Task A_change_to_a_variable_named_in_a_gauge_name_updates_that_name()
	{
		SetText("label_text", "before");
		await using var session = await Open(new { id = "a", variable = "cpu_metric", max = 100, name = "{{ vars.label_text }}" });
		session.DrainPatches();

		SetText("label_text", "after");
		_notifier.Publish("label_text");

		Assert.Multiple(() =>
		{
			Assert.That(session.DrainPatches(), Is.Not.Empty);
			Assert.That(Walk(session.BuildTree().Root)
					.Where(node => node.Type == UiComponents.Text)
					.Select(node => node.Properties.TryGetValue(UiComponentProperties.Text, out var text) ? text.ToString() : null),
				Has.Some.Contains("after"));
		});
	}

	[Test]
	public async Task A_change_to_an_unrelated_variable_sends_nothing()
	{
		await using var session = await Open();
		session.DrainPatches();

		Set("other_metric", 5);
		_notifier.Publish("other_metric");

		Assert.That(session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task A_disposed_session_no_longer_follows_its_variables()
	{
		var session = await Open();
		await session.DisposeAsync();
		session.DrainPatches();

		Set("cpu_metric", 99);
		_notifier.Publish("cpu_metric");

		Assert.That(session.DrainPatches(), Is.Empty);
	}

	private Task<IUiSession> Open()
		=> Open(new { id = "a", variable = "cpu_metric", max = 100 }, new { id = "b", variable = "ram_metric", max = 100 });

	private async Task<IUiSession> Open(params object[] gauges)
	{
		var data = new { gauges };

		var session = await _provider.CreateSessionAsync(new UiSessionRequest
			{
				UiModelVersion = 1,
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data),
					},
				},
			},
			CancellationToken.None);

		return session!;
	}

	private void Set(string name, double value)
	{
		var existing = _variables.FindByName(VariableScope.Global, null, name);

		_variables.Upsert(new VariableEntity
		{
			Id = existing?.Id ?? Guid.NewGuid(),
			Name = name,
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = value.ToString(CultureInfo.InvariantCulture),
			DecimalPlaces = 0,
			UpdatedAt = DateTime.UtcNow,
		});
	}

	private void SetText(string name, string value)
	{
		var existing = _variables.FindByName(VariableScope.Global, null, name);

		_variables.Upsert(new VariableEntity
		{
			Id = existing?.Id ?? Guid.NewGuid(),
			Name = name,
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = value,
			UpdatedAt = DateTime.UtcNow,
		});
	}

	private static Dictionary<string, double> Levels(UiNode root)
		=> Walk(root)
			.Where(node => node.Type == UiComponents.Gauge && !node.Id.Contains("_fallback", StringComparison.Ordinal))
			.GroupBy(node => node.Id.Contains(".g0.", StringComparison.Ordinal) ? "a" : "b")
			.ToDictionary(group => group.Key,
				group => group.Select(node => node.Properties[UiComponentProperties.Level].GetDouble()).Distinct().Single());

	private static IEnumerable<UiNode> Walk(UiNode node)
		=> new[] { node }
			.Concat(node.Children.SelectMany(Walk))
			.Concat(node.Fallback is null ? [] : Walk(node.Fallback));
}
