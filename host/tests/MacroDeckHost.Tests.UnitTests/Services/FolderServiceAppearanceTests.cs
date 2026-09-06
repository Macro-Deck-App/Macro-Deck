using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceAppearanceTests
{
	private ProfileCache _cache = null!;
	private FolderService _service = null!;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
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
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Update_SetsWidgetSpacingAndBorderRadius()
	{
		var result = await Update(widgetSpacing: 20, widgetBorderRadius: 30);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.WidgetSpacing, Is.EqualTo(20));
			Assert.That(result.Data!.WidgetBorderRadius, Is.EqualTo(30));
		});
	}

	[Test]
	public async Task Update_MinusOneClearsValuesBackToInherit()
	{
		await Update(widgetSpacing: 20, widgetBorderRadius: 30);

		var result = await Update(widgetSpacing: -1, widgetBorderRadius: -1);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.WidgetSpacing, Is.Null);
			Assert.That(result.Data!.WidgetBorderRadius, Is.Null);
		});
	}

	[Test]
	public async Task Update_NullLeavesValuesUnchanged()
	{
		await Update(widgetSpacing: 20, widgetBorderRadius: 30);

		var result = await Update();

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.WidgetSpacing, Is.EqualTo(20));
			Assert.That(result.Data!.WidgetBorderRadius, Is.EqualTo(30));
		});
	}

	[TestCase(-2)]
	[TestCase(41)]
	public async Task Update_RejectsOutOfRangeWidgetSpacing(int value)
	{
		var result = await Update(widgetSpacing: value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
		});
	}

	[TestCase(-2)]
	[TestCase(61)]
	public async Task Update_RejectsOutOfRangeWidgetBorderRadius(int value)
	{
		var result = await Update(widgetBorderRadius: value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
		});
	}

	[Test]
	public async Task Duplicate_CopiesWidgetSpacingAndBorderRadius()
	{
		await Update(widgetSpacing: 8, widgetBorderRadius: 16);

		var result = await _service.Duplicate(_folderId);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.WidgetSpacing, Is.EqualTo(8));
			Assert.That(result.Data!.WidgetBorderRadius, Is.EqualTo(16));
		});
	}

	[Test]
	public async Task Update_MinusOneClearsRowsAndColumnsBackToInherit()
	{
		var result = await Update(rows: -1, columns: -1);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Rows, Is.Null);
			Assert.That(result.Data!.Columns, Is.Null);
		});
	}

	[Test]
	public async Task Duplicate_KeepsAnInheritedGrid()
	{
		await Update(rows: -1, columns: -1);

		var result = await _service.Duplicate(_folderId);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Rows, Is.Null);
			Assert.That(result.Data!.Columns, Is.Null);
		});
	}

	private Task<Domain.Common.Result<FolderEntity, FolderError>> Update(
		int? rows = null,
		int? columns = null,
		int? widgetSpacing = null,
		int? widgetBorderRadius = null)
		=> _service.Update(_folderId, null, null, null, rows, columns, null, widgetSpacing, widgetBorderRadius, null);
}
