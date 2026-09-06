using System.Text.Json;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Tests.UnitTests.Caching;

[TestFixture]
public class ProfileFileMapperFocusRuleTests
{
	[Test]
	public void ToFolderFile_FolderWithNoRules_ProducesNullFocusRules()
	{
		var folder = NewFolder();

		var file = ProfileFileMapper.ToFolderFile(folder);

		Assert.That(file.FocusRules, Is.Null);
	}

	[Test]
	public void ToFolderFile_NullFocusRules_IsOmittedFromTheSerializedJson()
	{
		var folder = NewFolder();

		var file = ProfileFileMapper.ToFolderFile(folder);
		var json = JsonSerializer.Serialize(file, PersistenceJsonOptions.Default);

		Assert.That(json, Does.Not.Contain("focusRules"));
	}

	[Test]
	public void ToFolderFile_FolderWithRules_MapsEveryField()
	{
		var deviceId = Guid.NewGuid();
		var ruleId = Guid.NewGuid();
		var folder = NewFolder();
		folder.FocusRules.Add(new FolderFocusRule
		{
			Id = ruleId,
			Enabled = false,
			ApplicationIdentity = "com.example.App",
			IdentityKind = ApplicationIdentityKind.BundleId,
			DeviceId = deviceId,
			ReturnOnFocusLoss = true
		});

		var file = ProfileFileMapper.ToFolderFile(folder);

		Assert.That(file.FocusRules, Is.Not.Null);
		var mapped = file.FocusRules!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(mapped.Id, Is.EqualTo(ruleId));
			Assert.That(mapped.Enabled, Is.False);
			Assert.That(mapped.ApplicationIdentity, Is.EqualTo("com.example.App"));
			Assert.That(mapped.IdentityKind, Is.EqualTo(ApplicationIdentityKind.BundleId));
			Assert.That(mapped.DeviceId, Is.EqualTo(deviceId));
			Assert.That(mapped.ReturnOnFocusLoss, Is.True);
		});
	}

	[Test]
	public void ToFolderEntity_AbsentFocusRules_ProducesAnEmptyList()
	{
		var file = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "P",
			Folders = [NewProfileFolder()]
		};

		var entities = ProfileFileMapper.ToFolderEntities(file).ToList();

		Assert.That(entities.Single().FocusRules, Is.Empty);
	}

	[Test]
	public void ToFolderEntity_FolderWithRules_RoundTripsEveryField()
	{
		var deviceId = Guid.NewGuid();
		var ruleId = Guid.NewGuid();
		var profileFolder = NewProfileFolder();
		profileFolder.FocusRules =
		[
			new ProfileFolderFocusRule
			{
				Id = ruleId,
				Enabled = true,
				ApplicationIdentity = "notepad",
				IdentityKind = ApplicationIdentityKind.ProcessName,
				DeviceId = deviceId,
				ReturnOnFocusLoss = false
			}
		];
		var file = new ProfileFile { Id = Guid.NewGuid(), Name = "P", Folders = [profileFolder] };

		var entities = ProfileFileMapper.ToFolderEntities(file).ToList();

		var rule = entities.Single().FocusRules.Single();
		Assert.Multiple(() =>
		{
			Assert.That(rule.Id, Is.EqualTo(ruleId));
			Assert.That(rule.Enabled, Is.True);
			Assert.That(rule.ApplicationIdentity, Is.EqualTo("notepad"));
			Assert.That(rule.IdentityKind, Is.EqualTo(ApplicationIdentityKind.ProcessName));
			Assert.That(rule.DeviceId, Is.EqualTo(deviceId));
		});
	}

	private static FolderEntity NewFolder()
		=> new()
		{
			Id = Guid.NewGuid(), ProfileId = Guid.NewGuid(), Name = "Folder", Order = 0, Rows = 3, Columns = 5
		};

	private static ProfileFolder NewProfileFolder()
		=> new() { Id = Guid.NewGuid(), Name = "Folder", Order = 0, Rows = 3, Columns = 5 };
}
