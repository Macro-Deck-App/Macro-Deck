using System.Text.RegularExpressions;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceDuplicateSecretTests
{
	private ProfileCache _cache = null!;
	private FakeSecretService _secrets = null!;
	private FolderService _service = null!;
	private Guid _folderId;
	private Guid _originalSecret;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(_secrets),
			new WidgetSecretScrubber(_secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 4,
			Columns = 4
		});

		_originalSecret = _secrets.Store("secret-value");
		_cache.AddWidget(_folderId,
			new WidgetEntity
			{
				Id = Guid.NewGuid(),
				Type = WidgetTypeIds.ActionButton,
				PositionX = 0,
				PositionY = 0,
				Width = 1,
				Height = 1,
				Data = $"{{\"$secret\":\"{_originalSecret}\"}}",
				FolderId = _folderId
			});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Duplicate_ClonesWidgetSecretsSoTheCopyDoesNotShareThem()
	{
		var result = await _service.Duplicate(_folderId);

		Assert.That(result.Success, Is.True);
		var copiedWidget = result.Data!.Widgets.Single();
		Assert.That(copiedWidget.Data, Does.Not.Contain(_originalSecret.ToString()));

		var copiedSecret = SingleSecretId(copiedWidget.Data!);
		Assert.Multiple(() =>
		{
			Assert.That(copiedSecret, Is.Not.EqualTo(_originalSecret));
			Assert.That(_secrets.Resolve(copiedSecret).Result, Is.EqualTo("secret-value"));
			Assert.That(_secrets.Resolve(_originalSecret).Result, Is.EqualTo("secret-value"));
		});
	}

	private static Guid SingleSecretId(string data)
		=> Regex.Matches(data, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
			.Select(m => Guid.Parse(m.Value))
			.Single();
}
