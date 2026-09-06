namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

/// <summary>
/// Which of an event's two parameter lists an options request means. An event may declare the same
/// name in both, so the kind selects a list rather than ordering a search.
/// </summary>
public static class EventParameterKinds
{
	public const string Configuration = "configuration";

	public const string Payload = "payload";
}
