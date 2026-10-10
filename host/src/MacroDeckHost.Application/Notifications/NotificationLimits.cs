namespace MacroDeckHost.Application.Notifications;

public static class NotificationLimits
{
	public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(1);

	public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan DefaultToastDuration = TimeSpan.FromSeconds(6);

	public const int MaxActive = 50;

	public const int MaxActivePerOwner = 20;

	public const int MaxTitleLength = 200;

	public const int MaxMessageLength = 1000;

	public const int MaxActions = 4;

	public const int MaxActionIdLength = 64;

	public const int MaxContentBytes = 32 * 1024;

	public const int MaxContentNodes = 200;

	public const int MaxContentDepth = 12;

	public const int ContentBudgetPerOwnerBytes = 64 * 1024;

	public const int ContentBudgetGlobalBytes = 256 * 1024;

	public const int ContentBudgetSystemReserveBytes = 64 * 1024;

	public const int MaxTextFieldLength = 1000;

	public const int CreatesPerMinutePerOwner = 30;

	public const int UpdatesPerSecondPerOwner = 20;
}
