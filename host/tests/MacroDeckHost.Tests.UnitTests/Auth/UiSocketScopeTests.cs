using System.Net;
using System.Text.RegularExpressions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Auth;

// A client-scope token is what a deck device or the web client holds. It may press, watch and render, never
// open the admin configuration surfaces or learn script and automation definitions, which the REST routes
// already keep admin-only (engineering/api/authentication.md).
[NonParallelizable]
public partial class UiSocketScopeTests
{
	private static readonly string[] _adminOnlyTypes =
	[
		"RenderLabelPreview",
		"SubscribeLogs",
		"UnsubscribeLogs",
		"ListUiPreviews",
		"OpenUiPreviewSession",
		"OpenPageUiSession",
		"OpenConfigUiSession"
	];

	// Every request type the dispatcher serves, classified on purpose. A new type fails the source check
	// below until it is added here, so its scope is a decision rather than a default.
	private static readonly Dictionary<string, bool> _requiresAdmin = new(StringComparer.Ordinal)
	{
		["SubscribeLabel"] = false,
		["UnsubscribeLabel"] = false,
		["SubscribeWidgetState"] = false,
		["UnsubscribeWidgetState"] = false,
		["WatchVariables"] = false,
		["RenderLabelPreview"] = true,
		["GetMusicPlayerInstances"] = false,
		["GetMusicPlayerState"] = false,
		["GetWeatherInstances"] = false,
		["GetFolderViews"] = false,
		["GetScreenSavers"] = false,
		["GetDeviceScreenSaver"] = false,
		["GetDeviceClientSettings"] = false,
		["SetDeviceClientSettings"] = false,
		["OpenScreenSaverUiSession"] = false,
		["GetWeatherState"] = false,
		["GetVariableCatalogProviders"] = false,
		["DiscoverCatalogVariables"] = false,
		["ResolveCatalogVariable"] = false,
		["ReportFolderChanged"] = false,
		["RegisterClient"] = false,
		["SubscribeLogs"] = true,
		["UnsubscribeLogs"] = true,
		["AttachUiSession"] = false,
		["OpenConfigUiSession"] = true,
		["OpenWidgetUiSession"] = false,
		["ListUiPreviews"] = true,
		["OpenUiPreviewSession"] = true,
		["OpenPageUiSession"] = true,
		["OpenFolderUiSession"] = false,
		["OpenModalUiSession"] = false,
		["CompleteUiModal"] = false,
		["RespondToNotification"] = false,
		["GetActiveNotifications"] = false,
		["DetachUiSession"] = false,
		["CloseUiSession"] = false,
		["SendUiEvent"] = false,
		["FlushUiSession"] = false,
		["ReportCompanionState"] = false,
		["SyncCompanionLicense"] = false,
		["GetVideoStreams"] = false,
		["OpenVideoStream"] = false,
		["KeepAliveVideoStream"] = false,
		["SuspendVideoStream"] = false,
		["ResumeVideoStream"] = false,
		["CloseVideoStream"] = false
	};

	private InProcessHost _host = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_host = await InProcessHost.StartAsync();
		await _host.SetUpAccountAsync();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.DisposeAsync();
	}

	[Test]
	public async Task A_client_scope_socket_is_refused_every_admin_only_request()
	{
		await using var client = await _host.ConnectUiAsync(await _host.LoginTokenAsync("client", "Tablet"));

		var refusals = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var type in _adminOnlyTypes)
		{
			var reply = await client.RequestAsync(type, ConfigRequest());
			refusals[type] = reply.Kind == "error" ? reply.ErrorCode : reply.Kind;
		}

		Assert.That(refusals.Values, Is.All.EqualTo("forbidden"), string.Join(", ", refusals));
	}

	[Test]
	public async Task An_admin_socket_reaches_the_config_session_opener()
	{
		await using var admin = await _host.ConnectUiAsync(null);

		var reply = await admin.RequestAsync("OpenConfigUiSession", ConfigRequest());

		Assert.That(reply.Kind, Is.EqualTo("response"), reply.ErrorCode);
	}

	[Test]
	public async Task Script_and_automation_definitions_never_reach_a_client_scope_socket()
	{
		await using var client = await _host.ConnectUiAsync(await _host.LoginTokenAsync("client", "Tablet"));
		await using var admin = await _host.ConnectUiAsync(null);

		var script = await InProcessHost.ReadJsonAsync(await _host.SendAsync(HttpMethod.Post,
			"/api/scripts",
			new { name = "Secret script", flows = "[]" },
			loopback: true));
		var scriptId = script.GetProperty("script").GetProperty("id").GetString();
		await _host.SendAsync(HttpMethod.Put, $"/api/scripts/{scriptId}", new { name = "Renamed" }, loopback: true);
		await _host.SendAsync(HttpMethod.Delete, $"/api/scripts/{scriptId}", loopback: true);

		var automation = await InProcessHost.ReadJsonAsync(await _host.SendAsync(HttpMethod.Post,
			"/api/automations",
			new { name = "Secret automation", flows = "[]" },
			loopback: true));
		var automationId = automation.GetProperty("automation").GetProperty("id").GetString();
		await _host.SendAsync(HttpMethod.Put,
			$"/api/automations/{automationId}",
			new { name = "Renamed" },
			loopback: true);
		var deleted = await _host.SendAsync(HttpMethod.Delete, $"/api/automations/{automationId}", loopback: true);
		Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		await admin.WaitForAsync(message => message.Type == "AutomationDeletedEvent");
		await client.SyncAsync();

		var leaked = client.Received
			.Where(message => message.Type is { } type && (type.StartsWith("Script", StringComparison.Ordinal)
				|| type.StartsWith("Automation", StringComparison.Ordinal)))
			.Select(message => message.Type)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(admin.Received.Select(message => message.Type),
				Is.SupersetOf(new[]
				{
					"ScriptCreatedEvent", "ScriptUpdatedEvent", "ScriptDeletedEvent", "AutomationCreatedEvent",
					"AutomationUpdatedEvent", "AutomationDeletedEvent"
				}));
			Assert.That(leaked, Is.Empty);
		});
	}

	[Test]
	public void Every_dispatched_request_type_has_a_deliberate_scope()
	{
		var source = File.ReadAllText(Path.Combine(RepositoryRoot(),
			"host",
			"src",
			"MacroDeckHost",
			"Ui",
			"UiWebSocketDispatcher.cs"));
		var dispatched = DispatchArm().Matches(source)
			.ToDictionary(match => match.Groups["type"].Value,
				match => match.Groups["admin"].Success,
				StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(dispatched.Keys, Is.EquivalentTo(_requiresAdmin.Keys));
			foreach (var (type, admin) in _requiresAdmin)
			{
				if (dispatched.TryGetValue(type, out var actual))
				{
					Assert.That(actual, Is.EqualTo(admin), $"{type} admin gate");
				}
			}
		});
	}

	private static object ConfigRequest()
	{
		return new { entryPoint = "widget-config", widgetId = Guid.NewGuid().ToString() };
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
	}

	[GeneratedRegex("""^\s*"(?<type>\w+)"(?<admin>\s+when IsAdmin)?\s*=>""", RegexOptions.Multiline)]
	private static partial Regex DispatchArm();
}
