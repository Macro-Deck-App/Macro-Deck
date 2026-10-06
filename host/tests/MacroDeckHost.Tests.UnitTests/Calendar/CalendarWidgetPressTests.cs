using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Integrations.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Calendar;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarWidgetPressTests
{
	private const string ShortPressFlow = """{"flows":[{"triggerType":"onShortPress","children":[{"type":"action"}]}]}""";

	private CalendarWidgetHarness _harness = null!;
	private CalendarIntegration _integration = null!;
	private WidgetDefaultShortPress _defaults = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new CalendarWidgetHarness()
			.With(Event("running", Noon.AddMinutes(-10), TimeSpan.FromMinutes(30), title: "Running"))
			.With(Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), "team", "Planning"));
		_integration = new CalendarIntegration();
		_integration.UseCalendarServices(new CalendarHostServices(_harness.Cache,
			_harness.Opener,
			_harness.Time,
			_harness.Folders));
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(_integration);
		_defaults = new WidgetDefaultShortPress(() => new WidgetTypeRegistry(new RecordingMediator()),
			integrations,
			TestLocalization.Resolver);
	}

	[TestCase(CalendarWidgetTypes.LayoutAgenda)]
	[TestCase(CalendarWidgetTypes.LayoutNextEvent)]
	public void A_tap_without_a_short_press_flow_runs_show_details_on_screens_only(string layout)
	{
		var type = CalendarWidgetTypes.QualifiedId;
		var widget = Widget(type, CalendarWidgetHarness.WithLayout(layout, "{}"));

		var block = DefaultBlock(_defaults.FlowsSourceFor(widget, fromDevice: false));

		Assert.Multiple(() =>
		{
			Assert.That(block.GetProperty("integrationId").GetString(), Is.EqualTo(CalendarWidgetTypes.OwnerId));
			Assert.That(block.GetProperty("actionId").GetString(), Is.EqualTo("show-details"));
			Assert.That(_defaults.FlowsSourceFor(widget, fromDevice: true), Is.Null);
			Assert.That(_defaults.RunsOnDevices(type), Is.False);
			Assert.That(_defaults.FlowsSourceFor(Widget(type, CalendarWidgetHarness.WithLayout(layout, ShortPressFlow)),
				fromDevice: false), Is.Null);
		});
	}

	[TestCase("{}")]
	[TestCase(ShortPressFlow)]
	public async Task The_agenda_rows_never_claim_a_press_whatever_flows_the_widget_has(string data)
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			JsonDocument.Parse(data).RootElement.Clone());
		var root = session.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(Texts(root), Does.Contain("Planning"));
			Assert.That(Flatten(root).Where(IsPressable), Is.Empty);
		});
	}

	[Test]
	public async Task Show_details_opens_the_event_a_next_event_widget_shows_on_the_pressing_client()
	{
		await _harness.SyncAsync();
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, """{"whenStarted":"next"}""");

		var result = await ShowDetailsAsync(widget.Id.ToString());
		var (client, modal) = await _harness.Interactions.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client, Is.EqualTo(CalendarWidgetHarness.Client));
			Assert.That(modal.ViewId, Is.EqualTo(CalendarUiProvider.DialogViewId));
			Assert.That(modal.Data![CalendarUiProvider.DialogEventKey].GetString(), Is.EqualTo("planning"));
		});
	}

	[TestCase("""{"days":1}""")]
	[TestCase("""{"calendars":["nowhere"]}""")]
	public async Task Show_details_opens_the_agenda_dialog_of_an_agenda_even_without_upcoming_events(string data)
	{
		await _harness.SyncAsync();
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda, data);

		var result = await ShowDetailsAsync(widget.Id.ToString());
		var (client, modal) = await _harness.Interactions.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client, Is.EqualTo(CalendarWidgetHarness.Client));
			Assert.That(modal.ViewId, Is.EqualTo(CalendarUiProvider.AgendaDialogViewId));
			Assert.That(modal.Data![CalendarUiProvider.AgendaDialogWidgetKey].GetString(), Is.EqualTo(widget.Id.ToString()));
		});
	}

	[Test]
	public async Task A_calendar_widget_stored_without_a_layout_is_an_agenda_and_opens_the_agenda_dialog()
	{
		await _harness.SyncAsync();
		var widget = _harness.Folders.Add(CalendarWidgetTypes.QualifiedId, "{}");

		var result = await ShowDetailsAsync(widget.Id.ToString());
		var (_, modal) = await _harness.Interactions.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(modal.ViewId, Is.EqualTo(CalendarUiProvider.AgendaDialogViewId));
		});
	}

	[TestCase(CalendarWidgetTypes.LayoutAgenda)]
	[TestCase(CalendarWidgetTypes.LayoutNextEvent)]
	public async Task Without_a_screen_to_show_it_on_show_details_does_nothing_and_succeeds(string layout)
	{
		await _harness.SyncAsync();
		var widget = _harness.Folders.AddCalendar(layout, "{}");

		var result = await ShowDetailsAsync(widget.Id.ToString(), withUi: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_harness.Interactions.Opened.Task.IsCompleted, Is.False);
		});
	}

	[TestCase("none", ShowDetailsActionDefinition.NotCalendarWidgetCode)]
	[TestCase("button", ShowDetailsActionDefinition.NotCalendarWidgetCode)]
	[TestCase("deleted-agenda", ShowDetailsActionDefinition.WidgetMissingCode)]
	[TestCase("deleted-next-event", ShowDetailsActionDefinition.WidgetMissingCode)]
	[TestCase("next-event-without-event", ShowDetailsActionDefinition.NoEventCode)]
	public async Task Show_details_fails_with_a_message_when_there_is_nothing_of_a_calendar_widget_to_show(
		string owner,
		string code)
	{
		await _harness.SyncAsync();
		var ownerId = owner switch
		{
			"button" => _harness.Folders.Add(WidgetTypeIds.ActionButton, "{}").Id.ToString(),
			"deleted-agenda" => Deleted(CalendarWidgetTypes.LayoutAgenda),
			"deleted-next-event" => Deleted(CalendarWidgetTypes.LayoutNextEvent),
			"next-event-without-event" => _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent,
				$$"""{"calendars":["{{CalendarWidgetHarness.CalendarKey("elsewhere")}}"]}""").Id.ToString(),
			_ => null,
		};

		var result = await ShowDetailsAsync(ownerId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(code));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
			Assert.That(_harness.Interactions.Opened.Task.IsCompleted, Is.False);
		});
	}

	private string Deleted(string layout)
	{
		var widget = _harness.Folders.AddCalendar(layout, "{}");
		_harness.Folders.Remove(widget);

		return widget.Id.ToString();
	}

	private async Task<ActionResult> ShowDetailsAsync(string? ownerWidgetId, bool withUi = true)
	{
		var action = _integration.Actions.Single(a => a.Id == "show-details");

		return await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			OriginClientId = CalendarWidgetHarness.Client,
			OwnerWidgetId = ownerWidgetId,
			Ui = withUi ? _harness.Interactions : null,
		});
	}

	private static WidgetEntity Widget(string type, string data)
		=> new() { Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), Type = type, Data = data };

	private static JsonElement DefaultBlock(string? source)
	{
		Assert.That(source, Is.Not.Null, "a default should run");
		using var document = JsonDocument.Parse(source!);
		var flow = document.RootElement.GetProperty("flows")[0];
		Assert.That(flow.GetProperty("triggerType").GetString(), Is.EqualTo("onShortPress"));
		return flow.GetProperty("children")[0].Clone();
	}
}
