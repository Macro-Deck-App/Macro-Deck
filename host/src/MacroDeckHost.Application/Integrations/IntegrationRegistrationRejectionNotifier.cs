using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Application.Integrations;

public static class IntegrationRegistrationRejectionNotifier
{
	public static void Raise(
		IUserNotificationStore userNotificationStore,
		string integrationId,
		string integrationName,
		IntegrationRegistrationResult registration)
	{
		ArgumentNullException.ThrowIfNull(userNotificationStore);
		ArgumentNullException.ThrowIfNull(registration);

		var message = registration.Failure == IntegrationRegistrationFailure.DuplicateIntegrationId
			? $"Another integration is already registered as '{integrationId}'."
			: registration.Describe();

		userNotificationStore.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Error,
			Kind = UserNotificationKind.Integration,
			Title = $"{integrationName} could not be loaded",
			Message = message,
			SourceId = integrationId,
			SourceName = integrationName,
			DedupeKey = $"integration-registration:{integrationId}"
		});
	}
}
