using System.Text.Json;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Localization;
using MacroDeckHost.Ui;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationIssueDtoTests
{
	[Test]
	public void MaxSeverityName_returns_the_highest_severity_in_a_mixed_list()
	{
		var issues = new List<IntegrationIssue>
		{
			new() { Id = "a", Title = "Info", Severity = IntegrationIssueSeverity.Info },
			new() { Id = "b", Title = "Error", Severity = IntegrationIssueSeverity.Error },
			new() { Id = "c", Title = "Warning", Severity = IntegrationIssueSeverity.Warning }
		};

		Assert.That(IntegrationIssueDto.MaxSeverityName(issues), Is.EqualTo("error"));
	}

	[Test]
	public void MaxSeverityName_prefers_warning_over_info()
	{
		var issues = new List<IntegrationIssue>
		{
			new() { Id = "a", Title = "Info", Severity = IntegrationIssueSeverity.Info },
			new() { Id = "b", Title = "Warning", Severity = IntegrationIssueSeverity.Warning }
		};

		Assert.That(IntegrationIssueDto.MaxSeverityName(issues), Is.EqualTo("warning"));
	}

	[Test]
	public void MaxSeverityName_returns_null_for_an_empty_list()
	{
		Assert.That(IntegrationIssueDto.MaxSeverityName([]), Is.Null);
	}

	/// <summary>
	/// The client resolves localized text itself so a language change re-renders without a refetch
	/// (issue #326), so the wire must carry the reference - not the host's rendering of it - while a
	/// literal description stays a plain string on the very same kind of property.
	/// </summary>
	[Test]
	public void A_localized_issue_travels_as_a_reference_and_a_literal_one_as_text()
	{
		var issue = new IntegrationIssue
		{
			Id = "keyboard-permission",
			Title = AppStrings.Integrations.Keyboard.Issues.PermissionRequiredTitle(),
			Description = "Written by the provider itself",
			Severity = IntegrationIssueSeverity.Info,
			ActionLabel = AppStrings.Integrations.Keyboard.Issues.GrantPermissionAction()
		};

		using var payload = JsonDocument.Parse(
			JsonSerializer.Serialize(IntegrationIssueDto.From(issue), HostPayloadOptions()));
		var root = payload.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(Reference(root, "title"),
				Is.EqualTo(("macrodeck.app", "Integrations.Keyboard.Issues.PermissionRequiredTitle")));
			Assert.That(Reference(root, "actionLabel"),
				Is.EqualTo(("macrodeck.app", "Integrations.Keyboard.Issues.GrantPermissionAction")));
			Assert.That(root.GetProperty("description").GetString(), Is.EqualTo("Written by the provider itself"));
			Assert.That(root.GetRawText(), Does.Not.Contain("Accessibility permission required"));
			Assert.That(root.GetRawText(), Does.Not.Contain("Grant permission"));
		});
	}

	[Test]
	public void A_web_help_link_reaches_the_desktop()
	{
		var dto = IntegrationIssueDto.From(new IntegrationIssue
			{ Id = "missing", Title = "Missing", HelpUrl = new Uri("https://www.autohotkey.com/download/") });

		Assert.That(dto.HelpUrl, Is.EqualTo("https://www.autohotkey.com/download/"));
	}

	[TestCase("file:///C:/Windows/System32/calc.exe")]
	[TestCase("javascript:alert(1)")]
	[TestCase("ms-settings:privacy")]
	[TestCase("/relative/help")]
	public void A_help_link_that_is_not_an_absolute_web_address_is_dropped(string address)
	{
		var dto = IntegrationIssueDto.From(new IntegrationIssue
			{ Id = "missing", Title = "Missing", HelpUrl = new Uri(address, UriKind.RelativeOrAbsolute) });

		Assert.That(dto.HelpUrl, Is.Null);
	}

	[Test]
	public void An_issue_without_a_help_link_sends_the_payload_it_sent_before()
	{
		var json = JsonSerializer.Serialize(IntegrationIssueDto.From(new IntegrationIssue { Id = "a", Title = "A" }),
			HostPayloadOptions());

		Assert.That(json, Does.Not.Contain("helpUrl"));
	}

	private static (string Scope, string Key) Reference(JsonElement issue, string property)
	{
		var reference = issue.GetProperty(property).GetProperty("$localized");
		return (reference.GetProperty("scope").GetString()!, reference.GetProperty("key").GetString()!);
	}

	private static JsonSerializerOptions HostPayloadOptions()
	{
		return UiWebSocketProtocol.Json;
	}
}
