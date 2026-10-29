using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpRiskPolicyTests
{
	private const string BuiltIn = "test.builtin";
	private const string Plugin = "com.example.plugin";

	private IntegrationRegistry _registry = null!;
	private MemoryPreferences _preferences = null!;
	private McpRiskPolicy _policy = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			new LoggerConfiguration().CreateLogger());
		IActionDefinition[] actions =
		[
			.. Enum.GetValues<ActionRiskCategory>()
				.Select(category => new RiskAction(category.ToString().ToLowerInvariant(), category))
		];
		await _registry.RegisterAsync(new FakeIntegration { Id = BuiltIn, Actions = actions });
		await _registry.RegisterAsync(new FakeIntegration
				{ Id = Plugin, Actions = [new RiskAction("none", ActionRiskCategory.None)] },
			IntegrationOrigin.Plugin);
		_preferences = new MemoryPreferences();
		_policy = new McpRiskPolicy(_preferences, _registry);
	}

	[TestCase(ActionRiskCategory.None, McpRiskMode.Allow)]
	[TestCase(ActionRiskCategory.Scripts, McpRiskMode.Confirm)]
	[TestCase(ActionRiskCategory.Programs, McpRiskMode.Confirm)]
	[TestCase(ActionRiskCategory.Input, McpRiskMode.Confirm)]
	[TestCase(ActionRiskCategory.Network, McpRiskMode.Confirm)]
	[TestCase(ActionRiskCategory.Files, McpRiskMode.Confirm)]
	[TestCase(ActionRiskCategory.Power, McpRiskMode.Confirm)]
	public async Task A_built_in_action_asks_first_exactly_when_it_declares_a_risk_category(
		ActionRiskCategory category,
		McpRiskMode expected)
	{
		Assert.That(await _policy.Classify(BuiltIn, category.ToString().ToLowerInvariant()), Is.EqualTo(expected));
	}

	[Test]
	public async Task Plugin_unknown_and_offline_providers_ask_first_whatever_they_declare()
	{
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await _policy.Classify(Plugin, "none"), Is.EqualTo(McpRiskMode.Confirm));
			Assert.That(await _policy.Classify("com.example.offline", "anything"), Is.EqualTo(McpRiskMode.Confirm));
			Assert.That(await _policy.Classify(BuiltIn, "an-action-that-went-away"), Is.EqualTo(McpRiskMode.Confirm));
		});
	}

	[Test]
	public async Task A_user_override_wins_and_removing_it_restores_the_default()
	{
		await _policy.SetOverride(Plugin, McpRiskMode.Allow);
		var allowed = await _policy.Classify(Plugin, "none");
		await _policy.SetOverride(BuiltIn, McpRiskMode.Off);
		var off = await _policy.Classify(BuiltIn, "none");
		await _policy.SetOverride(Plugin, null);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(allowed, Is.EqualTo(McpRiskMode.Allow));
			Assert.That(off, Is.EqualTo(McpRiskMode.Off));
			Assert.That(await _policy.Classify(Plugin, "none"), Is.EqualTo(McpRiskMode.Confirm));
		});
	}

	[Test]
	public async Task An_unreadable_policy_turns_every_provider_off()
	{
		await _preferences.SetValue(McpRiskPolicy.PreferenceKey, "{not json");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await _policy.Classify(BuiltIn, "none"), Is.EqualTo(McpRiskMode.Off));
			Assert.That(await _policy.Classify(Plugin, "none"), Is.EqualTo(McpRiskMode.Off));
		});
	}

	private sealed class RiskAction(string id, ActionRiskCategory category) : IActionDefinition
	{
		public string Id { get; } = id;

		public LocalizedText Name => Id;

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters => [];

		public ActionRiskCategory RiskCategory { get; } = category;

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}
	}

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		private readonly Dictionary<string, string> _values = [];

		public Task<AppPreferenceEntity?> GetByKey(string key)
		{
			return Task.FromResult(_values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);
		}

		public Task SetValue(string key, string value)
		{
			_values[key] = value;
			return Task.CompletedTask;
		}
	}
}
