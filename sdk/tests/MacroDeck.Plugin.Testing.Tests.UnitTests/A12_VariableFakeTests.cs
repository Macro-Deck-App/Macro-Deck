using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A12 - the variable fakes reproduce the documented failure statuses, rather than a fake that always
/// reports success and hides a plugin that never handles <c>NotFound</c>.
/// </summary>
[TestFixture]
public class A12VariableFakeTests
{
	private static readonly string[] _refreshedIds = ["muted", "volume-percent"];
	private static readonly string[] _boundResources = ["entity/light.kitchen/state", "entity/switch.desk/state"];

	[Test]
	public async Task FakeUserVariableApi_reproduces_the_documented_failure_statuses()
	{
		var api = new FakeUserVariableApi();
		api.SeedGlobal("counter", VariableType.Numeric, 10.0);
		api.SeedGlobal("owned", VariableType.Numeric, 1.0, true);
		api.SeedGlobal("label", VariableType.Text, "hello");
		api.SeedGlobal("flag", VariableType.Boolean, false);
		api.SeedWidgetScoped("widget-1", "counter", VariableType.Numeric, 99.0);

		var neverSeeded = await api.ApplyAsync("never-seeded", null, UserVariableOperation.Set, "x");
		var notEditable = await api.ApplyAsync("owned", null, UserVariableOperation.Set, "2");
		var addOnText = await api.ApplyAsync("label", null, UserVariableOperation.Add, "1");
		var toggle = await api.ApplyAsync("flag", null, UserVariableOperation.Toggle, "this operand must be ignored");

		Assert.Multiple(() =>
		{
			Assert.That(neverSeeded.Status, Is.EqualTo(UserVariableWriteStatus.NotFound));
			Assert.That(notEditable.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
			Assert.That(addOnText.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
			Assert.That(toggle.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(api.GetCurrentValue("flag"), Is.EqualTo(true));
		});

		Assert.Multiple(() =>
		{
			// A widget-scoped variable wins over a global one of the same name...
			Assert.That(api.GetCurrentValue("counter", "widget-1"), Is.EqualTo(99.0));
			// ...but a null owner resolves globally, even though a widget-scoped "counter" exists elsewhere.
			Assert.That(api.GetCurrentValue("counter", null), Is.EqualTo(10.0));
		});
	}

	[Test]
	public async Task FakeUserVariableApi_resolves_and_creates_in_a_script_or_automation_context()
	{
		var api = new FakeUserVariableApi();
		api.SeedGlobal("count", VariableType.Numeric, 1m);
		api.SeedWidgetScoped("widget-1", "count", VariableType.Numeric, 10m);
		api.SeedScriptScoped("script-1", "count", VariableType.Numeric, 100m);
		api.SeedAutomation("automation-1");

		var inScript = await api.ApplyInContextAsync("count",
			new UserVariableContext("widget-1", "script-1"),
			UserVariableOperation.Add,
			"1");
		var created = await api.CreateInContextAsync("result",
			new UserVariableContext(AutomationId: "automation-1"),
			VariableType.Text,
			"x");
		var unknown = await api.CreateInContextAsync("result",
			new UserVariableContext(ScriptId: "script-2"),
			VariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(inScript.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(api.GetCurrentValueInContext("count", new UserVariableContext("widget-1", "script-1")),
				Is.EqualTo(101m));
			Assert.That(api.GetCurrentValue("count", "widget-1"), Is.EqualTo(10m));
			Assert.That(api.GetCurrentValue("count"), Is.EqualTo(1m));
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(api.GetCurrentValueInContext("result", new UserVariableContext(AutomationId: "automation-1")),
				Is.EqualTo("x"));
			Assert.That(api.GetCurrentValue("result"), Is.Null);
			Assert.That(unknown.Status, Is.EqualTo(UserVariableCreateStatus.UnknownScript));
			Assert.That(api.LastContext, Is.EqualTo(new UserVariableContext(ScriptId: "script-2")));
		});
	}

	[Test]
	public async Task An_action_run_as_a_script_step_reaches_the_fake_with_its_script()
	{
		TestIntegration? integration = null;
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ =>
			integration = new TestIntegration("test.a12.context").WithAction(new DelegateAction("write",
				async context =>
				{
					var written = await integration!.Context!.UserVariables.ApplyInContextAsync("result",
						UserVariableContext.From(context),
						UserVariableOperation.Set,
						"done",
						context.CancellationToken);
					return written.Status == UserVariableWriteStatus.Applied
						? ActionResult.Success()
						: ActionResult.Failed(ActionErrorCodes.NotFound, "not written");
				}))));
		await harness.InitializeIntegrationsAsync();
		harness.Context.UserVariables.SeedGlobal("result", VariableType.Text, "global");
		harness.Context.UserVariables.SeedScriptScoped("script-1", "result", VariableType.Text, "local");

		var outcome = await harness.Actions.ExecuteWithContextAsync("write", scriptId: "script-1");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.True);
			Assert.That(harness.Context.UserVariables.LastContext?.ScriptId, Is.EqualTo("script-1"));
			Assert.That(harness.Context.UserVariables.GetCurrentValueInContext("result",
					new UserVariableContext(ScriptId: "script-1")),
				Is.EqualTo("done"));
			Assert.That(harness.Context.UserVariables.GetCurrentValue("result"), Is.EqualTo("global"));
		});
	}

	[Test]
	public async Task FakeUserVariableApi_validates_a_color_variable_like_the_host()
	{
		var api = new FakeUserVariableApi();

		var created = await api.CreateAsync("primary", null, VariableType.Color);
		var refused = await api.CreateAsync("accent", null, VariableType.Color, "red");
		var set = await api.ApplyAsync("primary", null, UserVariableOperation.Set, "#3366FFCC");
		var invalid = await api.ApplyAsync("primary", null, UserVariableOperation.Set, "not a colour");
		var append = await api.ApplyAsync("primary", null, UserVariableOperation.Append, "ff");

		Assert.Multiple(() =>
		{
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(refused.Status, Is.EqualTo(UserVariableCreateStatus.InvalidValue));
			Assert.That(set.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(invalid.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
			Assert.That(append.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
			Assert.That(api.GetCurrentValue("primary"), Is.EqualTo("#3366ffcc"));
		});
	}

	[Test]
	public async Task FakeUserVariableApi_create_reproduces_the_documented_refusals()
	{
		var api = new FakeUserVariableApi();
		api.SeedGlobal("counter", VariableType.Numeric, 10.0);
		api.SeedWidget("widget-1");

		var duplicate = await api.CreateAsync("counter", null, VariableType.Numeric);
		var shadow = await api.CreateAsync("counter", "widget-1", VariableType.Numeric, "99");
		var unknownWidget = await api.CreateAsync("fresh", "no-such-widget", VariableType.Text);
		var badValue = await api.CreateAsync("count", null, VariableType.Numeric, "many");

		// Every assertion here is a refusal: a fake whose CreateAsync always succeeded would let a plugin
		// ship without ever handling UnknownWidget or AlreadyExists.
		Assert.Multiple(() =>
		{
			Assert.That(duplicate.Status, Is.EqualTo(UserVariableCreateStatus.AlreadyExists));
			Assert.That(api.GetCurrentValue("counter"), Is.EqualTo(10.0));

			Assert.That(shadow.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(api.GetCurrentValue("counter", "widget-1"), Is.EqualTo(99m));
			Assert.That(api.GetCurrentValue("counter"), Is.EqualTo(10.0));

			Assert.That(unknownWidget.Status, Is.EqualTo(UserVariableCreateStatus.UnknownWidget));
			Assert.That(api.GetCurrentValue("fresh", "no-such-widget"), Is.Null);

			Assert.That(badValue.Status, Is.EqualTo(UserVariableCreateStatus.InvalidValue));
			Assert.That(api.GetCurrentValue("count"), Is.Null);
		});
	}

	[Test]
	public async Task FakeVariableApi_reproduces_the_plain_crud_contract()
	{
		var api = new FakeVariableApi();

		Assert.That(await api.GetByNameAsync("unknown"), Is.Null);
		Assert.DoesNotThrowAsync(async () => await api.DeleteAsync(Guid.NewGuid()));

		var handle = await api.CreateAsync("temp", VariableType.Numeric, 5.0);

		Assert.Multiple(() =>
		{
			Assert.That(api.Created, Has.Count.EqualTo(1));
			Assert.That(api.Created[0].Id, Is.EqualTo(handle.Id));
		});
	}

	[Test]
	public async Task FakeVariableApi_records_definition_refreshes_and_bindings_as_the_host_does()
	{
		var api = new FakeVariableApi();
		var kitchen = new VariableBindingRequest("entity/light.kitchen/state", "kitchen", VariableType.Text);

		await api.RequestDefinitionRefreshAsync(_refreshedIds);
		await api.RequestDefinitionRefreshAsync([]);
		var first = await api.BindAsync([kitchen]);
		var second = await api.BindAsync([kitchen, kitchen with { LocalResourceId = "entity/switch.desk/state" }]);

		Assert.Multiple(() =>
		{
			Assert.That(api.DefinitionRefreshRequests.Single(), Is.EqualTo(_refreshedIds));
			Assert.That(api.RefreshRequests, Is.Zero);
			Assert.That(first, Is.EqualTo(new[] { VariableBindingOutcome.Bound }));
			Assert.That(second,
				Is.EqualTo(new[] { VariableBindingOutcome.AlreadyBound, VariableBindingOutcome.Bound }));
			Assert.That(api.Bindings.Select(binding => binding.LocalResourceId),
				Is.EqualTo(_boundResources));
			Assert.ThrowsAsync<ArgumentException>(() => api.BindAsync([kitchen with { Name = "Not Canonical" }]));
		});
	}

	[Test]
	public async Task FakeVariableApi_counts_refresh_requests_and_declared_set_changes()
	{
		var api = new FakeVariableApi();

		await api.RequestRefreshAsync();
		await api.RequestRefreshAsync();
		await api.NotifyDeclarationsChangedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(api.RefreshRequests, Is.EqualTo(2));
			Assert.That(api.DeclarationChanges, Is.EqualTo(1));
		});
	}
}
