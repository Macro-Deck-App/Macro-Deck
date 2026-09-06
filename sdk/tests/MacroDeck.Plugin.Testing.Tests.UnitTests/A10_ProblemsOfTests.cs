using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using Microsoft.AspNetCore.Builder;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A10 - <see cref="PluginTestHarness.ProblemsOf" /> reports every configuration problem, not just the
/// first, and never throws.
/// </summary>
[TestFixture]
public class A10_ProblemsOfTests
{
	[Test]
	public void ProblemsOf_reports_every_simultaneous_problem_without_throwing()
	{
		IReadOnlyList<string>? problems = null;

		// Id validity now comes from the manifest rather than a builder call, so a deliberately invalid
		// id ("MyPlugin" - no dot, not lowercase-kebab) has to be supplied through one, disposed here
		// since ProblemsOf never returns anything else that would own it.
		using var manifest = new PluginTestManifest(id: "MyPlugin");

		Assert.DoesNotThrow(() =>
		{
			problems = PluginTestHarness.ProblemsOf(builder => builder
					.RegisterIntegration(_ =>
					{
						var integration = new TestIntegration("test.a10.one");
						integration.WithAction(new DelegateAction("play",
							_ => Task.FromResult(ActionResult.Success())));
						return integration;
					})
					.RegisterIntegration(_ =>
					{
						var integration = new SecondTestIntegration("test.a10.two");
						integration.WithAction(new DelegateAction("play",
							_ => Task.FromResult(ActionResult.Success())));
						return integration;
					})
					// PluginHostBuilder.Configure's IApplicationBuilder is the same WebApplication instance
					// PluginHostBuilder.Build maps every capability endpoint onto, so the cast is safe.
					.Configure((_, app) => ((WebApplication)app).MapGet("/_macrodeck/custom", () => "not allowed")),
				manifest);
		});

		Assert.That(problems, Is.Not.Null);
		Assert.That(problems, Has.Count.EqualTo(3));
		Assert.That(problems!.Any(problem => problem.Contains("MyPlugin", StringComparison.Ordinal)), Is.True);
		Assert.That(problems!.Any(problem => problem.Contains("play", StringComparison.Ordinal)), Is.True);
		Assert.That(problems!.Any(problem => problem.Contains("/_macrodeck/custom", StringComparison.Ordinal)),
			Is.True);
	}

	[Test]
	public void ProblemsOf_returns_empty_for_a_valid_configuration()
	{
		var problems = PluginTestHarness.ProblemsOf(builder => builder
			.RegisterIntegration(_ => new TestIntegration("com.example.valid-plugin")));

		Assert.That(problems, Is.Empty);
	}
}
