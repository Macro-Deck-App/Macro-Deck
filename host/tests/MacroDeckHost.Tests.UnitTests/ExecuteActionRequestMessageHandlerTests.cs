using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

public class ExecuteActionRequestMessageHandlerTests
{
	private static readonly ILogger _logger = Log.Logger;

	private ConfigurableIntegrationRegistry _registry = null!;
	private FakeHostLockState _lockState = null!;
	private ExecuteActionRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new ConfigurableIntegrationRegistry();
		_lockState = new FakeHostLockState();
		_handler = new ExecuteActionRequestMessageHandler(_registry,
			new NullActionInteractions(),
			new MusicPlayerPollNudge(_registry),
			_lockState,
			_logger);
	}

	[Test]
	public async Task Runs_action_and_coerces_parameters_into_declared_types()
	{
		var action = new CapturingActionDefinition
		{
			Id = "capture",
			Parameters = [ActionParameter.Text("message"), ActionParameter.Number("level")]
		};
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var response = await _handler.Handle(new ExecuteActionRequest
			{
				IntegrationId = "integration",
				ActionId = "capture",
				Parameters = Params("""{ "message": "hi", "level": 42 }""")
			},
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		Assert.That(action.ExecuteCount, Is.EqualTo(1));
		Assert.That(action.CapturedParameters!["message"], Is.EqualTo("hi"));
		Assert.That(action.CapturedParameters!["level"], Is.EqualTo(42L));
	}

	// This path is not the deck button press - it is what POST /api/actions/run reaches. A guard
	// that only covers the button press leaves the whole action catalogue runnable from a locked host.
	[Test]
	public async Task Running_an_action_is_refused_while_locked_and_the_action_never_executes()
	{
		var action = new CapturingActionDefinition { Id = "capture", Parameters = [] };
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		_lockState.IsLocked = true;

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(action.ExecuteCount, Is.Zero);
		});
	}

	[Test]
	public async Task Numeric_string_is_parsed_for_number_parameters()
	{
		var action = new CapturingActionDefinition
		{
			Id = "capture",
			Parameters = [ActionParameter.Number("level")]
		};
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		await _handler.Handle(new ExecuteActionRequest
			{
				IntegrationId = "integration",
				ActionId = "capture",
				Parameters = Params("""{ "level": "12.5" }""")
			},
			CancellationToken.None);

		Assert.That(action.CapturedParameters!["level"], Is.EqualTo(12.5d));
	}

	[Test]
	public async Task Declared_parameter_without_a_value_falls_back_to_its_default()
	{
		var action = new CapturingActionDefinition
		{
			Id = "capture",
			Parameters = [ActionParameter.Text("message", defaultValue: "fallback")]
		};
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		await _handler.Handle(new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.That(action.CapturedParameters!["message"], Is.EqualTo("fallback"));
	}

	[Test]
	public async Task Unknown_action_returns_not_found()
	{
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [] });

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "missing" },
			CancellationToken.None);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("NOT_FOUND"));
	}

	[Test]
	public async Task An_action_restricted_to_other_platforms_returns_not_found()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var action = new CapturingActionDefinition { Id = "capture", Platforms = otherPlatforms };
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("NOT_FOUND"));
		Assert.That(action.ExecuteCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Disabled_integration_is_rejected()
	{
		var action = new CapturingActionDefinition { Id = "capture" };
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] }, enabled: false);

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("INTEGRATION_DISABLED"));
		Assert.That(action.ExecuteCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Missing_identifiers_are_rejected()
	{
		var response = await _handler.Handle(new ExecuteActionRequest { IntegrationId = "", ActionId = "" },
			CancellationToken.None);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("VALIDATION_ERROR"));
	}

	[Test]
	public async Task Executor_failure_is_reported_as_execution_error()
	{
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [new ThrowingActionDefinition()] });

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "throw" },
			CancellationToken.None);

		Assert.That(response.Success, Is.False);
		Assert.That(response.Error!.Code, Is.EqualTo("EXECUTION_ERROR"));
		Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.EqualTo("boom"));
	}

	[Test]
	public async Task An_executors_own_failure_code_passes_through()
	{
		var action = new CapturingActionDefinition
		{
			Id = "capture",
			Result = ActionResult.Failed(ActionErrorCodes.NotConnected, "The service is not connected.")
		};
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.EqualTo("The service is not connected."));
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Failed));
		});
	}

	[Test]
	public async Task An_accepted_result_is_reported_as_accepted()
	{
		var action = new CapturingActionDefinition
		{
			Id = "capture",
			Result = ActionResult.Accepted("Waiting for confirmation.")
		};
		_registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var response = await _handler.Handle(
			new ExecuteActionRequest { IntegrationId = "integration", ActionId = "capture" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Accepted));
		});
	}

	private static Dictionary<string, JsonElement> Params(string json)
		=> JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

	private sealed class ConfigurableIntegrationRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		private readonly Dictionary<string, IIntegration> _integrations = [];
		private readonly HashSet<string> _disabled = [];

		public IReadOnlyList<IIntegration> Integrations => _integrations.Values.ToList();

		public void Add(IIntegration integration, bool enabled = true)
		{
			_integrations[integration.Id] = integration;
			if (!enabled)
			{
				_disabled.Add(integration.Id);
			}
		}

		public IActionDefinition? FindAction(string integrationId, string actionId)
			=> _integrations.TryGetValue(integrationId, out var integration)
				? integration.Actions.FirstOrDefault(a => a.Id == actionId && a.RunsHere())
				: null;

		public IActionDefinition? FindAction(QualifiedId id) => FindAction(id.OwnerId, id.LocalId);

		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true)
		{
			var descriptors = new List<ActionDescriptor>();
			foreach (var integration in _integrations.Values)
			{
				if (enabledOnly && !IsEnabled(integration.Id))
				{
					continue;
				}

				foreach (var action in integration.Actions)
				{
					if (QualifiedId.TryCreate(integration.Id,
						action.Id,
						OwnerIdKind.Package,
						LocalIdKind.Declared,
						out var id))
					{
						descriptors.Add(new ActionDescriptor(id, integration, action));
					}
				}
			}

			return descriptors;
		}

		public bool IsEnabled(string integrationId)
			=> _integrations.ContainsKey(integrationId) && !_disabled.Contains(integrationId);

		public void SetEnabled(string integrationId, bool enabled)
		{
			if (enabled)
			{
				_disabled.Remove(integrationId);
			}
			else
			{
				_disabled.Add(integrationId);
			}
		}

		public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

		public Task<bool> UnregisterAsync(string integrationId)
		{
			var removed = _integrations.Remove(integrationId);
			_disabled.Remove(integrationId);
			return Task.FromResult(removed);
		}

		public Task<IntegrationRegistrationResult> RegisterAsync(
			IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
		{
			Add(integration);
			return Task.FromResult(IntegrationRegistrationResult.Success);
		}
	}

	private sealed class ThrowingActionDefinition : IActionDefinition
	{
		public string Id => "throw";
		public LocalizedText Name => "Throw";
		public LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => new Executor();

		private sealed class Executor : IActionExecutor
		{
			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
				=> throw new InvalidOperationException("boom");
		}
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(
			string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(
			string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}
}
