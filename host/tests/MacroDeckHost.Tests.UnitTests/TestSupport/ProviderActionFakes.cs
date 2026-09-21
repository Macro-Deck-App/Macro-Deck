using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ControllableStateProviderAction : IActionDefinition, IStateProviderActionDefinition
{
	public string Id { get; init; } = "mute";
	public LocalizedText Name { get; init; } = "Mute";
	public LocalizedText Description => "Mutes something";
	public IReadOnlyList<ActionParameter> Parameters => [];

	public Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ActionStateSnapshot?>> Answer { get; set; }
		= (_, _) => Task.FromResult<ActionStateSnapshot?>(null);

	public int Calls { get; private set; }

	public IActionExecutor CreateExecutor() => new CapturingActionDefinition().CreateExecutor();

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		Calls++;

		return Answer(parameters, cancellationToken);
	}

	public static ActionStateSnapshot Snapshot(params ActionStateDefinition[] states) => new(states, states[0].Id);
}

internal sealed class ControllableIconProviderAction : IActionDefinition, IIconProviderActionDefinition
{
	public string Id { get; init; } = "cover";
	public LocalizedText Name => "Cover";
	public LocalizedText Description => "Shows a cover";
	public IReadOnlyList<ActionParameter> Parameters => [];

	public ActionIconSnapshot? Icon { get; set; } = new() { Version = "v1", MediaType = "image/png" };

	public int Calls { get; private set; }

	public IActionExecutor CreateExecutor() => new CapturingActionDefinition().CreateExecutor();

	public Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		Calls++;

		return Task.FromResult(Icon);
	}
}

internal sealed class StateAndIconProviderAction : IActionDefinition, IStateProviderActionDefinition,
	IIconProviderActionDefinition
{
	public string Id { get; init; } = "now-playing";
	public LocalizedText Name => "Now playing";
	public LocalizedText Description => "Shows what is playing";
	public IReadOnlyList<ActionParameter> Parameters => [];

	public IActionExecutor CreateExecutor() => new CapturingActionDefinition().CreateExecutor();

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionStateSnapshot?>(ControllableStateProviderAction.Snapshot(
			new ActionStateDefinition("playing", "Playing"),
			new ActionStateDefinition("paused", "Paused")));

	public Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot { Version = "v1", MediaType = "image/png" });
}

internal static class RemoteIconProviderFixture
{
	public const string PluginId = "com.example.plugin";
	public const string ActionId = "artwork";

	public static IIntegration Integration()
		=> RemotePluginIntegrationFactory.Create(PluginId,
			"Example Plugin",
			"1.0.0",
			RemotePluginCapabilitySnapshot.Empty(PluginId) with
			{
				AcceptedKinds = [CapabilityKinds.Actions],
				Actions = [new RemoteActionDescriptor(ActionId, "Artwork", string.Empty, [], null, false, false)
					with { ProvidesIcon = true }],
			},
			new ThrowingInvoker(),
			new AlwaysDisconnected(),
			new InMemoryPluginAssetCache(),
			false,
			false,
			false);

	private sealed class ThrowingInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("No plugin is connected in this fixture.");

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => throw new NotSupportedException();

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class AlwaysDisconnected : IRemotePluginConnectionState
	{
		public bool IsConnected(string pluginId) => false;
	}
}
