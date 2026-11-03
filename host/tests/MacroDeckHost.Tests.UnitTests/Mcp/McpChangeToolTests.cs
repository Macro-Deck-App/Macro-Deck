using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Mcp;
using MacroDeckHost.Mcp.Tools;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public partial class McpEndpointTests
{
	private const string TestIntegration = "test.mcp";
	private const string SendAction = "send";

	private readonly CapturingActionDefinition _send = new()
	{
		Id = SendAction,
		Parameters = [ActionParameter.Text("target", required: true), ActionParameter.Secret("token")]
	};

	private bool _integrationRegistered;

	[Test]
	public async Task A_dry_run_reports_problems_and_writes_nothing()
	{
		await using var client = await ConnectWithToken();
		var profilesBefore = await ListCount(client, "profiles");

		var result = await Apply(client,
			[
				Change("create", "profile", data: new { name = "Dry run" }),
				Change("create", "widget", data: new { folderId = Guid.NewGuid().ToString() })
			],
			true);

		var problems = result.GetProperty("problems");
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
			Assert.That(problems.EnumerateArray().Select(problem => problem.GetProperty("code").GetString()),
				Does.Contain(McpProblemCodes.NotFound));
			Assert.That(await ListCount(client, "profiles"), Is.EqualTo(profilesBefore));
		});
	}

	[Test]
	public async Task A_batch_builds_a_widget_in_a_new_folder_by_placeholder_and_undo_removes_all_of_it()
	{
		await using var client = await ConnectWithToken();
		await Apply(client, [Change("create", "profile", data: new { name = "Kept, so the new one is not the last" })]);
		var result = await Apply(client,
		[
			Change("create", "profile", reference: "p", data: new { name = "Built by AI" }),
			Change("create", "folder", reference: "f", data: new { profileId = "@p", name = "AI folder" }),
			Change("create",
				"widget",
				reference: "w",
				data: new { folderId = "@f", positionX = 0, positionY = 0, width = 1, height = 1 })
		]);

		var created = result.GetProperty("createdIds");
		var widgetId = Guid.Parse(created.GetProperty("w").GetString()!);
		var folderId = Guid.Parse(created.GetProperty("f").GetString()!);
		var widgetExisted = FindWidget(widgetId) is not null;

		var undo = await client.CallToolAsync("undo");
		var undoProblems = undo.StructuredContent!.Value.GetProperty("problems");

		await Assert.MultipleAsync(() =>
		{
			Assert.That(result.GetProperty("applied").GetBoolean(), Is.True, result.ToString());
			Assert.That(widgetExisted, Is.True);
			Assert.That(undoProblems.GetArrayLength(), Is.Zero, undoProblems.ToString());
			Assert.That(FindWidget(widgetId), Is.Null);
			Assert.That(Folders().GetFolderById(folderId), Is.Null);
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Undo_restores_a_deleted_widget_with_its_original_id_and_its_secret()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var secretId = await SetSecret(client);
		var widgetId = await CreateWidget(client, folderId, Flow(secretId));

		var dataBefore = FindWidget(widgetId)!.Data;
		var ownSecret = SecretReferences.Extract(dataBefore).Single();

		await Apply(client, [Change("delete", "widget", widgetId.ToString())]);
		var keptWhileDeleted = await Resolve(ownSecret);
		var undo = await client.CallToolAsync("undo");
		var restored = FindWidget(widgetId);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(undo.IsError, Is.Not.True, JsonSerializer.Serialize(undo));
			Assert.That(keptWhileDeleted, Is.EqualTo(SecretValue));
			Assert.That(restored?.Data, Is.EqualTo(dataBefore));
			Assert.That(await Resolve(ownSecret), Is.EqualTo(SecretValue));
		});
	}

	[Test]
	public async Task Undo_leaves_an_object_alone_that_changed_after_the_batch()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var widgetId = await CreateWidget(client, folderId, null);
		await Apply(client, [Change("update", "widget", widgetId.ToString(), data: new { positionX = 1 })]);

		var widget = FindWidget(widgetId)!;
		widget.PositionX = 2;
		Folders().UpdateWidget(folderId, widget);

		var undo = await client.CallToolAsync("undo");
		var codes = undo.StructuredContent!.Value.GetProperty("problems")
			.EnumerateArray()
			.Select(problem => problem.GetProperty("code").GetString());

		await Assert.MultipleAsync(() =>
		{
			Assert.That(codes, Does.Contain(McpProblemCodes.EntityChangedSince));
			Assert.That(FindWidget(widgetId)!.PositionX, Is.EqualTo(2));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_secret_block_written_by_the_mcp_is_never_approved()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var secretId = await SetSecret(client);
		var widgetId = await CreateWidget(client, folderId, Flow(secretId));

		var block = FlowSecretBlocks.FromWidgetData(FindWidget(widgetId)!.Data).Single();
		var approved = _host.Services.GetRequiredService<ISecretApprovals>()
			.IsApproved(FlowOwner.Widget(widgetId), block.Id, block.Content);

		Assert.That(approved, Is.False);
	}

	[Test]
	public async Task A_secret_this_client_did_not_create_is_refused()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		Guid foreign;
		using (var scope = _host.Services.CreateScope())
		{
			foreign = await scope.ServiceProvider.GetRequiredService<ISecretService>()
				.Create("someone else's", SecretKind.Secret);
		}

		var result = await Apply(client,
			[Change("create", "widget", data: WidgetData(folderId, Flow(foreign)))],
			true);

		Assert.That(Codes(result), Does.Contain(McpProblemCodes.ForeignSecretRef));
	}

	[Test]
	public async Task A_provider_turned_off_is_refused_for_new_blocks_but_not_for_unchanged_ones()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var widgetId = await CreateWidget(client, folderId, Flow(null));
		await SetPolicy(McpRiskMode.Off);
		try
		{
			var unchanged = await Apply(client,
				[Change("update", "widget", widgetId.ToString(), data: new { positionX = 1 })],
				true);
			var added = await Apply(client,
				[Change("create", "widget", data: WidgetData(folderId, Flow(null), 2))],
				true);

			await Assert.MultipleAsync(() =>
			{
				Assert.That(unchanged.GetProperty("valid").GetBoolean(), Is.True, unchanged.ToString());
				Assert.That(Codes(added), Does.Contain(McpProblemCodes.ProviderBlocked));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[TestCase("step")]
	[TestCase(null)]
	public async Task A_turned_off_action_is_refused_in_every_block_shape_the_executor_runs(string? type)
	{
		await using var client = await ConnectWithToken();
		var block = Flow(null);
		if (type is null)
		{
			block.Remove("type");
		}
		else
		{
			block["type"] = type;
		}

		var created = await Apply(client,
		[
			Change("create",
				"script",
				reference: "s",
				data: new { name = "AI script", flows = ScriptFlows(block.DeepClone().AsObject()) })
		]);
		Assert.That(created.GetProperty("applied").GetBoolean(), Is.True, created.ToString());
		var scriptId = created.GetProperty("createdIds").GetProperty("s").GetString();
		await SetPolicy(McpRiskMode.Off);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var added = await Apply(client,
				[Change("create", "automation", data: new { name = "AI", flows = Flows(block) })],
				true);
			var run = await client.CallToolAsync("run_script",
				new Dictionary<string, object?> { ["scriptId"] = scriptId });

			await Assert.MultipleAsync(() =>
			{
				Assert.That(Codes(added), Does.Contain(McpProblemCodes.ProviderBlocked), added.ToString());
				Assert.That(JsonSerializer.Serialize(run), Does.Contain("PROVIDER_BLOCKED"));
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[TestCase("accept", true)]
	[TestCase("decline", false)]
	public async Task A_confirmation_provider_is_written_only_after_the_user_confirmed(string answer, bool written)
	{
		var asked = 0;
		await using var client = await ConnectWithToken((_, _) =>
		{
			asked++;
			var content = new Dictionary<string, JsonElement>
				{ ["confirm"] = JsonSerializer.SerializeToElement(answer == "accept") };
			return ValueTask.FromResult(new ElicitResult { Action = answer, Content = content });
		});
		await SetPolicy(McpRiskMode.Confirm);
		try
		{
			var before = await ListCount(client, "automations");
			var result = await client.CallToolAsync("apply_changes",
				new Dictionary<string, object?>
				{
					["changes"] = new[]
						{ Change("create", "automation", data: new { name = "AI", flows = Flows(Flow(null)) }) }
				});

			await Assert.MultipleAsync(() =>
			{
				Assert.That(asked, Is.EqualTo(1));
				Assert.That(result.IsError == true, Is.EqualTo(!written), JsonSerializer.Serialize(result));
				return Task.CompletedTask;
			});
			Assert.That(await ListCount(client, "automations"), Is.EqualTo(written ? before + 1 : before));
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Run_script_is_refused_when_a_nested_action_is_turned_off()
	{
		await using var client = await ConnectWithToken();
		var created = await Apply(client,
		[
			Change("create",
				"script",
				reference: "s",
				data: new { name = "AI script", flows = ScriptFlows(Flow(null)) })
		]);
		var scriptId = created.GetProperty("createdIds").GetProperty("s").GetString();
		await SetPolicy(McpRiskMode.Off);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = await client.CallToolAsync("run_script",
				new Dictionary<string, object?> { ["scriptId"] = scriptId });

			await Assert.MultipleAsync(() =>
			{
				Assert.That(run.IsError, Is.True);
				Assert.That(JsonSerializer.Serialize(run), Does.Contain("PROVIDER_BLOCKED"));
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Run_script_follows_nested_scripts_when_checking_what_it_would_run()
	{
		await using var client = await ConnectWithToken(ConfirmingHandler());
		var inner = await Apply(client,
		[
			Change("create",
				"script",
				reference: "inner",
				data: new { name = "Inner", flows = ScriptFlows(Flow(null)) })
		]);
		var innerId = inner.GetProperty("createdIds").GetProperty("inner").GetString()!;
		var outer = await Apply(client,
		[
			Change("create",
				"script",
				reference: "outer",
				data: new { name = "Outer", flows = ScriptFlows(RunScript(innerId, null)) })
		]);
		var outerId = outer.GetProperty("createdIds").GetProperty("outer").GetString();
		await SetPolicy(McpRiskMode.Off);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = await client.CallToolAsync("run_script",
				new Dictionary<string, object?> { ["scriptId"] = outerId });

			await Assert.MultipleAsync(() =>
			{
				Assert.That(JsonSerializer.Serialize(run), Does.Contain("PROVIDER_BLOCKED"));
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task A_script_input_that_reaches_an_approved_secret_block_needs_the_users_consent()
	{
		var asked = new List<string?>();
		await using var client = await ConnectWithToken((request, _) =>
		{
			asked.Add(request?.Message);
			return ValueTask.FromResult(new ElicitResult { Action = "decline" });
		});
		Guid secretId;
		using (var scope = _host.Services.CreateScope())
		{
			secretId = await scope.ServiceProvider.GetRequiredService<ISecretService>()
				.Create("approved", SecretKind.Secret);
		}

		var block = Flow(secretId);
		block["parameters"]![0]!["value"] = "{{ vars.dest }}";
		var created = await Send(HttpMethod.Post,
			"/api/scripts",
			FakeConnectionShape.Loopback,
			body: new { name = "Approved by the user", flows = ScriptFlows(block).ToJsonString() });
		var scriptId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("script")
			.GetProperty("id")
			.GetString();
		var executedBefore = _send.ExecuteCount;

		var run = await client.CallToolAsync("run_script",
			new Dictionary<string, object?>
				{ ["scriptId"] = scriptId, ["inputs"] = new Dictionary<string, object?> { ["dest"] = "attacker" } });

		await Assert.MultipleAsync(() =>
		{
			Assert.That(asked, Has.Count.EqualTo(1));
			Assert.That(asked[0], Does.Contain("dest"));
			Assert.That(run.IsError, Is.True);
			Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_catalog_tells_each_actions_risk_category_and_effective_policy()
	{
		await using var client = await ConnectWithToken();

		var before = await CatalogEntry(client);
		await SetPolicy(McpRiskMode.Confirm);
		try
		{
			var after = await CatalogEntry(client);

			await Assert.MultipleAsync(() =>
			{
				Assert.That(before.GetProperty("riskCategory").GetString(), Is.EqualTo("none"));
				Assert.That(before.GetProperty("policy").GetString(), Is.EqualTo("allow"));
				Assert.That(after.GetProperty("policy").GetString(), Is.EqualTo("ask-first"));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task The_confirmation_names_the_action_and_its_values_in_the_users_words_and_hides_secrets()
	{
		string? message = null;
		await using var client = await ConnectWithToken(async (request, _) =>
		{
			if (request?.Mode == "url")
			{
				await TypeSecretOnPage(request.Url!);
				return new ElicitResult { Action = "accept" };
			}

			message = request?.Message;
			return new ElicitResult { Action = "decline" };
		});
		var secretId = await SetSecret(client);
		await SetPolicy(McpRiskMode.Confirm);
		try
		{
			await client.CallToolAsync("run_action",
				new Dictionary<string, object?>
				{
					["integrationId"] = TestIntegration,
					["actionId"] = SendAction,
					["parameters"] = new Dictionary<string, object?>
					{
						["target"] = "https://example.test",
						["token"] = new Dictionary<string, string> { ["$secret"] = secretId.ToString() }
					}
				});

			await Assert.MultipleAsync(() =>
			{
				Assert.That(message, Does.Contain("Capture"));
				Assert.That(message, Does.Contain("Test Integration"));
				Assert.That(message, Does.Contain("target: https://example.test"));
				Assert.That(message, Does.Contain("token: a secret"));
				Assert.That(message, Does.Not.Contain(secretId.ToString()));
				Assert.That(message, Does.Not.Contain(McpConfirmationKinds.RiskyAction));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[TestCase("allow", false)]
	[TestCase("decline", true)]
	public async Task A_client_with_only_url_elicitation_confirms_on_the_macro_deck_page(string decision, bool refused)
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		string? page = null;
		HttpStatusCode? replay = null;
		await using var client = await Connect(token.Plaintext,
			LegacyProtocol,
			async (request, cancellationToken) =>
			{
				var path = new Uri(request!.Url!).PathAndQuery;
				page = await (await Send(HttpMethod.Get, path, FakeConnectionShape.LoopbackWithoutCredential)).Content
					.ReadAsStringAsync(cancellationToken);
				var form = new Dictionary<string, string>
					{ ["nonce"] = NonceField().Match(page).Groups[1].Value, ["decision"] = decision };
				Assert.That((await PostPageForm(path, form)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
				replay = (await PostPageForm(path, form)).StatusCode;
				return new ElicitResult { Action = "accept" };
			},
			new ElicitationCapability { Url = new UrlElicitationCapability() });
		await SetPolicy(McpRiskMode.Confirm);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = await client.CallToolAsync("run_action",
				new Dictionary<string, object?>
				{
					["integrationId"] = TestIntegration,
					["actionId"] = SendAction,
					["parameters"] = new Dictionary<string, object?> { ["target"] = "visible-target" }
				});

			await Assert.MultipleAsync(() =>
			{
				Assert.That(page, Does.Contain("test client").And.Contain("visible-target"));
				Assert.That(replay, Is.EqualTo(HttpStatusCode.NotFound));
				Assert.That(JsonSerializer.Serialize(run).Contains(McpConsent.Declined), Is.EqualTo(refused));
				Assert.That(_send.ExecuteCount, Is.EqualTo(refused ? executedBefore : executedBefore + 1));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await SetPolicy(null);
		}
	}

	[TestCase("allow", false)]
	[TestCase("decline", true)]
	public async Task A_client_without_any_elicitation_asks_in_macro_deck_and_runs_only_once_allowed(
		string decision,
		bool refused)
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol, null, _noElicitation);
		await SetPolicy(McpRiskMode.Confirm);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = RunSend(client, "visible-target");
			var pending = await WaitForPendingConfirmation();
			var answer = await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{pending.GetProperty("id").GetString()}/{decision}",
				FakeConnectionShape.Loopback,
				body: new { });
			var result = JsonSerializer.Serialize(await run);

			await Assert.MultipleAsync(async () =>
			{
				Assert.That(pending.GetProperty("clientName").GetString(), Is.EqualTo("test client"));
				Assert.That(pending.GetProperty("lines").ToString(), Does.Contain("visible-target"));
				Assert.That((await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean(),
					Is.True);
				Assert.That(result.Contains(McpConsent.Declined), Is.EqualTo(refused), result);
				Assert.That(_send.ExecuteCount, Is.EqualTo(refused ? executedBefore : executedBefore + 1));
				Assert.That(await PendingConfirmations(), Is.Empty);
			});
		}
		finally
		{
			await DeclinePending();
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Two_identical_calls_waiting_for_one_allow_run_the_action_once()
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol, null, _noElicitation);
		await SetPolicy(McpRiskMode.Confirm);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var first = RunSend(client, "twice");
			var pending = await WaitForPendingConfirmation();
			var second = RunSend(client, "twice");
			await Task.Delay(200);
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{pending.GetProperty("id").GetString()}/allow",
				FakeConnectionShape.Loopback,
				body: new { });
			var results = await Task.WhenAll(first, second);

			await Assert.MultipleAsync(() =>
			{
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore + 1));
				Assert.That(results.Count(result => result.IsError != true), Is.EqualTo(1));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await DeclinePending();
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Only_the_desktop_app_can_answer_a_request_in_macro_deck()
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol, null, _noElicitation);
		await SetPolicy(McpRiskMode.Confirm);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = RunSend(client, "x");
			var id = (await WaitForPendingConfirmation()).GetProperty("id").GetString();
			var fromLan = await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{id}/allow",
				FakeConnectionShape.PublicLan,
				_adminToken,
				new { });
			var listFromLan = await Send(HttpMethod.Get,
				"/api/mcp/confirmations",
				FakeConnectionShape.PublicLan,
				_adminToken);
			var onPage = await Send(HttpMethod.Get,
				$"/mcp/confirm/{id}",
				FakeConnectionShape.LoopbackWithoutCredential);
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{id}/decline",
				FakeConnectionShape.Loopback,
				body: new { });
			await run;

			await Assert.MultipleAsync(() =>
			{
				Assert.That(fromLan.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
				Assert.That(listFromLan.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
				Assert.That(onPage.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
				return Task.CompletedTask;
			});
		}
		finally
		{
			await DeclinePending();
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Always_allow_lets_only_this_client_run_the_action_without_asking_until_it_is_removed()
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		var other = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol, null, _noElicitation);
		await using var otherClient = await Connect(other.Plaintext, LegacyProtocol, null, _noElicitation);
		await SetPolicy(McpRiskMode.Confirm);
		var executedBefore = _send.ExecuteCount;
		try
		{
			var run = RunSend(client, "first");
			var id = (await WaitForPendingConfirmation()).GetProperty("id").GetString();
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{id}/allow",
				FakeConnectionShape.Loopback,
				body: new { alwaysAllow = true });
			await run;

			var unasked = await RunSend(client, "second");
			var policyForClient = await SendPolicy(client);
			var policyForOther = await SendPolicy(otherClient);
			var allowedActions = await AllowedActions(token.Id);

			await SetPolicy(McpRiskMode.Off);
			var blocked = JsonSerializer.Serialize(await RunSend(client, "blocked"));
			await SetPolicy(McpRiskMode.Confirm);

			var removed = await Send(HttpMethod.Delete,
				$"/api/mcp/tokens/{token.Id}/allowed-actions/{TestIntegration}/{SendAction}",
				FakeConnectionShape.Loopback);
			var askedAgain = RunSend(client, "third");
			var pendingAgain = await WaitForPendingConfirmation();
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{pendingAgain.GetProperty("id").GetString()}/decline",
				FakeConnectionShape.Loopback,
				body: new { });
			await askedAgain;

			await Assert.MultipleAsync(async () =>
			{
				Assert.That(unasked.IsError, Is.Not.True, JsonSerializer.Serialize(unasked));
				Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore + 2));
				Assert.That(policyForClient, Is.EqualTo("allow"));
				Assert.That(policyForOther, Is.EqualTo("ask-first"));
				Assert.That(allowedActions, Is.EqualTo(new[] { $"{TestIntegration}.{SendAction}" }));
				Assert.That(blocked, Does.Contain(McpRunTools.ProviderBlocked));
				Assert.That(
					(await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean(),
					Is.True);
				Assert.That(await AllowedActions(token.Id), Is.Empty);
			});
		}
		finally
		{
			await DeclinePending();
			await SetPolicy(null);
		}
	}

	[Test]
	public async Task Revoking_a_token_drops_the_actions_it_was_always_allowed_and_its_open_requests()
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol, null, _noElicitation);
		await SetPolicy(McpRiskMode.Confirm);
		try
		{
			var waiting = RunSend(client, "still waiting");
			var stillOpen = (await WaitForPendingConfirmation()).GetProperty("id").GetString();
			var run = RunSend(client, "x");
			var id = await WaitForAnotherPendingConfirmation(stillOpen);
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{id}/allow",
				FakeConnectionShape.Loopback,
				body: new { alwaysAllow = true });
			await run;
			var before = await AllowedActions(token.Id);

			await Send(HttpMethod.Post, $"/api/mcp/tokens/{token.Id}/revoke", FakeConnectionShape.Loopback);
			var lateAllow = await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{stillOpen}/allow",
				FakeConnectionShape.Loopback,
				body: new { alwaysAllow = true });
			await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10)));

			await Assert.MultipleAsync(async () =>
			{
				Assert.That(before, Is.Not.Empty);
				Assert.That(await PendingConfirmations(), Is.Empty);
				Assert.That((await lateAllow.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success")
					.GetBoolean(),
					Is.False);
				Assert.That(await AllowedActions(token.Id), Is.Empty);
			});
		}
		finally
		{
			await DeclinePending();
			await SetPolicy(null);
		}
	}

	private static Task<CallToolResult> RunSend(McpClient client, string target)
	{
		return client.CallToolAsync("run_action",
				new Dictionary<string, object?>
				{
					["integrationId"] = TestIntegration,
					["actionId"] = SendAction,
					["parameters"] = new Dictionary<string, object?> { ["target"] = target }
				})
			.AsTask();
	}

	private static async Task<string?> SendPolicy(McpClient client)
	{
		var catalog = await client.CallToolAsync("catalog",
			new Dictionary<string, object?> { ["kind"] = "actions", ["integrationId"] = TestIntegration });
		return catalog.StructuredContent!.Value.GetProperty("actions")
			.EnumerateArray()
			.Single(action => action.GetProperty("actionId").GetString() == SendAction)
			.GetProperty("policy")
			.GetString();
	}

	private async Task<IReadOnlyList<string>> AllowedActions(Guid tokenId)
	{
		var tokens = await (await Send(HttpMethod.Get, "/api/mcp/tokens", FakeConnectionShape.Loopback)).Content
			.ReadFromJsonAsync<JsonElement>();
		return tokens.GetProperty("tokens")
			.EnumerateArray()
			.Single(token => token.GetProperty("id").GetGuid() == tokenId)
			.GetProperty("allowedActions")
			.EnumerateArray()
			.Select(action
				=> $"{action.GetProperty("integrationId").GetString()}.{action.GetProperty("actionId").GetString()}")
			.ToList();
	}

	private async Task<IReadOnlyList<JsonElement>> PendingConfirmations()
	{
		var response = await Send(HttpMethod.Get, "/api/mcp/confirmations", FakeConnectionShape.Loopback);
		return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmations")
			.EnumerateArray()
			.ToList();
	}

	private async Task DeclinePending()
	{
		foreach (var pending in await PendingConfirmations())
		{
			await Send(HttpMethod.Post,
				$"/api/mcp/confirmations/{pending.GetProperty("id").GetString()}/decline",
				FakeConnectionShape.Loopback,
				body: new { });
		}
	}

	private async Task<string?> WaitForAnotherPendingConfirmation(string? known)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			var other = (await PendingConfirmations())
				.Select(pending => pending.GetProperty("id").GetString())
				.FirstOrDefault(id => id != known);
			if (other is not null)
			{
				return other;
			}

			await Task.Delay(20);
		}

		Assert.Fail("No second request appeared in Macro Deck.");
		return null;
	}

	private async Task<JsonElement> WaitForPendingConfirmation()
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			var pending = await PendingConfirmations();
			if (pending.Count > 0)
			{
				return pending[^1];
			}

			await Task.Delay(20);
		}

		Assert.Fail("No request appeared in Macro Deck.");
		return default;
	}

	private async Task<HttpResponseMessage> PostPageForm(string path, Dictionary<string, string> form)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader,
			nameof(FakeConnectionShape.LoopbackWithoutCredential));
		request.Headers.Add("Origin", "http://localhost");
		request.Content = new FormUrlEncodedContent(form);
		return await _client.SendAsync(request);
	}

	[Test]
	public async Task Run_action_runs_an_allowed_action_and_never_resolves_a_secret()
	{
		await using var client = await ConnectWithToken();
		var secretId = await SetSecret(client);

		var run = await client.CallToolAsync("run_action",
			new Dictionary<string, object?>
			{
				["integrationId"] = TestIntegration,
				["actionId"] = SendAction,
				["parameters"] = new Dictionary<string, object?>
				{
					["target"] = "x",
					["token"] = new Dictionary<string, string> { ["$secret"] = secretId.ToString() }
				}
			});

		await Assert.MultipleAsync(() =>
		{
			Assert.That(run.IsError, Is.Not.True, JsonSerializer.Serialize(run));
			Assert.That(JsonSerializer.Serialize(_send.CapturedParameters), Does.Not.Contain(SecretValue));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Deleting_a_token_scrubs_the_secrets_only_its_grants_held()
	{
		var token = await CreateToken();
		Guid secretId;
		await using (var client = await Connect(token.Plaintext, LegacyProtocol, TypingHandler()))
		{
			secretId = await SetSecret(client);
		}

		await Send(HttpMethod.Post, $"/api/mcp/tokens/{token.Id}/revoke", FakeConnectionShape.Loopback);
		var afterRevoke = await Resolve(secretId);
		await Send(HttpMethod.Delete, $"/api/mcp/tokens/{token.Id}", FakeConnectionShape.Loopback);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(afterRevoke, Is.EqualTo(SecretValue));
			Assert.That(await Resolve(secretId), Is.Null);
		});
	}

	private async Task<McpClient> ConnectWithToken(
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitation = null)
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		return await Connect(token.Plaintext, LegacyProtocol, elicitation ?? TypingHandler());
	}

	private static Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> ConfirmingHandler()
	{
		return (_, _) => ValueTask.FromResult(new ElicitResult
		{
			Action = "accept",
			Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
		});
	}

	private Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> TypingHandler()
	{
		return async (request, _) =>
		{
			if (request?.Mode == "url")
			{
				await TypeSecretOnPage(request.Url!);
			}

			return new ElicitResult { Action = "accept" };
		};
	}

	private async Task EnsureTestIntegration()
	{
		if (_integrationRegistered)
		{
			return;
		}

		var registry = _host.Services.GetRequiredService<IIntegrationRegistry>();
		await registry.RegisterAsync(new FakeIntegration { Id = TestIntegration, Actions = [_send] });
		await registry.RegisterAsync(new FakeIntegration
		{
			Id = McpRunInspector.ScriptsIntegrationId,
			Actions =
			[
				new CapturingActionDefinition
				{
					Id = McpRunInspector.RunScriptActionId,
					Parameters = [ActionParameter.Text("scriptId", required: true)]
				}
			]
		});
		_integrationRegistered = true;
	}

	private async Task SetPolicy(McpRiskMode? mode)
	{
		var response = await Send(HttpMethod.Put,
			"/api/mcp/policy",
			FakeConnectionShape.Loopback,
			body: new { integrationId = TestIntegration, mode = mode?.ToString() });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private static async Task<Guid> SetSecret(McpClient client)
	{
		var result = await client.CallToolAsync("set_secret",
			new Dictionary<string, object?> { ["kind"] = "secret", ["label"] = "Token" });
		Assert.That(result.IsError, Is.Not.True, JsonSerializer.Serialize(result));
		return result.StructuredContent!.Value.GetProperty("secret").GetProperty("$secret").GetGuid();
	}

	[Test]
	public async Task A_new_block_that_names_a_protected_variable_or_steers_an_approved_script_asks_first()
	{
		var asked = new List<string?>();
		await using var client = await ConnectWithToken((request, _) =>
		{
			asked.Add(request?.Message);
			return ValueTask.FromResult(new ElicitResult { Action = "decline" });
		});
		var scriptId = await CreateApprovedScriptReading("protected_target");
		var automationsBefore = await ListCount(client, "automations");

		var naming = Flow(null);
		naming["parameters"]![0]!["value"] = "protected_target";
		var namingRun = await client.CallToolAsync("apply_changes",
			new Dictionary<string, object?>
			{
				["changes"] = new[]
					{ Change("create", "automation", data: new { name = "Sets it", flows = Flows(naming) }) }
			});
		var steeringRun = await client.CallToolAsync("apply_changes",
			new Dictionary<string, object?>
			{
				["changes"] = new[]
				{
					Change("create",
						"automation",
						data: new { name = "Steers it", flows = Flows(RunScript(scriptId, "attacker")) })
				}
			});

		var automationsAfter = await ListCount(client, "automations");
		await Assert.MultipleAsync(() =>
		{
			Assert.That(namingRun.IsError, Is.True);
			Assert.That(steeringRun.IsError, Is.True);
			Assert.That(asked, Has.Count.EqualTo(2));
			Assert.That(asked[0], Does.Contain("protected_target"));
			Assert.That(asked[1], Does.Contain("dest"));
			Assert.That(automationsAfter, Is.EqualTo(automationsBefore));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Variable_values_may_be_numbers_or_booleans_and_anything_else_is_reported()
	{
		await using var client = await ConnectWithToken();

		var numbers = await Apply(client,
		[
			Change("create",
				"variable",
				reference: "n",
				data: new { name = $"n_{Guid.NewGuid():N}", type = "numeric", value = 5 }),
			Change("create",
				"variable",
				reference: "b",
				data: new { name = $"b_{Guid.NewGuid():N}", type = "boolean", value = true })
		]);
		var rejected = await Apply(client,
			[Change("create", "variable", data: new { name = $"o_{Guid.NewGuid():N}", value = new { nested = 1 } })],
			true);

		using var scope = _host.Services.CreateScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();
		var created = numbers.GetProperty("createdIds");
		var number = await variables.GetById(Guid.Parse(created.GetProperty("n").GetString()!));
		var boolean = await variables.GetById(Guid.Parse(created.GetProperty("b").GetString()!));

		await Assert.MultipleAsync(() =>
		{
			Assert.That(number?.Value, Is.EqualTo("5"));
			Assert.That(boolean?.Value, Is.EqualTo("true").IgnoreCase);
			Assert.That(Codes(rejected), Does.Contain(McpProblemCodes.InvalidArgument));
			return Task.CompletedTask;
		});
	}

	private async Task<string> CreateApprovedScriptReading(string variable)
	{
		Guid secretId;
		using (var scope = _host.Services.CreateScope())
		{
			secretId = await scope.ServiceProvider.GetRequiredService<ISecretService>()
				.Create("approved", SecretKind.Secret);
		}

		var block = Flow(secretId);
		block["parameters"]![0]!["value"] = "{{ vars." + variable + " }}{{ vars.dest }}";
		var created = await Send(HttpMethod.Post,
			"/api/scripts",
			FakeConnectionShape.Loopback,
			body: new { name = "Approved by the user", flows = ScriptFlows(block).ToJsonString() });
		return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("script")
			.GetProperty("id")
			.GetString()!;
	}

	private static async Task<Guid> CreateFolder(McpClient client)
	{
		var result = await Apply(client,
		[
			Change("create", "profile", reference: "p", data: new { name = "Test" }),
			Change("create", "folder", reference: "f", data: new { profileId = "@p", name = "Test" })
		]);
		Assert.That(result.GetProperty("applied").GetBoolean(), Is.True, result.ToString());
		return Guid.Parse(result.GetProperty("createdIds").GetProperty("f").GetString()!);
	}

	private static async Task<Guid> CreateWidget(McpClient client, Guid folderId, JsonObject? flow)
	{
		var result = await Apply(client,
			[Change("create", "widget", reference: "w", data: WidgetData(folderId, flow))]);
		Assert.That(result.GetProperty("applied").GetBoolean(), Is.True, result.ToString());
		return Guid.Parse(result.GetProperty("createdIds").GetProperty("w").GetString()!);
	}

	private static async Task<JsonElement> Apply(McpClient client, object[] changes, bool dryRun = false)
	{
		var result = await client.CallToolAsync("apply_changes",
			new Dictionary<string, object?> { ["changes"] = changes, ["dryRun"] = dryRun });
		Assert.That(result.IsError, Is.Not.True, JsonSerializer.Serialize(result));
		return result.StructuredContent!.Value;
	}

	private static async Task<int> ListCount(McpClient client, string entity)
	{
		var result = await client.CallToolAsync("list", new Dictionary<string, object?> { ["entity"] = entity });
		return result.StructuredContent!.Value.GetProperty("items").GetArrayLength();
	}

	private static object Change(
		string op,
		string entity,
		string? id = null,
		string? reference = null,
		object? data = null)
	{
		return new { op, entity, id, @ref = reference, data };
	}

	private static object WidgetData(Guid folderId, JsonObject? flow, int positionX = 0)
	{
		var data = new JsonObject();
		if (flow is not null)
		{
			data["flows"] = Flows(flow);
		}

		return new { folderId = folderId.ToString(), positionX, positionY = 0, width = 1, height = 1, data };
	}

	private static JsonArray ScriptFlows(JsonObject block)
	{
		return Flows(block, Application.Scripts.ScriptFlows.TriggerType);
	}

	private static JsonArray Flows(JsonObject block, string trigger = "press")
	{
		return
		[
			new JsonObject
			{
				["triggerId"] = trigger,
				["triggerType"] = trigger,
				["children"] = new JsonArray(block)
			}
		];
	}

	private static JsonObject Flow(Guid? secretId)
	{
		var parameters = new JsonArray(new JsonObject { ["name"] = "target", ["type"] = "string", ["value"] = "x" });
		if (secretId is { } id)
		{
			parameters.Add(new JsonObject
			{
				["name"] = "token",
				["type"] = "secret",
				["value"] = new JsonObject { ["$secret"] = id.ToString() }
			});
		}

		return new JsonObject
		{
			["id"] = "block-1",
			["type"] = "action",
			["integrationId"] = TestIntegration,
			["actionId"] = SendAction,
			["parameters"] = parameters
		};
	}

	private static JsonObject RunScript(string scriptId, string? input)
	{
		var parameters = new JsonArray(new JsonObject
			{ ["name"] = "scriptId", ["type"] = "dynamicchoice", ["value"] = scriptId });
		if (input is not null)
		{
			parameters.Add(new JsonObject { ["name"] = "input:dest", ["type"] = "string", ["value"] = input });
		}

		return new JsonObject
		{
			["id"] = "call-1",
			["type"] = "action",
			["integrationId"] = McpRunInspector.ScriptsIntegrationId,
			["actionId"] = McpRunInspector.RunScriptActionId,
			["parameters"] = parameters
		};
	}

	private static async Task<JsonElement> CatalogEntry(McpClient client)
	{
		var catalog = await client.CallToolAsync("catalog",
			new Dictionary<string, object?> { ["kind"] = "actions", ["integrationId"] = TestIntegration });
		return catalog.StructuredContent!.Value.GetProperty("actions")
			.EnumerateArray()
			.Single(action => action.GetProperty("actionId").GetString() == SendAction);
	}

	private static IEnumerable<string?> Codes(JsonElement result)
	{
		return result.GetProperty("problems")
			.EnumerateArray()
			.Select(problem => problem.GetProperty("code").GetString());
	}

	private IFolderCache Folders()
	{
		return _host.Services.GetRequiredService<IFolderCache>();
	}

	private Domain.Entities.WidgetEntity? FindWidget(Guid id)
	{
		return Folders().GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(widget => widget.Id == id);
	}

	private async Task<string?> Resolve(Guid secretId)
	{
		using var scope = _host.Services.CreateScope();
		return await scope.ServiceProvider.GetRequiredService<ISecretService>().Resolve(secretId);
	}
}
