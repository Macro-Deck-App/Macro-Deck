using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ColorBackgroundSourceTests
{
	private const string Reference = "{{ vars.brand | color | color_darken: 20 }}";

	private ProfileCache _cache = null!;
	private FolderCache _folders = null!;
	private UpdateProfileRequestMessageHandler _updateProfile = null!;
	private UpdateFolderRequestMessageHandler _updateFolder = null!;
	private Guid _profileId;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folders = new FolderCache(_cache);
		var secrets = new FakeSecretService();
		var folderService = new FolderService(_folders,
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
		var profileService = new ProfileService(_cache,
			_folders,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());

		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "brand",
			Scope = VariableScope.Global,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = "#3366ff"
		});
		var colors = new ColorReferenceResolver(registry);

		_updateProfile = new UpdateProfileRequestMessageHandler(profileService, colors, _cache);
		_updateFolder = new UpdateFolderRequestMessageHandler(folderService, colors, _folders);

		_profileId = (await profileService.Create("Gaming", defaultRows: 3, defaultColumns: 5)).Data!.Id;
		_folderId = _folders.GetFoldersByProfileId(_profileId).Single().Id;
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task A_profile_background_reference_is_shown_resolved_and_survives_a_rename()
	{
		var set = await _updateProfile.Handle(
			new UpdateProfileRequest { Id = _profileId.ToString(), DefaultBackgroundColorSource = Reference },
			CancellationToken.None);

		var renamed = await _updateProfile.Handle(new UpdateProfileRequest
			{
				Id = _profileId.ToString(),
				Name = "Streaming",
				DefaultBackgroundColor = set.Profile!.DefaultBackgroundColor
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(set.Profile.DefaultBackgroundColor, Is.EqualTo("#003df5"));
			Assert.That(set.Profile.DefaultBackgroundColorSource, Is.EqualTo(Reference));
			Assert.That(renamed.Profile!.Name, Is.EqualTo("Streaming"));
			Assert.That(renamed.Profile.DefaultBackgroundColorSource, Is.EqualTo(Reference));
			Assert.That(_cache.GetById(_profileId)!.DefaultBackgroundColor, Is.EqualTo(Reference));
		});
	}

	[Test]
	public async Task A_folder_background_reference_is_kept_until_a_different_colour_is_chosen()
	{
		await _updateFolder.Handle(new UpdateFolderRequest { Id = _folderId.ToString(), BackgroundColorSource = Reference },
			CancellationToken.None);

		var echoed = await _updateFolder.Handle(
			new UpdateFolderRequest { Id = _folderId.ToString(), Name = "Home", BackgroundColor = "#003DF5" },
			CancellationToken.None);
		var replaced = await _updateFolder.Handle(
			new UpdateFolderRequest { Id = _folderId.ToString(), BackgroundColor = "#ff0000" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(echoed.Folder!.BackgroundColor, Is.EqualTo("#003df5"));
			Assert.That(echoed.Folder.BackgroundColorSource, Is.EqualTo(Reference));
			Assert.That(replaced.Folder!.BackgroundColor, Is.EqualTo("#ff0000"));
			Assert.That(replaced.Folder.BackgroundColorSource, Is.Null);
		});
	}
}
