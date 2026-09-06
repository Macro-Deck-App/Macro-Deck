using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class IActionDefinitionTests
{
	/// <summary>
	/// A default interface member does not dispatch off the concrete type - reading
	/// <c>new Plain().Platforms</c> directly would not even compile - so the default has to be observed
	/// through an <see cref="IActionDefinition"/>-typed reference, exactly as the host reads it.
	/// </summary>
	[Test]
	public void An_action_runs_everywhere_unless_it_says_otherwise()
	{
		IActionDefinition action = new Plain();

		Assert.Multiple(() =>
		{
			Assert.That(action.Platforms, Is.EqualTo(MacroDeckPlatform.All));
			Assert.That(action.RunsHere(), Is.True);
		});
	}

	[Test]
	public void An_override_reports_the_narrowed_set()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		IActionDefinition action = new Restricted(otherPlatforms);

		Assert.That(action.Platforms, Is.EqualTo(otherPlatforms));
	}

	[Test]
	public void RunsHere_is_true_when_the_set_contains_the_current_platform()
	{
		IActionDefinition action = new Restricted(MacroDeckIntegrationAttribute.Current);

		Assert.That(action.RunsHere(), Is.True);
	}

	[Test]
	public void RunsHere_is_false_when_the_set_excludes_the_current_platform()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		IActionDefinition action = new Restricted(otherPlatforms);

		Assert.That(action.RunsHere(), Is.False);
	}

	private sealed class Plain : IActionDefinition
	{
		public string Id => "plain";

		public LocalizedText Name => "Plain";

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}

	private sealed class Restricted : IActionDefinition
	{
		public Restricted(MacroDeckPlatform platforms) => Platforms = platforms;

		public string Id => "restricted";

		public LocalizedText Name => "Restricted";

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public MacroDeckPlatform Platforms { get; }

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}
}
