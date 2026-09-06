namespace MacroDeckHost.Application.Ui.Transport.Messages.Events;

public class TriggerEventResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public int QueuedSubscriptions { get; set; }
}
