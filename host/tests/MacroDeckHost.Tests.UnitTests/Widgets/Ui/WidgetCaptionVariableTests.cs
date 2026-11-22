using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Clock;
using MacroDeckHost.Widgets.Gauges;
using MacroDeckHost.Widgets.HistoryGraph;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

public class WidgetCaptionVariableTests
{
	private VariableRegistry _variables = null!;
	private VariableChangeNotifier _notifier = null!;

	[SetUp]
	public void SetUp()
	{
		_variables = new VariableRegistry();
		_notifier = new VariableChangeNotifier();
	}

	[Test]
	public async Task A_clock_caption_shows_the_variables_value_and_follows_its_changes()
	{
		var day = Variable("system_day_of_week", "Sunday");
		await using var session = await OpenClockAsync(UiSurfaceKinds.Widget,
			UiWidgetSurfaceAttributes.WidgetId,
			Guid.NewGuid(),
			"{{ vars.system_day_of_week }}");
		var initial = TextOf(session, "caption");

		day.Value = "Monday";
		_notifier.Publish("system_day_of_week");

		Assert.Multiple(() =>
		{
			Assert.That(initial, Is.EqualTo("Sunday"));
			Assert.That(TextOf(session, "caption"), Is.EqualTo("Monday"));
		});
	}

	[Test]
	public async Task A_clock_preview_resolves_a_widget_scoped_variable_in_the_widgets_own_scope()
	{
		var widgetId = Guid.NewGuid();
		Variable("room", "Office", VariableScope.Widget, widgetId.ToString());
		Variable("room", "Global room");

		await using var session = await OpenClockAsync(UiSurfaceKinds.Preview,
			UiWidgetSurfaceAttributes.VariableScopeWidgetId,
			widgetId,
			"Room {{ vars.room }}");

		Assert.That(TextOf(session, "caption"), Is.EqualTo("Room Office"));
	}

	[Test]
	public async Task A_plain_clock_caption_is_shown_verbatim()
	{
		await using var session = await OpenClockAsync(UiSurfaceKinds.Widget,
			UiWidgetSurfaceAttributes.WidgetId,
			Guid.NewGuid(),
			"Office");

		Assert.That(TextOf(session, "caption"), Is.EqualTo("Office"));
	}

	[Test]
	public async Task A_broken_clock_caption_template_shows_its_text_and_keeps_the_clock_running()
	{
		await using var session = await OpenClockAsync(UiSurfaceKinds.Widget,
			UiWidgetSurfaceAttributes.WidgetId,
			Guid.NewGuid(),
			"{% if %}{{ vars.room }}");

		Assert.Multiple(() =>
		{
			Assert.That(TextOf(session, "caption"), Is.EqualTo("{% if %}{{ vars.room }}"));
			Assert.That(Walk(session.BuildTree().Root).Count(), Is.GreaterThan(1));
		});
	}

	[Test]
	public async Task A_closed_clock_no_longer_follows_the_variables_in_its_caption()
	{
		var day = Variable("system_day_of_week", "Sunday");
		var session = await OpenClockAsync(UiSurfaceKinds.Widget,
			UiWidgetSurfaceAttributes.WidgetId,
			Guid.NewGuid(),
			"{{ vars.system_day_of_week }}");
		await session.DisposeAsync();

		day.Value = "Monday";
		_notifier.Publish("system_day_of_week");

		Assert.That(TextOf(session, "caption"), Is.EqualTo("Sunday"));
	}

	[Test]
	public async Task A_gauges_title_shows_the_variables_value_and_follows_its_changes()
	{
		var host = Variable("host_name", "Studio");
		var provider = new GaugesWidgetUiProvider(_variables,
			_notifier,
			new SliderWidgetConfigTests.FakeWidgetIconResources(),
			TestLocalization.SampleText);
		await using var session = await OpenAsync(provider, new { title = "{{ vars.host_name }}" });
		var initial = TextOf(session, "title");

		host.Value = "Stage";
		_notifier.Publish("host_name");

		Assert.Multiple(() =>
		{
			Assert.That(initial, Is.EqualTo("Studio"));
			Assert.That(TextOf(session, "title"), Is.EqualTo("Stage"));
		});
	}

	[Test]
	public async Task A_history_graph_title_shows_the_variables_value_and_follows_its_changes()
	{
		var host = Variable("host_name", "Studio");
		var provider = new HistoryGraphWidgetUiProvider(_variables,
			new NullVariableHistory(),
			_notifier,
			TestLocalization.SampleText,
			new WidgetRenderSignals());
		await using var session = await OpenAsync(provider, new { title = "CPU {{ vars.host_name }}" });
		var initial = TextOf(session, "title");

		host.Value = "Stage";
		_notifier.Publish("host_name");

		Assert.Multiple(() =>
		{
			Assert.That(initial, Is.EqualTo("CPU Studio"));
			Assert.That(TextOf(session, "title"), Is.EqualTo("CPU Stage"));
		});
	}

	private VariableEntity Variable(
		string name,
		string value,
		VariableScope scope = VariableScope.Global,
		string? scopeRefId = null)
	{
		var variable = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = DomainVariableType.Text,
			Classification = VariableClassification.User,
			Value = value,
			UpdatedAt = DateTime.UtcNow
		};
		_variables.Upsert(variable);

		return variable;
	}

	private Task<IUiSession> OpenClockAsync(string kind, string widgetIdAttribute, Guid widgetId, string label)
	{
		return OpenAsync(new ClockWidgetUiProvider(_variables, _notifier),
			new { label, showLabel = true },
			kind,
			widgetIdAttribute,
			widgetId);
	}

	private static async Task<IUiSession> OpenAsync(
		IBuiltInWidgetUiProvider provider,
		object data,
		string kind = UiSurfaceKinds.Widget,
		string widgetIdAttribute = UiWidgetSurfaceAttributes.WidgetId,
		Guid? widgetId = null)
	{
		var surface = new UiSurface
		{
			Kind = kind,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data),
				[widgetIdAttribute] = JsonSerializer.SerializeToElement((widgetId ?? Guid.NewGuid()).ToString())
			}
		};

		return (await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None))!;
	}

	private static string? TextOf(IUiSession session, string key)
	{
		return Walk(session.BuildTree().Root)
			.Single(node => node.Id == key || node.Id.EndsWith("." + key, StringComparison.Ordinal))
			.Properties["text"]
			.GetString();
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		return new[] { node }
			.Concat(node.Children.SelectMany(Walk))
			.Concat(node.Fallback is null ? [] : Walk(node.Fallback));
	}

	private sealed class NullVariableHistory : IVariableHistory
	{
		public IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null)
		{
			return EmptyVariableHistoryWindow.Instance;
		}
	}
}
