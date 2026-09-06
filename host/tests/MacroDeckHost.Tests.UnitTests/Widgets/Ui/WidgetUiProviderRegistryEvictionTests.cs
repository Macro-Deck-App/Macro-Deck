using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// <see cref="WidgetUiProviderRegistry" /> caches one adapter per synthetic per-widget provider id
/// (<c>widget:&lt;id&gt;:*</c>, <c>widget-config:&lt;id&gt;:*</c>) and nothing ever removed an entry - a
/// deck's worth of deleted widgets would otherwise accumulate adapters for the life of the host.
/// </summary>
[TestFixture]
public class WidgetUiProviderRegistryEvictionTests
{
	[Test]
	public void EvictWidget_forgets_the_live_and_config_adapters_it_cached_for_that_widget()
	{
		var widgetId = Guid.NewGuid();
		var folderCache = new SingleWidgetFolderCache(widgetId, "gauge");
		var registry = new WidgetUiProviderRegistry(folderCache,
			[new StubWidgetUiProvider("gauge")],
			() => null!,
			Serilog.Core.Logger.None);

		var liveProviderId = WidgetUiProviderRegistry.ProviderIdFor(widgetId, "principal");
		var configProviderId = WidgetUiProviderRegistry.ConfigProviderIdFor(widgetId, "principal");

		var liveBefore = registry.Resolve(liveProviderId);
		var configBefore = registry.Resolve(configProviderId);
		Assert.That(liveBefore, Is.Not.Null, "the fixture must actually populate the cache before evicting it");
		Assert.That(configBefore, Is.Not.Null);

		registry.EvictWidget(widgetId);

		// Resolving again after eviction re-reads the folder cache rather than the evicted entry, so a
		// fresh adapter (not the disposed one) proves it was actually forgotten, not merely reused.
		Assert.Multiple(() =>
		{
			Assert.That(registry.Resolve(liveProviderId), Is.Not.SameAs(liveBefore));
			Assert.That(registry.Resolve(configProviderId), Is.Not.SameAs(configBefore));
		});
	}

	[Test]
	public void EvictWidget_leaves_another_widgets_adapters_alone()
	{
		var evicted = Guid.NewGuid();
		var kept = Guid.NewGuid();
		var folderCache = new SingleWidgetFolderCache(evicted, "gauge", kept, "gauge");
		var registry = new WidgetUiProviderRegistry(folderCache,
			[new StubWidgetUiProvider("gauge")],
			() => null!,
			Serilog.Core.Logger.None);

		var keptProviderId = WidgetUiProviderRegistry.ProviderIdFor(kept, "principal");
		var keptBefore = registry.Resolve(keptProviderId);

		registry.EvictWidget(evicted);

		Assert.That(registry.Resolve(keptProviderId), Is.SameAs(keptBefore));
	}

	private sealed class StubWidgetUiProvider : IBuiltInWidgetUiProvider
	{
		public StubWidgetUiProvider(string widgetType)
		{
			WidgetTypeId = widgetType;
			Surfaces = [new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];
		}

		public string WidgetTypeId { get; }

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; }

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult<IUiSession?>(null);
	}

	private sealed class SingleWidgetFolderCache : IFolderCache
	{
		private readonly List<WidgetEntity> _widgets = [];

		public SingleWidgetFolderCache(params object[] idsAndTypes)
		{
			for (var i = 0; i < idsAndTypes.Length; i += 2)
			{
				_widgets.Add(new WidgetEntity
				{
					Id = (Guid)idsAndTypes[i], Type = (string)idsAndTypes[i + 1], FolderId = Guid.NewGuid()
				});
			}
		}

		public Task InitializeCache() => throw new NotSupportedException();

		public FolderEntity? GetFolderById(Guid id) => throw new NotSupportedException();

		public List<FolderEntity> GetAllFolders()
			=>
			[
				new()
					{ Id = Guid.NewGuid(), Name = "Folder", Order = 0, Widgets = _widgets }
			];

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => throw new NotSupportedException();

		public Task AddOrUpdate(FolderEntity folder) => throw new NotSupportedException();

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => throw new NotSupportedException();

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId) => throw new NotSupportedException();

		public void AddWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => throw new NotSupportedException();

		public void UpdateWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
			=> throw new NotSupportedException();

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public void RemoveWidget(Guid folderId, Guid widgetId) => throw new NotSupportedException();

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds) => throw new NotSupportedException();

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
			=> throw new NotSupportedException();
	}
}
