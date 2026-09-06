using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A1 - an action invocation reports the plugin's real outcome, never a fabricated one.
/// </summary>
[TestFixture]
public class A1_ActionOutcomeTests
{
	[Test]
	public async Task Succeed_accept_and_fail_all_report_the_plugins_real_outcome()
	{
		var integration = new TestIntegration("test.a1");

		integration.WithAction(new DelegateAction("succeed",
			async _ =>
			{
				await integration.Context!.Variables.CreateAsync("sentinel", VariableType.Text, "created");
				return ActionResult.Success();
			}));

		integration.WithAction(new DelegateAction("accept",
			_ => Task.FromResult(ActionResult.Accepted("waiting on provider"))));

		integration.WithAction(new DelegateAction("fail",
			_ => Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
				"The provider is not connected."))));

		await using var harness = PluginTestHarness.Create(builder => builder
			.RegisterIntegration(_ => integration));

		await harness.InitializeIntegrationsAsync();

		var succeeded = await harness.Actions.ExecuteAsync("succeed");
		var accepted = await harness.Actions.ExecuteAsync("accept");
		var failed = await harness.Actions.ExecuteAsync("fail");

		Assert.Multiple(() =>
		{
			Assert.That(succeeded.Succeeded, Is.True);
			Assert.That(succeeded.DataAs<ActionExecuteResult>()!.Accepted, Is.False);
			Assert.That(harness.Context.Variables.Created,
				Has.Count.EqualTo(1),
				"the sentinel create was not recorded exactly once");

			Assert.That(accepted.Succeeded, Is.True);
			Assert.That(accepted.DataAs<ActionExecuteResult>()!.Accepted, Is.True);
			Assert.That(accepted.DataAs<ActionExecuteResult>()!.Message?.Literal, Is.EqualTo("waiting on provider"));

			Assert.That(failed.Succeeded, Is.False);
			Assert.That(failed.Error, Is.Not.Null);
			Assert.That(failed.Error!.Code, Is.EqualTo(ActionErrorCodes.NotConnected));
		});
	}
}
