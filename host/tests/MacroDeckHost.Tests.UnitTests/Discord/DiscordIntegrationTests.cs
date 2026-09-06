using System.Reflection;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordIntegrationTests
{
	private static readonly bool[] _bothBooleans = [false, true];

	[Test]
	public void The_integration_is_identified()
	{
		var integration = new DiscordIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Id, Is.EqualTo("app.macro-deck.discord"));
			Assert.That(TestLocalization.Resolve(integration.Name), Is.EqualTo("Discord"));
			Assert.That(typeof(DiscordIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var discord = discovered.OfType<DiscordIntegration>().SingleOrDefault();
		Assert.That(discord, Is.Not.Null, "the Discord integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(discord!.Actions, Is.Not.Empty);
			Assert.That(discord.Variables, Is.Not.Empty);
			Assert.That(discord.EventDefinitions, Is.Not.Empty);
		});
	}

	[Test]
	public void Every_action_parameter_uses_a_defined_parameter_type()
	{
		var integration = new DiscordIntegration();

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
	public void Only_one_discord_account_can_be_configured()
	{
		Assert.That(new DiscordIntegration().AllowsMultipleConfigurations, Is.False);
	}

	[Test]
	public void Every_declared_variable_can_be_resolved()
	{
		var integration = new DiscordIntegration();

		Assert.Multiple(() =>
		{
			foreach (var variable in integration.Variables)
			{
				Assert.That(variable.Name, Does.StartWith("discord_"));
				Assert.That(variable.RefreshInterval, Is.Not.Null, variable.Name);
			}
		});
	}

	[Test]
	public void The_declared_variables_are_unique()
	{
		Assert.That(new DiscordIntegration().Variables.Select(v => v.Name), Is.Unique);
	}

	[Test]
	public async Task Voice_variables_are_unavailable_rather_than_false_while_disconnected()
	{
		var integration = new DiscordIntegration();

		Assert.Multiple(async () =>
		{
			Assert.That((await integration.ReadAsync("discord-is-connected", CancellationToken.None)).Value, Is.False);

			foreach (var variable in integration.Variables.Where(v => v.Name != "discord_is_connected"))
			{
				Assert.That((await integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value,
					Is.Null,
					variable.Name);
			}
		});
	}

	[Test]
	public async Task An_unknown_variable_resolves_to_nothing()
	{
		var integration = new DiscordIntegration();

		Assert.That((await integration.ReadAsync("discord-nonsense", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public void Discord_is_muted_is_true_while_self_deafened_with_the_raw_mute_flag_false()
	{
		var state = new DiscordState { IsConnected = true, SelfMuted = false, SelfDeafened = true };

		Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-muted"), Is.EqualTo(true));
	}

	[Test]
	public void The_raw_mute_variables_stay_raw_while_self_deafened()
	{
		var state = new DiscordState { IsConnected = true, SelfMuted = false, SelfDeafened = true };

		Assert.Multiple(() =>
		{
			Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-self-muted"), Is.EqualTo(false));
			Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-server-muted"), Is.EqualTo(false));
		});
	}

	[Test]
	public void All_combinations_of_the_four_flags_reach_the_mute_and_deafen_variables_correctly()
	{
		Assert.Multiple(() =>
		{
			foreach (var selfMuted in _bothBooleans)
			foreach (var selfDeafened in _bothBooleans)
			foreach (var serverMuted in _bothBooleans)
			foreach (var serverDeafened in _bothBooleans)
			{
				var state = new DiscordState
				{
					IsConnected = true,
					SelfMuted = selfMuted,
					SelfDeafened = selfDeafened,
					ServerMuted = serverMuted,
					ServerDeafened = serverDeafened
				};
				var label = $"self={selfMuted}/{selfDeafened} server={serverMuted}/{serverDeafened}";

				Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-self-muted"),
					Is.EqualTo(selfMuted),
					label);
				Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-self-deafened"),
					Is.EqualTo(selfDeafened),
					label);
				Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-server-muted"),
					Is.EqualTo(serverMuted),
					label);
				Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-server-deafened"),
					Is.EqualTo(serverDeafened),
					label);
				Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-muted"),
					Is.EqualTo(selfMuted || selfDeafened || serverMuted || serverDeafened),
					label);
			}
		});
	}

	[Test]
	public void Undeafening_does_not_report_the_microphone_as_active_while_a_raw_mute_remains()
	{
		var state = new DiscordState { IsConnected = true, SelfMuted = true, SelfDeafened = false };

		Assert.That(DiscordIntegration.ReadVariable(state, "discord-is-muted"), Is.EqualTo(true));
	}

	[Test]
	public void Every_event_id_constant_has_a_matching_definition()
	{
		var idConstants = typeof(DiscordEventIds)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral && f.FieldType == typeof(string))
			.Select(f => (string)f.GetRawConstantValue()!)
			.ToList();

		var defined = DiscordEventDefinitions.All.Select(d => d.Id).ToList();

		Assert.That(defined, Is.EquivalentTo(idConstants));
	}

	[Test]
	public void The_declared_events_are_unique_and_named()
	{
		var integration = new DiscordIntegration();

		Assert.That(integration.EventDefinitions.Select(e => e.Id), Is.Unique);
		Assert.Multiple(() =>
		{
			foreach (var definition in integration.EventDefinitions)
			{
				Assert.That(TestLocalization.Resolve(definition.Name), Is.Not.Empty, definition.Id);
				Assert.That(TestLocalization.Resolve(definition.Category), Is.Not.Empty, definition.Id);
			}
		});
	}

	[Test]
	public void Every_event_filter_matches_a_payload_parameter()
	{
		var integration = new DiscordIntegration();

		Assert.Multiple(() =>
		{
			foreach (var definition in integration.EventDefinitions)
			{
				var payloadNames = definition.PayloadParameters.Select(p => p.Name).ToList();
				foreach (var filter in definition.ConfigurationParameters)
				{
					Assert.That(payloadNames,
						Does.Contain(filter.Name),
						$"{definition.Id}: the filter '{filter.Name}' matches no payload parameter");
				}
			}
		});
	}

	[Test]
	public async Task No_issue_is_reported_before_setup()
	{
		var integration = new DiscordIntegration();

		Assert.That(await integration.GetIssuesAsync(CancellationToken.None), Is.Empty);
	}

	[Test]
	public async Task An_unknown_issue_cannot_be_resolved()
	{
		var integration = new DiscordIntegration();

		var resolution = await integration.ResolveIssueAsync("nonsense", CancellationToken.None);

		Assert.That(resolution.Success, Is.False);
	}

	[Test]
	public async Task Expired_authorization_reopens_the_setup_flow()
	{
		var integration = new DiscordIntegration();

		var resolution = await integration.ResolveIssueAsync(DiscordIntegration.ReauthorizationIssueId,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}

	[Test]
	public async Task A_privilege_mismatch_explains_the_manual_fix()
	{
		var integration = new DiscordIntegration();

		var resolution = await integration.ResolveIssueAsync(DiscordIntegration.PrivilegeMismatchIssueId,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.False);
			Assert.That(TestLocalization.Resolve(resolution.Message), Does.Contain("same privileges"));
		});
	}

	[Test]
	public void An_ignored_voice_setting_raises_an_issue_naming_every_stuck_field()
	{
		var issues = DiscordIntegration.BuildIssues(needsReauthorization: false,
			accessDenied: false,
			ignoredVoiceSettings: ["noise_suppression", "mode.type"]);

		var issue = issues.Single(i => i.Id == DiscordIntegration.VoiceSettingsIgnoredIssueId);
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("noise_suppression"));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("mode.type"));
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
		});
	}

	[Test]
	public void No_issue_is_raised_when_nothing_is_stuck()
	{
		var issues = DiscordIntegration.BuildIssues(needsReauthorization: false,
			accessDenied: false,
			ignoredVoiceSettings: []);

		Assert.That(issues.Any(i => i.Id == DiscordIntegration.VoiceSettingsIgnoredIssueId), Is.False);
	}

	[Test]
	public async Task An_ignored_voice_setting_explains_the_manual_fix()
	{
		var integration = new DiscordIntegration();

		var resolution = await integration.ResolveIssueAsync(DiscordIntegration.VoiceSettingsIgnoredIssueId,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.False);
			Assert.That(TestLocalization.Resolve(resolution.Message), Does.Contain("Voice & Video"));
		});
	}

	/// <summary>What the removed slider action contract used to say about the two volume actions, said
	/// where ADR 0081 puts it instead: both volumes are the same writable percentage, declared on the
	/// variable rather than on whichever action happened to set it.</summary>
	[Test]
	public void Both_volumes_are_writable_percentage_variables()
	{
		var integration = new DiscordIntegration();

		var volumes = integration.Variables
			.Where(variable => variable.ResolvedId is "discord-input-volume" or "discord-output-volume")
			.ToList();

		Assert.That(volumes, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(volumes.Select(variable => variable.CanWrite), Is.All.True);
			Assert.That(volumes.Select(variable => variable.Unit), Is.All.EqualTo("%"));
			Assert.That(volumes.Select(variable => variable.SemanticKind),
				Is.All.EqualTo(VariableSemanticKinds.Percentage));
			Assert.That(volumes.Select(variable => variable.Write!.CommitOnRelease),
				Is.All.False,
				"hearing the level change while dragging is the point, so neither commits on release");
		});
	}

	[Test]
	public async Task A_volume_write_while_discord_is_unreachable_is_refused_rather_than_swallowed()
	{
		var integration = new DiscordIntegration();

		var result = await integration.SetValueAsync("discord-input-volume", 50d);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
	}

	[Test]
	public async Task A_write_to_a_variable_that_declares_no_write_capability_is_refused()
	{
		var integration = new DiscordIntegration();

		var result = await integration.SetValueAsync("discord-is-muted", true);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
	}

	[Test]
	public void The_brand_icon_is_embedded_as_svg()
	{
		var integration = new DiscordIntegration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(integration.GetIcon(), Is.Not.Empty);
		});
	}

	[Test]
	public void Shutting_down_before_initializing_is_harmless()
	{
		var integration = new DiscordIntegration();

		Assert.DoesNotThrowAsync(async () => await integration.ShutdownAsync());
		Assert.DoesNotThrow(integration.Dispose);
	}
}
