using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

public abstract class RemoteActionDefinition(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	RemoteActionDescriptor descriptor) : IActionDefinition, IConfigurableActionDefinition
{
	protected IPluginCapabilityInvoker Invoker { get; } = invoker;

	protected string PluginId { get; } = pluginId;

	protected RemoteActionDescriptor Descriptor { get; } = descriptor;

	public string Id => Descriptor.LocalId;

	public LocalizedText Name => Descriptor.Name;

	public LocalizedText Description => Descriptor.Description;

	public IReadOnlyList<ActionParameter> Parameters => Descriptor.Parameters;

	public string? DescriptiveUiSchema => Descriptor.DescriptiveUiSchema;

	/// <summary>Whether the plugin's own descriptor said this action configures with a UI tree. Read as
	/// this flag rather than by testing the adapter against <c>IUiConfigurableActionDefinition</c> - a
	/// remote adapter never implements that SDK interface, so the flag is the only way a consumer can
	/// learn this without a round trip to the plugin.</summary>
	public bool ConfiguresWithUiTree => Descriptor.ConfiguresWithUiTree;

	/// <summary>Whether the plugin's own descriptor said this action implements
	/// <c>IIconProviderActionDefinition</c>. Read as this flag rather than by testing the adapter against
	/// that SDK interface - a remote adapter never implements it directly, per ADR 0004/0056's closed
	/// eight-leaf family. A provider action is instead reached through
	/// <see cref="RemoteIconProviderActionRegistry" />, which this flag is what tells a consumer to even
	/// ask for.</summary>
	public bool ProvidesIcon => Descriptor.ProvidesIcon;

	public IActionExecutor CreateExecutor() => new RemoteActionExecutor(Invoker, PluginId, Descriptor.LocalId);

	protected async Task<ActionStateSnapshot?> GetActionStateCoreAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Actions.State,
			new ActionExecuteArguments { Parameters = ToWireParameters(parameters) },
			cancellationToken).ConfigureAwait(false);

		var result = data?.Deserialize<ActionStateResult>(PluginProtocolJson.Options);

		return result is { HasValue: true }
			? new ActionStateSnapshot([
					.. result.States.Select(state => new ActionStateDefinition(state.Id, state.Label)
					{
						DefaultAppearance = state.DefaultAppearance is { } appearance
							? new ActionStateAppearance
							{
								Label = appearance.Label,
								BackgroundColor = appearance.BackgroundColor,
								LabelColor = appearance.LabelColor,
								IconId = appearance.IconId
							}
							: null
					})
				],
				result.ActiveStateId)
			: null;
	}

	protected async Task<DynamicOptionsResult> GetDynamicOptionsCoreAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Actions.Options,
			new DynamicOptionsArguments
			{
				ParameterName = context.ParameterName,
				Filter = context.Filter,
				CurrentParameters = ToWireParameters(context.CurrentParameters)
			},
			cancellationToken).ConfigureAwait(false);

		var result = data?.Deserialize<DynamicOptionsResultDto>(PluginProtocolJson.Options);

		return new DynamicOptionsResult
		{
			Options = result?.Options.Select(option => new ActionParameterOption
					{
						Value = option.Value, Label = option.Label ?? default, Metadata = option.Metadata
					})
					.ToList() ??
				[],
			AllowsCustomValue = result?.AllowsCustomValue ?? false,
			CacheSeconds = result?.CacheSeconds,
			Error = result?.Error ?? default
		};
	}

	private Task<JsonElement?> InvokeAsync(string operation, object arguments, CancellationToken cancellationToken)
		=> Invoker.InvokeAsync(PluginId,
			new CapabilityInvokeRequest
			{
				Kind = CapabilityKinds.Actions, LocalId = Descriptor.LocalId, Operation = operation,
				Arguments = arguments
			},
			cancellationToken);

	private static Dictionary<string, JsonElement> ToWireParameters(
		IReadOnlyDictionary<string, object?> parameters)
	{
		var wire = new Dictionary<string, JsonElement>(parameters.Count, StringComparer.Ordinal);

		foreach (var (name, value) in parameters)
		{
			wire[name] = value is JsonElement element
				? element
				: JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
		}

		return wire;
	}
}
