using System.Text.Json;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpVariableGuardTests
{
	private const string Integration = "app.macro-deck.http";
	private const string Protected = "api_target";

	private FakeTimeProvider _time = null!;
	private McpExecutionRegistry _registry = null!;
	private RecordingUserVariables _inner = null!;
	private McpGuardedUserVariableApi _api = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_registry = new McpExecutionRegistry(_time);
		_inner = new RecordingUserVariables();
		var guard = new McpVariableWriteGuard(_registry, new FixedReferences(Protected));
		_api = new McpGuardedUserVariableApi(Integration, _inner, guard);
	}

	[Test]
	public async Task A_user_started_write_to_a_protected_variable_passes_when_no_mcp_run_is_in_flight()
	{
		var result = await Write(Protected);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
	}

	[Test]
	public async Task A_write_to_a_protected_variable_during_an_mcp_run_is_refused()
	{
		using (_registry.Enter(Integration))
		{
			var result = await Write(Protected);
			var other = await Write("unrelated");

			await Assert.MultipleAsync(() =>
			{
				Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
				Assert.That(result.Message, Does.StartWith(McpVariableWriteGuard.RefusalCode));
				Assert.That(other.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
				Assert.That(_inner.Written, Is.EqualTo(new[] { "unrelated" }));
				return Task.CompletedTask;
			});
		}
	}

	[Test]
	public async Task A_late_write_after_an_accepted_mcp_action_returned_is_refused_within_the_grace_period()
	{
		_registry.Enter(Integration).Dispose();
		_time.Advance(McpExecutionRegistry.WriteGracePeriod - TimeSpan.FromSeconds(1));

		var result = await Write(Protected);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
	}

	[Test]
	public async Task A_user_started_write_succeeds_once_the_grace_period_after_an_mcp_run_has_passed()
	{
		_registry.Enter(Integration).Dispose();
		_time.Advance(McpExecutionRegistry.WriteGracePeriod + TimeSpan.FromSeconds(1));

		var result = await Write(Protected);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
	}

	[Test]
	public async Task
		A_consented_run_may_write_the_variable_the_user_confirmed_but_a_parallel_unconsented_run_blocks_it()
	{
		UserVariableWriteResult consented;
		UserVariableWriteResult parallel;
		using (_registry.Enter(Integration, [Protected]))
		{
			consented = await Write(Protected);
			using (_registry.Enter(Integration))
			{
				parallel = await Write(Protected);
			}
		}

		await Assert.MultipleAsync(() =>
		{
			Assert.That(consented.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(parallel.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Another_integration_is_not_attributed_to_an_mcp_run()
	{
		var other = new McpGuardedUserVariableApi("app.macro-deck.obs",
			_inner,
			new McpVariableWriteGuard(_registry, new FixedReferences(Protected)));
		using (_registry.Enter(Integration))
		{
			var result = await other.ApplyInContextAsync(Protected,
				new UserVariableContext(),
				UserVariableOperation.Set,
				"x");

			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
		}
	}

	[Test]
	public void Template_references_cover_every_form_a_block_parameter_reads_a_value_in()
	{
		var block = FlowBlockContent.FromJson(JsonDocument.Parse("""
																 {
																 	"type": "action",
																 	"integrationId": "app.macro-deck.http",
																 	"actionId": "request",
																 	"parameters": [
																 		{ "name": "url", "type": "string", "value": "https://{{ vars.host }}/{{ vars[\"path\"] }}" },
																 		{ "name": "body", "type": "json", "value": { "a": { "$var": "payload" }, "b": { "$event": "user" } } },
																 		{ "name": "note", "type": "string", "value": "{{ event.title }}" },
																 		{ "name": "token", "type": "secret", "value": { "$secret": "6f9619ff-8b86-d011-b42d-00c04fc964ff" } }
																 	]
																 }
																 """)
			.RootElement);

		var references = McpTemplateReferences.OfNonSecretParameters(block);

		Assert.Multiple(() =>
		{
			Assert.That(references.Variables, Is.EquivalentTo(new[] { "host", "path", "payload" }));
			Assert.That(references.EventParameters, Is.EquivalentTo(new[] { "user", "title" }));
		});
	}

	private Task<UserVariableWriteResult> Write(string name)
	{
		return _api.ApplyInContextAsync(name, new UserVariableContext(), UserVariableOperation.Set, "value");
	}

	private sealed class FixedReferences : IMcpProtectedReferences
	{
		private readonly HashSet<string> _names;

		public FixedReferences(params string[] names)
		{
			_names = names.ToHashSet(StringComparer.Ordinal);
		}

		public IReadOnlySet<string> ProtectedVariables()
		{
			return _names;
		}

		public McpBlockReferences OfOwner(FlowOwner owner, string? flowsJson)
		{
			return new McpBlockReferences(new HashSet<string>(), new HashSet<string>());
		}
	}

	private sealed class RecordingUserVariables : IUserVariableApi
	{
		public List<string> Written { get; } = [];

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			Written.Add(name);
			return Task.FromResult(UserVariableWriteResult.Applied());
		}
	}
}
