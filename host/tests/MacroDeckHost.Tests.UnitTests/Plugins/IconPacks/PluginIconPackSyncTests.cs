using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;

[TestFixture]
internal sealed class PluginIconPackSyncTests
{
	private const string PluginId = "com.example.logos";

	private PluginIconPackTestHost _host = null!;

	[SetUp]
	public void SetUp() => _host = new PluginIconPackTestHost();

	[TearDown]
	public void TearDown() => _host.Dispose();

	[Test]
	public async Task A_declared_pack_is_added_as_a_read_only_plugin_pack_that_cannot_be_deleted()
	{
		var archive = await _host.BuildArchive("Logos", ("spotify", "green"), ("discord", "blue"));

		var result = await _host.SyncDevelopment(PluginId, ("logos", archive));

		var pack = _host.PluginPack(PluginId, "logos");
		Assert.That(pack, Is.Not.Null);
		var owner = _host.OwnerRegistry.Describe(pack!);
		var delete = await _host.PackService().Delete(pack!.Id);
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PluginIconPackSyncStatus.Synced));
			Assert.That(result.Changed, Is.True);
			Assert.That(pack.Name, Is.EqualTo("Logos"));
			Assert.That(_host.Icons.Cache.GetIconsByPackId(pack.Id).Select(icon => icon.Name),
				Is.EquivalentTo(new[] { "spotify", "discord" }));
			Assert.That(owner.Kind, Is.EqualTo(IconPackOwnerKind.Plugin));
			Assert.That(owner.OwnerName, Is.EqualTo("Test Plugin"));
			Assert.That(_host.OwnerRegistry.IsReadOnly(pack), Is.True);
			Assert.That(delete.Error, Is.EqualTo(IconPackError.OwnedBySource));
			Assert.That(_host.Icons.Mediator.Published.OfType<IconPackCreatedNotification>().Single().Pack.SourceId,
				Is.EqualTo($"{PluginId}/logos"),
				"the pack is announced already stamped, never as an ordinary user pack first");
		});
	}

	[Test]
	public async Task A_replaced_pack_keeps_icon_ids_by_name_and_only_announces_the_icons_that_changed()
	{
		await _host.SyncDevelopment(PluginId,
			("logos", await _host.BuildArchive("Logos", ("home", "one"), ("play", "one"), ("stop", "one"), ("pause", "one"))));
		var pack = _host.PluginPack(PluginId, "logos")!;
		var home = _host.Icon(pack, "home");
		var homeHash = home.MasterContentHash;
		var play = _host.Icon(pack, "play");
		var stop = _host.Icon(pack, "stop");
		var pause = _host.Icon(pack, "pause");
		_host.Usage.InUse.Add(stop.Id);
		_host.Icons.Mediator.Published.Clear();

		await _host.SyncDevelopment(PluginId,
			("logos", await _host.BuildArchive("Logos", ("home", "two"), ("play", "one"), ("record", "one"))));

		var icons = _host.Icons.Cache.GetIconsByPackId(pack.Id);
		var published = _host.Icons.Mediator.Published;
		Assert.Multiple(() =>
		{
			Assert.That(_host.PluginPack(PluginId, "logos")!.Id, Is.EqualTo(pack.Id));
			Assert.That(icons.Single(icon => icon.Name == "home").Id, Is.EqualTo(home.Id));
			Assert.That(icons.Single(icon => icon.Name == "home").MasterContentHash, Is.Not.EqualTo(homeHash));
			Assert.That(icons.Single(icon => icon.Name == "play").Id, Is.EqualTo(play.Id));
			Assert.That(icons.Select(icon => icon.Name), Does.Contain("record"));
			Assert.That(icons.Select(icon => icon.Id), Does.Contain(stop.Id), "a dropped icon a button uses is kept");
			Assert.That(icons.Select(icon => icon.Id), Does.Not.Contain(pause.Id), "a dropped unused icon is deleted");
			Assert.That(published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id), Is.EqualTo(new[] { home.Id }));
			Assert.That(published.OfType<IconDeletedNotification>().Select(n => n.IconId), Is.EqualTo(new[] { pause.Id }));
		});
	}

	[Test]
	public async Task An_unchanged_archive_is_not_touched_again()
	{
		var archive = await _host.BuildArchive("Logos", ("home", "one"));
		await _host.SyncDevelopment(PluginId, ("logos", archive));
		_host.Icons.Mediator.Published.Clear();

		var result = await _host.SyncDevelopment(PluginId, ("logos", archive));

		Assert.Multiple(() =>
		{
			Assert.That(result.Changed, Is.False);
			Assert.That(_host.Icons.Mediator.Published.OfType<IconUpdatedNotification>(), Is.Empty);
			Assert.That(_host.Icons.Mediator.Published.OfType<IconPackUpdatedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task A_pack_with_duplicate_icon_names_is_refused_and_the_installed_one_stays()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));
		var pack = _host.PluginPack(PluginId, "logos")!;

		var result = await _host.SyncDevelopment(PluginId,
			("logos", await _host.BuildArchive("Logos", ("home", "two"), ("Home", "three"))));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(PluginIconPackSyncStatus.Invalid));
			Assert.That(result.InvalidKeys, Is.EqualTo(new[] { "logos" }));
			Assert.That(_host.Icons.Cache.GetIconsByPackId(pack.Id).Select(icon => icon.Name), Is.EqualTo(new[] { "home" }));
		});
	}

	[Test]
	public async Task A_pack_no_longer_declared_is_removed_when_nothing_uses_it()
	{
		await _host.SyncDevelopment(PluginId,
			("logos", await _host.BuildArchive("Logos", ("home", "one"))),
			("status", await _host.BuildArchive("Status", ("ok", "one"))));
		var status = _host.PluginPack(PluginId, "status")!;

		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));

		Assert.Multiple(() =>
		{
			Assert.That(_host.Icons.Cache.GetPackById(status.Id), Is.Null);
			Assert.That(_host.PluginPack(PluginId, "logos"), Is.Not.Null);
			Assert.That(_host.Icons.Mediator.Published.OfType<IconPackDeletedNotification>().Select(n => n.PackId),
				Is.EqualTo(new[] { status.Id }));
			Assert.That(_host.Notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_pack_no_longer_declared_but_in_use_is_kept_as_an_editable_pack_and_the_user_is_told_why()
	{
		await _host.SyncDevelopment(PluginId,
			("logos", await _host.BuildArchive("Logos", ("home", "one"))),
			("status", await _host.BuildArchive("Status", ("ok", "one"))));
		var status = _host.PluginPack(PluginId, "status")!;
		_host.Usage.InUse.Add(_host.Icon(status, "ok").Id);
		_host.Icons.Mediator.Published.Clear();

		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));
		_host.Usage.InUse.Clear();
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "two"))));

		var kept = _host.Icons.Cache.GetPackById(status.Id);
		var notification = _host.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(kept, Is.Not.Null, "once kept, the pack is the user's and a later sync leaves it alone");
			Assert.That(_host.OwnerRegistry.Describe(kept!).Kind, Is.EqualTo(IconPackOwnerKind.User));
			Assert.That(_host.OwnerRegistry.IsReadOnly(kept!), Is.False);
			Assert.That(notification.Message, Does.Contain("Status").And.Contain("Test Plugin"));
			Assert.That(_host.Icons.Mediator.Published.OfType<IconPackUpdatedNotification>().Select(n => n.Pack.Id),
				Does.Contain(status.Id),
				"clients learn that the pack is no longer read-only");
		});
	}

	[Test]
	public async Task A_returning_declaration_adopts_a_kept_pack_again_and_undoes_edits_made_meanwhile()
	{
		var archive = await _host.BuildArchive("Status", ("ok", "one"), ("error", "one"));
		await _host.SyncDevelopment(PluginId, ("status", archive));
		var status = _host.PluginPack(PluginId, "status")!;
		var ok = _host.Icon(status, "ok");
		var error = _host.Icon(status, "error");
		_host.Usage.InUse.Add(ok.Id);
		await _host.SyncDevelopment(PluginId);

		await _host.IconService().Rename(ok.Id, "fine");
		await _host.IconService().Delete(error.Id);
		await _host.SyncDevelopment(PluginId, ("status", archive));

		var icons = _host.Icons.Cache.GetIconsByPackId(status.Id);
		Assert.Multiple(() =>
		{
			Assert.That(_host.PluginPack(PluginId, "status")!.Id, Is.EqualTo(status.Id));
			Assert.That(_host.OwnerRegistry.Describe(_host.PluginPack(PluginId, "status")!).Kind,
				Is.EqualTo(IconPackOwnerKind.Plugin));
			Assert.That(icons.Select(icon => icon.Name), Is.EquivalentTo(new[] { "ok", "error" }));
			Assert.That(icons.Single(icon => icon.Name == "ok").Id, Is.EqualTo(ok.Id), "the icon a button uses keeps its id");
		});
	}

	[Test]
	public async Task Buttons_driven_by_the_plugins_icon_providers_are_asked_to_re_read_after_a_sync()
	{
		_host.Snapshots.DeclareIconProvider(PluginId, "now-playing");

		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));

		Assert.That(_host.Invalidator.Invalidations, Is.EqualTo(new[] { (PluginId, "now-playing") }));
	}

	[Test]
	public async Task Buttons_driven_by_the_plugins_icon_providers_are_asked_to_re_read_after_a_pack_is_removed()
	{
		_host.Snapshots.DeclareIconProvider(PluginId, "now-playing");
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));
		_host.Invalidator.Invalidations.Clear();

		await _host.SyncDevelopment(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_host.PluginPack(PluginId, "logos"), Is.Null);
			Assert.That(_host.Invalidator.Invalidations, Is.EqualTo(new[] { (PluginId, "now-playing") }));
		});
	}

	[Test]
	public async Task A_plugin_icon_no_longer_resolves_into_a_pack_the_plugin_stopped_declaring()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));
		_host.Usage.InUse.Add(_host.Icon(_host.PluginPack(PluginId, "logos")!, "spotify").Id);

		await _host.SyncDevelopment(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(_host.PluginPack(PluginId, "logos"), Is.Not.Null, "kept for the icon in use");
			Assert.That(_host.Resolver.Resolve(PluginId, "logos/spotify"), Is.Null);
		});
	}

	[Test]
	public async Task A_plugin_icon_never_resolves_against_another_plugins_pack()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));

		Assert.Multiple(() =>
		{
			Assert.That(_host.Resolver.Resolve(PluginId, "logos/Spotify"), Is.Not.Null, "names match case-insensitively");
			Assert.That(_host.Resolver.Resolve("com.example.other", "logos/spotify"), Is.Null);
			Assert.That(_host.Resolver.Resolve(PluginId, "logos/missing"), Is.Null);
		});
	}

	[Test]
	public async Task A_development_session_that_ends_leaves_its_packs_as_ordinary_packs_when_the_plugin_is_not_installed()
	{
		await _host.Sessions.Create(Session("session-1"));
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("home", "one"))));
		var pack = _host.PluginPack(PluginId, "logos")!;

		await _host.Sessions.TerminateForPlugin(PluginId, 1000, "done");
		await WaitUntil(() => _host.OwnerRegistry.Describe(pack).Kind == IconPackOwnerKind.User);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Icons.Cache.GetPackById(pack.Id), Is.Not.Null);
			Assert.That(_host.OwnerRegistry.IsReadOnly(pack), Is.False);
		});
	}

	private static PluginSessionRecord Session(string sessionId) => new()
	{
		SessionId = sessionId,
		PluginId = PluginId,
		DisplayName = "Test Plugin",
		Origin = PluginSessionOrigin.SelfRegistered,
		NegotiatedVersion = 1,
		Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
		DeclaredCapabilities = [],
		State = PluginSessionState.Connected,
		CreatedAt = DateTimeOffset.UtcNow
	};

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}
}
