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
			Assert.That(signal.DrainFor("app.test.one", VariableRefreshLane.Eager), Is.EqualTo(new[] { variable }));
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
			Assert.That(signal.DrainDefinitionsFor("app.test.one", VariableRefreshLane.Eager),
				Is.EquivalentTo(new[] { "volume", "muted" }));
			Assert.That(signal.DrainDefinitionsFor("app.test.one", VariableRefreshLane.Eager), Is.Empty);
		});
	}

	[Test]
	public void Draining_one_lane_leaves_the_request_for_the_other_loop()
	{
		var signal = new VariableRefreshSignal();
		var variable = Guid.NewGuid();

		signal.RequestRefresh("app.test.one", variable);
		signal.RequestDefinitionRefresh("app.test.one", "input/mic/muted");

		var eagerVariables = signal.DrainFor("app.test.one", VariableRefreshLane.Eager);
		var eagerDefinitions = signal.DrainDefinitionsFor("app.test.one", VariableRefreshLane.Eager);

		Assert.Multiple(() =>
		{
			Assert.That(eagerVariables, Is.EqualTo(new[] { variable }));
			Assert.That(eagerDefinitions, Is.EqualTo(new[] { "input/mic/muted" }));
			Assert.That(signal.DrainFor("app.test.one", VariableRefreshLane.Catalog), Is.EqualTo(new[] { variable }));
			Assert.That(signal.DrainDefinitionsFor("app.test.one", VariableRefreshLane.Catalog),
				Is.EqualTo(new[] { "input/mic/muted" }));
		});
	}

	[Test]
	public void Pending_integrations_lists_each_integration_with_a_request_until_it_is_drained()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestDefinitionRefresh("app.test.one", "volume");
		signal.RequestRefresh("app.test.two", Guid.NewGuid());
		var before = signal.PendingIntegrations(VariableRefreshLane.Catalog);

		signal.DrainDefinitionsFor("app.test.one", VariableRefreshLane.Catalog);

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EquivalentTo(new[] { "app.test.one", "app.test.two" }));
			Assert.That(signal.PendingIntegrations(VariableRefreshLane.Catalog),
				Is.EquivalentTo(new[] { "app.test.two" }));
			Assert.That(signal.PendingIntegrations(VariableRefreshLane.Eager),
				Is.EquivalentTo(new[] { "app.test.one", "app.test.two" }));
		});
	}

	[Test]
	public void A_definition_request_belongs_to_one_integration_and_raises_no_other_request()
	{
		var signal = new VariableRefreshSignal();

		signal.RequestDefinitionRefresh("app.test.one", "volume");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainDefinitionsFor("app.test.two", VariableRefreshLane.Eager), Is.Empty);
			Assert.That(signal.DrainFor("app.test.one", VariableRefreshLane.Eager), Is.Empty);
			Assert.That(signal.DrainEagerRefreshRequested("app.test.one"), Is.False);
		});
	}
}
