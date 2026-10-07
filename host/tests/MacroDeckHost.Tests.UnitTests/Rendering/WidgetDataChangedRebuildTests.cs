using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Widgets.Ui;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class WidgetDataChangedRebuildTests
{
	[Test]
	public async Task A_save_rebuilds_the_widget_unless_every_open_session_took_it_on_itself()
	{
		var signals = new WidgetRenderSignals();
		var sessions = new RecordingUiSessionBroker();
		var handler = new WidgetUpdatedNotificationHandler(new SliderWidgetSessionTests.NullUiTransport(),
			new LabelRenderChannel(),
			signals,
			sessions,
			TestColors.None);
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), Type = WidgetTypeIds.Slider, Data = "{}"
		};
		var key = widget.Id.ToString();

		using (signals.SubscribeDataChanged(key, _ => { }))
		using (signals.SubscribeDataChanged(key, (WidgetEntity _) => true))
		{
			await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);
		}

		var rebuiltWhenAllTookIt = sessions.InvalidatedWidgets.ToList();

		using (signals.SubscribeDataChanged(key, _ => { }))
		using (signals.SubscribeDataChanged(key, (WidgetEntity _) => false))
		{
			await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);
		}

		Assert.Multiple(() =>
		{
			Assert.That(rebuiltWhenAllTookIt, Is.Empty);
			Assert.That(sessions.InvalidatedWidgets, Is.EqualTo(new[] { widget.Id }));
		});
	}

	[Test]
	public async Task A_saved_colour_reference_reaches_open_sessions_resolved()
	{
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "primary",
			Scope = VariableScope.Global,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = "#3366ff"
		});
		var signals = new WidgetRenderSignals();
		var handler = new WidgetUpdatedNotificationHandler(new SliderWidgetSessionTests.NullUiTransport(),
			new LabelRenderChannel(),
			signals,
			new RecordingUiSessionBroker(),
			new ColorReferenceResolver(registry));
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = Guid.NewGuid(),
			Type = WidgetTypeIds.Slider,
			Data = """{"color":"{{ vars.primary | color | color_opacity: 50 }}"}"""
		};
		WidgetEntity? received = null;

		using (signals.SubscribeDataChanged(widget.Id.ToString(), entity =>
		{
			received = entity;
			return true;
		}))
		{
			await handler.Handle(new WidgetUpdatedNotification(widget), CancellationToken.None);
		}

		Assert.That(JsonNode.Parse(received!.Data!)!["color"]!.GetValue<string>(), Is.EqualTo("#3366ff80"));
	}
}
