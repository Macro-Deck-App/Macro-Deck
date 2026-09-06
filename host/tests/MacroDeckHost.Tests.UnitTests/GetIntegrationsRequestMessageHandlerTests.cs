using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests;

public class GetIntegrationsRequestMessageHandlerTests
{
	private static GetIntegrationsRequestMessageHandler CreateHandler(
		ConfigurableIntegrationRegistry registry,
		FakeIntegrationConfigStore configStore)
		=> new(registry,
			configStore,
			new IntegrationIssueService(registry, new FakeIntegrationHostIssueStore(), new FakeIntegrationLifecycle()));

	[Test]
	public async Task An_unconfigured_integration_reports_its_declared_variable_count_not_zero()
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int1",
			IsInitialized = false,
			Variables = [],
			DeclaredVariablesOverride =
			[
				VariableDefinition.Eager("v1", VariableType.Text),
				VariableDefinition.Eager("v2", VariableType.Text)
			]
		};
		var registry = new ConfigurableIntegrationRegistry([integration], disabled: [integration.Id]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 0);
		var handler = CreateHandler(registry, configStore);

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.That(response.Integrations.Single().VariableCount, Is.EqualTo(2));
	}

	[TestCase(false, 0, false, TestName = "Not_configured")]
	[TestCase(false, 1, false, TestName = "Disabled")]
	[TestCase(true, 1, false, TestName = "Enabled_not_initialized")]
	[TestCase(true, 1, true, TestName = "Enabled_disconnected")]
	public async Task Action_and_variable_counts_are_identical_across_states(
		bool enabled,
		int configuredEntryCount,
		bool isInitialized)
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int2",
			IsInitialized = isInitialized,
			Actions = [new CapturingActionDefinition { Id = "a1" }, new CapturingActionDefinition { Id = "a2" }],
			Variables = [],
			DeclaredVariablesOverride =
			[
				VariableDefinition.Eager("v1", VariableType.Text),
				VariableDefinition.Eager("v2", VariableType.Text),
				VariableDefinition.Eager("v3", VariableType.Text)
			]
		};
		var registry = new ConfigurableIntegrationRegistry([integration], enabled ? [] : [integration.Id]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, configuredEntryCount);
		var handler = CreateHandler(registry, configStore);

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		var dto = response.Integrations.Single();
		Assert.Multiple(() =>
		{
			Assert.That(dto.ActionCount, Is.EqualTo(2));
			Assert.That(dto.VariableCount, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task ActionCount_excludes_actions_restricted_to_other_platforms()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int3",
			IsInitialized = true,
			Actions =
			[
				new CapturingActionDefinition { Id = "hibernate", Platforms = otherPlatforms },
				new CapturingActionDefinition { Id = "sleep" }
			],
			Variables = []
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 1);
		var handler = CreateHandler(registry, configStore);

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.That(response.Integrations.Single().ActionCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Non_variable_providers_report_zero_variables()
	{
		var integration = StubIntegration.Create("plain-int");
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore());

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.That(response.Integrations.Single().VariableCount, Is.EqualTo(0));
	}

	[Test]
	public async Task System_integrations_are_hidden()
	{
		var system = StubIntegration.Create("system-1", system: true);
		var registry = new ConfigurableIntegrationRegistry([system]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore());

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.That(response.Integrations, Is.Empty);
	}

	// Issue #754: the icon URL the UI builds is keyed by the integration id, so the list has to carry
	// something that changes with the icon itself or a replaced icon keeps the URL it was cached under.
	[Test]
	public async Task A_replaced_icon_changes_the_icon_version_the_list_reports()
	{
		var integration = new IconStubIntegration { Id = "icon-int" };
		var handler = CreateHandler(new ConfigurableIntegrationRegistry([integration]),
			new FakeIntegrationConfigStore());

		var before = (await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None))
			.Integrations.Single().IconVersion;
		integration.Icon = [7, 7, 7];
		var after = (await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None))
			.Integrations.Single().IconVersion;

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.Not.Null.And.Not.Empty);
			Assert.That(after, Is.Not.EqualTo(before));
		});
	}

	[Test]
	public async Task An_integration_without_an_icon_reports_no_icon_version()
	{
		var registry = new ConfigurableIntegrationRegistry([StubIntegration.Create("plain-int")]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore());

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		var dto = response.Integrations.Single();
		Assert.Multiple(() =>
		{
			Assert.That(dto.HasIcon, Is.False);
			Assert.That(dto.IconVersion, Is.Null);
		});
	}

	private sealed class IconStubIntegration : IIntegration, IIntegrationIconProvider
	{
		public string Id { get; init; } = "icon-int";
		public LocalizedText Name => "Icon Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public byte[] Icon { get; set; } = [1, 2, 3];

		public string IconMimeType => "image/png";

		public byte[] GetIcon() => Icon;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
