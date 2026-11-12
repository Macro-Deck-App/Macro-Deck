using System.Runtime.Versioning;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.PlatformInput.MacOS;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
[Category("MacOS")]
public sealed class MacOsWithoutAccessibilityTests
{
	[OneTimeSetUp]
	public void RequireMissingAccessibility()
	{
		if (MacOsAccessibility.IsTrusted())
		{
			Assert.Ignore(
				"Environment: Accessibility is already granted to this process, so its absence cannot be observed.");
		}
	}

	[Test]
	public async Task Both_input_integrations_report_the_missing_permission_as_an_error()
	{
		var keyboardIssues = await new KeyboardInputIntegration().GetIssuesAsync();
		var mouseIssues = await new MouseInputIntegration().GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(keyboardIssues.Select(i => (i.Id, i.Severity)),
				Does.Contain(("accessibility-permission", IntegrationIssueSeverity.Error)));
			Assert.That(mouseIssues.Select(i => (i.Id, i.Severity)),
				Does.Contain(("accessibility-permission", IntegrationIssueSeverity.Error)));
		});
	}

	[Test]
	public async Task Typing_without_the_permission_reports_that_it_is_not_possible()
	{
		var action = new KeyboardInputIntegration().Actions.OfType<TypeTextActionDefinition>().Single();

		ActionResult? result = null;
		Assert.DoesNotThrowAsync(async () => result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["text"] = "no permission" }
			}));
		await Task.CompletedTask;

		Assert.That(result!.Status, Is.Not.EqualTo(ActionResultStatus.Succeeded));
	}
}
