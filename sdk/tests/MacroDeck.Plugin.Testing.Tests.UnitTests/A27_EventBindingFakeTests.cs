using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A27_EventBindingFakeTests
{
	[Test]
	public void SetBindings_is_what_the_integration_reads_back_and_raises_BindingsChanged()
	{
		var context = new FakeIntegrationContext();
		var events = ((IIntegrationContext)context).Events;
		var raised = 0;
		events.BindingsChanged += () => raised++;

		context.Events.SetBindings(new EventBinding { EventId = "hotkey-pressed" });

		Assert.Multiple(() =>
		{
			Assert.That(events.GetBindings().Single().EventId, Is.EqualTo("hotkey-pressed"));
			Assert.That(raised, Is.EqualTo(1));
		});
	}
}
