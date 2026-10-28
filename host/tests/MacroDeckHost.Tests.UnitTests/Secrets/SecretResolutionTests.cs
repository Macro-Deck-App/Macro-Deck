using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Secrets.Approvals;
using static MacroDeckHost.Tests.UnitTests.Secrets.SecretApprovalFixture;

namespace MacroDeckHost.Tests.UnitTests.Secrets;

public class SecretResolutionTests
{
	private static readonly FlowOwner _widget = FlowOwner.Widget(Guid.NewGuid());

	[Test]
	public async Task A_secret_placed_in_a_parameter_the_action_does_not_declare_secret_is_never_decrypted()
	{
		var fixture = new SecretApprovalFixture();
		var secret = fixture.Secrets.Store("hunter2");
		var flows = Flows(Block("b1",
			$$"""[{ "name": "note", "type": "password", "value": {{Ref(secret)}} }, { "name": "smuggled", "type": "secret", "value": {{Ref(secret)}} }]"""));
		fixture.Record(_widget, flows, SecretApprovalWrite.Approve);

		var result = await fixture.Run(_widget, flows);

		Assert.Multiple(() =>
		{
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(fixture.Action.CapturedParameters!["note"], Is.EqualTo(string.Empty));
			Assert.That(fixture.Action.CapturedParameters!["smuggled"], Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public async Task A_secret_in_a_flow_control_parameter_is_never_decrypted()
	{
		var fixture = new SecretApprovalFixture();
		var secret = fixture.Secrets.Store("3");
		var flows = $$"""
					  [{ "triggerId": "t1", "triggerType": "onShortPress", "children": [
					    { "id": "loop", "type": "loop", "blockType": "repeatLoop",
					      "parameters": [{ "name": "count", "type": "number", "value": {{Ref(secret)}} }],
					      "children": [{{Block("inner", "[]")}}] }
					  ] }]
					  """;

		await fixture.Run(_widget, flows, true);

		Assert.That(fixture.Action.ExecuteCount, Is.Zero);
	}

	[Test]
	public async Task
		Declared_secrets_nested_in_objects_and_array_items_are_decrypted_and_their_undeclared_siblings_are_not()
	{
		var fixture = new SecretApprovalFixture();
		var password = fixture.Secrets.Store("pw");
		var headerValue = fixture.Secrets.Store("header-secret");
		var flows = Flows(Block("b1",
			$$"""
			  [{ "name": "auth", "type": "object",
			     "value": { "user": "me", "password": {{Ref(password)}}, "extra": {{Ref(password)}} } },
			   { "name": "headers", "type": "array",
			     "value": [{ "name": "X-Token", "value": {{Ref(headerValue)}} }] }]
			  """));
		fixture.Record(_widget, flows, SecretApprovalWrite.Approve);

		await fixture.Run(_widget, flows);

		var auth = (IReadOnlyDictionary<string, object?>)fixture.Action.CapturedParameters!["auth"];
		var header = (IReadOnlyDictionary<string, object?>)((IReadOnlyList<object?>)fixture.Action
			.CapturedParameters!["headers"])[0]!;
		Assert.Multiple(() =>
		{
			Assert.That(auth["user"], Is.EqualTo("me"));
			Assert.That(auth["password"], Is.EqualTo("pw"));
			Assert.That(auth["extra"], Is.EqualTo(string.Empty));
			Assert.That(header["value"], Is.EqualTo("header-secret"));
		});
	}

	[Test]
	public async Task A_plugin_built_against_the_old_sdk_still_receives_its_declared_secret_and_password()
	{
		var fixture = new SecretApprovalFixture([ActionParameter.Secret("apiKey"), ActionParameter.Password("pin")]);
		var apiKey = fixture.Secrets.Store("api-key");
		var pin = fixture.Secrets.Store("1234");
		var flows = Flows(Block("b1",
			$$"""[{ "name": "apiKey", "type": "secret", "value": {{Ref(apiKey)}} }, { "name": "pin", "type": "password", "value": {{Ref(pin)}} }]"""));
		fixture.Record(_widget, flows, SecretApprovalWrite.Approve);

		await fixture.Run(_widget, flows);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Action.CapturedParameters!["apiKey"], Is.EqualTo("api-key"));
			Assert.That(fixture.Action.CapturedParameters!["pin"], Is.EqualTo("1234"));
		});
	}

	[Test]
	public async Task An_untrusted_edit_of_any_parameter_of_an_approved_secret_block_stops_it_until_approved()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var approved = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://home.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		var redirected = approved.Replace("https://home.example", "https://attacker.example", StringComparison.Ordinal);
		fixture.Record(_widget, approved, SecretApprovalWrite.Approve);
		fixture.Record(_widget, redirected, SecretApprovalWrite.Withhold);

		var pending = await fixture.Run(_widget, redirected);
		var executedWhilePending = fixture.Action.ExecuteCount;
		fixture.Approve(_widget, redirected, "b1");
		await fixture.Run(_widget, redirected);

		Assert.Multiple(() =>
		{
			Assert.That(pending.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(pending.Actions.Single().ErrorCode,
				Is.EqualTo(ActionExecutionErrorCodes.SecretApprovalPending));
			Assert.That(executedWhilePending, Is.Zero);
			Assert.That(fixture.Action.CapturedParameters!["token"], Is.EqualTo("tok"));
		});
	}

	[Test]
	public async Task A_pending_block_does_not_stop_the_rest_of_the_flow_and_raises_one_approval_notification()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var flows = Flows(Block("b1", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""),
			Block("b2", """[{ "name": "note", "type": "string", "value": "after" }]"""));
		fixture.Record(_widget, flows, SecretApprovalWrite.Withhold);

		var result = await fixture.Run(_widget, flows);
		await fixture.Run(_widget, flows);

		Assert.Multiple(() =>
		{
			Assert.That(result.Actions.Select(action => action.Status),
				Is.EqualTo(new[] { ActionOutcomeStatus.Skipped, ActionOutcomeStatus.Succeeded }));
			Assert.That(fixture.Action.CapturedParameters!["note"], Is.EqualTo("after"));
			Assert.That(fixture.Notifications.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Admin_edits_keep_running_with_their_secret()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var first = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://a.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		var edited = first.Replace("https://a.example", "https://b.example", StringComparison.Ordinal);
		fixture.Record(_widget, first, SecretApprovalWrite.Approve);
		fixture.Record(_widget, edited, SecretApprovalWrite.Approve);

		await fixture.Run(_widget, edited);

		Assert.That(fixture.Action.CapturedParameters!["token"], Is.EqualTo("tok"));
	}

	[Test]
	public async Task A_non_admin_write_that_changes_an_approved_block_leaves_it_pending()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var first = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://a.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		fixture.Record(_widget, first, SecretApprovalWrite.Approve);
		var changed = first.Replace("https://a.example", "https://b.example", StringComparison.Ordinal);
		fixture.Record(_widget, changed, SecretApprovalWrite.Preserve);

		var result = await fixture.Run(_widget, changed);

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
	}

	[Test]
	public async Task An_admin_save_of_a_sibling_keeps_an_unchanged_pending_block_pending_through_editor_normalization()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var pendingBlock = Block("b1",
			$$"""[{ "name": "token", "type": "string", "value": {{Ref(token)}} }, { "name": "url", "type": "string", "value": "https://attacker.example" }]""");
		var sibling = Block("b2", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(token)}} }]""");
		fixture.Record(_widget, Flows(pendingBlock, sibling), SecretApprovalWrite.Withhold);
		var hydratedByEditor = Block("b1",
			$$"""[{ "name": "url", "type": "url", "label": "URL", "value": "https://attacker.example" }, { "name": "token", "type": "secret", "label": "Token", "value": {{Ref(token)}} }, { "name": "auth", "type": "object", "value": {} }, { "name": "headers", "type": "array", "value": [] }, { "name": "note", "type": "string", "value": "" }]""");
		var editedSibling = sibling.Replace("\"b2\"", "\"b2\", \"color\": \"#fff\"", StringComparison.Ordinal)
			.Replace("[{ \"name\": \"token\"",
				"[{ \"name\": \"note\", \"type\": \"string\", \"value\": \"x\" }, { \"name\": \"token\"",
				StringComparison.Ordinal);
		var adminSave = Flows(hydratedByEditor, editedSibling);
		fixture.Record(_widget, adminSave, SecretApprovalWrite.Approve);

		var result = await fixture.Run(_widget, adminSave);

		Assert.That(result.Actions.Select(action => action.Status),
			Is.EqualTo(new[] { ActionOutcomeStatus.Skipped, ActionOutcomeStatus.Succeeded }));
	}

	[Test]
	public async Task A_trusted_draft_still_skips_a_pending_block_it_carries_unchanged_but_runs_one_it_changed()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var pending = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://attacker.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		fixture.Record(_widget, pending, SecretApprovalWrite.Withhold);
		var edited = pending.Replace("https://attacker.example", "https://home.example", StringComparison.Ordinal);

		var carried = await fixture.Run(_widget, pending, true);
		var changed = await fixture.Run(_widget, edited, true);

		Assert.Multiple(() =>
		{
			Assert.That(carried.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(changed.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task Reverting_to_an_earlier_approved_version_is_pending()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var versionA = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://old.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		var versionB = versionA.Replace("https://old.example", "https://new.example", StringComparison.Ordinal);
		fixture.Record(_widget, versionA, SecretApprovalWrite.Approve);
		fixture.Record(_widget, versionB, SecretApprovalWrite.Approve);
		fixture.Record(_widget, versionA, SecretApprovalWrite.Withhold);

		var result = await fixture.Run(_widget, versionA);

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
	}

	[Test]
	public async Task A_copy_of_a_pending_block_in_another_widget_stays_pending_as_a_draft_and_after_saving()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var source = Flows(Block("b1", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		fixture.Record(_widget, source, SecretApprovalWrite.Withhold);
		var clone = fixture.Secrets.Store("tok");
		fixture.Lineage.RecordClone(token, clone);
		var target = FlowOwner.Widget(Guid.NewGuid());
		var pasted = Flows(Block("pasted", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(clone)}} }]"""));

		var asDraft = await fixture.Run(target, pasted, true);
		fixture.Record(target, pasted, SecretApprovalWrite.Approve);
		var afterSave = await fixture.Run(target, pasted);

		Assert.Multiple(() =>
		{
			Assert.That(asDraft.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
			Assert.That(afterSave.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task A_copy_of_a_block_approved_after_review_runs_as_a_draft_and_after_saving()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var source = Flows(Block("b1", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		fixture.Record(_widget, source, SecretApprovalWrite.Withhold);
		fixture.Approve(_widget, source, "b1");
		var clone = fixture.Secrets.Store("tok");
		fixture.Lineage.RecordClone(token, clone);
		var target = FlowOwner.Widget(Guid.NewGuid());
		var pasted = Flows(Block("pasted", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(clone)}} }]"""));

		var asDraft = await fixture.Run(target, pasted, true);
		fixture.Record(target, pasted, SecretApprovalWrite.Withhold);
		var afterSave = await fixture.Run(target, pasted);

		Assert.Multiple(() =>
		{
			Assert.That(asDraft.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(afterSave.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task A_block_without_an_owner_never_decrypts()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var flows = Flows(Block("b1", $$"""[{ "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));

		var result = await fixture.Run(null, flows);

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
	}

	[Test]
	public async Task The_icon_rewriter_keeps_an_approved_block_approved_and_an_unrelated_change_does_not()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var oldIcon = Guid.NewGuid();
		var newIcon = Guid.NewGuid();
		var flows = Flows(Block("b1",
			$$"""[{ "name": "note", "type": "string", "value": "{{oldIcon}}" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		fixture.Record(_widget, flows, SecretApprovalWrite.Approve);
		var rewritten = flows.Replace(oldIcon.ToString(), newIcon.ToString(), StringComparison.Ordinal);
		fixture.Record(_widget,
			rewritten,
			SecretApprovalWrite.Rewrite(snapshot =>
				snapshot.Replace(oldIcon.ToString(), newIcon.ToString(), StringComparison.Ordinal)));
		var tampered = rewritten.Replace(newIcon.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);

		var kept = await fixture.Run(_widget, rewritten);
		fixture.Record(_widget,
			tampered,
			SecretApprovalWrite.Rewrite(snapshot =>
				snapshot.Replace(oldIcon.ToString(), newIcon.ToString(), StringComparison.Ordinal)));
		var other = await fixture.Run(_widget, tampered);

		Assert.Multiple(() =>
		{
			Assert.That(kept.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(other.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}

	[Test]
	public async Task A_duplicated_parameter_name_never_reaches_a_decrypted_secret()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var approved = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://home.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		var smuggled = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://home.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }, { "name": "url", "type": "url", "value": "https://attacker.example" }]"""));
		fixture.Record(_widget, approved, SecretApprovalWrite.Approve);
		fixture.Record(_widget, smuggled, SecretApprovalWrite.Withhold);

		var asStored = await fixture.Run(_widget, smuggled);
		var asDraft = await fixture.Run(_widget, smuggled, true);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Action.ExecuteCount, Is.Zero);
			Assert.That(asStored.Actions.Single().Status, Is.Not.EqualTo(ActionOutcomeStatus.Succeeded));
			Assert.That(asDraft.Actions.Single().Status, Is.Not.EqualTo(ActionOutcomeStatus.Succeeded));
		});
	}

	[Test]
	public async Task An_edit_made_while_the_plugin_was_away_and_then_reverted_is_not_approved_by_the_old_version()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");
		var versionA = Flows(Block("b1",
			$$"""[{ "name": "url", "type": "url", "value": "https://old.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		var versionB = versionA.Replace("https://old.example", "https://new.example", StringComparison.Ordinal);
		fixture.Record(_widget, versionA, SecretApprovalWrite.Approve);

		fixture.CatalogOnline = false;
		fixture.Record(_widget, versionB, SecretApprovalWrite.Approve);
		fixture.Record(_widget, versionA, SecretApprovalWrite.Withhold);
		fixture.CatalogOnline = true;
		var result = await fixture.Run(_widget, versionA);

		Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
	}

	[Test]
	public async Task Untrusted_rewrites_keep_only_a_bounded_history_but_a_cut_block_is_still_recognised()
	{
		var fixture = new SecretApprovalFixture();
		var token = fixture.Secrets.Store("tok");

		string Version(int number)
		{
			return Flows(Block("b1",
				$$"""[{ "name": "url", "type": "url", "value": "https://v{{number}}.example" }, { "name": "token", "type": "secret", "value": {{Ref(token)}} }]"""));
		}

		for (var number = 0; number < 50; number++)
		{
			fixture.Record(_widget, Version(number), SecretApprovalWrite.Withhold);
		}

		fixture.Record(_widget, Flows(), SecretApprovalWrite.Approve);
		var target = FlowOwner.Widget(Guid.NewGuid());
		var pasted = Version(49).Replace("\"b1\"", "\"pasted\"", StringComparison.Ordinal);
		fixture.Record(target, pasted, SecretApprovalWrite.Approve);
		var result = await fixture.Run(target, pasted);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Ledger.RetainedSnapshotCount(_widget), Is.LessThanOrEqualTo(8));
			Assert.That(result.Actions.Single().Status, Is.EqualTo(ActionOutcomeStatus.Skipped));
		});
	}
}
