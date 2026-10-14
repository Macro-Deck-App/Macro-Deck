using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Ui.Sessions;

internal static class UiEventFeedback
{
	private static readonly Dictionary<string, string> _triggerTypes = new(StringComparer.Ordinal)
	{
		[UiComponentEvents.Press] = WidgetTriggerTypes.ShortPress,
		[UiComponentEvents.LongPress] = WidgetTriggerTypes.LongPress,
		[UiComponentEvents.DoublePress] = WidgetTriggerTypes.DoublePress
	};

	public static ActionExecutionStatusEvent Failed(
		string code,
		LocalizedText message,
		UiSurface? surface,
		string? eventName)
	{
		return new ActionExecutionStatusEvent
		{
			ExecutionId = Guid.NewGuid().ToString(),
			Status = ActionExecutionStatus.Failed,
			WidgetId = WidgetIdOf(surface),
			TriggerType = eventName is null ? null : _triggerTypes.GetValueOrDefault(eventName),
			Error = new TransportError { Code = code, Message = message }
		};
	}

	private static string? WidgetIdOf(UiSurface? surface)
	{
		return surface is { Kind: UiSurfaceKinds.Widget }
			&& surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetId, out var id)
			&& id.ValueKind == JsonValueKind.String
				? id.GetString()
				: null;
	}
}
