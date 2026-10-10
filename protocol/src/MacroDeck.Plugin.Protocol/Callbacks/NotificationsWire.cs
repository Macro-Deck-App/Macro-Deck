namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The string vocabularies of the notification operations. Frozen once shipped, like the operation names.</summary>
public static class NotificationsWire
{
	public static class Severity
	{
		public const string Info = "info";

		public const string Success = "success";

		public const string Warning = "warning";

		public const string Error = "error";
	}

	public static class Popup
	{
		public const string None = "none";

		public const string Toast = "toast";

		public const string Dialog = "dialog";
	}

	public static class TargetKind
	{
		public const string All = "all";

		public const string AdminUi = "admin-ui";

		public const string Devices = "devices";
	}

	public static class ActionStyle
	{
		public const string Default = "default";

		public const string Primary = "primary";

		public const string Danger = "danger";
	}

	public static class CloseReason
	{
		public const string Action = "action";

		public const string Dismissed = "dismissed";

		public const string Timeout = "timeout";

		public const string Programmatic = "programmatic";

		public const string OwnerGone = "owner-gone";

		public const string HostUnavailable = "host-unavailable";
	}
}
