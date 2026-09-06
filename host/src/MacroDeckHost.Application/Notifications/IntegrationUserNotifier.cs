using MacroDeck.Sdk.Notifications;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Notifications;

public sealed class IntegrationUserNotifier : IUserNotifier
{
	private readonly string _integrationId;
	private readonly string _integrationName;
	private readonly IUserNotificationStore _store;
	private readonly ILogger _logger;

	public IntegrationUserNotifier(
		string integrationId,
		string integrationName,
		IUserNotificationStore store,
		ILogger logger)
	{
		_integrationId = integrationId;
		_integrationName = integrationName;
		_store = store;
		_logger = logger;
	}

	public void Notify(UserNotificationRequest notification)
	{
		try
		{
			if (notification is null || string.IsNullOrWhiteSpace(notification.Title))
			{
				return;
			}

			_store.Raise(new UserNotificationDraft
			{
				Severity = ToSeverity(notification.Level),
				Kind = UserNotificationKind.Integration,
				Title = notification.Title,
				Message = notification.Message,
				SourceId = _integrationId,
				SourceName = _integrationName,
				// The SDK deliberately exposes no action model - an integration cannot name a
				// client route - so the action is always "open this integration".
				Action = new UserNotificationAction(UserNotificationActionKind.OpenIntegration, _integrationId),
				DedupeKey = string.IsNullOrWhiteSpace(notification.Key) ? null : QualifyKey(notification.Key)
			});
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Integration '{IntegrationId}' failed to raise a notification", _integrationId);
		}
	}

	public void Dismiss(string key)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				return;
			}

			_store.DismissByKey(QualifyKey(key));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Integration '{IntegrationId}' failed to dismiss a notification", _integrationId);
		}
	}

	private string QualifyKey(string? key) => $"integration:{_integrationId}:{key}";

	private static UserNotificationSeverity ToSeverity(UserNotificationLevel level) => level switch
	{
		UserNotificationLevel.Warning => UserNotificationSeverity.Warning,
		UserNotificationLevel.Error => UserNotificationSeverity.Error,
		_ => UserNotificationSeverity.Info
	};
}
