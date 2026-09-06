using System.Text.Json;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetDataSchemaProviderTests
{
	[Test]
	public void All_returns_one_entry_per_WidgetType_keyed_by_the_enum_name_and_each_is_parseable_JSON()
	{
		var all = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())).All();
		var types = WidgetTypeIds.BuiltIn;

		Assert.Multiple(() =>
		{
			Assert.That(all, Has.Count.EqualTo(types.Count));

			foreach (var type in types)
			{
				Assert.That(all.ContainsKey(type.ToString()), Is.True, $"missing schema entry for {type}");
			}
		});

		foreach (var (key, element) in all)
		{
			using var reparsed = JsonDocument.Parse(element.GetRawText());
			Assert.That(reparsed.RootElement.ValueKind,
				Is.EqualTo(JsonValueKind.Object),
				$"{key} is not a JSON object");
		}
	}
}
