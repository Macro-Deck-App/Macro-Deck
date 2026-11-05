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
			new LoggerConfiguration().CreateLogger(),
			TestColors.None);
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
	public async Task GetProfiles_ListsEqualOrdersByName_AndPluginProfilesLast()
	{
		var alpha = Guid.NewGuid();
		var beta = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile { Id = beta, Name = "beta", Order = 3 },
			new ProfileFile { Id = alpha, Name = "Alpha", Order = 3 });
		var registry
			= await BuildRegistry(store, new FakeProfileProviderIntegration("spotify", CarThing(out _, out _)));

		var profiles = registry.GetProfiles();

		Assert.That(profiles.Select(p => p.Id),
			Is.EqualTo(new[] { alpha.ToString(), beta.ToString(), "spotify::car-thing" }));
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
			new ConfigurableIntegrationRegistry([integration], ["spotify"]),
			new WidgetTypeRegistry(new RecordingMediator()),
			TestDeviceLayoutConstraintTracker.Build(),
			new LoggerConfiguration().CreateLogger(),
			TestColors.None);

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
			Assert.That(folders[0].Widgets[0].Id, Is.EqualTo("spotify::car-thing:main:play"));
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
			"spotify::car-thing:main:play",
			new WidgetInteraction("press"));

		Assert.Multiple(() =>
		{
			Assert.That(routed, Is.True);
			Assert.That(integration.Interactions, Has.Count.EqualTo(1));
			Assert.That(integration.Interactions[0].WidgetId, Is.EqualTo("play"));
			Assert.That(integration.Interactions[0].FolderId, Is.EqualTo("main"));
			Assert.That(integration.Interactions[0].ProfileId, Is.EqualTo("car-thing"));
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

	[Test]
	public async Task A_widget_id_reused_in_two_folders_names_each_widget_and_routes_to_its_own_folder()
	{
		var mixer = new VirtualProfileDescriptor("mixer",
			"Mixer",
			ProfileLayout.Grid(1, 1),
			[
				new VirtualFolderDescriptor("page-1",
					"Page 1",
					[new VirtualWidgetDescriptor("up-0", "ActionButton", 0, 0, Data: """{"label":"one"}""")]),
				new VirtualFolderDescriptor("page-2",
					"Page 2",
					[new VirtualWidgetDescriptor("up-0", "ActionButton", 0, 0, Data: """{"label":"two"}""")])
			]);
		var integration = new FakeProfileProviderIntegration("volume", mixer);
		var registry = await BuildRegistry(new InMemoryProfileStore(), integration);

		var ids = registry.GetFoldersForProfile("volume::mixer").Select(folder => folder.Widgets.Single().Id).ToList();
		await registry.RouteWidgetInteraction(string.Empty, ids[1], new WidgetInteraction("onShortPress"));

		Assert.Multiple(() =>
		{
			Assert.That(ids, Is.Unique);
			Assert.That(registry.FindVirtualWidget(ids[0])?.Data, Is.EqualTo("""{"label":"one"}"""));
			Assert.That(registry.FindVirtualWidget(ids[1])?.Data, Is.EqualTo("""{"label":"two"}"""));
			Assert.That(integration.Interactions.Single(),
				Is.EqualTo(("mixer", "page-2", "up-0", new WidgetInteraction("onShortPress"))));
		});
	}

	[TestCase("a:", ":b")]
	[TestCase("a%3A", "b")]
	[TestCase("a", "%b")]
	public async Task Local_ids_containing_the_separator_or_the_escape_stay_distinct_and_round_trip(
		string firstFolder,
		string secondFolder)
	{
		var profile = new VirtualProfileDescriptor("p",
			"P",
			ProfileLayout.Grid(1, 1),
			[
				new VirtualFolderDescriptor(firstFolder,
					"First",
					[new VirtualWidgetDescriptor(":w", "ActionButton", 0, 0)]),
				new VirtualFolderDescriptor(secondFolder,
					"Second",
					[new VirtualWidgetDescriptor(":w", "ActionButton", 0, 0)])
			]);
		var integration = new FakeProfileProviderIntegration("int", profile);
		var registry = await BuildRegistry(new InMemoryProfileStore(), integration);

		var ids = registry.GetFoldersForProfile("int::p").Select(folder => folder.Widgets.Single().Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Has.Count.EqualTo(2).And.Unique);
			Assert.That(registry.FindVirtualWidget(ids[0]), Is.Not.Null);
			Assert.That(VirtualWidgetIds.TryParse(ids[0], out _, out _, out var folder0, out _), Is.True);
			Assert.That(folder0, Is.EqualTo(firstFolder));
			Assert.That(VirtualWidgetIds.TryParse(ids[1], out _, out _, out var folder1, out var widget1), Is.True);
			Assert.That(folder1, Is.EqualTo(secondFolder));
			Assert.That(widget1, Is.EqualTo(":w"));
		});
	}

	[Test]
	public async Task A_widget_whose_combined_id_is_too_long_is_skipped()
	{
		var longId = new string('x', 200);
		var profile = new VirtualProfileDescriptor("p",
			"P",
			ProfileLayout.Grid(1, 2),
			[
				new VirtualFolderDescriptor(longId,
					"Main",
					[
						new VirtualWidgetDescriptor(longId, "ActionButton", 0, 0),
						new VirtualWidgetDescriptor("short", "ActionButton", 1, 0)
					])
			]);
		var registry
			= await BuildRegistry(new InMemoryProfileStore(), new FakeProfileProviderIntegration("int", profile));

		var widgets = registry.GetFoldersForProfile("int::p").Single().Widgets;

		Assert.That(widgets.Select(widget => widget.PositionX), Is.EqualTo(new[] { 1 }));
	}

	[Test]
	public async Task A_widget_id_containing_the_qualifier_separator_is_skipped()
	{
		var profile = new VirtualProfileDescriptor("p",
			"P",
			ProfileLayout.Grid(1, 2),
			[
				new VirtualFolderDescriptor("main",
					"Main",
					[
						new VirtualWidgetDescriptor("a::b", "ActionButton", 0, 0),
						new VirtualWidgetDescriptor("ok", "ActionButton", 1, 0)
					])
			]);
		var registry
			= await BuildRegistry(new InMemoryProfileStore(), new FakeProfileProviderIntegration("int", profile));

		var widgets = registry.GetFoldersForProfile("int::p").Single().Widgets;

		Assert.That(widgets.Select(widget => widget.PositionX), Is.EqualTo(new[] { 1 }));
	}

	[Test]
	public async Task A_widget_its_provider_no_longer_returns_is_not_found()
	{
		var registry = await BuildRegistry(new InMemoryProfileStore(),
			new FakeProfileProviderIntegration("spotify", CarThing(out _, out _)));

		Assert.Multiple(() =>
		{
			Assert.That(registry.FindVirtualWidget("spotify::car-thing:main:gone"), Is.Null);
			Assert.That(registry.FindVirtualWidget("spotify::play"), Is.Null);
			Assert.That(registry.FindVirtualWidget(Guid.NewGuid().ToString()), Is.Null);
		});
	}
}
