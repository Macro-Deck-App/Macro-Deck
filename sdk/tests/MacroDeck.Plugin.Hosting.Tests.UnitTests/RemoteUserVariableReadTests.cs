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
public class RemoteUserVariableReadTests
{
	[Test]
	public async Task A_read_sends_the_name_and_the_whole_context_and_returns_the_hosts_typed_value()
	{
		var invoker = new ScriptedInvoker(() => JsonSerializer.SerializeToElement(new UserVariablesReadResult
			{
				Status = "Found",
				Type = "Boolean",
				Value = JsonSerializer.SerializeToElement(true),
				Text = "true"
			},
			PluginProtocolJson.Options));
		var api = new RemoteUserVariableApi(invoker);

		var result = await api.ReadInContextAsync("muted", new UserVariableContext("w1", "s1", "a1"));

		var sent = (UserVariablesReadArguments)invoker.LastArguments!;
		Assert.Multiple(() =>
		{
			Assert.That((invoker.LastApi, invoker.LastOperation),
				Is.EqualTo((HostApis.UserVariables, HostOperations.UserVariables.Read)));
			Assert.That((sent.Name, sent.OwnerWidgetId, sent.ScriptId, sent.AutomationId),
				Is.EqualTo(("muted", "w1", "s1", "a1")));
			Assert.That(result, Is.EqualTo(UserVariableReadResult.Found(VariableType.Boolean, true, "true")));
		});
	}

	[Test]
	public async Task A_host_that_predates_reads_is_reported_as_not_supported()
	{
		var api = new RemoteUserVariableApi(new ScriptedInvoker(() =>
			throw HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported,
				"The 'user-variables' api has no operation 'read'.")));

		var result = await api.ReadInContextAsync("muted", new UserVariableContext());

		Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.NotSupported));
	}

	[Test]
	public async Task A_lost_connection_is_reported_as_unavailable()
	{
		var api = new RemoteUserVariableApi(new ScriptedInvoker(() =>
			throw HostInvocationException.CreateRetryable(ProtocolErrorCodes.CapabilityUnavailable, "Disconnected.")));

		var result = await api.ReadInContextAsync("muted", new UserVariableContext());

		Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.Unavailable));
	}

	[Test]
	public async Task A_reply_without_a_result_is_reported_as_unavailable()
	{
		var api = new RemoteUserVariableApi(new ScriptedInvoker(() => null));

		var result = await api.ReadInContextAsync("muted", new UserVariableContext());

		Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.Unavailable));
	}

	private sealed class ScriptedInvoker(Func<JsonElement?> answer) : IHostInvoker
	{
		public string? LastApi { get; private set; }

		public string? LastOperation { get; private set; }

		public object? LastArguments { get; private set; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			LastApi = api;
			LastOperation = operation;
			LastArguments = arguments;
			return Task.FromResult(answer());
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
