using System.Text.Json;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Events;

[TestFixture]
public class UiEventTests
{
	[Test]
	public void An_event_omits_its_absent_optional_members()
	{
		var uiEvent = new UiEvent { NodeId = "submit", Name = "click" };

		Assert.That(UiCanonicalJson.Serialize(uiEvent), Is.EqualTo("""{"nodeId":"submit","name":"click"}"""));
	}

	[Test]
	public void Event_data_keeps_the_producers_member_order()
	{
		var data = JsonDocument.Parse("""{"b":1,"a":2}""").RootElement.Clone();
		var uiEvent = new UiEvent { NodeId = "n", Name = "change", Data = data, Revision = 7 };

		Assert.That(UiCanonicalJson.Serialize(uiEvent),
			Is.EqualTo("""{"nodeId":"n","name":"change","data":{"b":1,"a":2},"revision":7}"""));
	}
}
