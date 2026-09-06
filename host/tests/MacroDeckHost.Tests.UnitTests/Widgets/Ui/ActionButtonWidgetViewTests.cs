using System.Text.Json;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.ActionButton;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class ActionButtonWidgetViewTests
{
	[Test]
	public void A_bare_button_carries_no_background_source_border_or_framing_keys()
	{
		var host = Render(new { });
		var button = host.ById("actionButton");
		var label = host.ById("actionButton.label");

		Assert.Multiple(() =>
		{
			Assert.That(button.HasProperty("background"), Is.False);
			Assert.That(button.HasProperty("source"), Is.False);
			Assert.That(button.HasProperty("borderStyle"), Is.False);
			Assert.That(button.HasProperty("borderColor"), Is.False);
			Assert.That(button.HasProperty("zoom"), Is.False);
			Assert.That(button.HasProperty("offsetX"), Is.False);
			Assert.That(button.HasProperty("offsetY"), Is.False);
			Assert.That(button.HasProperty("opacity"), Is.False);
			Assert.That(button.HasProperty("fit"), Is.False);
			Assert.That(button.Text("justify"), Is.EqualTo(UiComponentJustify.Center));
			Assert.That(label.Text("color"), Is.EqualTo("#ffffff"));
			Assert.That(label.Text("align"), Is.EqualTo(UiComponentAlignments.Center));
			Assert.That(label.Property("size")!.Value.GetProperty("basis").GetDouble(), Is.EqualTo(0.14).Within(1e-9));
		});
	}

	[Test]
	public void The_label_wraps_with_no_line_cap_matching_the_retired_component()
	{
		// The retired ActionButtonWidgetComponent's label CSS was `white-space: pre-wrap;
		// overflow-wrap: anywhere` with no line clamp at all - wrap:true and no maxLines is what
		// reproduces that (see UiComponentProperties.MaxLines: absent + wrap resolves to uncapped).
		var host = Render(new { });
		var label = host.ById("actionButton.label");

		Assert.Multiple(() =>
		{
			Assert.That(label.Flag("wrap"), Is.True);
			Assert.That(label.HasProperty("maxLines"), Is.False);
		});
	}

	[Test]
	public void An_unset_background_and_the_accent_sentinel_both_omit_the_background_key()
	{
		var unset = Render(new { });
		var accent = Render(new { backgroundColor = "var(--color-accent)" });
		var configured = Render(new { backgroundColor = "#2B6CEE" });

		Assert.Multiple(() =>
		{
			Assert.That(unset.ById("actionButton").HasProperty("background"), Is.False);
			Assert.That(accent.ById("actionButton").HasProperty("background"), Is.False);
			Assert.That(configured.ById("actionButton").Text("background"), Is.EqualTo("#2b6cee"));
		});
	}

	[Test]
	public void A_state_with_no_icon_never_falls_back_to_the_root_icon()
	{
		var host = Render(new
			{
				stateMode = true,
				iconId = "root-icon",
				activeStateId = "a",
				states = new object[]
				{
					new { id = "a", label = "A", appearance = new { } },
				},
			},
			icons: new Dictionary<WidgetIconReference, UiResource>
				{ [WidgetIconReference.IconPack("root-icon")] = Icon() });

		var button = host.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(button.HasProperty("source"), Is.False);
			Assert.That(button.HasProperty("fit"), Is.False);
			Assert.That(button.HasProperty("zoom"), Is.False);
			Assert.That(button.HasProperty("offsetX"), Is.False);
			Assert.That(button.HasProperty("offsetY"), Is.False);
			Assert.That(button.HasProperty("opacity"), Is.False);
		});
	}

	[Test]
	public void A_state_cascades_unset_fields_from_the_root_while_overriding_its_own()
	{
		var host = Render(new
		{
			stateMode = true,
			activeStateId = "a",
			labelColor = "#ff0000",
			fontSize = 20,
			textAlign = "left",
			labelPosition = "top",
			border = new { style = "static", color = "#00ff00" },
			states = new object[]
			{
				new { id = "a", label = "A", appearance = new { fontSize = 30 } },
			},
		});

		var button = host.ById("actionButton");
		var label = host.ById("actionButton.label");

		Assert.Multiple(() =>
		{
			Assert.That(label.Text("color"), Is.EqualTo("#ff0000"));
			Assert.That(label.Property("size")!.Value.GetProperty("basis").GetDouble(), Is.EqualTo(0.30).Within(1e-9));
			Assert.That(label.Text("align"), Is.EqualTo(UiComponentAlignments.Start));
			Assert.That(button.Text("justify"), Is.EqualTo(UiComponentJustify.Start));
			Assert.That(button.Text("borderStyle"), Is.EqualTo("static"));
			Assert.That(button.Text("borderColor"), Is.EqualTo("#00ff00"));
		});
	}

	[Test]
	public void A_reset_colour_renders_as_its_default_rather_than_as_a_blank_one()
	{
		// What the editor's reset cell stores (issue #896): an empty string, which has to arrive here as
		// "no background configured" and as the border's own default ring - never as an empty colour.
		var button = Render(new
			{
				backgroundColor = string.Empty,
				border = new { style = "static", color = string.Empty },
			})
			.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(button.HasProperty("background"), Is.False);
			Assert.That(button.Text("borderColor"), Is.EqualTo("#ffffff"));
		});
	}

	[Test]
	public void A_border_style_of_off_emits_neither_border_key()
	{
		var host = Render(new
		{
			stateMode = true,
			activeStateId = "a",
			states = new object[]
			{
				new { id = "a", label = "A", appearance = new { border = new { style = "off" } } },
			},
		});

		var button = host.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(button.HasProperty("borderStyle"), Is.False);
			Assert.That(button.HasProperty("borderColor"), Is.False);
		});
	}

	[Test]
	public void A_hue_shift_border_emits_no_colour_while_static_defaults_to_white()
	{
		var hueShift = Render(new { border = new { style = "hue-shift", color = "#123456" } });
		var staticNoColor = Render(new { border = new { style = "static" } });
		var rgb = Render(new { border = new { style = "rgb" } });

		Assert.Multiple(() =>
		{
			Assert.That(hueShift.ById("actionButton").Text("borderStyle"), Is.EqualTo("hue-shift"));
			Assert.That(hueShift.ById("actionButton").HasProperty("borderColor"), Is.False);
			Assert.That(staticNoColor.ById("actionButton").Text("borderColor"), Is.EqualTo("#ffffff"));
			Assert.That(rgb.ById("actionButton").Text("borderStyle"), Is.EqualTo("rgb"));
			Assert.That(rgb.ById("actionButton").HasProperty("borderColor"), Is.False);
		});
	}

	[Test]
	public void Icon_framing_is_clamped_and_converted_to_fractions_and_defaults_are_omitted()
	{
		var host = Render(new { iconId = "icon-a", iconDisplay = new { zoom = 5000, offsetX = -400, opacity = 50 } },
			icons: new Dictionary<WidgetIconReference, UiResource>
				{ [WidgetIconReference.IconPack("icon-a")] = Icon() });

		var button = host.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(button.Number("zoom"), Is.EqualTo(4.0).Within(1e-9), "5000 clamps to 400, then /100");
			Assert.That(button.Number("offsetX"), Is.EqualTo(-1.0).Within(1e-9), "-400 clamps to -100, then /100");
			Assert.That(button.Number("opacity"), Is.EqualTo(0.5).Within(1e-9));
			Assert.That(button.HasProperty("offsetY"), Is.False, "0 is the default and stays omitted");
			Assert.That(button.HasProperty("fit"), Is.False, "contain is the default and stays omitted");
		});
	}

	[Test]
	public void An_opacity_that_clamps_to_the_default_is_omitted_even_though_it_was_configured()
	{
		var host = Render(new { iconId = "icon-a", iconDisplay = new { opacity = 250 } },
			icons: new Dictionary<WidgetIconReference, UiResource>
				{ [WidgetIconReference.IconPack("icon-a")] = Icon() });

		Assert.That(host.ById("actionButton").HasProperty("opacity"),
			Is.False,
			"250 clamps to 100, the default, and absence is about the effective value, not the stored one");
	}

	[Test]
	public void Label_position_and_text_align_are_independent_axes()
	{
		var bottomLeft = Render(new { labelPosition = "bottom", textAlign = "left" });
		var topRight = Render(new { labelPosition = "top", textAlign = "right" });

		Assert.Multiple(() =>
		{
			Assert.That(bottomLeft.ById("actionButton").Text("justify"), Is.EqualTo(UiComponentJustify.End));
			Assert.That(bottomLeft.ById("actionButton.label").Text("align"), Is.EqualTo(UiComponentAlignments.Start));
			Assert.That(topRight.ById("actionButton").Text("justify"), Is.EqualTo(UiComponentJustify.Start));
			Assert.That(topRight.ById("actionButton.label").Text("align"), Is.EqualTo(UiComponentAlignments.End));
		});
	}

	[Test]
	public void The_stack_fallback_carries_the_same_layout_and_background_with_no_source_or_border()
	{
		var host = Render(new { backgroundColor = "#112233" });
		var fallback = host.ById("actionButton").Fallback;

		Assert.That(fallback, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(fallback!.Type, Is.EqualTo(UiComponents.Stack));
			Assert.That(fallback.Text("background"), Is.EqualTo("#112233"));
			Assert.That(fallback.HasProperty("source"), Is.False);
			Assert.That(fallback.HasProperty("borderStyle"), Is.False);
		});
	}

	[Test]
	public void With_no_icon_the_legacy_image_resource_is_drawn_as_the_backdrop_source()
	{
		var imageResource = new UiResource { ResourceId = "legacy-image-1" };
		var host = Render(new { }, imageResource: imageResource);
		var button = host.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(button.HasProperty("source"), Is.True);
			Assert.That(button.Property("source")!.Value.GetProperty("resourceId").GetString(),
				Is.EqualTo("legacy-image-1"));
			Assert.That(button.HasProperty("fit"),
				Is.False,
				"the legacy image path carries no display framing of its own");
		});
	}

	[Test]
	public void A_resolved_icon_is_drawn_instead_of_the_legacy_image_resource_never_alongside_it()
	{
		// The legacy imageUrl face is a last resort, drawn only when no icon resolves at all - never a
		// substitute for an icon this session simply failed to fetch bytes for (see ActionButtonWidgetView.Backdrop).
		var imageResource = new UiResource { ResourceId = "legacy-image-1" };
		var host = Render(new { iconId = "icon-a" },
			icons: new Dictionary<WidgetIconReference, UiResource>
				{ [WidgetIconReference.IconPack("icon-a")] = Icon() },
			imageResource: imageResource);
		var button = host.ById("actionButton");

		Assert.That(button.Property("source")!.Value.GetProperty("resourceId").GetString(), Is.EqualTo("res-1"));
	}

	private static UiResource Icon() => new() { ResourceId = "res-1" };

	private static UiTestHost Render(object data,
		IReadOnlyDictionary<WidgetIconReference, UiResource>? icons = null,
		UiResource? imageResource = null,
		WidgetIconResolution? iconProvider = null)
	{
		var config = ActionButtonWidgetData.Parse(JsonSerializer.SerializeToElement(data));
		var configState = new UiState<ActionButtonWidgetData>(config);
		var activeState = new UiState<string?>(config.InitialStateId);
		var labelText = new UiState<string?>(config.Resolve(activeState.Peek()).Label);
		var element = ActionButtonWidgetView.Build(configState,
			activeState,
			labelText,
			new UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>>(icons ??
				new Dictionary<WidgetIconReference, UiResource>()),
			new UiState<WidgetIconResolution>(iconProvider ?? WidgetIconResolution.Inactive),
			imageResource,
			events: []);

		return UiTestHost.Render(element,
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });
	}
}
