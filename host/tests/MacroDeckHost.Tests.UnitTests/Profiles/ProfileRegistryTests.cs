using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Profiles;
using Serilog;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

[TestFixture]
public class ProfileRegistryTests
{
	private static VirtualProfileDescriptor CarThing(out string folderId, out string widgetId)
	{
		folderId = "main";
		widgetId = "play";
		return new VirtualProfileDescriptor("car-thing",
			"Car Thing",
			ProfileLayout.Grid(2, 4),
			[
				new VirtualFolderDescriptor("main",
					"Main",
					[new VirtualWidgetDescriptor("play", "ActionButton", 0, 0)])
			]);
	}

	private static async Task<ProfileRegistry> BuildRegistry(
		InMemoryProfileStore store,
		params FakeProfileProviderIntegration[] integrations)
	{
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		await cache.InitializeCache();
		var folderCache = new FolderCache(cache);
		var integrationRegistry = new ConfigurableIntegrationRegistry(integrations);
		return new ProfileRegistry(cache,
			folderCache,
			integrationRegistry,
			new WidgetTypeRegistry(new RecordingMediator()),
			TestDeviceLayoutConstraintTracker.Build(),
			new LoggerConfiguration().CreateLogger());
	}

	[Test]
	public async Task GetProfiles_MergesJsonAndVirtualProfiles()
	{
		var jsonId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile { Id = jsonId, Name = "Default", Order = 0 });
		var integration = new FakeProfileProviderIntegration("spotify", CarThing(out _, out _));
		var registry = await BuildRegistry(store, integration);

		var profiles = registry.GetProfiles();

		Assert.Multiple(() =>
		{
			Assert.That(profiles, Has.Count.EqualTo(2));
			Assert.That(profiles.Any(p => p.Id == jsonId.ToString() && !p.IsVirtual), Is.True);
			var virtualProfile = profiles.Single(p => p.IsVirtual);
			Assert.That(virtualProfile.Id, Is.EqualTo("spotify::car-thing"));
			Assert.That(virtualProfile.SourceIntegrationId, Is.EqualTo("spotify"));
			Assert.That(virtualProfile.Layout.ColumnsLocked, Is.True);
			Assert.That(virtualProfile.Layout.Columns, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task GetProfiles_ExcludesDisabledProviders()
	{
		var store = new InMemoryProfileStore();
		var integration = new FakeProfileProviderIntegration("spotify", CarThing(out _, out _));
		var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		await cache.InitializeCache();
		var registry = new ProfileRegistry(cache,
			new FolderCache(cache),
			new ConfigurableIntegrationRegistry([integration], disabled: ["spotify"]),
			new WidgetTypeRegistry(new RecordingMediator()),
			TestDeviceLayoutConstraintTracker.Build(),
			new LoggerConfiguration().CreateLogger());

		Assert.That(registry.GetProfiles(), Is.Empty);
	}

	[Test]
	public async Task GetFoldersForProfile_Virtual_PrefixesFolderAndWidgetIds()
	{
		var store = new InMemoryProfileStore();
		var integration = new FakeProfileProviderIntegration("spotify", CarThing(out _, out _));
		var registry = await BuildRegistry(store, integration);

		var folders = registry.GetFoldersForProfile("spotify::car-thing");

		Assert.That(folders, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(folders[0].Id, Is.EqualTo("spotify::main"));
			Assert.That(folders[0].ProfileId, Is.EqualTo("spotify::car-thing"));
			Assert.That(folders[0].Columns, Is.EqualTo(4));
			Assert.That(folders[0].Widgets, Has.Count.EqualTo(1));
			Assert.That(folders[0].Widgets[0].Id, Is.EqualTo("spotify::play"));
			Assert.That(folders[0].Widgets[0].Type, Is.EqualTo(WidgetTypeIds.ActionButton));
		});
	}

	[Test]
	public async Task GetFoldersForProfile_Json_ReturnsCachedFolders()
	{
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "Default",
			Folders = [new ProfileFolder { Id = folderId, Name = "Main", Rows = 3, Columns = 5 }]
		});
		var registry = await BuildRegistry(store);

		var folders = registry.GetFoldersForProfile(profileId.ToString());

		Assert.That(folders.Select(f => f.Id), Is.EquivalentTo([folderId.ToString()]));
	}

	[Test]
	public async Task RouteWidgetInteraction_RoutesToProviderForVirtualWidget()
	{
		var store = new InMemoryProfileStore();
		var integration = new FakeProfileProviderIntegration("spotify", CarThing(out _, out _));
		var registry = await BuildRegistry(store, integration);

		var routed = await registry.RouteWidgetInteraction("spotify::main",
			"spotify::play",
			new WidgetInteraction("press"));

		Assert.Multiple(() =>
		{
			Assert.That(routed, Is.True);
			Assert.That(integration.Interactions, Has.Count.EqualTo(1));
			Assert.That(integration.Interactions[0].WidgetId, Is.EqualTo("play"));
			Assert.That(integration.Interactions[0].FolderId, Is.EqualTo("main"));
			Assert.That(integration.Interactions[0].Interaction.TriggerType, Is.EqualTo("press"));
		});
	}

	[Test]
	public async Task RouteWidgetInteraction_ReturnsFalseForNonVirtualWidget()
	{
		var registry = await BuildRegistry(new InMemoryProfileStore());

		var routed = await registry.RouteWidgetInteraction(Guid.NewGuid().ToString(),
			Guid.NewGuid().ToString(),
			new WidgetInteraction("press"));

		Assert.That(routed, Is.False);
	}
}
