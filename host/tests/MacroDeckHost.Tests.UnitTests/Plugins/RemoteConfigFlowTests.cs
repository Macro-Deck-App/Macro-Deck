using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
internal sealed class RemoteConfigFlowTests
{
	[Test]
	public async Task Entry_title_is_forwarded_to_remote_flows_on_start_and_submit()
	{
		var invoker = new RecordingInvoker();
		var flow = new RemoteConfigFlow("plugin.example", invoker);
		var context = new EntryContext("Streaming PC");

		await flow.StartAsync(context, CancellationToken.None);
		await flow.SubmitAsync("connection",
			new Dictionary<string, object?>(),
			context,
			CancellationToken.None);

		var start = (FlowStartArguments)invoker.Requests[0].Arguments!;
		var submit = (FlowSubmitArguments)invoker.Requests[1].Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(start.EntryTitle, Is.EqualTo("Streaming PC"));
			Assert.That(submit.EntryTitle, Is.EqualTo("Streaming PC"));
		});
	}

	private sealed class EntryContext(string entryTitle) : IConfigFlowEntryContext
	{
		public string? EntryTitle { get; } = entryTitle;
		public IOAuthSession OAuth { get; } = new OAuthSession();
	}

	private sealed class OAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://127.0.0.1/callback";
		public string State => "state";
		public string? AuthorizationCode => null;
	}

	private sealed class RecordingInvoker : IPluginCapabilityInvoker
	{
		public List<CapabilityInvokeRequest> Requests { get; } = [];

		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			var result = new ConfigFlowResultDto
			{
				Kind = request.Operation == CapabilityOperations.ConfigFlow.FlowSubmit
					? nameof(ConfigFlowResultKind.Complete)
					: nameof(ConfigFlowResultKind.Step),
				NextStep = request.Operation == CapabilityOperations.ConfigFlow.FlowSubmit
					? null
					: new ConfigFlowStepDto { StepId = "connection", Fields = [] },
				EntryTitle = request.Operation == CapabilityOperations.ConfigFlow.FlowSubmit ? "Streaming PC" : null
			};
			return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(result,
				PluginProtocolJson.Options));
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
