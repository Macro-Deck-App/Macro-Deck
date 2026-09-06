using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Integrations.System.Application;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ApplicationActionsTests
{
	private static readonly string[] _expectedOpenedFiles = ["/tmp/file.txt"];
	private static readonly string[] _expectedOpenedWebsites = ["https://example.com/dashboard"];
	private static readonly string[] _expectedOpenedFolders = ["/tmp"];

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	[Test]
	public async Task LaunchApplication_forwards_path_arguments_working_directory_mode_and_admin()
	{
		var applications = new FakeApplicationService();
		var action = new LaunchApplicationActionDefinition(applications);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["path"] = "/usr/bin/app",
			["arguments"] = "--flag",
			["workingDirectory"] = "/work",
			["mode"] = "start-focus",
			["runAsAdmin"] = true
		}));

		Assert.That(applications.Launches, Has.Count.EqualTo(1));
		Assert.That(applications.Launches[0],
			Is.EqualTo(("/usr/bin/app", "--flag", "/work", LaunchMode.StartFocus, true)));
	}

	[Test]
	public void LaunchApplication_exposes_run_as_admin_only_on_windows()
	{
		var action = new LaunchApplicationActionDefinition(new FakeApplicationService());

		var hasRunAsAdmin = action.Parameters.Any(p => p.Name == "runAsAdmin");
		var hasWorkingDirectory = action.Parameters.Any(p => p.Name == "workingDirectory");

		Assert.Multiple(() =>
		{
			Assert.That(hasRunAsAdmin, Is.EqualTo(OperatingSystem.IsWindows()));
			Assert.That(hasWorkingDirectory, Is.True);
		});
	}

	[Test]
	public async Task LaunchApplication_ignores_blank_path()
	{
		var applications = new FakeApplicationService();
		var action = new LaunchApplicationActionDefinition(applications);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["path"] = "" }));

		Assert.That(applications.Launches, Is.Empty);
	}

	[TestCase("start", LaunchMode.Start)]
	[TestCase("start-stop", LaunchMode.StartStop)]
	[TestCase("start-focus", LaunchMode.StartFocus)]
	[TestCase("unknown", LaunchMode.Start)]
	public void ParseMode_maps_choice_values(string value, LaunchMode expected)
		=> Assert.That(LaunchApplicationActionDefinition.ParseMode(value), Is.EqualTo(expected));

	[Test]
	public async Task OpenWebsite_OpenFile_and_OpenFolder_forward_values()
	{
		var applications = new FakeApplicationService();

		await new OpenWebsiteActionDefinition(applications).CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["url"] = "https://example.com/dashboard" }));
		await new OpenFileActionDefinition(applications).CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["path"] = "/tmp/file.txt" }));
		await new OpenFolderActionDefinition(applications).CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["path"] = "/tmp" }));

		Assert.Multiple(() =>
		{
			Assert.That(applications.OpenedWebsites, Is.EqualTo(_expectedOpenedWebsites));
			Assert.That(applications.OpenedFiles, Is.EqualTo(_expectedOpenedFiles));
			Assert.That(applications.OpenedFolders, Is.EqualTo(_expectedOpenedFolders));
		});
	}

	[Test]
	public void OpenWebsite_exposes_a_required_url_parameter()
	{
		var action = new OpenWebsiteActionDefinition(new FakeApplicationService());
		var parameter = action.Parameters.Single();

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Name, Is.EqualTo("url"));
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.Url));
			Assert.That(parameter.Required, Is.True);
		});
	}

	[TestCase("http://example.com")]
	[TestCase("https://example.com/dashboard?tab=1")]
	public void IsWebsiteUrl_accepts_http_and_https_urls(string url)
		=> Assert.That(ApplicationServiceBase.IsWebsiteUrl(url), Is.True);

	[TestCase("")]
	[TestCase("example.com")]
	[TestCase("ftp://example.com")]
	[TestCase("mailto:hello@example.com")]
	public void IsWebsiteUrl_rejects_missing_or_unsupported_protocols(string url)
		=> Assert.That(ApplicationServiceBase.IsWebsiteUrl(url), Is.False);

	[Test]
	public async Task KillApplication_defaults_to_graceful()
	{
		var applications = new FakeApplicationService();
		var action = new KillApplicationActionDefinition(applications);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["process"] = "notepad" }));

		Assert.That(applications.Kills, Is.EqualTo(new[] { ("notepad", true) }));
	}
}
