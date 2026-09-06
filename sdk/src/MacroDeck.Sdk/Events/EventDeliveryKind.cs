namespace MacroDeck.Sdk.Events;

/// <summary>
/// How occurrences of an event come into being. This is not a detail of the event's payload - it
/// decides who is responsible for producing an occurrence at all, so the host knows whether to sit
/// and wait or to run a schedule.
/// </summary>
public enum EventDeliveryKind
{
	/// <summary>
	/// The provider publishes an occurrence when something happens. The trigger's configuration
	/// parameters narrow which occurrences it reacts to.
	/// </summary>
	Push = 0,

	/// <summary>
	/// The host produces occurrences from the trigger's own configuration (an interval, a time of
	/// day, a cron expression). Nothing pushes; the configuration is a schedule, not a filter.
	/// </summary>
	Scheduled = 1
}
