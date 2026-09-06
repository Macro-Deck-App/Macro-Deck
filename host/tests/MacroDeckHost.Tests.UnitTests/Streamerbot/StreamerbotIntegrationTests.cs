using System.Text;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Streamerbot;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotIntegrationTests
{
	private static readonly string[] _actionIds =
		["do-action", "execute-code-trigger", "send-chat-message", "get-global-variable"];

	private static readonly string[] _eventIds = ["event", "connected", "disconnected"];

	private StreamerbotIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_integration = new StreamerbotIntegration();
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	[Test]
	public void The_integration_is_identified()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.streamerbot"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Streamer.bot"));
			Assert.That(
				typeof(StreamerbotIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var streamerbot = discovered.OfType<StreamerbotIntegration>().SingleOrDefault();
		Assert.That(streamerbot, Is.Not.Null, "the Streamer.bot integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(streamerbot!.Actions.Select(action => action.Id), Is.EqualTo(_actionIds));
			Assert.That(streamerbot.Variables, Is.Not.Empty);
			Assert.That(streamerbot.EventDefinitions.Select(definition => definition.Id), Is.EqualTo(_eventIds));
		});
	}

	[Test]
	public void Setup_configures_a_single_instance()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.AllowsMultipleConfigurations, Is.False);
			Assert.That(_integration.CreateConfigFlow(), Is.InstanceOf<StreamerbotConfigFlow>());
		});
	}

	[Test]
	public async Task Everything_but_the_connection_flag_is_unknown_while_disconnected()
	{
		// A closed Streamer.bot must not read as a signed-out channel.
		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync("streamerbot-is-connected", CancellationToken.None)).Value,
				Is.EqualTo(false));

			foreach (var variable in _integration.Variables.Where(v => v.Name != "streamerbot_is_connected"))
			{
				Assert.That((await _integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value,
					Is.Null,
					variable.Name);
			}
		});

		await Task.CompletedTask;
	}

	[Test]
	public async Task An_unknown_variable_answers_null()
	{
		Assert.That((await _integration.ReadAsync("nonsense", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public void The_generic_event_filters_on_names_its_payload_also_carries()
	{
		var definition = _integration.EventDefinitions.Single(e => e.Id == "event");
		var payloadNames = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();

		Assert.Multiple(() =>
		{
			foreach (var configuration in definition.ConfigurationParameters)
			{
				Assert.That(payloadNames, Has.Member(configuration.Name));
				Assert.That(configuration.Type, Is.EqualTo(ActionParameterType.DynamicChoice));
				Assert.That(configuration.Required, Is.False, "an unset filter has to mean 'any'");
				Assert.That(TestLocalization.Resolve(configuration.Placeholder), Is.Not.Null.And.Not.Empty);
			}
		});
	}

	[Test]
	public void Every_event_parameter_uses_a_defined_parameter_type()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in _integration.EventDefinitions)
			{
				foreach (var parameter in definition.ConfigurationParameters.Concat(definition.PayloadParameters))
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{definition.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public async Task Event_options_answer_instantly_while_disconnected_and_stay_typeable()
	{
		var sources = await _integration.GetEventOptionsAsync(Options("source"), CancellationToken.None);
		var types = await _integration.GetEventOptionsAsync(Options("type"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sources.Options, Is.Empty);
			Assert.That(sources.AllowsCustomValue, Is.True);
			Assert.That(types.Options, Is.Empty);
			Assert.That(types.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task An_unknown_event_parameter_has_no_options()
	{
		var result = await _integration.GetEventOptionsAsync(Options("nonsense"), CancellationToken.None);

		Assert.That(result.Options, Is.Empty);
	}

	[Test]
	public async Task No_issue_is_reported_just_because_streamerbot_is_closed()
	{
		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues, Is.Empty);
	}

	[Test]
	public async Task An_unknown_issue_cannot_be_resolved()
	{
		var resolution = await _integration.ResolveIssueAsync("nonsense");

		Assert.That(resolution.Success, Is.False);
	}

	[Test]
	public async Task The_authentication_issue_reopens_setup()
	{
		var resolution = await _integration.ResolveIssueAsync(StreamerbotIntegration.AuthenticationIssueId);

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}

	[Test]
	public void The_brand_icon_is_an_svg()
	{
		var icon = Encoding.UTF8.GetString(_integration.GetIcon());

		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(icon, Does.Contain("<svg"));
		});
	}

	[Test]
	public void Shutdown_is_safe_without_a_connection()
	{
		Assert.DoesNotThrowAsync(() => _integration.ShutdownAsync());
		Assert.DoesNotThrow(() => _integration.Dispose());
	}

	private static EventOptionsContext Options(string parameterName)
		=> new()
		{
			EventId = "event",
			ParameterName = parameterName,
			CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		};
}
