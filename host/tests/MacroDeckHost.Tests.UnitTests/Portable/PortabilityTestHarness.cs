using System.Globalization;
using System.Text;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Portable;

internal sealed class PortabilityTestHarness : IDisposable
{
	public IconTestHarness Icons { get; } = new();
	public ProfileCache ProfileCache { get; }
	public FolderCache FolderCache { get; }
	public ScriptCache ScriptCache { get; }
	public ScriptService Scripts { get; }
	public FakeSecretService Secrets { get; } = new();
	public ConfigurableIntegrationRegistry Integrations { get; }
	public VariableRegistry VariableRegistry { get; } = new();
	public IVariableService Variables { get; }
	public WidgetVariableCloner VariableCloner { get; }
	public PortableAssetManager AssetManager { get; }
	public ProfilePortabilityService ProfileService { get; }
	public WidgetPortabilityService WidgetService { get; }
	public FolderPortabilityService FolderService { get; }

	public PortabilityTestHarness()
	{
		ProfileCache = new ProfileCache(new InMemoryProfileStore(), Icons.Logger);
		ProfileCache.InitializeCache().GetAwaiter().GetResult();
		FolderCache = new FolderCache(ProfileCache);
		ScriptCache = new ScriptCache(new InMemoryScriptStore(), Icons.Logger);
		ScriptCache.InitializeCache().GetAwaiter().GetResult();
		Scripts = new ScriptService(ScriptCache, Icons.Mediator);
		Integrations = new ConfigurableIntegrationRegistry([
			StubIntegration.Create("app.macro-deck.obs", "OBS Studio", configFlow: true),
			StubIntegration.Create("app.macro-deck.keyboard", "Keyboard"),
			StubIntegration.Create("app.macro-deck.music-player", "Music Player", system: true)
		]);
		Variables = TestVariableServices.Create(VariableRegistry, new NullUserVariableStore(), Icons.Mediator);
		VariableCloner = new WidgetVariableCloner(Variables, Icons.Logger);
		AssetManager = new PortableAssetManager(Icons.Cache,
			Icons.Storage,
			Secrets,
			Scripts,
			Integrations,
			VariableCloner,
			Icons.Mediator,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Icons.Logger);
		ProfileService
			= new ProfilePortabilityService(ProfileCache, FolderCache, AssetManager, Icons.Mediator, Icons.Logger);
		WidgetService
			= new WidgetPortabilityService(FolderCache, ProfileCache, AssetManager, Icons.Mediator, Icons.Logger);
		FolderService
			= new FolderPortabilityService(ProfileCache, FolderCache, AssetManager, Icons.Mediator, Icons.Logger);
	}

	public async Task<Guid> AddWidgetVariable(
		Guid widgetId,
		string name,
		VariableClassification classification,
		VariableType type = VariableType.Text,
		object? value = null,
		int? decimalPlaces = null)
	{
		var scopeRefId = widgetId.ToString();
		if (classification == VariableClassification.Widget)
		{
			await Variables.UpsertWidgetVariable(VariableScope.Widget, scopeRefId, name, type, value);
			return VariableRegistry.FindByName(VariableScope.Widget, scopeRefId, name)!.Id;
		}

		if (classification == VariableClassification.Integration)
		{
			var result = await Variables.CreateIntegrationVariable("app.macro-deck.obs",
				name,
				VariableScope.Widget,
				scopeRefId,
				type,
				value,
				decimalPlaces);
			return result.Data!.Id;
		}

		var created = await Variables.CreateUserVariable(name,
			VariableScope.Widget,
			scopeRefId,
			type,
			value,
			decimalPlaces);
		return created.Data!.Id;
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	public async Task<IconEntity> AddReadyIcon(Guid packId, string name, int[] sizes)
	{
		var master = MasterBytesFor(name);
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			Name = name,
			SourceContentHash = SourceContentHash.Compute(Encoding.UTF8.GetBytes($"source:{name}")).Value,
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = sizes,
			CreatedAt = DateTime.UtcNow
		};
		await Icons.Cache.AddIcons(packId, [icon]);
		await Icons.Storage.WriteVariant(packId, icon.Id, IconVariants.Master, master, CancellationToken.None);
		foreach (var size in sizes)
		{
			await Icons.Storage.WriteVariant(packId,
				icon.Id,
				size.ToString(CultureInfo.InvariantCulture),
				Encoding.UTF8.GetBytes($"{name}:{size}"),
				CancellationToken.None);
		}

		return icon;
	}

	public static byte[] MasterBytesFor(string name) => Encoding.UTF8.GetBytes($"master:{name}");

	public async Task<(ProfileEntity Profile, FolderEntity Folder, WidgetEntity Widget)> SeedProfile(string? widgetData)
	{
		var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = "Source", CreatedAt = DateTime.UtcNow };
		await ProfileCache.AddOrUpdate(profile);

		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profile.Id,
			Name = "Root",
			Order = 0,
			Rows = 3,
			Columns = 5,
			IsDefault = true
		};
		await FolderCache.AddOrUpdate(folder);

		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folder.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = widgetData
		};
		FolderCache.AddWidget(folder.Id, widget);

		return (profile, folder, widget);
	}

	public async Task<ProfileEntity> AddProfile(string name)
	{
		var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
		await ProfileCache.AddOrUpdate(profile);
		return profile;
	}

	public async Task<FolderEntity> AddFolder(Guid profileId,
		string name,
		Guid? parentId = null,
		int rows = 3,
		int columns = 5,
		int order = 1)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profileId,
			Name = name,
			ParentId = parentId,
			Order = order,
			Rows = rows,
			Columns = columns
		};
		await FolderCache.AddOrUpdate(folder);
		return folder;
	}

	public WidgetEntity AddWidget(Guid folderId,
		string? data = null,
		int x = 0,
		int y = 0,
		int width = 1,
		int height = 1,
		bool isPinned = false)
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = width,
			Height = height,
			Data = data,
			IsPinned = isPinned
		};
		FolderCache.AddWidget(folderId, widget);
		return widget;
	}

	public void Dispose()
	{
		ProfileCache.Dispose();
		ScriptCache.Dispose();
		Icons.Dispose();
	}
}
