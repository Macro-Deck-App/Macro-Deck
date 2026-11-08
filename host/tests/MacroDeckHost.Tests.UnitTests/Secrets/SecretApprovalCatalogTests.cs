using System.Security.Claims;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using static MacroDeckHost.Tests.UnitTests.Secrets.SecretApprovalFixture;

namespace MacroDeckHost.Tests.UnitTests.Secrets;

public class SecretApprovalCatalogTests
{
	private SecretApprovalFixture _fixture = null!;
	private AutomationCache _automations = null!;
	private SecretApprovalCatalog _catalog = null!;
	private Guid _token;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new SecretApprovalFixture();
		_token = _fixture.Secrets.Store("tok");
		var profiles = new ProfileCache(new InMemoryProfileStore(), Log.Logger, approvals: _fixture.Ledger);
		await profiles.InitializeCache();
		_automations = new AutomationCache(new InMemoryAutomationStore(), Log.Logger, _fixture.Ledger);
		await _automations.InitializeCache();
		var scripts = new ScriptCache(new InMemoryScriptStore(), Log.Logger, _fixture.Ledger);
		await scripts.InitializeCache();
		_catalog = new SecretApprovalCatalog(new FolderCache(profiles),
			profiles,
			_automations,
			scripts,
			_fixture.Registry,
			_fixture.Ledger,
			_fixture.Lineage);
	}

	[TearDown]
	public void TearDown()
	{
		_automations.Dispose();
	}

	[Test]
	public async Task A_pending_block_is_listed_with_its_change_and_never_with_a_secret_value()
	{
		var automation = await SaveAutomation("https://home.example", SecretApprovalWrite.Approve);
		await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold, automation);

		var item = _catalog.Pending().Items.Single();

		var url = item.Parameters.Single(parameter => parameter.Name == "url");
		var token = item.Parameters.Single(parameter => parameter.Name == "token");
		Assert.Multiple(() =>
		{
			Assert.That(item.OwnerKind, Is.EqualTo("automation"));
			Assert.That(item.OwnerName, Is.EqualTo("Lights"));
			Assert.That(item.ApprovedBefore, Is.True);
			Assert.That(url.Approved, Is.EqualTo("https://home.example"));
			Assert.That(url.Current, Is.EqualTo("https://attacker.example"));
			Assert.That(url.Changed, Is.True);
			Assert.That(token.Secret, Is.True);
			Assert.That(token.Changed, Is.False);
			Assert.That(token.Approved, Is.Null);
			Assert.That(token.Current, Is.Null);
		});
	}

	[Test]
	public async Task Approving_the_listed_version_lets_it_run_and_a_stale_fingerprint_is_refused()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		var listed = _catalog.Pending().Items.Single();
		await SaveAutomation("https://other.example", SecretApprovalWrite.Withhold, automation);

		var stale = _catalog.Approve(Request(listed));
		var current = _catalog.Approve(Request(_catalog.Pending().Items.Single()));
		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows);

		Assert.Multiple(() =>
		{
			Assert.That(stale.Error!.Code, Is.EqualTo(ApproveSecretBlockResponse.ChangedCode));
			Assert.That(current.Success, Is.True);
			Assert.That(_catalog.Pending().Count, Is.Zero);
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task Declining_the_listed_version_unlists_it_and_the_step_stays_skipped_without_a_new_notification()
	{
		var automation = await SaveAutomation("https://home.example", SecretApprovalWrite.Approve);
		await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold, automation);

		var declined = _catalog.Decline(Request(_catalog.Pending().Items.Single()));
		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows);

		var step = result.Actions.Single();
		Assert.Multiple(() =>
		{
			Assert.That(declined.Success, Is.True);
			Assert.That(_catalog.Pending().Count, Is.Zero);
			Assert.That(step.Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(step.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.SecretApprovalDeclined));
			Assert.That(_fixture.Action.CapturedParameters, Is.Null);
			Assert.That(_fixture.Notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_trusted_draft_run_of_a_declined_version_is_skipped_without_a_notification()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		_catalog.Decline(Request(_catalog.Pending().Items.Single()));

		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Actions.Single().ErrorCode,
				Is.EqualTo(ActionExecutionErrorCodes.SecretApprovalDeclined));
			Assert.That(_fixture.Notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_stale_unknown_or_already_approved_version_cannot_be_declined()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		var listed = _catalog.Pending().Items.Single();
		await SaveAutomation("https://other.example", SecretApprovalWrite.Withhold, automation);
		var current = _catalog.Pending().Items.Single();

		var stale = _catalog.Decline(Request(listed));
		var unknown = _catalog.Decline(new ApproveSecretBlockRequest
		{
			OwnerKind = current.OwnerKind,
			OwnerId = current.OwnerId,
			BlockId = "missing",
			Fingerprint = current.Fingerprint
		});
		_catalog.Approve(Request(current));
		var approved = _catalog.Decline(Request(current));
		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows);

		Assert.Multiple(() =>
		{
			Assert.That(stale.Error!.Code, Is.EqualTo(ApproveSecretBlockResponse.ChangedCode));
			Assert.That(unknown.Error!.Code, Is.EqualTo(ApproveSecretBlockResponse.NotFoundCode));
			Assert.That(approved.Error!.Code, Is.EqualTo(ApproveSecretBlockResponse.ChangedCode));
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task Saving_the_flow_in_the_app_with_the_declined_step_untouched_keeps_it_declined()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		_catalog.Decline(Request(_catalog.Pending().Items.Single()));

		await SaveAutomation("https://attacker.example", SecretApprovalWrite.Approve, automation);
		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows);

		Assert.Multiple(() =>
		{
			Assert.That(_catalog.Pending().Count, Is.Zero);
			Assert.That(result.Actions.Single().ErrorCode,
				Is.EqualTo(ActionExecutionErrorCodes.SecretApprovalDeclined));
		});
	}

	[Test]
	public async Task Another_change_outside_the_app_after_a_decline_is_listed_again()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		_catalog.Decline(Request(_catalog.Pending().Items.Single()));

		await SaveAutomation("https://other.example", SecretApprovalWrite.Withhold, automation);

		var item = _catalog.Pending().Items.Single();
		Assert.That(item.Parameters.Single(parameter => parameter.Name == "url").Current,
			Is.EqualTo("https://other.example"));
	}

	[Test]
	public async Task A_declined_version_that_comes_back_after_an_edit_in_the_app_is_listed_again()
	{
		var automation = await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold);
		_catalog.Decline(Request(_catalog.Pending().Items.Single()));
		await SaveAutomation("https://home.example", SecretApprovalWrite.Approve, automation);

		await SaveAutomation("https://attacker.example", SecretApprovalWrite.Withhold, automation);
		var result = await _fixture.Run(FlowOwner.Automation(automation.Id), automation.Flows);

		var item = _catalog.Pending().Items.Single();
		Assert.Multiple(() =>
		{
			Assert.That(item.Parameters.Single(parameter => parameter.Name == "url").Current,
				Is.EqualTo("https://attacker.example"));
			Assert.That(result.Actions.Single().ErrorCode,
				Is.EqualTo(ActionExecutionErrorCodes.SecretApprovalPending));
		});
	}

	[Test]
	public void Only_an_admin_principal_writes_with_approval()
	{
		var admin = new SecretWriteAuthority();
		admin.Grant(Principal(AuthDefaults.AdminScope));
		var client = new SecretWriteAuthority();
		client.Grant(Principal(AuthDefaults.ClientScope));
		var nobody = new SecretWriteAuthority();

		Assert.Multiple(() =>
		{
			Assert.That(admin.Mode, Is.EqualTo(SecretApprovalMode.Approve));
			Assert.That(client.Mode, Is.EqualTo(SecretApprovalMode.Preserve));
			Assert.That(nobody.Mode, Is.EqualTo(SecretApprovalMode.Preserve));
		});
	}

	private async Task<AutomationEntity> SaveAutomation(
		string url,
		SecretApprovalWrite write,
		AutomationEntity? existing = null)
	{
		var automation = existing ?? new AutomationEntity { Id = Guid.NewGuid(), Name = "Lights" };
		automation.Flows = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "{{url}}" }, { "name": "token", "type": "secret", "value": {{Ref(_token)}} }]"""));
		await _automations.AddOrUpdate(automation, write);
		return automation;
	}

	private static ApproveSecretBlockRequest Request(PendingSecretApproval item)
	{
		return new ApproveSecretBlockRequest
		{
			OwnerKind = item.OwnerKind,
			OwnerId = item.OwnerId,
			BlockId = item.BlockId,
			Fingerprint = item.Fingerprint
		};
	}

	private static ClaimsPrincipal Principal(string scope)
	{
		return new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.ScopeClaim, scope)], "test"));
	}

	[TestCase(
		"""{ "id": "s1", "type": "step", "integrationId": "integration", "actionId": "send", "parameters": PARAMS }""")]
	[TestCase("""{ "id": "s1", "type": "step", "blockType": "integration.send", "parameters": PARAMS }""")]
	public async Task
		A_block_the_executor_runs_as_an_action_without_the_action_type_is_backfilled_listed_and_approvable(
			string blockTemplate)
	{
		string Flows(string url)
		{
			var parameters
				= $$"""[{ "name": "url", "type": "url", "value": "{{url}}" }, { "name": "token", "type": "secret", "value": {{Ref(_token)}} }]""";
			return SecretApprovalFixture.Flows(blockTemplate.Replace("PARAMS", parameters, StringComparison.Ordinal));
		}

		var owner = FlowOwner.Automation(Guid.NewGuid());
		_fixture.Files.Backfilled.Remove(SecretApprovalGroup.Automations);
		_fixture.Ledger.LoadGroup(SecretApprovalGroup.Automations,
			new Dictionary<Guid, IReadOnlyList<(FlowOwner Owner, string? Flows)>>
			{
				[owner.Id] = [(owner, Flows("https://home.example"))]
			});
		var backfilled = await _fixture.Run(owner, Flows("https://home.example"));

		var automation = new AutomationEntity { Id = owner.Id, Name = "Lights", Flows = Flows("https://home.example") };
		await _automations.AddOrUpdate(automation, SecretApprovalWrite.Approve);
		automation.Flows = Flows("https://attacker.example");
		await _automations.AddOrUpdate(automation, SecretApprovalWrite.Withhold);
		var pending = await _fixture.Run(owner, automation.Flows);
		var listed = _catalog.Pending().Items.Single();
		var approved = _catalog.Approve(Request(listed));
		var afterApproval = await _fixture.Run(owner, automation.Flows);

		Assert.Multiple(() =>
		{
			Assert.That(backfilled.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(pending.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(listed.BlockId, Is.EqualTo("s1"));
			Assert.That(listed.ActionId, Is.EqualTo("send"));
			Assert.That(approved.Success, Is.True);
			Assert.That(afterApproval.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(_fixture.Action.CapturedParameters!["token"], Is.EqualTo("tok"));
		});
	}
}
