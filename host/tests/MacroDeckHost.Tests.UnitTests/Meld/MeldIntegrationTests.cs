using System.Text;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Meld;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldIntegrationTests
{
	private static readonly string[] _actionIds =
	[
		"show-scene", "stage-scene", "show-staged-scene",
		"set-layer-visibility", "set-effect-state",
		"start-streaming", "stop-streaming", "toggle-streaming",
		"start-recording", "stop-recording", "toggle-recording",
		"take-screenshot",
		"set-track-mute", "set-track-monitoring", "set-track-volume", "adjust-track-volume",
		"get-layer-visibility", "get-effect-state", "get-track-mute", "get-track-volume"
	];

	private static readonly string[] _eventIds =
	[
		"scene-changed", "staged-scene-changed",
		"streaming-started", "streaming-stopped",
		"recording-started", "recording-stopped",
		"layer-visibility-changed", "effect-state-changed",
		"track-mute-changed", "track-monitoring-changed",
		"session-changed",
		"connected", "disconnected"
	];

	private MeldIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_integration = new MeldIntegration();
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
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.meld"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Meld Studio"));
			Assert.That(typeof(MeldIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var meld = discovered.OfType<MeldIntegration>().SingleOrDefault();
		Assert.That(meld, Is.Not.Null, "the Meld Studio integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(meld!.Actions.Select(action => action.Id), Is.EqualTo(_actionIds));
			Assert.That(meld.EventDefinitions.Select(definition => definition.Id), Is.EqualTo(_eventIds));
		});
	}

	[Test]
	public void Setup_configures_a_single_instance()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.AllowsMultipleConfigurations, Is.False);
			Assert.That(_integration.CreateConfigFlow(), Is.InstanceOf<MeldConfigFlow>());
		});
	}

	[Test]
	public async Task Everything_but_the_connection_flag_is_unknown_while_disconnected()
	{
		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync("meld-is-connected", CancellationToken.None)).Value,
				Is.EqualTo(false));

			foreach (var variable in _integration.Variables.Where(v => v.Name != "meld_is_connected"))
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

	/// <summary>
	/// Meld is the in-tree provider that exercises both materialization policies of ADR 0081's one merged
	/// contract at once: eight eager variables the host registers up front, and a browsable track catalog
	/// nothing materializes until the user binds it. The two id namespaces have to stay disjoint and route
	/// separately, because a single <c>ResolveAsync</c>/<c>ReadAsync</c> pair now answers for both.
	/// </summary>
	[Test]
	public async Task The_eager_half_and_the_catalog_half_live_in_one_provider_without_colliding()
	{
		var eagerIds = _integration.Variables.Select(variable => variable.ResolvedId).ToList();

		var resolvedEager = await _integration.ResolveAsync("meld-is-connected", CancellationToken.None);
		var resolvedCatalog = await _integration.ResolveAsync("track/track1/gain", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_integration.SupportsCatalog, Is.True);
			Assert.That(_integration.Variables,
				Has.All.Matches<VariableDefinition>(v => v.Materialization == VariableMaterialization.Eager));
			Assert.That(eagerIds,
				Has.All.Matches<string?>(id => id!.StartsWith("meld-", StringComparison.Ordinal)),
				"an eager id that did not carry the prefix would be routed to the catalog");

			Assert.That(resolvedEager?.Name, Is.EqualTo("meld_is_connected"));
			Assert.That(resolvedEager?.Materialization, Is.EqualTo(VariableMaterialization.Eager));
			Assert.That(resolvedEager?.Write, Is.Null, "the eager half of this provider is read-only");

			// The catalog half answers for its own namespace rather than the eager lookup swallowing the id,
			// and it is the half that carries a write capability and the attributes a Slider reads.
			Assert.That(resolvedCatalog?.Materialization, Is.EqualTo(VariableMaterialization.OnDemand));
			Assert.That(resolvedCatalog?.Write, Is.Not.Null);
			Assert.That(resolvedCatalog?.Unit, Is.EqualTo("%"));
			Assert.That(resolvedCatalog?.SemanticKind, Is.EqualTo("percentage"));
		});
	}

	[Test]
	public void Every_configuration_parameter_filters_on_a_payload_parameter_of_the_same_name()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in _integration.EventDefinitions)
			{
				var payloadNames = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(payloadNames, Has.Member(configuration.Name), $"{definition.Id}.{configuration.Name}");
					Assert.That(configuration.Type, Is.EqualTo(ActionParameterType.DynamicChoice), definition.Id);
					Assert.That(configuration.Required,
						Is.False,
						$"{definition.Id}: an unset filter has to mean 'any'");
					Assert.That(TestLocalization.Resolve(configuration.Placeholder),
						Is.Not.Null.And.Not.Empty,
						definition.Id);
				}
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
		var options = await _integration.GetEventOptionsAsync(Options("scene-changed", "sceneId"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Is.Empty);
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task An_unknown_event_parameter_has_no_options()
	{
		var result = await _integration.GetEventOptionsAsync(Options("scene-changed", "nonsense"),
			CancellationToken.None);

		Assert.That(result.Options, Is.Empty);
	}

	[Test]
	public async Task No_issue_is_reported_on_a_fresh_integration()
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
	public async Task The_wrong_endpoint_issue_reopens_setup()
	{
		var resolution = await _integration.ResolveIssueAsync(MeldIntegration.WrongEndpointIssueId);

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

	[Test]
	public async Task InitializeAsync_skips_connecting_when_the_stored_host_cannot_build_a_uri()
	{
		var context = new FakeContext();
		context.ConfigStore.AddEntry("Meld Studio",
			new Dictionary<string, string?>
			{
				[MeldConfigKeys.Host] = "not a valid host",
				[MeldConfigKeys.Port] = "13376"
			});

		Assert.DoesNotThrowAsync(() => _integration.InitializeAsync(context));
		Assert.That(_integration.IsInitialized, Is.True);
		Assert.That((await _integration.ReadAsync("meld-is-connected", CancellationToken.None)).Value,
			Is.EqualTo(false));
	}

	private static EventOptionsContext Options(string eventId, string parameterName)
		=> new()
		{
			EventId = eventId,
			ParameterName = parameterName,
			CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		};

	private sealed class FakeContext : IIntegrationContext
	{
		public FakeConfig ConfigStore { get; } = new();

		public IIntegrationConfig Config => ConfigStore;

		public IEventPublisher Events { get; } = new NullPublisher();

		public IVariableApi Variables { get; } = new NullVariableApi();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class NullPublisher : IEventPublisher
	{
		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		{
		}
	}

	private sealed class NullVariableApi : IVariableApi
	{
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync() => Task.FromResult<IReadOnlyList<VariableHandle>>([]);

		public Task<VariableHandle?> GetByNameAsync(string name) => Task.FromResult<VariableHandle?>(null);

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
			=> Task.FromResult(new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
			{
				DefinitionId = definitionId
			});

		public Task SetValueAsync(Guid variableId, object? value) => Task.CompletedTask;

		public Task DeleteAsync(Guid variableId) => Task.CompletedTask;
	}

	private sealed class FakeConfig : IIntegrationConfig
	{
		private readonly List<ConfigEntrySnapshot> _entries = [];
		private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];

		public Guid AddEntry(string title, IReadOnlyDictionary<string, string?> values)
		{
			var entryId = Guid.NewGuid();
			_entries.Add(new ConfigEntrySnapshot(entryId, title));

			foreach (var (key, value) in values)
			{
				_strings[(entryId, key)] = value;
			}

			return entryId;
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(null);

		public Task SetStringAsync(
			Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
		{
			_strings[(entryId, key)] = value;
			return Task.CompletedTask;
		}

		public Task SetSecretAsync(Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
