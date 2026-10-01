using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UndefinedJsonElementTests
{
	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiWidgetConfiguration ConfigurationWith(UiState<JsonElement> flows)
		=> new()
		{
			Key = "root",
			Properties = new UiWidgetProperties { Key = "properties", Children = [] },
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows) }],
			},
		};

	[Test]
	public void An_undefined_element_fails_the_build_naming_the_node_and_property()
	{
		var flows = new UiState<JsonElement>(default);

		var exception = Assert.Throws<UiViewException>(() => new UiView(ConfigSurface(), ConfigurationWith(flows)));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Message, Does.Contain("'flows'"));
			Assert.That(exception.Message, Does.Contain("'value'"));
			Assert.That(exception.Message, Does.Contain("JsonElement"));
		});
	}

	[Test]
	public void An_element_that_becomes_undefined_fails_the_write_naming_the_node()
	{
		var flows = new UiState<JsonElement>(UiCanonicalJson.ToElement(Array.Empty<object>()));
		_ = new UiView(ConfigSurface(), ConfigurationWith(flows));

		var exception = Assert.Throws<UiViewException>(() => flows.Set(default));

		Assert.That(exception!.Message, Does.Contain("'flows'"));
	}

	[Test]
	public void An_undefined_element_nested_in_a_value_fails_naming_the_node()
	{
		var holder = new UiState<ElementHolder>(new ElementHolder(default));

		var exception = Assert.Throws<UiViewException>(() => new UiView(ConfigSurface(),
			new HolderInput { Key = "holder", Binding = Bind.To(holder) }));

		Assert.That(exception!.Message, Does.Contain("'holder'"));
	}

	[TestCase("[]")]
	[TestCase("{\"a\":1}")]
	[TestCase("null")]
	public void A_defined_element_builds_and_serializes(string json)
	{
		using var document = JsonDocument.Parse(json);
		var flows = new UiState<JsonElement>(document.RootElement.Clone());

		var view = new UiView(ConfigSurface(), ConfigurationWith(flows));

		Assert.That(UiCanonicalJson.Serialize(view.Tree), Does.Contain("\"flows\""));
	}

	private sealed record ElementHolder(JsonElement Element);

	private sealed record HolderInput : UiInput<ElementHolder>
	{
		public override string Type => "holder-input";
	}
}
