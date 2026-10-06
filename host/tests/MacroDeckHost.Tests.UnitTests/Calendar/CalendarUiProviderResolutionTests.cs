using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
internal sealed class CalendarUiProviderResolutionTests : UiSessionFixture
{
	private CalendarWidgetHarness _harness = null!;
	private string _providerId = null!;

	[SetUp]
	public async Task SetUpCalendar()
	{
		var planning = Event("planning", Noon.AddHours(1), TimeSpan.FromHours(1), title: "Planning");
		_harness = new CalendarWidgetHarness().With(planning);
		_harness.Google.Details["planning"] = planning;
		await _harness.SyncAsync();

		var registry = TestWidgetTypeProviders.Registry();
		await new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None).StartAsync(new CalendarIntegration());

		Assert.That(registry.TryResolve(CalendarWidgetTypes.QualifiedId, out var calendar), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(registry.All.Where(entry => entry.ProviderId == calendar.ProviderId).Select(entry => entry.WidgetTypeId),
				Is.EqualTo(new[] { CalendarWidgetTypes.QualifiedId }),
				"one calendar widget type, whose layout is a setting");
			Assert.That(calendar.IsBuiltIn, Is.False, "a built-in type would be served by a synthetic provider id");
			Assert.That(calendar.Descriptor.HasConfiguration, Is.True);
		});
		_providerId = calendar.ProviderId;

		Integrations.Add(new CalendarIntegration());

		Resolver.Fallback = new UiSessionProviderResolver(
			new RemoteUiProviderRegistry(new EmptyRemotePluginSnapshotStore(), Invoker),
			new UiProviderRegistry(Integrations, () => Broker, Serilog.Core.Logger.None),
			new ConfigFlowUiProviderRegistry(() => Broker, Serilog.Core.Logger.None),
			new ActionConfigUiProviderRegistry(Integrations, () => Broker, Serilog.Core.Logger.None),
			new WidgetUiProviderRegistry(new EmptyFolderCache(), [], () => Broker, Serilog.Core.Logger.None),
			new IntegrationUiProviderRegistry([_harness.Provider], () => Broker, Serilog.Core.Logger.None),
			new UiPreviewProviderRegistry([], () => Broker, Serilog.Core.Logger.None));
	}

	[Test]
	public async Task A_placed_widget_opens_in_either_layout()
	{
		var agenda = await OpenTreeAsync(CalendarWidgetHarness.WidgetSurface(CalendarWidgetTypes.QualifiedId,
			CalendarWidgetHarness.WithLayout(CalendarWidgetTypes.LayoutAgenda, new { days = 2 })));
		var nextEvent = await OpenTreeAsync(CalendarWidgetHarness.WidgetSurface(
			CalendarWidgetTypes.QualifiedId,
			CalendarWidgetHarness.WithLayout(CalendarWidgetTypes.LayoutNextEvent, new { })));

		Assert.Multiple(() =>
		{
			Assert.That(agenda, Does.Contain("Planning"));
			Assert.That(nextEvent, Does.Contain("Planning"));
		});
	}

	[Test]
	public async Task The_picker_sample_opens_for_both_layouts()
	{
		var agenda = await OpenTreeAsync(CalendarWidgetHarness.WidgetSurface(CalendarWidgetTypes.QualifiedId,
			new { }, sample: true));
		var nextEvent = await OpenTreeAsync(CalendarWidgetHarness.WidgetSurface(
			CalendarWidgetTypes.QualifiedId,
			CalendarWidgetHarness.WithLayout(CalendarWidgetTypes.LayoutNextEvent, new { }), sample: true));

		Assert.Multiple(() =>
		{
			Assert.That(agenda, Does.Not.Contain("Planning"));
			Assert.That(nextEvent, Does.Not.Contain("Planning"));
		});
	}

	[Test]
	public async Task The_configuration_opens_with_the_fields_of_both_layouts()
	{
		var config = await OpenTreeAsync(CalendarWidgetHarness.ConfigSurface(CalendarWidgetTypes.QualifiedId, new { }));

		Assert.Multiple(() =>
		{
			Assert.That(config, Does.Contain($"\"{CalendarWidgetTypes.LayoutKey}\""));
			Assert.That(config, Does.Contain($"\"{CalendarWidgetTypes.DaysKey}\""));
			Assert.That(config, Does.Contain($"\"{CalendarWidgetTypes.WhenStartedKey}\""));
		});
	}

	[Test]
	public async Task The_event_dialog_opens()
	{
		var dialog = await OpenTreeAsync(CalendarWidgetHarness.DialogSurface(CalendarWidgetHarness.CalendarKey(),
			"planning"));

		Assert.That(dialog, Does.Contain("Widgets.Calendar.Details.TimedRange"));
	}

	[Test]
	public async Task The_agenda_dialog_opens()
	{
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda, "{}");

		var dialog = await OpenTreeAsync(CalendarWidgetHarness.AgendaDialogSurface(widget.Id));

		Assert.That(dialog, Does.Contain("Planning"));
	}

	[Test]
	public async Task A_surface_for_another_widget_type_is_declined()
	{
		var ticket = await Broker.OpenAsync(_providerId,
			CalendarWidgetHarness.WidgetSurface("app.macro-deck.calendar::other", new { }),
			DeviceA,
			CancellationToken.None);

		Assert.That(ticket.Accepted, Is.False);
	}

	private async Task<string> OpenTreeAsync(UiSurface surface)
	{
		var ticket = await Broker.OpenAsync(_providerId, surface, DeviceA, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);

		var tree = await Broker.FirstTreeAsync(ticket.SessionId, CancellationToken.None);
		Assert.That(tree, Is.Not.Null, "the session produced no tree");

		using var document = JsonDocument.Parse(tree!.Value.Utf8);
		return document.RootElement.GetRawText();
	}
}
