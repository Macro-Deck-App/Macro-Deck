using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class PinScopeFixture : IDisposable
{
	public InMemoryProfileStore Store { get; }
	public ProfileCache Cache { get; }
	public FolderCache FolderCache { get; }
	public RecordingMediator Mediator { get; }
	public WidgetService WidgetService { get; }
	public FolderService FolderService { get; }
	public Guid ProfileId { get; }

	public FolderEntity Main { get; private set; } = null!;
	public FolderEntity Games { get; private set; } = null!;
	public FolderEntity Retro { get; private set; } = null!;
	public FolderEntity Media { get; private set; } = null!;
	public FolderEntity Work { get; private set; } = null!;
	public FolderEntity Mail { get; private set; } = null!;

	public PinScopeFixture()
	{
		Store = new InMemoryProfileStore();
		Cache = new ProfileCache(Store, new LoggerConfiguration().CreateLogger());
		Cache.InitializeCache().GetAwaiter().GetResult();
		FolderCache = new FolderCache(Cache);
		Mediator = new RecordingMediator();
		var secrets = new FakeSecretService();
		WidgetService = new WidgetService(FolderCache,
			Cache,
			Mediator,
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());
		FolderService = new FolderService(FolderCache,
			Cache,
			new InMemoryDeviceRepository(),
			Mediator,
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());

		ProfileId = Guid.NewGuid();
		Cache.AddOrUpdate(new ProfileEntity { Id = ProfileId, Name = "P", DefaultRows = 4, DefaultColumns = 4 })
			.GetAwaiter()
			.GetResult();

		Main = AddFolder("Main", null);
		Games = AddFolder("Games", Main.Id);
		Retro = AddFolder("Retro", Games.Id);
		Media = AddFolder("Media", Main.Id);
		Work = AddFolder("Work", null);
		Mail = AddFolder("Mail", Work.Id);
	}

	public FolderEntity AddFolder(string name, Guid? parentId, int rows = 4, int columns = 4)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = ProfileId,
			Name = name,
			ParentId = parentId,
			Order = 0,
			Rows = rows,
			Columns = columns,
			CreatedAt = DateTime.UtcNow
		};
		FolderCache.AddOrUpdate(folder).GetAwaiter().GetResult();
		return folder;
	}

	public WidgetEntity AddWidget(FolderEntity folder, int x, int y, int width = 1, int height = 1)
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folder.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = width,
			Height = height,
			CreatedAt = DateTime.UtcNow
		};
		FolderCache.AddWidget(folder.Id, widget);
		return widget;
	}

	public FolderEntity Reload(FolderEntity folder) => Cache.GetFolderById(folder.Id)!;

	public void Dispose() => Cache.Dispose();
}
