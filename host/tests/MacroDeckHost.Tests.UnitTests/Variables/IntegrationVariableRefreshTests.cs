using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class IntegrationVariableRefreshTests
{
	private const string IntegrationId = "app.example.servers";

	[Test]
	public async Task A_refresh_request_makes_the_polling_loop_read_the_integrations_variables_again()
	{
		var refresh = new VariableRefreshSignal();
		var api = new IntegrationVariableApi(IntegrationId, null!, refresh);

		await api.RequestRefreshAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refresh.DrainEagerRefreshRequested(IntegrationId), Is.True);
			Assert.That(refresh.DrainEagerRefreshRequested("app.example.other"), Is.False);
		});
	}

	[Test]
	public async Task A_changed_declared_set_makes_the_polling_loop_describe_the_integration_again()
	{
		var declarations = new VariablePollingInvalidationSignal();
		var api = new IntegrationVariableApi(IntegrationId, null!, declarations: declarations);

		await api.NotifyDeclarationsChangedAsync();

		Assert.That(declarations.DrainStale(), Is.EqualTo(new[] { IntegrationId }));
	}

	[Test]
	public async Task A_definition_refresh_makes_the_polling_loop_read_only_the_named_variables_again()
	{
		var refresh = new VariableRefreshSignal();
		var api = new IntegrationVariableApi(IntegrationId, null!, refresh);

		await api.RequestDefinitionRefreshAsync(["volume-percent", "muted"]);

		Assert.Multiple(() =>
		{
			Assert.That(refresh.DrainDefinitionsFor(IntegrationId, VariableRefreshLane.Eager),
				Is.EquivalentTo(new[] { "volume-percent", "muted" }));
			Assert.That(refresh.DrainEagerRefreshRequested(IntegrationId), Is.False);
		});
	}

	[Test]
	public void A_context_without_a_binding_store_reports_bindings_as_not_supported()
	{
		var api = new IntegrationVariableApi(IntegrationId, null!);

		Assert.ThrowsAsync<NotSupportedException>(() => api.BindAsync([
			new MacroDeck.Sdk.Variables.VariableBindingRequest("a/b", "a_b", MacroDeck.Sdk.Variables.VariableType.Text)
		]));
	}
}
