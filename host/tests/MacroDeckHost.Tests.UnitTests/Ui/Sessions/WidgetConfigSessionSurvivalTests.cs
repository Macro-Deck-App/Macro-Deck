using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// A widget's editor and the tiles drawing that widget are separate sessions, and the two things that can
/// happen to a widget want opposite treatment of the editor: reconfiguring it comes <i>from</i> the editor,
/// deleting it takes the editor with it.
/// </summary>
[TestFixture]
internal sealed class WidgetConfigSessionSurvivalTests : UiSessionFixture
{
	private static readonly Guid _widgetId = Guid.NewGuid();

	[Test]
	public async Task Reconfiguring_a_widget_redraws_its_tiles_and_leaves_its_editor_open()
	{
		AddProvider();
		var tile = await OpenAsync(WidgetSurface());
		var editor = await OpenAsync(ConfigSurface());

		Broker.InvalidateWidgetSessions(_widgetId);

		Assert.Multiple(() =>
		{
			Assert.That(Registry.Find(tile),
				Is.Null,
				"A tile drawing the reconfigured widget has to be built again.");
			Assert.That(Registry.Find(editor)?.State,
				Is.EqualTo(UiSessionState.Open),
				"The save that reconfigured the widget came from this editor - tearing it down mid-edit is " +
				"exactly what the invalidation must not do.");
		});
	}

	[Test]
	public async Task Deleting_a_widget_closes_its_editor_as_well_as_its_tiles()
	{
		AddProvider();
		var tile = await OpenAsync(WidgetSurface());
		var editor = await OpenAsync(ConfigSurface());

		Broker.CloseWidgetSessions(_widgetId, "the widget was deleted");

		Assert.Multiple(() =>
		{
			Assert.That(Registry.Find(tile), Is.Null);
			Assert.That(Registry.Find(editor),
				Is.Null,
				"There is nothing left to configure, so the editor must not be left holding a dead widget.");
		});
	}

	private async Task<string> OpenAsync(UiSurface surface)
	{
		var ticket = await Broker.OpenAsync(ProviderId, surface, DeviceA, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, "The session did not open.");
		return ticket.SessionId;
	}

	private static UiSurface WidgetSurface()
		=> new()
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString())
			}
		};

	private static UiSurface ConfigSurface()
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(_widgetId.ToString())
			}
		};
}
