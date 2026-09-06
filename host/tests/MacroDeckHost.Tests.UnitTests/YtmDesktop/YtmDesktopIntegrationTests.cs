using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopIntegrationTests
{
	private static readonly string[] _payloadNames = ["likeStatus", "previousLikeStatus", "trackName", "videoId"];

	[Test]
	public async Task Without_a_configuration_there_is_no_player_and_nothing_throws()
	{
		using var integration = new YtmDesktopIntegration();
		var context = new FakeYtmDesktopIntegrationContext();

		await integration.InitializeAsync(context);

		Assert.Multiple(() =>
		{
			Assert.That(integration.IsInitialized, Is.True);
			Assert.That(integration.GetInstances(), Is.Empty);
		});
	}

	[Test]
	public async Task An_entry_without_a_token_is_skipped()
	{
		using var integration = new YtmDesktopIntegration();
		var context = new FakeYtmDesktopIntegrationContext();
		context.ConfigStore.AddEntry("YouTube Music Desktop App",
			new Dictionary<string, string?> { ["host"] = "127.0.0.1", ["port"] = "9863" });

		await integration.InitializeAsync(context);

		Assert.That(integration.GetInstances(), Is.Empty);
	}

	[Test]
	public async Task A_configured_entry_becomes_one_player_keyed_on_the_entry_id()
	{
		using var integration = new YtmDesktopIntegration();
		var entryId = await ConfigureAsync(integration);

		var instances = integration.GetInstances();

		Assert.Multiple(() =>
		{
			Assert.That(instances, Has.Count.EqualTo(1));
			Assert.That(instances[0].Id, Is.EqualTo(entryId.ToString()));
			Assert.That(integration.GetPlayer(entryId.ToString()), Is.Not.Null);
			Assert.That(integration.GetPlayer("something-else"), Is.Null);
		});
	}

	[Test]
	public async Task Shutting_down_releases_the_player()
	{
		using var integration = new YtmDesktopIntegration();
		await ConfigureAsync(integration);

		await integration.ShutdownAsync();

		Assert.That(integration.GetInstances(), Is.Empty);
	}

	[Test]
	public async Task While_disconnected_only_the_connection_flag_has_a_value()
	{
		using var integration = new YtmDesktopIntegration();
		await ConfigureAsync(integration);

		foreach (var variable in integration.Variables)
		{
			var value = (await integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value;

			if (variable.Name == YtmDesktopVariables.IsConnected)
			{
				Assert.That(value, Is.EqualTo(false), variable.Name);
				continue;
			}

			Assert.That(value, Is.Null, variable.Name);
		}
	}

	[Test]
	public async Task An_unknown_variable_has_no_value()
	{
		using var integration = new YtmDesktopIntegration();
		await ConfigureAsync(integration);

		Assert.That((await integration.ReadAsync("ytmdesktop-nonsense", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public void The_declared_variables_are_the_ones_it_answers()
	{
		using var integration = new YtmDesktopIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Variables, Is.EqualTo(YtmDesktopVariables.All));
			Assert.That(((IVariableProvider)integration).VariablesDependOnConfiguration, Is.False);
			Assert.That(integration.Variables.Select(v => v.Name), Is.Unique);
		});
	}

	[Test]
	public async Task Being_disconnected_is_not_reported_as_an_issue()
	{
		using var integration = new YtmDesktopIntegration();
		await ConfigureAsync(integration);

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task Resolving_the_authorization_issue_reopens_setup()
	{
		using var integration = new YtmDesktopIntegration();

		var resolution = await integration.ResolveIssueAsync("authorization-revoked");
		var unknown = await integration.ResolveIssueAsync("something-else");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
			Assert.That(unknown.Success, Is.False);
		});
	}

	[Test]
	public void The_integration_reports_its_identity_and_capabilities()
	{
		using var integration = new YtmDesktopIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Id, Is.EqualTo("app.macro-deck.ytmdesktop"));
			Assert.That(integration.AllowsMultipleConfigurations, Is.False);
			Assert.That(integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(integration.GetIcon(), Is.Not.Empty);
			Assert.That(TestLocalization.Resolve(integration.Name), Is.EqualTo("YouTube Music Desktop App"));
			Assert.That(integration.CreateConfigFlow(), Is.InstanceOf<YtmDesktopConfigFlow>());
		});
	}

	[Test]
	public void The_actions_cover_the_transport_controls_and_this_integration_s_own()
	{
		using var integration = new YtmDesktopIntegration();
		var ids = integration.Actions.Select(a => a.Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Does.Contain("play"));
			Assert.That(ids, Does.Contain("toggle-play-pause"));
			Assert.That(ids, Does.Contain("seek"));
			Assert.That(ids, Does.Contain("toggle-shuffle"));
			Assert.That(ids, Does.Contain("set-repeat-mode"));
			Assert.That(ids, Does.Contain("play-track"));
			Assert.That(ids, Does.Contain("play-playlist"));
			Assert.That(ids, Does.Contain("rate-track"));
			Assert.That(ids, Does.Contain("set-mute"));
			Assert.That(ids, Does.Contain("play-video"));
			Assert.That(ids, Is.Unique);
		});
	}

	[Test]
	public void Every_action_parameter_uses_a_defined_parameter_type()
	{
		using var integration = new YtmDesktopIntegration();

		Assert.Multiple(() =>
		{
			foreach (var action in integration.Actions)
			{
				foreach (var parameter in action.Parameters)
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{action.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public void The_rating_event_can_be_left_unfiltered()
	{
		using var integration = new YtmDesktopIntegration();
		var definition = integration.EventDefinitions.Single(e => e.Id == "like-changed");
		var filter = definition.ConfigurationParameters.Single(p => p.Name == "likeStatus");

		Assert.Multiple(() =>
		{
			Assert.That(filter.Options![0].Value, Is.Empty);
			Assert.That(filter.DefaultValue, Is.Empty);
			Assert.That(definition.PayloadParameters.Select(p => p.Name),
				Is.EqualTo(_payloadNames));
		});
	}

	[Test]
	public async Task An_action_without_a_configuration_fails_instead_of_throwing()
	{
		using var integration = new YtmDesktopIntegration();
		var action = integration.Actions.Single(a => a.Id == "rate-track");

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["mode"] = "like" }
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConfigured));
		});
	}

	private static async Task<Guid> ConfigureAsync(YtmDesktopIntegration integration)
	{
		var context = new FakeYtmDesktopIntegrationContext();
		var entryId = context.ConfigStore.AddEntry("YouTube Music Desktop App",
			new Dictionary<string, string?> { ["host"] = "127.0.0.1", ["port"] = "1" },
			new Dictionary<string, string> { ["token"] = "token" });

		await integration.InitializeAsync(context);
		return entryId;
	}
}
