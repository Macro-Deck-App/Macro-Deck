using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.ActionButton;
using MacroDeckHost.Widgets.Slider;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

/// <summary>
/// Acceptance Group A, scenarios 1-3: legacy bare <c>iconId</c> data migrates to the typed <c>icon</c>
/// shape invisibly (no rendering change) and the migration never oscillates across repeated saves.
/// </summary>
[TestFixture]
public class WidgetIconReferenceMigrationTests
{
	private const string IconA = "0198aaaa-1111-2222-3333-444444444444";
	private const string IconB = "0198bbbb-1111-2222-3333-444444444444";
	private const string IconC = "0198cccc-1111-2222-3333-444444444444";

	[Test]
	public void Normalize_LegacyRootIconId_MigratesInvisiblyAndIsIdempotent()
	{
		var widget = ActionButtonWidget($$"""
										  {"stateMode":false,"iconId":"{{IconA}}",
										  "iconDisplay":{"fit":"cover","zoom":140,"offsetX":-12,"offsetY":8,"opacity":85},"label":"Play"}
										  """);

		var changed = WidgetIconReferenceMigration.Normalize(widget);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		var resolved = ActionButtonWidgetData.Parse(JsonDocument.Parse(widget.Data!).RootElement).ResolveIcon(null);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["icon"]!["type"]!.GetValue<string>(), Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo(IconA));
			Assert.That(data.ContainsKey("iconId"), Is.False, "writing always emits icon and removes iconId");

			// iconDisplay is untouched by the migration - field-for-field identical to what was stored.
			Assert.That(data["iconDisplay"]!["fit"]!.GetValue<string>(), Is.EqualTo("cover"));
			Assert.That(data["iconDisplay"]!["zoom"]!.GetValue<double>(), Is.EqualTo(140));
			Assert.That(data["iconDisplay"]!["offsetX"]!.GetValue<double>(), Is.EqualTo(-12));
			Assert.That(data["iconDisplay"]!["offsetY"]!.GetValue<double>(), Is.EqualTo(8));
			Assert.That(data["iconDisplay"]!["opacity"]!.GetValue<double>(), Is.EqualTo(85));

			// The resolved image is the same asset as before: same provider, same reference.
			Assert.That(resolved!.Icon, Is.EqualTo(WidgetIconReference.IconPack(IconA)));
		});

		var afterFirstMigration = widget.Data;
		var changedAgain = WidgetIconReferenceMigration.Normalize(widget);

		Assert.Multiple(() =>
		{
			Assert.That(changedAgain, Is.False, "a widget that already has the typed shape must not oscillate");
			Assert.That(widget.Data, Is.EqualTo(afterFirstMigration), "the second load's icon must equal the first");
		});
	}

	[Test]
	public void Normalize_LegacyPerStateIconId_MigratesAndTheNonCascadeSurvives()
	{
		var widget = ActionButtonWidget($$"""
										  {"stateMode":true,"iconId":"{{IconC}}","states":[
										  {"id":"on","label":"On","appearance":{"iconId":"{{IconA}}"} },
										  {"id":"off","label":"Off","appearance":{} }]}
										  """);

		var changed = WidgetIconReferenceMigration.Normalize(widget);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		var states = data["states"]!.AsArray().OfType<JsonObject>().ToList();
		var onAppearance = states.Single(s => s["id"]!.GetValue<string>() == "on")["appearance"]!.AsObject();
		var offAppearance = states.Single(s => s["id"]!.GetValue<string>() == "off")["appearance"]!.AsObject();

		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(widget.Data!).RootElement);
		var onIcon = config.ResolveIcon("on");
		var offIcon = config.ResolveIcon("off");

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(onAppearance["icon"]!["type"]!.GetValue<string>(),
				Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(onAppearance["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo(IconA));
			Assert.That(offAppearance.ContainsKey("icon"), Is.False, "off never had an icon and must not acquire one");

			Assert.That(onIcon!.Icon, Is.EqualTo(WidgetIconReference.IconPack(IconA)), "state on renders ICON_A");
			Assert.That(offIcon, Is.Null, "state off renders no image and specifically not ICON_C");
		});

		var afterFirstMigration = widget.Data;
		Assert.That(WidgetIconReferenceMigration.Normalize(widget), Is.False);
		Assert.That(widget.Data, Is.EqualTo(afterFirstMigration));
	}

	[Test]
	public void Normalize_LegacySliderRootIconId_Migrates()
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			Type = WidgetTypeIds.Slider,
			Data = $$"""{"iconId":"{{IconB}}","label":"Volume"}"""
		};

		var changed = WidgetIconReferenceMigration.Normalize(widget);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		var config = SliderWidgetData.Parse(JsonDocument.Parse(widget.Data!).RootElement);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["icon"]!["type"]!.GetValue<string>(), Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo(IconB));
			Assert.That(data.ContainsKey("iconId"), Is.False);
			Assert.That(config.Icon, Is.EqualTo(WidgetIconReference.IconPack(IconB)), "same rendered image as before");
		});
	}

	[Test]
	public void InitializeCache_ALegacyProfile_MigratesTheIconAndPersistsItBackWithoutOscillatingOnAReload()
	{
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var widgetId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "P",
			Folders =
			[
				new ProfileFolder
				{
					Id = folderId,
					Name = "Home",
					IsDefault = true,
					CreatedAt = DateTime.UtcNow,
					Widgets =
					[
						new ProfileWidget
						{
							Id = widgetId,
							Type = WidgetTypeIds.ActionButton,
							Data = $$"""{"stateMode":false,"iconId":"{{IconA}}","label":"Play"}"""
						}
					]
				}
			]
		});

		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		cache.InitializeCache().GetAwaiter().GetResult();

		var migrated = cache.GetFoldersByProfileId(profileId).Single().Widgets.Single();
		var data = JsonNode.Parse(migrated.Data!)!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo(IconA));
			Assert.That(data.ContainsKey("iconId"), Is.False);
			Assert.That(store.SaveCount, Is.EqualTo(1));
		});

		// "saved with no user edit, loaded again": a fresh cache over what is now an already-migrated,
		// persisted profile must settle with no further change - no oscillation across saves.
		var reloadedCache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		reloadedCache.InitializeCache().GetAwaiter().GetResult();
		var reloaded = reloadedCache.GetFoldersByProfileId(profileId).Single().Widgets.Single();

		Assert.Multiple(() =>
		{
			Assert.That(reloaded.Data, Is.EqualTo(migrated.Data), "the second load's icon must equal the first");
			Assert.That(store.SaveCount, Is.EqualTo(1), "an already-migrated profile must not be re-persisted on load");
		});

		cache.Dispose();
		reloadedCache.Dispose();
	}

	private static WidgetEntity ActionButtonWidget(string data)
		=> new() { Id = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, Data = data };
}
