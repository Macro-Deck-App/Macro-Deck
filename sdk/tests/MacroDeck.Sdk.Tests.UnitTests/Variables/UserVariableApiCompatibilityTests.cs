using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableApiCompatibilityTests
{
	/// <summary>
	/// The compatibility commitment for a plugin-facing interface: an implementation compiled against the
	/// SDK that had only <c>ApplyAsync</c> still builds and still runs. It must also not claim to have
	/// created anything, which a default returning <c>Created()</c> would.
	/// </summary>
	[Test]
	public async Task An_implementation_written_before_CreateAsync_reports_that_it_does_not_support_it()
	{
		IUserVariableApi api = new ApplyOnlyUserVariableApi();

		var result = await api.CreateAsync("greeting", null, VariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.NotSupported));
			Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public async Task An_implementation_written_before_contexts_receives_a_context_write_as_its_owner_widget()
	{
		var implementation = new ApplyOnlyUserVariableApi();
		IUserVariableApi api = implementation;

		var applied = await api.ApplyInContextAsync("count",
			new UserVariableContext("widget-1", "script-1", "automation-1"),
			UserVariableOperation.Add,
			"1");
		var created = await api.CreateInContextAsync("count",
			new UserVariableContext(ScriptId: "script-1"),
			VariableType.Numeric);

		Assert.Multiple(() =>
		{
			Assert.That(applied.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(implementation.Calls.Single(),
				Is.EqualTo(("count", "widget-1", UserVariableOperation.Add, "1")));
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.NotSupported));
		});
	}

	[Test]
	public async Task A_caller_passing_a_literal_null_owner_still_reaches_the_original_methods()
	{
		var implementation = new ApplyOnlyUserVariableApi();
		IUserVariableApi api = implementation;

		await api.ApplyAsync("count", null, UserVariableOperation.Set, "1");
		await api.ApplyAsync("count", default, UserVariableOperation.Set, "1", CancellationToken.None);
		var created = await api.CreateAsync("count", null, VariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(implementation.Calls.Select(call => call.OwnerWidgetId), Is.All.Null);
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.NotSupported));
		});
	}

	[Test]
	public void The_context_of_an_executing_step_names_its_widget_script_and_automation()
	{
		var context = UserVariableContext.From(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			OwnerWidgetId = "widget-1",
			ScriptId = "script-1",
			AutomationId = "automation-1"
		});

		Assert.That(context, Is.EqualTo(new UserVariableContext("widget-1", "script-1", "automation-1")));
	}

	[Test]
	public void The_new_create_statuses_extend_the_old_numbering_without_moving_it()
	{
		Assert.Multiple(() =>
		{
			Assert.That((int)UserVariableCreateStatus.Unavailable, Is.EqualTo(6));
			Assert.That((int)UserVariableCreateStatus.UnknownScript, Is.EqualTo(7));
			Assert.That((int)UserVariableCreateStatus.UnknownAutomation, Is.EqualTo(8));
		});
	}

	private sealed class ApplyOnlyUserVariableApi : IUserVariableApi
	{
		public List<(string Name, string? OwnerWidgetId, UserVariableOperation Operation, string? Value)> Calls { get; }
			= [];

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			Calls.Add((name, ownerWidgetId, operation, value));
			return Task.FromResult(UserVariableWriteResult.Applied());
		}
	}
}
