using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class EmptyCellStylePersistenceTests
{
	private TestPaths _paths = null!;
	private JsonProfileStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new JsonProfileStore(_paths, new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void A_saved_style_is_written_lowercase_and_reads_back()
	{
		var profile = new ProfileEntity
		{
			Id = Guid.NewGuid(), Name = "Deck", DefaultEmptyCellStyle = EmptyCellStyle.Transparent
		};
		var folder = Folder(profile.Id, EmptyCellStyle.Visible);

		_store.Save(ProfileFileMapper.ToFile(profile, [folder]));

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		var loaded = _store.LoadAll().Profiles.Single();
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"defaultEmptyCellStyle\": \"transparent\""));
			Assert.That(json, Does.Contain("\"emptyCellStyle\": \"visible\""));
			Assert.That(ProfileFileMapper.ToProfileEntity(loaded).DefaultEmptyCellStyle,
				Is.EqualTo(EmptyCellStyle.Transparent));
			Assert.That(ProfileFileMapper.ToFolderEntities(loaded).Single().EmptyCellStyle,
				Is.EqualTo(EmptyCellStyle.Visible));
		});
	}

	[Test]
	public void An_unset_style_is_absent_from_the_json_and_reads_back_unset()
	{
		var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = "Deck" };

		_store.Save(ProfileFileMapper.ToFile(profile, [Folder(profile.Id, null)]));

		var json = File.ReadAllText(Path.Combine(_paths.ProfilesDirectory, profile.Id + ".json"));
		var loaded = _store.LoadAll().Profiles.Single();
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("mptyCellStyle"));
			Assert.That(ProfileFileMapper.ToProfileEntity(loaded).DefaultEmptyCellStyle, Is.Null);
			Assert.That(ProfileFileMapper.ToFolderEntities(loaded).Single().EmptyCellStyle, Is.Null);
		});
	}

	[Test]
	public void A_file_from_an_older_or_newer_host_loads_with_the_style_unset()
	{
		var id = Guid.NewGuid();
		var json = $$"""
					{
						"id": "{{id}}",
						"name": "Deck",
						"defaultEmptyCellStyle": "shimmer",
						"folders": [
							{ "id": "{{Guid.NewGuid()}}", "name": "Home", "order": 0, "widgets": [] },
							{ "id": "{{Guid.NewGuid()}}", "name": "Other", "order": 1, "emptyCellStyle": "TRANSPARENT", "widgets": [] }
						]
					}
					""";
		Directory.CreateDirectory(_paths.ProfilesDirectory);
		File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, id + ".json"), json);

		var loaded = _store.LoadAll().Profiles.Single();
		var folders = ProfileFileMapper.ToFolderEntities(loaded).OrderBy(folder => folder.Order).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(ProfileFileMapper.ToProfileEntity(loaded).DefaultEmptyCellStyle, Is.Null);
			Assert.That(folders[0].EmptyCellStyle, Is.Null);
			Assert.That(folders[1].EmptyCellStyle, Is.EqualTo(EmptyCellStyle.Transparent));
		});
	}

	private static FolderEntity Folder(Guid profileId, EmptyCellStyle? style)
		=> new() { Id = Guid.NewGuid(), ProfileId = profileId, Name = "Home", Order = 0, EmptyCellStyle = style };
}
