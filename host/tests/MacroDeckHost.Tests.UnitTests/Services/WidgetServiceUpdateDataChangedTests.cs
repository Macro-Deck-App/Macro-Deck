using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

/// <summary>
/// A save reports whether it changed the widget's configuration, because that is what decides whether an
/// open session drawing that widget has to be built again. Dragging a widget publishes the same
/// notification on every step, and rebuilding a tree for a move would cost the deck its artwork and its
/// animations mid-drag.
/// </summary>
[TestFixture]
public class WidgetServiceUpdateDataChangedTests
{
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private WidgetService _service = null!;
	private Guid _profileId;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		var secrets = new FakeSecretService();
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			_mediator,
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());

		_profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = _profileId,
			Name = "F",
			Order = 0,
			Rows = 4,
			Columns = 4
		});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task ReportsAConfigurationChange()
	{
		var stored = Widget("""{"label":"Value"}""");
		_cache.AddWidget(_folderId, stored);
		_mediator.Published.Clear();

		await _service.Update(Copy(stored, """{"label":"Volume"}"""));

		Assert.That(Published().DataChanged, Is.True);
	}

	[Test]
	public async Task ReportsNoConfigurationChangeForAMove()
	{
		var stored = Widget("""{"label":"Volume"}""");
		_cache.AddWidget(_folderId, stored);
		_mediator.Published.Clear();

		var moved = Copy(stored, stored.Data);
		moved.PositionX = 2;

		await _service.Update(moved);

		Assert.That(Published().DataChanged, Is.False);
	}

	/// <summary>A caller that loaded the stored entity, changed it and handed the same instance back has
	/// nothing left to compare against - so the save counts as a configuration change rather than being
	/// measured against itself.</summary>
	[Test]
	public async Task ReportsAChangeWhenTheStoredWidgetWasEditedInPlace()
	{
		var stored = Widget("""{"label":"Value"}""");
		_cache.AddWidget(_folderId, stored);
		_mediator.Published.Clear();

		stored.Data = """{"label":"Volume"}""";
		await _service.Update(stored);

		Assert.That(Published().DataChanged, Is.True);
	}

	private WidgetUpdatedNotification Published()
		=> _mediator.Published.OfType<WidgetUpdatedNotification>().Single();

	private WidgetEntity Widget(string data) => new()
	{
		Id = Guid.NewGuid(),
		FolderId = _folderId,
		Type = WidgetTypeIds.Slider,
		PositionX = 0,
		PositionY = 0,
		Width = 1,
		Height = 1,
		Data = data
	};

	private static WidgetEntity Copy(WidgetEntity source, string? data) => new()
	{
		Id = source.Id,
		FolderId = source.FolderId,
		Type = source.Type,
		PositionX = source.PositionX,
		PositionY = source.PositionY,
		Width = source.Width,
		Height = source.Height,
		Data = data
	};
}
