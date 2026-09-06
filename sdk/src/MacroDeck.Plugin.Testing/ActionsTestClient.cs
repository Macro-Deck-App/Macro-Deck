using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>actions</c> capability, invoked on a <see cref="PluginSessionView" />. Deserialize a
/// successful outcome's data with <see cref="CapabilityInvocationOutcome.DataAs{T}" /> - <see cref="ActionCatalogPayload" />
/// for <see cref="DescribeAsync" />, <see cref="ActionExecuteResult" /> for <see cref="ExecuteAsync" />,
/// <see cref="DynamicOptionsResultDto" /> for <see cref="GetOptionsAsync" />,
/// <see cref="SliderStateResult" /> for <see cref="GetSliderStateAsync" />,
/// <see cref="ActionStateResult" /> for <see cref="GetActionStateAsync" />,
/// <see cref="ActionIconResult" /> for <see cref="GetActionIconAsync" />, and
/// <see cref="ActionIconContentResult" /> for <see cref="GetActionIconContentAsync" />.
/// </summary>
public sealed class ActionsTestClient
{
	// Not a protocol constant - actions has no ProviderCapabilityId to address describe with, since
	// every declared local id belongs to a specific action. Any string works here because
	// ActionsCapabilityHandler.Describe ignores CapabilityInvocation.LocalId entirely; this value is
	// this package's own convention, not something a wire trace should be expected to explain.
	private const string DescribeLocalId = "describe";

	private readonly ICapabilityInvoker _invoker;

	internal ActionsTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>The full action catalogue - see <see cref="ActionCatalogPayload" />.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			DescribeLocalId,
			CapabilityOperations.Actions.Describe,
			null,
			options);

	/// <summary>Runs the action declared under <paramref name="localId" />.</summary>
	public Task<CapabilityInvocationOutcome> ExecuteAsync(
		string localId,
		IReadOnlyDictionary<string, object?>? parameters = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			localId,
			CapabilityOperations.Actions.Execute,
			new ActionExecuteArguments
			{
				Parameters = ToElements(parameters),
				OriginClientId = originClientId,
				OwnerWidgetId = ownerWidgetId
			},
			options);

	/// <summary>Asks a dynamic-options action declared under <paramref name="localId" /> for its current options.</summary>
	public Task<CapabilityInvocationOutcome> GetOptionsAsync(
		string localId,
		string parameterName,
		string? filter = null,
		IReadOnlyDictionary<string, object?>? currentParameters = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			localId,
			CapabilityOperations.Actions.Options,
			new DynamicOptionsArguments
			{
				ParameterName = parameterName,
				Filter = filter,
				CurrentParameters = ToElements(currentParameters)
			},
			options);

	/// <summary>Asks a state-provider action declared under <paramref name="localId" /> for its current state.</summary>
	public Task<CapabilityInvocationOutcome> GetActionStateAsync(
		string localId,
		IReadOnlyDictionary<string, object?>? parameters = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			localId,
			CapabilityOperations.Actions.State,
			new ActionExecuteArguments { Parameters = ToElements(parameters) },
			options);

	/// <summary>Asks an icon-provider action declared under <paramref name="localId" /> for its current icon.</summary>
	public Task<CapabilityInvocationOutcome> GetActionIconAsync(
		string localId,
		IReadOnlyDictionary<string, object?>? parameters = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			localId,
			CapabilityOperations.Actions.Icon,
			new ActionExecuteArguments { Parameters = ToElements(parameters) },
			options);

	/// <summary>Asks an icon-provider action declared under <paramref name="localId" /> for the bytes
	/// behind the identity <paramref name="version" /> - normally the <c>Version</c> a prior
	/// <see cref="GetActionIconAsync" /> reply carried.</summary>
	public Task<CapabilityInvocationOutcome> GetActionIconContentAsync(
		string localId,
		string version,
		IReadOnlyDictionary<string, object?>? parameters = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Actions,
			localId,
			CapabilityOperations.Actions.IconContent,
			new ActionIconContentArguments { Parameters = ToElements(parameters), Version = version },
			options);

	private static Dictionary<string, JsonElement> ToElements(IReadOnlyDictionary<string, object?>? parameters)
	{
		var elements = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		if (parameters is null)
		{
			return elements;
		}

		foreach (var (key, value) in parameters)
		{
			elements[key] = JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
		}

		return elements;
	}
}
