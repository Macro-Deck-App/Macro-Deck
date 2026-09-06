using System.Text.RegularExpressions;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceSecretTests
{
	private ProfileCache _cache = null!;
	private FakeSecretService _secrets = null!;
	private WidgetService _service = null!;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_secrets = new FakeSecretService();
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(_secrets),
			new WidgetSecretCloner(_secrets),
			new NullWidgetVariableCloner());

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
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_ClonesReferencedSecretsSoTheNewWidgetOwnsItsOwn()
	{
		var original = _secrets.Store("pw");
		var widget = Widget(1, 0, $"{{\"$secret\":\"{original}\"}}");

		var result = await _service.Create(_folderId, widget);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data!.Data, Does.Not.Contain(original.ToString()));
		Assert.That(await _secrets.Resolve(original), Is.EqualTo("pw"));
	}

	[Test]
	public async Task Create_WithoutSecretReferences_LeavesDataUnchanged()
	{
		var result = await _service.Create(_folderId, Widget(1, 0, "{\"label\":\"hi\"}"));

		Assert.That(result.Data!.Data, Is.EqualTo("{\"label\":\"hi\"}"));
	}

	[Test]
	public async Task PasteAsMove_ThenDeleteSource_KeepsThePastedWidgetsSecret()
	{
		var s = _secrets.Store("api-key");
		var source = Widget(0, 0, $"{{\"$secret\":\"{s}\"}}");
		source.Id = Guid.NewGuid();
		source.FolderId = _folderId;
		_cache.AddWidget(_folderId, source);

		var created = await _service.Create(_folderId, Widget(1, 0, source.Data!));
		Assert.That(created.Success, Is.True);

		await _service.Delete(source.Id, _folderId);

		var pastedSecret = SingleSecretId(created.Data!.Data!);
		Assert.Multiple(() =>
		{
			Assert.That(_secrets.Resolve(pastedSecret).Result, Is.EqualTo("api-key"));
			Assert.That(_secrets.Resolve(s).Result, Is.Null);
		});
	}

	private static WidgetEntity Widget(int x, int y, string data) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1,
		Data = data
	};

	private static Guid SingleSecretId(string data)
		=> Regex.Matches(data, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
			.Select(m => Guid.Parse(m.Value))
			.Single();
}
