using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class MusicPlayerWidgetOptionsConfigTests
{
	private const string AnyApp = "system-media::any";
	private const string Spotify = "system-media::spotify";
	private const string Vlc = "system-media::vlc";
	private const string Plain = "sinusbot::bot";
	private const string CycleField = "instanceOptions.cycleSeconds";

	[Test]
	public void The_selected_instance_offers_its_options_filled_with_what_the_widget_stored()
	{
		var host = Render(new { instanceId = AnyApp, instanceOptions = new { cycleSeconds = 30 } });

		var field = host.ById(CycleField);
		Assert.Multiple(() =>
		{
			Assert.That(field.Number(UiConfigProperties.Value), Is.EqualTo(30));
			Assert.That(field.Text(UiConfigProperties.Label), Is.EqualTo("Cycle every"));
			Assert.That(host.ById("instanceOptions.order").Text(UiConfigProperties.Value), Is.EqualTo("newest"));
		});
	}

	[Test]
	public void The_active_player_and_an_instance_without_options_offer_no_option_fields()
	{
		var active = Render(new { instanceId = "", instanceOptions = new { cycleSeconds = 30 } });
		var plain = Render(new { instanceId = Plain });

		Assert.Multiple(() =>
		{
			Assert.That(active.FindById(CycleField), Is.Null);
			Assert.That(plain.FindById(CycleField), Is.Null);
		});
	}

	[Test]
	public void A_typed_value_survives_editing_other_fields()
	{
		var host = Render(new { instanceId = AnyApp });

		host.ById(CycleField).Change(45);
		host.ById("showTitle").Change(false);
		host.ById("coverStyle").Change("full");

		Assert.That(host.ById(CycleField).Number(UiConfigProperties.Value), Is.EqualTo(45));
	}

	[Test]
	public void Switching_to_another_instance_starts_its_options_from_that_instance_defaults()
	{
		var host = Render(new { instanceId = AnyApp, instanceOptions = new { cycleSeconds = 45 } });

		host.ById("instanceId").Change(Spotify);
		var sameKind = host.ById(CycleField);
		var sameKindType = sameKind.Type;
		var sameKindValue = sameKind.Number(UiConfigProperties.Value);
		var orderAfterSwitch = host.FindById("instanceOptions.order");

		host.ById("instanceId").Change(Vlc);
		var otherKind = host.ById(CycleField);

		Assert.Multiple(() =>
		{
			Assert.That(sameKindValue, Is.EqualTo(20));
			Assert.That(orderAfterSwitch, Is.Null);
			Assert.That(otherKind.Type, Is.Not.EqualTo(sameKindType));
			Assert.That(otherKind.Flag(UiConfigProperties.Value), Is.True);
		});
	}

	[Test]
	public void Leaving_for_the_active_player_removes_the_option_fields()
	{
		var host = Render(new { instanceId = AnyApp });

		host.ById("instanceId").Change("");

		Assert.That(host.FindById(CycleField), Is.Null);
	}

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(MusicPlayerWidgetConfigView.Build(JsonSerializer.SerializeToElement(data), new Registry()));

	private sealed class Registry : IMusicPlayerRegistry
	{
		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() =>
		[
			Descriptor(AnyApp, "Any app",
			[
				ActionParameter.Number("cycleSeconds", "Cycle every", min: 5, max: 60, defaultValue: 10),
				ActionParameter.Choice("order",
					[new ActionParameterOption { Value = "newest" }, new ActionParameterOption { Value = "oldest" }],
					"Order"),
			]),
			Descriptor(Spotify, "Spotify", [ActionParameter.Number("cycleSeconds", "Cycle every", defaultValue: 20)]),
			Descriptor(Vlc, "VLC", [ActionParameter.Toggle("cycleSeconds", "Follow VLC", defaultValue: true)]),
			new(Plain, "sinusbot", LocalizedText.FromLiteral("SinusBot"), "Bot", false),
		];

		public IMusicPlayer? GetPlayer(string instanceId) => null;

		public IMusicPlayer? GetPlayerWithOptions(string instanceId, IReadOnlyDictionary<string, object> options)
			=> null;

		public IMusicPlayer? DefaultPlayer => null;

		private static MusicPlayerInstanceDescriptor Descriptor(
			string id,
			string name,
			IReadOnlyList<ActionParameter> options)
			=> new(id, "system-media", LocalizedText.FromLiteral("System Media"), name, false) { Options = options };
	}
}
