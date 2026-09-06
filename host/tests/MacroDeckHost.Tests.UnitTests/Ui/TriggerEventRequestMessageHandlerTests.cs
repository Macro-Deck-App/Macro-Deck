using System.Text.Json;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class TriggerEventRequestMessageHandlerTests
{
	private RecordingEventBus _bus = null!;
	private StubEventSubscriptionIndex _index = null!;
	private ConfigurableIntegrationRegistry _integrations = null!;
	private FakeHostLockState _lockState = null!;

	[SetUp]
	public void SetUp()
	{
		_bus = new RecordingEventBus();
		_index = new StubEventSubscriptionIndex();
		_integrations = new ConfigurableIntegrationRegistry(
			[StubIntegration.Create("app.macro-deck.obs", "OBS Studio")]);
		_lockState = new FakeHostLockState();
	}

	[Test]
	public async Task Locked_is_refused_and_publishes_nothing()
	{
		_lockState.IsLocked = true;

		var response = await Handle(new TriggerEventRequest { EventId = "app.macro-deck.obs::scene-changed" });

		AssertRefused(response, "HOST_LOCKED");
		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public async Task A_blank_id_is_rejected()
	{
		var response = await Handle(new TriggerEventRequest { EventId = "  " });

		AssertRefused(response, "VALIDATION_ERROR");
	}

	[Test]
	public async Task An_unqualified_id_is_rejected()
	{
		var response = await Handle(new TriggerEventRequest { EventId = "scene-changed" });

		AssertRefused(response, "VALIDATION_ERROR");
	}

	[Test]
	public async Task An_unknown_event_is_not_found()
	{
		var response = await Handle(new TriggerEventRequest { EventId = "app.macro-deck.obs::no-such-event" });

		AssertRefused(response, "NOT_FOUND");
	}

	[Test]
	public async Task A_disabled_integration_says_so_rather_than_not_found()
	{
		_integrations.SetEnabled("app.macro-deck.obs", false);

		var response = await Handle(new TriggerEventRequest { EventId = "app.macro-deck.obs::scene-changed" });

		AssertRefused(response, "INTEGRATION_DISABLED");
	}

	[Test]
	public async Task A_scheduled_event_is_refused_and_nothing_is_published()
	{
		var response = await Handle(new TriggerEventRequest { EventId = "time::every-day" });

		AssertRefused(response, "SCHEDULED_EVENT");
		Assert.That(_bus.Published, Is.Empty);
	}

	[Test]
	public async Task A_push_event_is_published_untargeted()
	{
		var response = await Handle(new TriggerEventRequest
		{
			EventId = "app.macro-deck.obs::scene-changed",
			Parameters = Values(("sceneName", "\"Intro\""))
		});

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_bus.Published, Has.Count.EqualTo(1));
			Assert.That(_bus.Published[0].EventId, Is.EqualTo("app.macro-deck.obs::scene-changed"));
			Assert.That(_bus.Published[0].Target, Is.Null);
			Assert.That(_bus.Published[0].Parameters["sceneName"], Is.EqualTo("Intro"));
		});
	}

	[Test]
	public async Task The_payload_is_coerced_defaulted_and_confined_to_declared_parameters()
	{
		await Handle(new TriggerEventRequest
		{
			EventId = "app.macro-deck.obs::volume-changed",
			Parameters = Values(("volume", "\"0.5\""), ("smuggled", "\"nope\""))
		});

		var parameters = _bus.Published[0].Parameters;

		Assert.Multiple(() =>
		{
			Assert.That(parameters["volume"], Is.EqualTo(0.5d));
			Assert.That(parameters.ContainsKey("inputName"), Is.True, "an omitted parameter is still present");
			Assert.That(parameters.ContainsKey("smuggled"), Is.False);
		});
	}

	[Test]
	public async Task Queued_subscriptions_exclude_the_ones_the_payload_provably_cannot_match()
	{
		_index.Add(Subscription("app.macro-deck.obs::scene-changed", ("sceneName", "\"Intro\"")));
		_index.Add(Subscription("app.macro-deck.obs::scene-changed", ("sceneName", "\"Outro\"")));
		_index.Add(Subscription("app.macro-deck.obs::scene-changed"));

		var response = await Handle(new TriggerEventRequest
		{
			EventId = "app.macro-deck.obs::scene-changed",
			Parameters = Values(("sceneName", "\"Intro\""))
		});

		Assert.That(response.QueuedSubscriptions, Is.EqualTo(2));
	}

	private async Task<TriggerEventResponse> Handle(TriggerEventRequest request)
	{
		var registry = new EventRegistry(_integrations,
			[new StubHostEventProvider()],
			new LoggerConfiguration().CreateLogger());
		var handler = new TriggerEventRequestMessageHandler(new StaticEventRegistry(registry, StubIntegrationEvents()),
			_integrations,
			_bus,
			_index,
			_lockState,
			new LoggerConfiguration().CreateLogger());
		return await handler.Handle(request, CancellationToken.None);
	}

	private static void AssertRefused(TriggerEventResponse response, string code)
		=> Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(code));
		});

	private static Dictionary<string, JsonElement> Values(params (string Name, string Json)[] values)
		=> values.ToDictionary(v => v.Name, v => JsonDocument.Parse(v.Json).RootElement, StringComparer.Ordinal);

	private static EventSubscription Subscription(string eventId, params (string Name, string Json)[] configuration)
		=> new(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			Guid.NewGuid().ToString(),
			eventId,
			configuration.ToDictionary(c => c.Name,
				c => new EventConfigurationValue(JsonDocument.Parse(c.Json).RootElement),
				StringComparer.Ordinal),
			null);

	private static IReadOnlyList<EventDefinitionDescriptor> StubIntegrationEvents() =>
	[
		new(QualifiedId.Parse("app.macro-deck.obs::scene-changed"),
			"app.macro-deck.obs",
			"OBS Studio",
			true,
			new EventDefinition
			{
				Id = "scene-changed",
				Name = "Scene Changed",
				ConfigurationParameters = [ActionParameter.Text("sceneName", label: "Scene")],
				PayloadParameters = [ActionParameter.Text("sceneName", label: "Scene")]
			}),
		new(QualifiedId.Parse("app.macro-deck.obs::volume-changed"),
			"app.macro-deck.obs",
			"OBS Studio",
			true,
			new EventDefinition
			{
				Id = "volume-changed",
				Name = "Volume Changed",
				PayloadParameters =
				[
					ActionParameter.Text("inputName", label: "Input"),
					ActionParameter.Number("volume", label: "Volume")
				]
			})
	];

	private sealed class StubHostEventProvider : IHostEventProvider
	{
		public string ProviderId => "time";

		public LocalizedText ProviderName => "Time";

		public IReadOnlyList<EventDefinition> EventDefinitions =>
		[
			new()
			{
				Id = "every-day",
				Name = "Every Day",
				DeliveryKind = EventDeliveryKind.Scheduled
			}
		];
	}

	private sealed class StaticEventRegistry : IEventRegistry
	{
		private readonly IEventRegistry _inner;
		private readonly IReadOnlyList<EventDefinitionDescriptor> _integrationEvents;

		public StaticEventRegistry(IEventRegistry inner, IReadOnlyList<EventDefinitionDescriptor> integrationEvents)
		{
			_inner = inner;
			_integrationEvents = integrationEvents;
		}

		public IReadOnlyList<EventDefinitionDescriptor> GetDefinitions()
			=> [.._inner.GetDefinitions(), .._integrationEvents];

		public EventDefinitionDescriptor? Find(string qualifiedEventId)
			=> GetDefinitions().FirstOrDefault(d => d.Id.ToString() == qualifiedEventId);

		public object? FindProvider(string qualifiedEventId) => _inner.FindProvider(qualifiedEventId);
	}

	private sealed class StubEventSubscriptionIndex : IEventSubscriptionIndex
	{
		private readonly List<EventSubscription> _subscriptions = [];

		public event Action? Changed;

		public void Add(EventSubscription subscription)
		{
			_subscriptions.Add(subscription);
			Changed?.Invoke();
		}

		public bool HasSubscribers(string qualifiedEventId)
			=> _subscriptions.Any(s => s.EventId == qualifiedEventId);

		public IReadOnlyList<EventSubscription> Find(string qualifiedEventId)
			=> _subscriptions.Where(s => s.EventId == qualifiedEventId).ToList();

		public EventSubscription? Find(EventTarget target)
			=> _subscriptions.FirstOrDefault(s => s.Target == target);

		public IReadOnlyList<EventSubscription> FindByProvider(string providerId) => [];

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string? widgetData)
		{
		}

		public void ReindexAutomation(Guid automationId, string? flows, bool enabled)
		{
		}

		public void Remove(EventTriggerOwner owner)
		{
		}
	}
}
