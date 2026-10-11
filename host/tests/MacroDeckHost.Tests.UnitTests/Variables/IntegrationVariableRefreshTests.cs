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
}
