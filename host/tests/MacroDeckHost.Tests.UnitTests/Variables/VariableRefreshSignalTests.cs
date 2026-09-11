using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class VariableRefreshSignalTests
{
	[Test]
	public void An_eager_request_is_reported_once_and_then_cleared()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestEagerRefresh("app.test.one");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.True);
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.False);
		});
	}

	[Test]
	public void Repeated_eager_requests_coalesce_into_one()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestEagerRefresh("app.test.one");
		signal.RequestEagerRefresh("app.test.one");
		signal.RequestEagerRefresh("app.test.one");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.True);
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.False);
		});
	}

	[Test]
	public void An_eager_request_belongs_to_one_integration()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestEagerRefresh("app.test.one");

		Assert.That(signal.DrainEagerRefreshRequested("app.test.two"), Is.False);
	}

	[Test]
	public void An_eager_request_leaves_per_variable_requests_alone()
	{
		var signal = new VariableRefreshSignal();
		var variable = Guid.NewGuid();

		signal.RequestRefresh("app.test.one", variable);
		signal.RequestEagerRefresh("app.test.one");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.True);
			Assert.That(signal.DrainFor("app.test.one"), Is.EqualTo(new[] { variable }));
		});
	}

	[Test]
	public void Definition_requests_are_reported_once_and_coalesce()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestDefinitionRefresh("app.test.one", "volume");
		signal.RequestDefinitionRefresh("app.test.one", "volume");
		signal.RequestDefinitionRefresh("app.test.one", "muted");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainDefinitionsFor("app.test.one"), Is.EquivalentTo(new[] { "volume", "muted" }));
			Assert.That(signal.DrainDefinitionsFor("app.test.one"), Is.Empty);
		});
	}

	[Test]
	public void A_definition_request_belongs_to_one_integration_and_raises_no_other_request()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestDefinitionRefresh("app.test.one", "volume");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainDefinitionsFor("app.test.two"), Is.Empty);
			Assert.That(signal.DrainFor("app.test.one"), Is.Empty);
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.False);
		});
	}
}
