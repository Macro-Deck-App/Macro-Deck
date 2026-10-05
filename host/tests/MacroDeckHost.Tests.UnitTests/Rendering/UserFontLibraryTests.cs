using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Infrastructure.Rendering;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class UserFontLibraryTests
{
	private const string FixtureFamily = "Anta";

	private TestPaths _paths = null!;
	private List<SkiaFontCatalog.SystemFaceEntry> _systemFaces = null!;
	private SkiaFontCatalog _catalog = null!;
	private FileSystemUserFontLibrary _library = null!;

	private static byte[] Fixture =>
		File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Rendering", "Fixtures", "Anta-Regular.ttf"));

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_systemFaces = [];
		CreateLibrary();
	}

	[TearDown]
	public void TearDown()
	{
		_library.Dispose();
		if (Directory.Exists(_paths.BaseDirectory))
		{
			Directory.Delete(_paths.BaseDirectory, recursive: true);
		}
	}

	[Test]
	public async Task An_imported_font_is_offered_as_a_user_face_and_served()
	{
		var results = await Import(("Anta-Regular.ttf", Fixture));

		var face = _catalog.GetFaces().SingleOrDefault(candidate => candidate.Family == FixtureFamily);
		var served = face is null ? null : _catalog.GetFaceFile(face.FaceId);
		using var reparsed = served is null ? null : SKTypeface.FromStream(new MemoryStream(served));

		Assert.Multiple(() =>
		{
			Assert.That(results.Single().Status, Is.EqualTo(UserFontImportStatus.Imported));
			Assert.That(results.Single().Font?.Family, Is.EqualTo(FixtureFamily));
			Assert.That(face, Is.Not.Null);
			Assert.That(face?.UserImported, Is.True);
			Assert.That(face?.RemoteRenderable, Is.True);
			Assert.That(face?.FaceId, Is.EqualTo("anta-400-5-upright"));
			Assert.That(reparsed?.FamilyName, Is.EqualTo(FixtureFamily));
			Assert.That(_library.List().Select(font => font.FaceId), Is.EqualTo(new[] { "anta-400-5-upright" }));
		});
	}

	[Test]
	public async Task A_font_whose_family_is_installed_on_the_computer_is_rejected_and_never_listed()
	{
		_systemFaces.Add(new SkiaFontCatalog.SystemFaceEntry(FixtureFamily, 0, 700, 5, SKFontStyleSlant.Upright, true, () => null));
		CreateLibrary();

		var results = await Import(("Anta-Regular.ttf", Fixture));
		File.WriteAllBytes(Path.Combine(_paths.FontsDirectory, "0123456789abcdef.ttf"), Fixture);
		_catalog.Reload();

		Assert.Multiple(() =>
		{
			Assert.That(results.Single().Status, Is.EqualTo(UserFontImportStatus.AlreadyInstalled));
			Assert.That(_catalog.GetFaces().Where(face => face.UserImported), Is.Empty);
			Assert.That(_catalog.GetFaces().Single().FaceId, Is.EqualTo("anta-700-5-upright"));
		});
	}

	[Test]
	public async Task Importing_the_same_file_again_is_harmless_but_a_different_file_with_the_same_style_is_refused()
	{
		await Import(("Anta-Regular.ttf", Fixture));
		var variant = Fixture.Concat(new byte[4]).ToArray();

		var results = await Import(("again.ttf", Fixture), ("variant.ttf", variant));

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(result => result.Status),
				Is.EqualTo(new[] { UserFontImportStatus.AlreadyPresent, UserFontImportStatus.AlreadyImported }));
			Assert.That(_library.List(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Files_that_are_not_usable_ttf_or_otf_fonts_are_rejected_without_being_stored()
	{
		var collection = new byte[64];
		"ttcf"u8.CopyTo(collection);

		var results = await Import(("Anta.woff2", Fixture),
			("garbage.ttf", "not a font at all"u8.ToArray()),
			("collection.otf", collection),
			("huge.ttf", new byte[32 * 1024 * 1024 + 1]));

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(result => result.Status),
				Is.EqualTo(new[]
				{
					UserFontImportStatus.UnsupportedFormat,
					UserFontImportStatus.InvalidFont,
					UserFontImportStatus.InvalidFont,
					UserFontImportStatus.TooLarge
				}));
			Assert.That(Directory.EnumerateFiles(_paths.FontsDirectory), Is.Empty);
		});
	}

	[Test]
	public async Task Removing_a_served_font_deletes_it_and_takes_it_out_of_the_catalog()
	{
		var font = (await Import(("Anta-Regular.ttf", Fixture))).Single().Font!;
		Assert.That(_catalog.GetFaceFile(font.FaceId), Is.Not.Null);

		var removed = await _library.Remove(font.FontId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(removed, Is.True);
			Assert.That(Directory.EnumerateFiles(_paths.FontsDirectory), Is.Empty);
			Assert.That(((IFontCatalog)_catalog).FindFace(font.FaceId), Is.Null);
			Assert.That(_catalog.GetFaceFile(font.FaceId), Is.Null);
		});
	}

	[Test]
	public async Task A_font_the_catalog_no_longer_offers_is_still_listed_so_it_can_be_removed()
	{
		var font = (await Import(("Anta-Regular.ttf", Fixture))).Single().Font!;
		_systemFaces.Add(new SkiaFontCatalog.SystemFaceEntry(FixtureFamily, 0, 400, 5, SKFontStyleSlant.Upright, true, () => null));
		CreateLibrary();

		var listed = _library.List().Single();
		var reimport = await Import(("Anta-Regular.ttf", Fixture));
		var removed = await _library.Remove(listed.FontId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(listed.FontId, Is.EqualTo(font.FontId));
			Assert.That(listed.FaceId, Is.Empty);
			Assert.That(listed.Family, Is.EqualTo(FixtureFamily));
			Assert.That(reimport.Single().Status, Is.EqualTo(UserFontImportStatus.AlreadyInstalled));
			Assert.That(removed, Is.True);
			Assert.That(_library.List(), Is.Empty);
		});
	}

	private void CreateLibrary()
	{
		_library?.Dispose();
		_catalog = new SkiaFontCatalog([],
			() => _systemFaces,
			_paths.FontsDirectory);
		_library = new FileSystemUserFontLibrary(_catalog, _paths);
	}

	private Task<IReadOnlyList<UserFontImportResult>> Import(params (string Name, byte[] Bytes)[] files)
		=> _library.Import(files.Select(file => new UserFontUpload(file.Name, file.Bytes)).ToList(), CancellationToken.None);
}
