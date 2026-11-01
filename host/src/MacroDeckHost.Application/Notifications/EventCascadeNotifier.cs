using MacroDeck.Localization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Notifications;

public interface IEventCascadeNotifier
{
	Task NotifyStopped(EventTriggerOwner owner, CancellationToken cancellationToken);
}

public sealed class EventCascadeNotifier : IEventCascadeNotifier
{
	private readonly IUserNotificationStore _notifications;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly IAutomationCache _automations;

	public EventCascadeNotifier(
		IUserNotificationStore notifications,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		IAutomationCache automations)
	{
		_notifications = notifications;
		_preferences = preferences;
		_localization = localization;
		_automations = automations;
	}

	public async Task NotifyStopped(EventTriggerOwner owner, CancellationToken cancellationToken)
	{
		var culture = (await _preferences.GetLocalization()).Culture;

		_notifications.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.EventCascadeStopped(), culture),
			Message = _localization.Resolve(AppStrings.Notifications.EventCascadeStoppedMessage(), culture),
			SourceName = owner.Kind == EventTriggerOwnerKind.Automation ? _automations.GetById(owner.Id)?.Name : null,
			DedupeKey = $"events.cascade.{owner.Kind}.{owner.Id}"
		});
	}
}
