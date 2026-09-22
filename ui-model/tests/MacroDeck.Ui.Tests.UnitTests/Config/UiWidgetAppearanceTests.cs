using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UiWidgetAppearanceTests
{
	private static readonly string[] _pickerKeys =
	[
		UiWidgetAppearanceKeys.BackgroundColor, UiWidgetAppearanceKeys.LabelColor, UiWidgetAppearanceKeys.AccentColor,
		UiWidgetAppearanceKeys.TextAlign, UiWidgetAppearanceKeys.LabelPosition
	];

	[Test]
	public void Untouched_fields_for_a_widget_without_appearance_carry_nothing_a_save_would_store()
	{
		var view = View("{}", UiWidgetAppearanceFields.All);

		Assert.Multiple(() =>
		{
			Assert.That(Value(view, UiWidgetAppearanceKeys.Label).ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(Value(view, UiWidgetAppearanceKeys.FontFaceId).ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(Value(view, UiWidgetAppearanceKeys.Border).ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(Node(view, UiWidgetAppearanceKeys.FontSize).Properties.ContainsKey(UiConfigProperties.Value),
				Is.False);
			foreach (var key in _pickerKeys)
			{
				Assert.That(Value(view, key).GetString(), Is.Empty, key);
			}
		});
	}

	[Test]
	public void Fields_show_the_stored_values_and_keep_what_the_border_field_does_not_edit()
	{
		var view = View("""
						{"label":"Photos","backgroundColor":"#101010","labelColor":"#202020","accentColor":"#303030",
						"fontFaceId":"inter-bold","fontSize":14,"textAlign":"left","labelPosition":"bottom",
						"border":{"style":"static","color":"#fff000","extra":1}}
						""",
			UiWidgetAppearanceFields.All);

		Assert.Multiple(() =>
		{
			Assert.That(Value(view, UiWidgetAppearanceKeys.Label).GetString(), Is.EqualTo("Photos"));
			Assert.That(Value(view, UiWidgetAppearanceKeys.BackgroundColor).GetString(), Is.EqualTo("#101010"));
			Assert.That(Value(view, UiWidgetAppearanceKeys.FontFaceId).GetString(), Is.EqualTo("inter-bold"));
			Assert.That(Value(view, UiWidgetAppearanceKeys.FontSize).GetDouble(), Is.EqualTo(14));
			Assert.That(Value(view, UiWidgetAppearanceKeys.LabelPosition).GetString(), Is.EqualTo("bottom"));
			Assert.That(Value(view, UiWidgetAppearanceKeys.Border).GetRawText(),
				Is.EqualTo("""{"style":"static","color":"#fff000","extra":1}"""));
		});
	}

	[Test]
	public void A_border_with_only_a_style_stays_as_stored()
	{
		var view = View("""{"border":{"style":"off"}}""", UiWidgetAppearanceFields.Border);

		Assert.That(Value(view, UiWidgetAppearanceKeys.Border).GetRawText(), Is.EqualTo("""{"style":"off"}"""));
	}

	[Test]
	public void Choosing_a_border_style_writes_the_border_object()
	{
		var view = View("{}", UiWidgetAppearanceFields.Border);
		var style = Node(view, UiWidgetAppearanceKeys.BorderStyle);

		view.Dispatch(new UiEvent
		{
			NodeId = style.Id, Name = UiConfigEvents.Change, Data = UiCanonicalJson.ToElement("comet")
		});

		Assert.That(Value(view, UiWidgetAppearanceKeys.Border).GetRawText(), Is.EqualTo("""{"style":"comet"}"""));
	}

	[Test]
	public void Clearing_the_label_asks_the_save_to_remove_it()
	{
		var view = View("""{"label":"Photos"}""", UiWidgetAppearanceFields.Label);

		view.Dispatch(new UiEvent
		{
			NodeId = Node(view, UiWidgetAppearanceKeys.Label).Id,
			Name = UiConfigEvents.Change,
			Data = UiCanonicalJson.ToElement(string.Empty)
		});

		Assert.That(Value(view, UiWidgetAppearanceKeys.Label).ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	[Test]
	public void The_font_field_asks_the_host_for_its_fonts()
	{
		var font = Node(View("{}", UiWidgetAppearanceFields.Font), UiWidgetAppearanceKeys.FontFaceId);

		Assert.Multiple(() =>
		{
			Assert.That(font.Properties[UiConfigProperties.OptionsSourceId].GetString(), Is.EqualTo("macrodeck.fonts"));
			Assert.That(font.Properties[UiConfigProperties.DynamicOptions].GetBoolean(), Is.True);
			Assert.That(font.Properties.ContainsKey(UiConfigProperties.Options), Is.False);
		});
	}

	[Test]
	public void Only_the_requested_fields_are_built()
	{
		var view = View("{}", UiWidgetAppearanceFields.BackgroundColor);

		Assert.Multiple(() =>
		{
			Assert.That(Find(view.Tree.Root, UiWidgetAppearanceKeys.BackgroundColor), Is.Not.Null);
			Assert.That(Find(view.Tree.Root, UiWidgetAppearanceKeys.Label), Is.Null);
			Assert.That(Find(view.Tree.Root, UiWidgetAppearanceKeys.Border), Is.Null);
		});
	}

	[Test]
	public void Read_returns_the_stored_values_and_null_for_missing_or_mistyped_ones()
	{
		using var document = JsonDocument.Parse("""
												{"label":"Photos","fontSize":12,"labelColor":5,
												"border":{"style":"rgb"}}
												""");

		var values = UiWidgetAppearance.Read(document.RootElement);

		Assert.That(values, Is.EqualTo(new UiWidgetAppearanceValues
		{
			Label = "Photos", FontSize = 12, BorderStyle = "rgb"
		}));
	}

	private static UiView View(string data, UiWidgetAppearanceFields fields)
	{
		using var document = JsonDocument.Parse(data);
		return new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			new UiWidgetConfiguration
			{
				Key = "root",
				Properties = new UiWidgetProperties
				{
					Key = "properties",
					Children = [UiWidgetAppearance.Section(document.RootElement.Clone(), fields)],
				},
			});
	}

	private static JsonElement Value(UiView view, string key) => Node(view, key).Properties[UiConfigProperties.Value];

	private static UiNode Node(UiView view, string key)
		=> Find(view.Tree.Root, key) ?? throw new AssertionException($"no node for '{key}'");

	private static UiNode? Find(UiNode node, string key)
	{
		if (node.Id == key || node.Id.EndsWith("." + key, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (Find(child, key) is { } found)
			{
				return found;
			}
		}

		return null;
	}
}
