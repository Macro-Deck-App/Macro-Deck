using MacroDeckHost.Integrations.Keyboard;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardActionDefinitionTests
{
	[TestCase("press-key")]
	[TestCase("type-text")]
	[TestCase("run-sequence")]
	public void Targetable_actions_expose_process_scoping_parameters(string actionId)
	{
		var action = new KeyboardInputIntegration().Actions.Single(a => a.Id == actionId);
		var names = action.Parameters.Select(p => p.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("targetProcess"));
			Assert.That(names, Does.Contain("targetMode"));
		});
	}

	[Test]
	public void Target_process_is_backed_by_the_processes_options_source()
	{
		var pressKey = new KeyboardInputIntegration().Actions.Single(a => a.Id == "press-key");
		var targetProcess = pressKey.Parameters.Single(p => p.Name == "targetProcess");

		Assert.Multiple(() =>
		{
			Assert.That(targetProcess.Type, Is.EqualTo(ActionParameterType.Autocomplete));
			Assert.That(targetProcess.OptionsSourceId, Is.EqualTo("system.processes"));
		});
	}

	[Test]
	public void Hold_key_offers_a_target_without_focus_then_send()
	{
		var action = new KeyboardInputIntegration().Actions.Single(a => a.Id == "key-down");
		var targetMode = action.Parameters.Single(p => p.Name == "targetMode");

		Assert.Multiple(() =>
		{
			Assert.That(action.Parameters.Select(p => p.Name), Does.Contain("targetProcess"));
			Assert.That(targetMode.Options!.Select(o => o.Value), Is.EquivalentTo(new[] { "focused", "background" }));
		});
	}

	[Test]
	public void Release_key_needs_no_target_because_it_releases_wherever_the_key_is_held()
	{
		var action = new KeyboardInputIntegration().Actions.Single(a => a.Id == "key-up");
		Assert.That(action.Parameters.Select(p => p.Name), Does.Not.Contain("targetProcess"));
	}
}
