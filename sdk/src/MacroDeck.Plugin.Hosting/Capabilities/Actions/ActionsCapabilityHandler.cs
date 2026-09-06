using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Plugin.Hosting.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Actions;

/// <summary>
/// Exposes every registered integration's actions as the <c>actions</c> capability, and runs one when
/// the host invokes it.
///
/// <para>
/// Note the identity shift. In process, an action is identified by (integration id, action id) and two
/// integrations may each declare <c>play</c>. Over the wire the owner is the <em>plugin</em>, so an
/// action's local id has to be unique across every integration in the process. That is checked at
/// build rather than discovered when one of two actions turns out to be unreachable.
/// </para>
/// </summary>
internal sealed class ActionsCapabilityHandler(IEnumerable<IPluginIntegration> integrations) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 3, Maximum = 3 };

	private readonly IReadOnlyList<IPluginIntegration> _integrations = [.. integrations];

	public string Kind => CapabilityKinds.Actions;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=>
		[
			.. _integrations
				.SelectMany(integration => integration.Actions)
				.Where(action => action.RunsHere())
				.Select(action => new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions,
					LocalId = action.Id,
					VersionRange = _version,
					DisplayName = PluginText.ToLiteral(action.Name)
				})
		];

	public async Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		return invocation.Operation switch
		{
			CapabilityOperations.Actions.Describe => Describe(),
			CapabilityOperations.Actions.Execute => await ExecuteAsync(invocation, cancellationToken),
			CapabilityOperations.Actions.Options => await GetOptionsAsync(invocation, cancellationToken),
			CapabilityOperations.Actions.State => await GetActionStateAsync(invocation, cancellationToken),
			CapabilityOperations.Actions.Icon => await GetActionIconAsync(invocation, cancellationToken),
			CapabilityOperations.Actions.IconContent => await GetActionIconContentAsync(invocation, cancellationToken),
			_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The actions capability has no operation '{invocation.Operation}'.")
		};
	}

	/// <summary>
	/// The declared catalogue for every action in the process, keyed the same way
	/// <see cref="DeclareCapabilities" /> keys the plugin's declaration - see the class remarks on why
	/// that has to be a plugin-wide, not per-integration, namespace.
	/// </summary>
	private CapabilityInvocationResult Describe()
	{
		var payload = new ActionCatalogPayload
		{
			Actions =
			[
				.. _integrations
					.SelectMany(integration => integration.Actions)
					.Select(ToDto)
			]
		};

		return CapabilityInvocationResult.Ok(payload);
	}

	private async Task<CapabilityInvocationResult> ExecuteAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var definition = Find(invocation.LocalId);
		if (definition is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No action '{invocation.LocalId}' is registered in this plugin.");
		}

		var arguments = invocation.Arguments?.Deserialize<ActionExecuteArguments>(PluginProtocolJson.Options);

		var hostInvoker = invocation.Services.GetRequiredService<IHostInvoker>();
		// Resolved rather than required: an invocation driven by something other than a built plugin
		// (the contract-test harness) has no logger registered, and a picker call that cannot be logged
		// is still a picker call worth making.
		var interactionsLogger = (invocation.Services.GetService<ILogger>() ?? Serilog.Core.Logger.None)
			.ForContext<RemoteActionInteractions>();

		var context = new ActionExecutionContext
		{
			Parameters = ActionArgumentBinder.Bind(definition.Parameters, SerializeParameters(arguments?.Parameters)),
			CancellationToken = cancellationToken,
			OriginClientId = arguments?.OriginClientId,
			OwnerWidgetId = arguments?.OwnerWidgetId,

			// Built from this invocation's own correlation id, so the host can verify a picker request
			// names a live actions/execute of this same plugin before honouring it - see
			// RemoteActionInteractions and ActionInteractionsRequestItemPickerArguments.ExecuteCorrelationId.
			Interactions = new RemoteActionInteractions(hostInvoker, interactionsLogger, invocation.CorrelationId),

			// Same correlation, same gate: a modal is only opened while this execute is live. The store is
			// resolved rather than required for the same reason the logger above is: an invocation driven
			// by something other than a built plugin has no DI to resolve it from, and a modal that cannot
			// correlate its answer is still a modal worth opening.
			Ui = new RemoteUiInteractions(hostInvoker,
				invocation.Services.GetService<ModalResultStore>() ?? new ModalResultStore(),
				interactionsLogger,
				invocation.CorrelationId)
		};

		var result = await definition.CreateExecutor().ExecuteAsync(context);
		return MapExecuteResult(result);
	}

	private async Task<CapabilityInvocationResult> GetOptionsAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (Find(invocation.LocalId) is not IDynamicOptionsActionDefinition definition)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Action '{invocation.LocalId}' does not support dynamic options.");
		}

		var arguments = invocation.Arguments?.Deserialize<DynamicOptionsArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The options operation requires arguments.");
		}

		var context = new DynamicOptionsContext
		{
			ParameterName = arguments.ParameterName,
			Filter = arguments.Filter,
			CurrentParameters = ToObjectDictionary(arguments.CurrentParameters, definition)
		};

		var result = await definition.GetDynamicOptionsAsync(context, cancellationToken);

		return CapabilityInvocationResult.Ok(new DynamicOptionsResultDto
		{
			Options = [.. result.Options.Select(ToDto)],
			AllowsCustomValue = result.AllowsCustomValue,
			CacheSeconds = result.CacheSeconds,
			Error = PluginText.ToWireOrNull(result.Error)
		});
	}

	private async Task<CapabilityInvocationResult> GetActionStateAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (Find(invocation.LocalId) is not IStateProviderActionDefinition definition)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Action '{invocation.LocalId}' does not provide state.");
		}

		var arguments = invocation.Arguments?.Deserialize<ActionExecuteArguments>(PluginProtocolJson.Options);
		var declaration = (IActionDefinition)definition;
		var bound = ActionArgumentBinder.Bind(declaration.Parameters, SerializeParameters(arguments?.Parameters));
		var parameters = bound.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);

		var state = await definition.GetActionStateAsync(parameters, cancellationToken);

		return CapabilityInvocationResult.Ok(state is null
			? new ActionStateResult { HasValue = false }
			: new ActionStateResult
			{
				HasValue = true,
				States =
				[
					.. state.States.Select(s => new ActionStateDefinitionDto
					{
						Id = s.Id,
						Label = PluginText.ToWire(s.Label),
						DefaultAppearance = s.DefaultAppearance is { } appearance
							? new ActionStateAppearanceDto
							{
								Label = appearance.Label,
								BackgroundColor = appearance.BackgroundColor,
								LabelColor = appearance.LabelColor,
								IconId = appearance.IconId
							}
							: null
					})
				],
				ActiveStateId = state.ActiveStateId
			});
	}

	private async Task<CapabilityInvocationResult> GetActionIconAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (Find(invocation.LocalId) is not IIconProviderActionDefinition definition)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Action '{invocation.LocalId}' does not provide an icon.");
		}

		var arguments = invocation.Arguments?.Deserialize<ActionExecuteArguments>(PluginProtocolJson.Options);
		var declaration = (IActionDefinition)definition;
		var bound = ActionArgumentBinder.Bind(declaration.Parameters, SerializeParameters(arguments?.Parameters));
		var parameters = bound.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);

		var snapshot = await definition.GetActionIconAsync(parameters, cancellationToken);

		return CapabilityInvocationResult.Ok(snapshot is null
			? new ActionIconResult { HasValue = false }
			: new ActionIconResult
			{
				HasValue = true,
				Version = snapshot.Version,
				Reference = snapshot.Reference is { } reference
					? new ActionIconReferenceDto { Type = reference.Type, Reference = reference.Reference }
					: null,
				MediaType = snapshot.MediaType,
				NoIcon = snapshot.NoIcon
			});
	}

	/// <summary>
	/// Bytes never ride a capability reply (<see cref="ProtocolLimits.MaxMessageBytes" /> is 256 KiB
	/// and an icon is not), so this mirrors <c>MusicPlayerCapabilityHandler.ArtworkAsync</c>'s
	/// over-threshold path: reject an oversized icon outright, otherwise upload it through the
	/// <c>asset.*</c> pipeline and reply with the content hash the host now holds.
	/// </summary>
	private async Task<CapabilityInvocationResult> GetActionIconContentAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (Find(invocation.LocalId) is not IIconProviderActionDefinition definition)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Action '{invocation.LocalId}' does not provide an icon.");
		}

		var arguments = invocation.Arguments?.Deserialize<ActionIconContentArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The icon.content operation requires arguments.");
		}

		var declaration = (IActionDefinition)definition;
		var bound = ActionArgumentBinder.Bind(declaration.Parameters, SerializeParameters(arguments.Parameters));
		var parameters = bound.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);

		var content = await definition.GetActionIconContentAsync(parameters, arguments.Version, cancellationToken);
		if (content is null)
		{
			return CapabilityInvocationResult.Ok(new ActionIconContentResult { HasValue = false });
		}

		if (content.Data.Length > ProtocolLimits.MaxAssetBytes)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.AssetTooLarge,
				$"Action '{invocation.LocalId}' returned an icon of {content.Data.Length} bytes, over the {ProtocolLimits.MaxAssetBytes} byte asset limit.");
		}

		var assetUploader = invocation.Services.GetRequiredService<IPluginAssetUploader>();

		try
		{
			var contentHash = await assetUploader
				.UploadAsync(AssetKinds.ActionIcon, content.MediaType, content.Data, cancellationToken)
				.ConfigureAwait(false);

			return CapabilityInvocationResult.Ok(new ActionIconContentResult
				{ HasValue = true, ContentHash = contentHash, MediaType = content.MediaType });
		}
		catch (AssetUploadException exception)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Uploading the icon for action '{invocation.LocalId}' failed: {exception.Message}");
		}
	}

	private IActionDefinition? Find(string localId)
		=> _integrations
			.SelectMany(integration => integration.Actions)
			.Where(action => action.RunsHere())
			.FirstOrDefault(action => string.Equals(action.Id, localId, StringComparison.Ordinal));

	private static ActionDescriptorDto ToDto(IActionDefinition action)
		=> new()
		{
			LocalId = action.Id,
			Name = PluginText.ToWire(action.Name),
			Description = PluginText.ToWire(action.Description),
			Parameters = [.. action.Parameters.Select(ActionParameterMapper.ToDto)],
			DescriptiveUiSchema = (action as IConfigurableActionDefinition)?.DescriptiveUiSchema,
			SupportsDynamicOptions = action is IDynamicOptionsActionDefinition,
			ProvidesState = action is IStateProviderActionDefinition,
			ConfiguresWithUiTree = action is IUiConfigurableActionDefinition,
			ProvidesIcon = action is IIconProviderActionDefinition
		};

	private static ActionParameterOptionDto ToDto(ActionParameterOption option)
		=> new() { Value = option.Value, Label = PluginText.ToWireOrNull(option.Label), Metadata = option.Metadata };

	/// <summary>Reconstructs a <see cref="JsonElement" /> object from a wire dictionary, so the shared
	/// <see cref="ActionArgumentBinder" /> can bind it exactly as it binds an <c>execute</c> payload.</summary>
	private static JsonElement? SerializeParameters(IReadOnlyDictionary<string, JsonElement>? parameters)
		=> parameters is null
			? null
			: JsonSerializer.SerializeToElement(parameters, PluginProtocolJson.Options);

	private static Dictionary<string, object?> ToObjectDictionary(
		IReadOnlyDictionary<string, JsonElement> parameters,
		IActionDefinition definition)
	{
		var bound = ActionArgumentBinder.Bind(definition.Parameters, SerializeParameters(parameters));
		return bound.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
	}

	private static CapabilityInvocationResult MapExecuteResult(ActionResult result)
	{
		switch (result.Status)
		{
			case ActionResultStatus.Succeeded:
				return CapabilityInvocationResult.Ok(new ActionExecuteResult
					{ ExpectedStateId = result.ExpectedStateId });

			case ActionResultStatus.Accepted:
				// Accepted is a success the provider has not confirmed. It stays a success on the wire
				// - the action did what it could - with the caveat carried in the data so a caller
				// that cares can tell the two apart.
				return CapabilityInvocationResult.Ok(new ActionExecuteResult
				{
					Accepted = true,
					Message = PluginText.ToWireOrNull(result.Message),
					ExpectedStateId = result.ExpectedStateId
				});

			case ActionResultStatus.Failed:
			default:
				return CapabilityInvocationResult.Failed(result.ErrorCode ?? ActionErrorCodes.ProviderError,
					result.ErrorMessage.IsEmpty
						? "The action failed without saying why."
						: PluginText.ToLiteral(result.ErrorMessage));
		}
	}
}
