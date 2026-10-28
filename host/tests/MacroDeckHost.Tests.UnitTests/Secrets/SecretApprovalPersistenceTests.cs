using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using static MacroDeckHost.Tests.UnitTests.Secrets.SecretApprovalFixture;

namespace MacroDeckHost.Tests.UnitTests.Secrets;

public class SecretApprovalPersistenceTests
{
	private SecretApprovalFixture _fixture = null!;
	private InMemoryProfileStore _store = null!;
	private Guid _token;
	private Guid _profileId;

	[SetUp]
	public void SetUp()
	{
		_fixture = new SecretApprovalFixture();
		_store = new InMemoryProfileStore();
		_token = _fixture.Secrets.Store("tok");
	}

	[Test]
	public async Task Existing_secret_blocks_keep_running_after_the_upgrade_that_introduces_approvals()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));

		var restarted = await Restart();
		var result = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(restarted, widget.Id));

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
	}

	[Test]
	public async Task A_block_backfilled_while_its_plugin_was_offline_runs_once_the_plugin_connects()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));
		_fixture.CatalogOnline = false;

		var restarted = await Restart();
		_fixture.CatalogOnline = true;
		var result = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(restarted, widget.Id));

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
	}

	[Test]
	public async Task
		A_plugin_update_that_adds_an_optional_parameter_keeps_approved_blocks_running_but_not_untrusted_edits()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));
		var restarted = await Restart();
		var owner = FlowOwner.Widget(widget.Id);
		_fixture.UpdatedAction = new CapturingActionDefinition
		{
			Id = ActionId,
			Parameters = [.. _fixture.Action.Parameters, ActionParameter.Text("timeout", defaultValue: "30")]
		};

		var afterUpdate = await _fixture.Run(owner, WidgetFlows(restarted, widget.Id));
		var edited = FolderWidget(restarted, widget.Id);
		edited.Data = WidgetData(TokenBlock("https://attacker.example"));
		restarted.UpdateWidget(edited.FolderId, edited, SecretApprovalWrite.Withhold);
		var afterEdit = await _fixture.Run(owner, WidgetFlows(restarted, widget.Id));

		Assert.Multiple(() =>
		{
			Assert.That(afterUpdate.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(afterEdit.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task An_unreadable_approval_file_approves_nothing_and_does_not_backfill()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));
		_fixture.Files.MarkBackfilled(SecretApprovalGroup.Profiles);
		_fixture.Files.Unreadable.Add((SecretApprovalGroup.Profiles, _profileId));

		var restarted = await Restart();
		var result = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(restarted, widget.Id));

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
	}

	[Test]
	public async Task An_admin_save_of_another_widget_does_not_approve_a_block_found_without_a_record()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://attacker.example"));
		_fixture.Files.MarkBackfilled(SecretApprovalGroup.Profiles);
		_fixture.Files.Unreadable.Add((SecretApprovalGroup.Profiles, _profileId));

		var restarted = await Restart();
		var sibling = Widget(FolderWidget(restarted, widget.Id).FolderId, TokenBlock("https://home.example"));
		sibling.PositionX = 1;
		restarted.AddWidget(sibling.FolderId, sibling, SecretApprovalWrite.Approve);
		var carried = FolderWidget(restarted, widget.Id);
		restarted.UpdateWidget(carried.FolderId, carried, SecretApprovalWrite.Approve);

		var unknown = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(restarted, widget.Id));
		var added = await _fixture.Run(FlowOwner.Widget(sibling.Id), WidgetFlows(restarted, sibling.Id));
		Assert.Multiple(() =>
		{
			Assert.That(unknown.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(added.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task Restoring_profiles_from_a_backup_without_approvals_backfills_only_that_group()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));
		_fixture.Files.MarkBackfilled(SecretApprovalGroup.Automations);

		var restarted = await Restart();
		var result = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(restarted, widget.Id));

		Assert.Multiple(() =>
		{
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(_fixture.Files.IsBackfilled(SecretApprovalGroup.Profiles), Is.True);
		});
	}

	[Test]
	public async Task A_startup_repair_with_the_plugin_offline_keeps_approved_blocks_approved()
	{
		var widget = await SeedProfileWithoutApprovals(TokenBlock("https://home.example"));
		await Restart();
		_fixture.CatalogOnline = false;

		var repaired = await Restart();
		var folder = repaired.GetFolderById(FolderWidget(repaired, widget.Id).FolderId)!;
		await repaired.AddOrUpdateFolder(folder);
		_fixture.CatalogOnline = true;
		var result = await _fixture.Run(FlowOwner.Widget(widget.Id), WidgetFlows(repaired, widget.Id));

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
	}

	[Test]
	public async Task Moving_a_folder_to_another_profile_keeps_approved_and_pending_blocks_as_they_were()
	{
		var cache = await Restart();
		var (profile, folder) = await SeedProfile(cache);
		var approved = Widget(folder.Id, TokenBlock("https://home.example"));
		var pending = Widget(folder.Id, TokenBlock("https://attacker.example"));
		cache.AddWidget(folder.Id, approved, SecretApprovalWrite.Approve);
		cache.AddWidget(folder.Id, pending, SecretApprovalWrite.Withhold);
		var other = new ProfileEntity { Id = Guid.NewGuid(), Name = "Other", CreatedAt = DateTime.UtcNow };
		await cache.AddOrUpdate(other);

		folder.ProfileId = other.Id;
		folder.IsDefault = false;
		await cache.AddOrUpdateFolder(folder, SecretApprovalWrite.Approve);
		var restarted = await Restart();

		var approvedResult = await _fixture.Run(FlowOwner.Widget(approved.Id), WidgetFlows(restarted, approved.Id));
		var pendingResult = await _fixture.Run(FlowOwner.Widget(pending.Id), WidgetFlows(restarted, pending.Id));
		Assert.Multiple(() =>
		{
			Assert.That(profile.Id, Is.Not.EqualTo(other.Id));
			Assert.That(approvedResult.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(pendingResult.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task Deleting_a_profile_deletes_its_approval_records()
	{
		var cache = await Restart();
		var (profile, folder) = await SeedProfile(cache);
		cache.AddWidget(folder.Id, Widget(folder.Id, TokenBlock("https://home.example")), SecretApprovalWrite.Approve);

		await cache.Remove(profile.Id);

		Assert.That(_fixture.Files.Saved.Keys.Any(key => key.PartitionId == profile.Id), Is.False);
	}

	private async Task<WidgetEntity> SeedProfileWithoutApprovals(string block)
	{
		var cache = new ProfileCache(_store, Log.Logger);
		await cache.InitializeCache();
		var (profile, folder) = await SeedProfile(cache);
		_profileId = profile.Id;
		var widget = Widget(folder.Id, block);
		cache.AddWidget(folder.Id, widget);
		return widget;
	}

	private static async Task<(ProfileEntity Profile, FolderEntity Folder)> SeedProfile(ProfileCache cache)
	{
		var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = "Main", CreatedAt = DateTime.UtcNow };
		await cache.AddOrUpdate(profile);
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
		await cache.AddOrUpdateFolder(folder);
		return (profile, folder);
	}

	private async Task<ProfileCache> Restart()
	{
		var ledger = new SecretApprovalLedger(_fixture.FindAction, _fixture.Lineage, _fixture.Files, Log.Logger);
		_fixture.UseLedger(ledger);
		var cache = new ProfileCache(_store, Log.Logger, approvals: ledger);
		await cache.InitializeCache();
		return cache;
	}

	private string TokenBlock(string url)
	{
		return Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "{{url}}" }, { "name": "token", "type": "secret", "value": {{Ref(_token)}} }]""");
	}

	private static WidgetEntity Widget(Guid folderId, string block)
	{
		return new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = WidgetData(block)
		};
	}

	private static string WidgetData(string block)
	{
		return JsonSerializer.Serialize(new Dictionary<string, JsonElement>
		{
			["flows"] = JsonDocument.Parse(Flows(block)).RootElement.Clone()
		});
	}

	private static WidgetEntity FolderWidget(ProfileCache cache, Guid widgetId)
	{
		return cache.GetAllFolders().SelectMany(folder => folder.Widgets).Single(widget => widget.Id == widgetId);
	}

	private static string WidgetFlows(ProfileCache cache, Guid widgetId)
	{
		return WidgetFlowsJson.TryExtract(FolderWidget(cache, widgetId).Data, out var flows) ? flows : string.Empty;
	}
}
