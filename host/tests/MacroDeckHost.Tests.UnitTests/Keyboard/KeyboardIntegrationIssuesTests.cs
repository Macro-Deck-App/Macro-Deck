using MacroDeck.Localization;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.Keyboard;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardIntegrationIssuesTests
{
	[Test]
	public async Task A_degraded_session_is_a_warning_with_the_reason_of_the_provider()
	{
		var reason = LocalizedText.FromLiteral("limited");
		var integration = new KeyboardInputIntegration(new FakeKeyboardInputProvider
			{ IsDegraded = true, DegradedReason = reason });

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues, Has.Count.EqualTo(1));
		await Assert.MultipleAsync(() =>
		{
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			Assert.That(issues[0].Description, Is.EqualTo(reason));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_full_session_reports_nothing()
	{
		var integration = new KeyboardInputIntegration(new FakeKeyboardInputProvider());

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
	}
}
