using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.ActionButton;
using MacroDeckHost.Widgets.Slider;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
internal sealed class IconAppearancePinTests
{
	private IconTestHarness _harness = null!;
	private IconEntity _lamp = null!;
	private IconEntity _plain = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new IconTestHarness();
		var pack = await _harness.CreatePack();
		_lamp = await _harness.AddReadyIcon(pack.Id, "lamp", [1]);
		_plain = await _harness.AddReadyIcon(pack.Id, "plain", [2]);
		Assert.That(IconAppearanceTraits.TryParse("colorScheme=dark", out var traits), Is.True);
		await _harness.Cache.AddIcons(pack.Id,
		[
			new IconEntity
			{
				Id = Guid.CreateVersion7(),
				PackId = pack.Id,
				Name = "lamp",
				ProcessingState = IconProcessingState.Ready,
				AppearanceOfId = _lamp.Id,
				AppearanceTraits = traits
			}
		]);
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public void The_appearance_choice_is_offered_only_for_an_icon_that_has_appearances()
	{
		var withAppearances = RenderButton(new { icon = Icon(_lamp, appearance: null) });
		var plain = RenderButton(new { icon = Icon(_plain, appearance: null) });

		var options = withAppearances.ById("icon.appearance").Property(UiConfigProperties.Options)!.Value
			.EnumerateArray()
			.Select(option => option.GetProperty("value").GetString())
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(options, Is.EqualTo(new[] { "automatic", "default", "colorScheme=dark" }));
			Assert.That(withAppearances.ById("icon.appearance").Text(UiConfigProperties.Value), Is.EqualTo("automatic"));
			Assert.That(plain.FindById("icon.appearance"), Is.Null);
		});
	}

	[Test]
	public async Task A_custom_variant_is_offered_for_pinning_by_its_key_and_name()
	{
		await _harness.AddReadyAppearance(_lamp, "variant=duoTone", [3]);
		var host = RenderButton(new { icon = Icon(_lamp, "variant=duoTone") });

		var options = host.ById("icon.appearance").Property(UiConfigProperties.Options)!.Value
			.EnumerateArray()
			.ToDictionary(option => option.GetProperty("value").GetString()!, option => option.GetRawText());

		Assert.Multiple(() =>
		{
			Assert.That(options.Keys, Does.Contain("variant=duoTone"));
			Assert.That(options["variant=duoTone"], Does.Contain("Duo tone"));
			Assert.That(host.ById("icon.appearance").Text(UiConfigProperties.Value), Is.EqualTo("variant=duoTone"));
		});
	}

	[Test]
	public void A_pin_survives_a_label_edit_and_picking_the_same_icon_again_and_goes_with_a_different_icon()
	{
		var host = RenderButton(new { label = "Lamp", icon = Icon(_lamp, "colorScheme=dark") });

		host.ById("label").Change("Desk lamp");
		var afterLabel = StoredIcon(host);
		host.ById("icon.reference").Change(new { type = "icon-pack", reference = _lamp.Id.ToString() });
		var afterSameIcon = StoredIcon(host);
		host.ById("icon.reference").Change(new { type = "icon-pack", reference = _plain.Id.ToString() });
		var afterOtherIcon = StoredIcon(host);

		Assert.Multiple(() =>
		{
			Assert.That(afterLabel.Appearance, Is.EqualTo("colorScheme=dark"));
			Assert.That(afterSameIcon.Appearance, Is.EqualTo("colorScheme=dark"));
			Assert.That(afterOtherIcon.Reference, Is.EqualTo(_plain.Id.ToString()));
			Assert.That(afterOtherIcon.Appearance, Is.Null);
		});
	}

	[Test]
	public void A_state_icon_can_be_pinned_and_keeps_the_pin_through_other_state_edits()
	{
		var host = RenderButton(new
		{
			stateMode = true,
			states = new object[]
			{
				new { id = "off", label = "Off", appearance = new { icon = Icon(_lamp, appearance: null) } },
				new { id = "on", label = "On", appearance = new { } },
			},
		});
		host.ById("activeStateId").Change("off");

		host.ById("states.off.appearance.icon.appearance").Change("default");
		host.ById("states.off.appearance.label").Change("Idle");

		var stored = host.ById("states").Property(UiConfigProperties.Value)!.Value
			.EnumerateArray()
			.Single(state => state.GetProperty("id").GetString() == "off")
			.GetProperty("appearance")
			.GetProperty("icon");
		Assert.That(stored.GetProperty("appearance").GetString(), Is.EqualTo("default"));
	}

	[Test]
	public void A_slider_keeps_its_pin_until_its_icon_changes()
	{
		var host = UiTestHost.Render(SliderWidgetConfigView.Build(
			JsonSerializer.SerializeToElement(new { label = "Volume", icon = Icon(_lamp, "colorScheme=dark") }),
			new VariableRegistry(),
			icons: _harness.Cache));

		host.ById("label").Change("Master");
		var afterLabel = StoredIcon(host);
		host.ById("icon.reference").Change(new { type = "icon-pack", reference = _plain.Id.ToString() });

		Assert.Multiple(() =>
		{
			Assert.That(afterLabel.Appearance, Is.EqualTo("colorScheme=dark"));
			Assert.That(StoredIcon(host).Appearance, Is.Null);
		});
	}

	[TestCase(true, "colorScheme=dark")]
	[TestCase(false, null)]
	public void A_plugin_patch_keeps_the_pin_only_while_it_names_the_same_icon(bool sameIcon, string? expected)
	{
		var data = JsonNode.Parse(JsonSerializer.Serialize(new { icon = Icon(_lamp, "colorScheme=dark") }))!.AsObject();
		var patched = sameIcon ? _lamp.Id : _plain.Id;

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { IconId = patched.ToString() },
			["default"]);

		var icon = WidgetIconReference.Read(data["icon"], null)!.Value;
		Assert.Multiple(() =>
		{
			Assert.That(icon.Reference, Is.EqualTo(patched.ToString()));
			Assert.That(icon.Appearance, Is.EqualTo(expected));
		});
	}

	private static WidgetIconReference StoredIcon(UiTestHost host)
		=> WidgetIconReference.Read(JsonNode.Parse(host.ById("icon").Property(UiConfigProperties.Value)!.Value.GetRawText()),
			null)!.Value;

	private static Dictionary<string, string> Icon(IconEntity icon, string? appearance)
	{
		var json = new Dictionary<string, string> { ["type"] = "icon-pack", ["reference"] = icon.Id.ToString() };
		if (appearance is not null)
		{
			json["appearance"] = appearance;
		}

		return json;
	}

	private UiTestHost RenderButton(object data)
	{
		var registry = new FakeIntegrationRegistry();
		var clock = TimeProvider.System;

		return UiTestHost.Render(ActionButtonWidgetConfigView.Build(JsonSerializer.SerializeToElement(data),
			1,
			registry,
			new NoFonts(),
			null,
			new ActionButtonConfigContext(new ActionProviderProbe(registry,
					new RemoteIconProviderActionRegistry(null!, null!, null!),
					clock,
					Serilog.Core.Logger.None),
				TestLocalization.Resolver,
				"en",
				clock,
				_harness.Cache,
				CancellationToken.None)));
	}

	private sealed class NoFonts : MacroDeckHost.Application.Rendering.IFontCatalog
	{
		public IReadOnlyList<MacroDeckHost.Application.Rendering.FontFaceInfo> GetFaces() => [];

		public byte[]? GetFaceFile(string faceId) => null;
	}
}
