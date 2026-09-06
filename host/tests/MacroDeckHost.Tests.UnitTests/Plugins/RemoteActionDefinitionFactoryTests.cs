using System.Text.Json;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

/// <summary>
/// The four {dynamic-options} x {state} leaves RemoteActionDefinitionFactory picks between. ADR 0004
/// requires a leaf drifting from its name to be a test failure - each of the 4 combinations must
/// implement exactly the interfaces its descriptor claims, no more and no less.
/// </summary>
[TestFixture]
public class RemoteActionDefinitionFactoryTests
{
	private static readonly IPluginCapabilityInvoker _invoker = new ThrowingInvoker();

	private static RemoteActionDescriptor Descriptor(bool dynamicOptions, bool state)
		=> new("action-id",
			"Action",
			string.Empty,
			[],
			null,
			dynamicOptions,
			state);

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(true, true)]
	public void Each_leaf_implements_exactly_the_interfaces_its_descriptor_claims(
		bool dynamicOptions,
		bool state)
	{
		var action = RemoteActionDefinitionFactory.Create(_invoker,
			"com.example.plugin",
			Descriptor(dynamicOptions, state));

		Assert.Multiple(() =>
		{
			Assert.That(action is IDynamicOptionsActionDefinition,
				Is.EqualTo(dynamicOptions),
				nameof(IDynamicOptionsActionDefinition));
			Assert.That(action is IStateProviderActionDefinition,
				Is.EqualTo(state),
				nameof(IStateProviderActionDefinition));

			// Every leaf is always a plain action and always configurable, regardless of the other two.
			Assert.That(action, Is.InstanceOf<IActionDefinition>());
			Assert.That(action, Is.InstanceOf<IConfigurableActionDefinition>());
		});
	}

	private sealed class ThrowingInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("Not exercised by the presence-matrix tests.");

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope result)
			=> throw new NotSupportedException();

		public void AbortAll(string pluginId, MacroDeck.Plugin.Protocol.Errors.ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
