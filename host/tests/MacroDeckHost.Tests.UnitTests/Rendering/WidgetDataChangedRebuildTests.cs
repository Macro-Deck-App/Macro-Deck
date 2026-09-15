using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;
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
			sessions);
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
}
