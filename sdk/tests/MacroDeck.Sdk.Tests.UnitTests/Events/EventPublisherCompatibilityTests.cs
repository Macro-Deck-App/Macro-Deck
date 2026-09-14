using MacroDeck.Sdk.Events;

namespace MacroDeck.Sdk.Tests.UnitTests.Events;

[TestFixture]
public class EventPublisherCompatibilityTests
{
	[Test]
	public void A_publisher_written_before_bindings_existed_reports_nothing_bound()
	{
		IEventPublisher publisher = new PublishOnlyPublisher();
		var raised = false;
		Action handler = () => raised = true;

		publisher.BindingsChanged += handler;
		publisher.BindingsChanged -= handler;

		Assert.Multiple(() =>
		{
			Assert.That(publisher.GetBindings(), Is.Empty);
			Assert.That(raised, Is.False);
		});
	}

	private sealed class PublishOnlyPublisher : IEventPublisher
	{
		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		{
		}
	}
}
