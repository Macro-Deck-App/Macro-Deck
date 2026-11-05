using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VirtualProfilesChangedHandler : INotificationHandler<VirtualProfilesChangedNotification>
{
	private readonly UiSessionRegistry _sessions;
	private readonly WidgetUiProviderRegistry _widgetUiProviders;

	public VirtualProfilesChangedHandler(UiSessionRegistry sessions, WidgetUiProviderRegistry widgetUiProviders)
	{
		_sessions = sessions;
		_widgetUiProviders = widgetUiProviders;
	}

	public ValueTask Handle(VirtualProfilesChangedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);

		var previous = WidgetsById(notification.IntegrationId, notification.Previous);
		var current = WidgetsById(notification.IntegrationId, notification.Current);

		foreach (var (widgetId, before) in previous)
		{
			if (!current.TryGetValue(widgetId, out var after))
			{
				Close(widgetId);
			}
			else if (!string.Equals(before.Type, after.Type, StringComparison.Ordinal))
			{
				_widgetUiProviders.EvictWidget(widgetId);
				Invalidate(widgetId);
			}
			else if (before != after)
			{
				Invalidate(widgetId);
			}
		}

		return ValueTask.CompletedTask;
	}

	private static Dictionary<string, VirtualWidgetDescriptor> WidgetsById(
		string integrationId,
		IReadOnlyList<VirtualProfileDescriptor> profiles)
	{
		var widgets = new Dictionary<string, VirtualWidgetDescriptor>(StringComparer.Ordinal);

		foreach (var profile in profiles)
		{
			foreach (var folder in profile.Folders)
			{
				foreach (var widget in folder.Widgets)
				{
					if (VirtualWidgetIds.TryCreate(integrationId, profile.Id, folder.Id, widget.Id, out var widgetId))
					{
						widgets.TryAdd(widgetId, widget);
					}
				}
			}
		}

		return widgets;
	}

	private void Invalidate(string widgetId)
	{
		foreach (var session in _sessions.SessionsForWidget(widgetId))
		{
			_sessions.TryInvalidate(session.SessionId,
				UiSessionErrorCodes.WidgetReconfigured,
				"The widget was reconfigured and its view has to be built again.",
				true);
		}
	}

	private void Close(string widgetId)
	{
		foreach (var session in _sessions.SessionsForWidget(widgetId))
		{
			_sessions.TryClose(session.SessionId, "The widget was removed by its integration.");
		}

		_widgetUiProviders.EvictWidget(widgetId);
	}
}
