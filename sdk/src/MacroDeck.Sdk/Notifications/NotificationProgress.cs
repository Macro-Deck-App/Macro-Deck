namespace MacroDeck.Sdk.Notifications;

/// <summary>Progress shown on a notification. A null <see cref="Total" /> is shown as indeterminate.</summary>
public sealed record NotificationProgress(int Processed, int? Total = null);
