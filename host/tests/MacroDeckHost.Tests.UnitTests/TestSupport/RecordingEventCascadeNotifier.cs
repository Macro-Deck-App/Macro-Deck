using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingEventCascadeNotifier : IEventCascadeNotifier
{
	public List<EventTriggerOwner> Stopped { get; } = [];

	public Task NotifyStopped(EventTriggerOwner owner, CancellationToken cancellationToken)
	{
		Stopped.Add(owner);
		return Task.CompletedTask;
	}
}
