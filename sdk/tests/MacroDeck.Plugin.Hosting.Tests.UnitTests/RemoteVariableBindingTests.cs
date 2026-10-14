using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteVariableBindingTests
{
	private static readonly VariableBindingRequest _kitchen
		= new("entity/light.kitchen/state", "ha_light_kitchen", VariableType.Text) { MigratedFrom = "v1" };

	private static readonly VariableBindingRequest _desk
		= new("entity/switch.desk/state", "ha_switch_desk", VariableType.Boolean);

	[Test]
	public async Task Bindings_travel_as_one_bind_request_and_answer_an_outcome_each()
	{
		var host = new AnsweringHost(new VariablesBindResult
		{
			Outcomes = [VariableBindingOutcomes.Bound, VariableBindingOutcomes.AlreadyBound]
		});

		var outcomes = await new RemoteVariableApi(host).BindAsync([_kitchen, _desk]);

		Assert.Multiple(() =>
		{
			Assert.That(outcomes,
				Is.EqualTo(new[] { VariableBindingOutcome.Bound, VariableBindingOutcome.AlreadyBound }));
			Assert.That(host.Operation, Is.EqualTo(HostOperations.Variables.Bind));
			Assert.That(host.Arguments,
				Is.EqualTo(
					"""{"bindings":[{"localResourceId":"entity/light.kitchen/state","name":"ha_light_kitchen","type":"Text","migratedFrom":"v1"},{"localResourceId":"entity/switch.desk/state","name":"ha_switch_desk","type":"Boolean"}]}"""));
		});
	}

	[Test]
	public void A_macro_deck_without_bindings_reports_them_as_not_supported()
	{
		var host = new AnsweringHost(failure: HostInvocationException.CreateNonRetryable(
			ProtocolErrorCodes.CapabilityUnsupported,
			"no"));

		Assert.ThrowsAsync<NotSupportedException>(() => new RemoteVariableApi(host).BindAsync([_kitchen]));
	}

	[Test]
	public void A_binding_the_host_refuses_is_an_argument_error()
	{
		var host = new AnsweringHost(failure: HostInvocationException.CreateNonRetryable(
			ProtocolErrorCodes.InvalidPayload,
			"bad id"));

		Assert.ThrowsAsync<ArgumentException>(() => new RemoteVariableApi(host).BindAsync([_kitchen]));
	}

	[Test]
	public void A_name_that_is_not_canonical_is_refused_before_anything_is_sent()
	{
		var host = new AnsweringHost(new VariablesBindResult());

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(()
				=> new RemoteVariableApi(host).BindAsync([_kitchen with { Name = "Kitchen Light" }]));
			Assert.That(host.Operation, Is.Null);
		});
	}

	[Test]
	public async Task Binding_nothing_sends_nothing()
	{
		var host = new AnsweringHost(new VariablesBindResult());

		var outcomes = await new RemoteVariableApi(host).BindAsync([]);

		Assert.Multiple(() =>
		{
			Assert.That(outcomes, Is.Empty);
			Assert.That(host.Operation, Is.Null);
		});
	}

	private sealed class AnsweringHost(object? answer = null, Exception? failure = null) : IHostInvoker
	{
		public string? Operation { get; private set; }

		public string? Arguments { get; private set; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Operation = operation;
			Arguments = arguments is null ? null : JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
			return failure is not null
				? Task.FromException<JsonElement?>(failure)
				: Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(answer, PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
