using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopIntegrationTests
{
	private StreamlabsDesktopIntegration _integration = null!;

	[SetUp]
	public void SetUp() => _integration = new StreamlabsDesktopIntegration();

	[TearDown]
	public void TearDown() => _integration.Dispose();

	[Test]
	public void TheIntegrationDescribesItself()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.streamlabs-desktop"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Streamlabs Desktop"));
			Assert.That(_integration.AllowsMultipleConfigurations, Is.False);
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/png"));
			Assert.That(_integration.GetIcon(), Is.Not.Empty);
			Assert.That(_integration.CreateConfigFlow(), Is.TypeOf<StreamlabsDesktopConfigFlow>());
		});
	}

	[Test]
	public void TheVariablesAreNamespacedAndDoNotCollideWithObs()
	{
		var names = _integration.Variables.Select(variable => variable.Name).ToList();
		var obsNames = new ObsIntegration().Variables.Select(variable => variable.Name)
			.ToHashSet(StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(names, Is.All.StartWith("streamlabs_"));
			Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
			Assert.That(names.Any(obsNames.Contains), Is.False);
			Assert.That(_integration.Variables.Select(variable => variable.RefreshInterval),
				Is.All.Not.Null);
		});
	}

	[Test]
	public async Task WhileDisconnected_OnlyTheConnectionFlagAnswers()
	{
		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync(StreamlabsDesktopVariables.IsConnected, default)).Value,
				Is.EqualTo(false));

			foreach (var variable in _integration.Variables
				.Where(candidate => candidate.Name != StreamlabsDesktopVariables.IsConnected))
			{
				Assert.That((await _integration.ReadAsync(variable.ResolvedId!, default)).Value,
					Is.Null,
					$"{variable.Name} must not read as a real value while disconnected");
			}
		});
	}

	[Test]
	public async Task AnUnknownVariable_IsNull()
	{
		Assert.That((await _integration.ReadAsync("streamlabs-nope", default)).Value, Is.Null);
	}

	[Test]
	public void TheEventDefinitionsAreUniqueAndDescribed()
	{
		var events = _integration.EventDefinitions;

		Assert.Multiple(() =>
		{
			Assert.That(events, Has.Count.EqualTo(14));
			Assert.That(events.Select(definition => definition.Id).Distinct().Count(), Is.EqualTo(events.Count));
			Assert.That(events.Select(definition => TestLocalization.Resolve(definition.Name)), Is.All.Not.Empty);
			Assert.That(events.Select(definition => TestLocalization.Resolve(definition.Category)),
				Is.All.Not.Null);
		});
	}

	[Test]
	public async Task GetIssuesAsync_IsEmptyUntilTheTokenIsRejected()
	{
		Assert.That(await _integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task ResolveIssueAsync_SendsTheUserBackIntoSetup()
	{
		var resolution = await _integration.ResolveIssueAsync("token-rejected");
		var unknown = await _integration.ResolveIssueAsync("something-else");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
			Assert.That(unknown.Success, Is.False);
		});
	}

	[Test]
	public async Task GetEventOptionsAsync_AllowsAHandTypedNameWhileStreamlabsIsClosed()
	{
		var options = await _integration.GetEventOptionsAsync(new EventOptionsContext
			{
				EventId = StreamlabsDesktopEventIds.SceneChanged,
				ParameterName = "sceneName",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.AllowsCustomValue, Is.True);
			Assert.That(options.Options, Is.Empty);
		});
	}

	[Test]
	public async Task InitializeAsync_WithoutAConfigEntry_ConnectsNothing()
	{
		var context = new FakeContext();

		await _integration.InitializeAsync(context);

		Assert.Multiple(async () =>
		{
			Assert.That(_integration.IsInitialized, Is.True);
			Assert.That((await _integration.ReadAsync(StreamlabsDesktopVariables.IsConnected, default)).Value,
				Is.EqualTo(false));
		});

		await _integration.ShutdownAsync();
		Assert.That(_integration.IsInitialized, Is.False);
	}

	[Test]
	public async Task InitializeAsync_WithoutAToken_ConnectsNothing()
	{
		var context = new FakeContext();
		context.ConfigStore.AddEntry("Streamlabs Desktop",
			new Dictionary<string, string?>
			{
				[StreamlabsDesktopConfigKeys.Host] = "127.0.0.1",
				[StreamlabsDesktopConfigKeys.Port] = "59650"
			});

		await _integration.InitializeAsync(context);

		Assert.That((await _integration.ReadAsync(StreamlabsDesktopVariables.IsConnected, default)).Value,
			Is.EqualTo(false));
	}

	[Test]
	public async Task ShutdownAsync_IsIdempotent()
	{
		await _integration.InitializeAsync(new FakeContext());

		await _integration.ShutdownAsync();
		await _integration.ShutdownAsync();

		Assert.That(_integration.IsInitialized, Is.False);
	}

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
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
			=> Task.FromResult<IReadOnlyList<VariableHandle>>([]);

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
		private readonly Dictionary<(Guid EntryId, string Key), string> _secrets = [];

		public Guid AddEntry(
			string title,
			IReadOnlyDictionary<string, string?> values,
			IReadOnlyDictionary<string, string>? secrets = null)
		{
			var entryId = Guid.NewGuid();
			_entries.Add(new ConfigEntrySnapshot(entryId, title));

			foreach (var (key, value) in values)
			{
				_strings[(entryId, key)] = value;
			}

			foreach (var (key, value) in secrets ?? new Dictionary<string, string>(StringComparer.Ordinal))
			{
				_secrets[(entryId, key)] = value;
			}

			return entryId;
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));

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
		{
			_secrets[(entryId, key)] = value;
			return Task.CompletedTask;
		}
	}
}
