namespace MacroDeckHost.Application.Notifications;

// No label: the wording is derived from Kind in the UI, which is the only place that
// knows the active culture. A host-side label would always be English.
public sealed record UserNotificationAction(UserNotificationActionKind Kind, string? Target);
