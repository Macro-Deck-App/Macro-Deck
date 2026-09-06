using System.Text.Json.Nodes;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

[TestFixture]
public class WidgetFontFaceMigrationTests
{
	[Test]
	public void Normalize_LegacyWidgetAndStateFonts_ResolveToDifferentFacesAndAreIdempotent()
	{
		var catalog = new FakeFontCatalog(
			new FontFaceInfo("roboto-400-5-upright", "Roboto", 400, 5, "upright", "Regular", true),
			new FontFaceInfo("roboto-700-5-upright", "Roboto", 700, 5, "upright", "Bold", true));

		var widget = ActionButtonWidget("""
										{"mode":"toggle","fontFamily":"Roboto","fontBold":true,"fontItalic":false,
										"states":{"off":{"fontFamily":"Roboto","fontBold":false},"on":{}}}
										""");

		var changed = WidgetFontFaceMigration.Normalize(widget, catalog);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["fontFaceId"]!.GetValue<string>(), Is.EqualTo("roboto-700-5-upright"));
			Assert.That(data["states"]!["off"]!["fontFaceId"]!.GetValue<string>(), Is.EqualTo("roboto-400-5-upright"));
			Assert.That(data.ContainsKey("fontFamily"), Is.False);
			Assert.That(data.ContainsKey("fontBold"), Is.False);
			Assert.That(data.ContainsKey("fontItalic"), Is.False);
			Assert.That(data["states"]!["off"]!.AsObject().ContainsKey("fontFamily"), Is.False);
			// The "on" state never had a font of its own - it must keep inheriting rather than being
			// stamped with the widget-level face.
			Assert.That(data["states"]!["on"]!.AsObject().ContainsKey("fontFaceId"), Is.False);
		});

		var afterFirstRun = widget.Data;
		var changedAgain = WidgetFontFaceMigration.Normalize(widget, catalog);

		Assert.Multiple(() =>
		{
			Assert.That(changedAgain, Is.False);
			Assert.That(widget.Data, Is.EqualTo(afterFirstRun));
		});
	}

	[Test]
	public void Normalize_BoldRequestedButOnlyOneWeightExists_ResolvesToTheAvailableFaceNotAStampedWeight()
	{
		var catalog = new FakeFontCatalog(new FontFaceInfo("singleweightfamily-400-5-upright",
			"SingleWeightFamily",
			400,
			5,
			"upright",
			"Regular",
			true));

		var widget = ActionButtonWidget("""{"fontFamily":"SingleWeightFamily","fontBold":true}""");

		WidgetFontFaceMigration.Normalize(widget, catalog);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		var faceId = data["fontFaceId"]!.GetValue<string>();
		var face = catalog.GetFaces().Single(f => f.FaceId == faceId);

		Assert.Multiple(() =>
		{
			Assert.That(face.Weight, Is.EqualTo(400));
			Assert.That(face.Family, Is.EqualTo("SingleWeightFamily"));
		});
	}

	[Test]
	public void Normalize_FamilyNotInCatalog_KeepsADeterministicIdInsteadOfSubstitutingAnotherFamily()
	{
		var catalog = new FakeFontCatalog(new FontFaceInfo("arial-400-5-upright",
			"Arial",
			400,
			5,
			"upright",
			"Regular",
			true));

		var widget = ActionButtonWidget("""{"fontFamily":"NoSuchFamily__xyz","fontBold":false}""");

		WidgetFontFaceMigration.Normalize(widget, catalog);

		var data = JsonNode.Parse(widget.Data!)!.AsObject();
		var faceId = data["fontFaceId"]!.GetValue<string>();

		Assert.Multiple(() =>
		{
			Assert.That(faceId, Is.EqualTo("nosuchfamily-xyz-400-5-upright"));
			Assert.That(catalog.GetFaces().Any(f => f.FaceId == faceId), Is.False);
		});
	}

	[Test]
	public void InitializeCache_ALegacyProfile_MigratesTheFontAndPersistsItBack()
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
							Data = """{"fontFamily":"Roboto","fontBold":true}"""
						}
					]
				}
			]
		});
		var catalog = new FakeFontCatalog(new FontFaceInfo("roboto-700-5-upright",
			"Roboto",
			700,
			5,
			"upright",
			"Bold",
			true));
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger(), catalog);

		cache.InitializeCache().GetAwaiter().GetResult();

		var migrated = cache.GetFoldersByProfileId(profileId).Single().Widgets.Single();
		var data = JsonNode.Parse(migrated.Data!)!.AsObject();
		Assert.Multiple(() =>
		{
			Assert.That(data["fontFaceId"]!.GetValue<string>(), Is.EqualTo("roboto-700-5-upright"));
			Assert.That(data.ContainsKey("fontFamily"), Is.False);
			Assert.That(store.SaveCount, Is.EqualTo(1));
		});

		cache.Dispose();
	}

	private static WidgetEntity ActionButtonWidget(string data)
		=> new() { Id = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, Data = data };

	private sealed class FakeFontCatalog : IFontCatalog
	{
		private readonly List<FontFaceInfo> _faces;

		public FakeFontCatalog(params FontFaceInfo[] faces)
		{
			_faces = faces.ToList();
		}

		public IReadOnlyList<FontFaceInfo> GetFaces() => _faces;

		public byte[]? GetFaceFile(string faceId) => null;
	}
}
