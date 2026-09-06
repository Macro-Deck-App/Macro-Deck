using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="RemoteVariableSink" />, the plugin-side <see cref="IVariableSink" /> that carries a
/// provider's pushes over <c>host.invoke</c>. The two Api/Operation tests here
/// (<see cref="Publishing_sends_the_declared_Api_Operation_pair" /> and
/// <see cref="Invalidating_the_catalog_sends_the_declared_Api_Operation_pair" />) are load-bearing for
/// <c>RemoteIntegrationContextTests.Every_declared_HostApis_operation_has_a_covered_case</c>, which
/// points at this file for <c>variable-values</c> coverage.
/// </summary>
[TestFixture]
public class RemoteVariableSinkTests
{
	private static RemoteVariableSink Sink(IHostInvoker invoker, VariableSubscriptions? subscriptions = null)
		=> new(invoker, subscriptions ?? Subscribed(), Serilog.Core.Logger.None);

	private static VariableSubscriptions Subscribed(params string[] ids)
	{
		var subscriptions = new VariableSubscriptions(Serilog.Log.Logger);
		subscriptions.Replace(ids);
		return subscriptions;
	}

	[Test]
	public async Task Publishing_sends_the_declared_Api_Operation_pair()
	{
		var invoker = new RecordingInvoker();
		var sink = Sink(invoker, Subscribed("a"));

		await sink.PublishAsync([VariableValue.Of("a", "on")], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(invoker.Calls, Has.Count.EqualTo(1));
			Assert.That(invoker.Calls[0].Api, Is.EqualTo(HostApis.VariableValues));
			Assert.That(invoker.Calls[0].Operation, Is.EqualTo(HostOperations.VariableValues.Value));
		});
	}

	[Test]
	public async Task Invalidating_the_catalog_sends_the_declared_Api_Operation_pair()
	{
		var invoker = new RecordingInvoker();
		var sink = Sink(invoker);

		await sink.InvalidateCatalogAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(invoker.Calls, Has.Count.EqualTo(1));
			Assert.That(invoker.Calls[0].Api, Is.EqualTo(HostApis.VariableValues));
			Assert.That(invoker.Calls[0].Operation, Is.EqualTo(HostOperations.VariableValues.Invalidate));
		});
	}

	[Test]
	public async Task A_value_for_an_unsubscribed_id_is_dropped_before_it_reaches_the_host()
	{
		var invoker = new RecordingInvoker();
		var sink = Sink(invoker, Subscribed("a", "b"));

		await sink.PublishAsync([VariableValue.Of("a", "on"), VariableValue.Of("c", "off")],
			CancellationToken.None);

		var sent = SentIds(invoker.Calls.Single());

		Assert.That(sent, Is.EqualTo(new[] { "a" }));
	}

	/// <summary>The volatile attributes travel with the value rather than with the declaration, so a push
	/// has to carry them - a range that changed since the last read is exactly what a push is for.</summary>
	[Test]
	public async Task A_published_reading_carries_its_volatile_attributes()
	{
		var invoker = new RecordingInvoker();
		var sink = Sink(invoker, Subscribed("position"));

		await sink.PublishAsync([VariableValue.Of("position", VariableReading.Of(187d, 0, 245, 1))],
			CancellationToken.None);

		var reading = ((VariableValuesValueArguments)invoker.Calls.Single().Arguments!).Values.Single().Reading;

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value.Number, Is.EqualTo(187d));
			Assert.That(reading.Min, Is.EqualTo(0));
			Assert.That(reading.Max, Is.EqualTo(245));
			Assert.That(reading.Step, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_batch_larger_than_the_protocol_bound_is_split_across_invokes()
	{
		var ids = Enumerable.Range(0, ProtocolLimits.MaxVariableValuesPerBatch + 10)
			.Select(index => $"id-{index}")
			.ToList();
		var invoker = new RecordingInvoker();
		var sink = Sink(invoker, Subscribed(ids.ToArray()));

		var values = ids.Select(id => VariableValue.Of(id, "on")).ToList();
		await sink.PublishAsync(values, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(invoker.Calls, Has.Count.EqualTo(2));
			Assert.That(invoker.Calls, Has.All.Property(nameof(RecordedCall.Api)).EqualTo(HostApis.VariableValues));
			Assert.That(invoker.Calls,
				Has.All.Property(nameof(RecordedCall.Operation)).EqualTo(HostOperations.VariableValues.Value));
			Assert.That(invoker.Calls.SelectMany(SentIds), Is.EquivalentTo(ids));
		});
	}

	/// <summary>
	/// The SDK's <see cref="IVariableSink" /> doc promises publishing is fire-and-forget and a failed
	/// delivery cannot take down a provider's event loop, so a throwing invoker must be swallowed and
	/// logged rather than propagate out of either member.
	/// </summary>
	[Test]
	public void A_transport_failure_does_not_propagate_out_of_publish()
	{
		var invoker = new ThrowingInvoker();
		var sink = Sink(invoker, Subscribed("a"));

		Assert.DoesNotThrowAsync(async () =>
			await sink.PublishAsync([VariableValue.Of("a", "on")], CancellationToken.None));
		Assert.DoesNotThrowAsync(async () => await sink.InvalidateCatalogAsync(CancellationToken.None));
	}

	private static IReadOnlyList<string> SentIds(RecordedCall call)
	{
		var arguments = (VariableValuesValueArguments)call.Arguments!;
		return arguments.Values.Select(value => value.Id).ToList();
	}

	private sealed record RecordedCall(string Api, string Operation, object? Arguments);

	private sealed class RecordingInvoker : IHostInvoker
	{
		public List<RecordedCall> Calls { get; } = [];

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Calls.Add(new RecordedCall(api, operation, arguments));
			return Task.FromResult<JsonElement?>(null);
		}

		public bool TryComplete(ProtocolEnvelope result) => false;
	}

	private sealed class ThrowingInvoker : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
			=> throw new InvalidOperationException("Simulated transport failure.");

		public bool TryComplete(ProtocolEnvelope result) => false;
	}
}
