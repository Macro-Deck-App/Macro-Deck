using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>
/// Answers one <c>host.invoke</c> the same way a minimal but genuine host would: by actually running it
/// against a <see cref="FakeIntegrationContext" />, so <c>context.Variables</c>,
/// <c>context.Config</c> and the rest of <see cref="MacroDeck.Sdk.IIntegrationContext" /> behave
/// normally for a plugin hosted over the wire without every test having to override them by hand.
///
/// <para>
/// A test that wants to assert on the fakes directly should still inject its own
/// <see cref="FakeIntegrationContext" /> through <c>PluginHostBuilder.ConfigureServices</c> - see the
/// package's remarks on overriding <c>IIntegrationContext</c>. This dispatcher exists so a plugin that
/// does not is answered rather than left hanging on a call that never replies.
/// </para>
/// </summary>
internal static class HostInvokeDispatcher
{
	/// <summary>Runs one <c>host.invoke</c> against <paramref name="context" />/<paramref name="interactions" />.
	/// <paramref name="negotiatedVersion" /> is this session's negotiated protocol version - the same
	/// version the plugin under test used to decide which wire shape to send for an operation whose
	/// shape differs by version (currently only <c>widgets/apply</c>).</summary>
	public static async Task<HostInvokeOutcome> DispatchAsync(
		FakeIntegrationContext context,
		FakeActionInteractions interactions,
		HostInvokePayload payload,
		int negotiatedVersion,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(interactions);
		ArgumentNullException.ThrowIfNull(payload);

		if (!HostApis.IsKnown(payload.Api))
		{
			return HostInvokeOutcome.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"'{payload.Api}' is not a host API this test host knows.");
		}

		if (!HostOperations.IsKnown(payload.Api, payload.Operation))
		{
			return HostInvokeOutcome.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The '{payload.Api}' host API has no operation '{payload.Operation}'.");
		}

		try
		{
			return payload.Api switch
			{
				HostApis.Variables => await VariablesAsync(context, payload).ConfigureAwait(false),
				HostApis.UserVariables => await UserVariablesAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				HostApis.Config => await ConfigAsync(context, payload, cancellationToken).ConfigureAwait(false),
				HostApis.Deck => await DeckAsync(context, payload, cancellationToken).ConfigureAwait(false),
				HostApis.Scripts => await ScriptsAsync(context, payload, cancellationToken).ConfigureAwait(false),
				HostApis.Widgets => await WidgetsAsync(context, payload, negotiatedVersion, cancellationToken)
					.ConfigureAwait(false),
				HostApis.Notifications => Notifications(context, payload),
				HostApis.ActionInteractions => ActionInteractions(interactions, payload),
				HostApis.Devices => await DevicesAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				HostApis.VariableValues => await VariableValuesAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				HostApis.Layouts => await LayoutsAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				HostApis.FolderViews => await FolderViewsAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				HostApis.WidgetTypes => await WidgetTypesAsync(context, payload, cancellationToken)
					.ConfigureAwait(false),
				_ => HostInvokeOutcome.Failed(ProtocolErrorCodes.CapabilityUnsupported,
					$"'{payload.Api}' is not a host API this test host knows.")
			};
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return HostInvokeOutcome.Failed(ProtocolErrorCodes.InternalError,
				$"The test host's fake '{payload.Api}' threw: {exception.Message}");
		}
	}

	private static async Task<HostInvokeOutcome> DevicesAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.Devices.Register:
			{
				var arguments = Require<DevicesRegisterArguments>(payload);
				var registration = await context.Devices
					.RegisterDeviceAsync(ToDescriptor(arguments.Device), cancellationToken)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok(new DevicesRegisterResult
				{
					DeviceId = registration.DeviceId, ProviderDeviceId = registration.ProviderDeviceId
				});
			}

			case HostOperations.Devices.Update:
			{
				var arguments = Require<DevicesRegisterArguments>(payload);
				await context.Devices.UpdateDeviceAsync(ToDescriptor(arguments.Device), cancellationToken)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok((JsonElement?)null);
			}

			case HostOperations.Devices.Presence:
			{
				var arguments = Require<DevicesPresenceArguments>(payload);
				await context.Devices
					.SetDevicePresenceAsync(arguments.DeviceId, ToPresence(arguments.Presence), cancellationToken)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok((JsonElement?)null);
			}

			case HostOperations.Devices.Interaction:
			{
				var arguments = Require<DevicesInteractionArguments>(payload);
				if (context.Devices.ResolveSession(arguments.SessionId) is not { } session)
				{
					return UnknownSession();
				}

				var result = await session.SendInteractionAsync(new DeviceInteraction
						{
							Kind = Enum.TryParse<DeviceInteractionKind>(arguments.Kind, ignoreCase: true, out var kind)
								? kind
								: DeviceInteractionKind.Unknown,
							Target = new DeviceInteractionTarget
							{
								WidgetId = arguments.WidgetId, ControlIndex = arguments.ControlIndex
							},
							Value = arguments.Value,
							SurfaceRevision = arguments.SurfaceRevision,
							Data = arguments.Data
						},
						cancellationToken)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok(new DevicesInteractionResult
				{
					Accepted = result.Status != DeviceInteractionStatus.Rejected,
					Unsupported = result.Status == DeviceInteractionStatus.NotSupported,
					ReasonCode = result.ReasonCode
				});
			}

			case HostOperations.Devices.Icon:
			{
				var arguments = Require<DevicesIconArguments>(payload);
				if (context.Devices.ResolveSession(arguments.SessionId) is not { } session)
				{
					return UnknownSession();
				}

				var icon = await session
					.GetIconAsync(arguments.IconId, arguments.Size, arguments.KnownETag, cancellationToken)
					.ConfigureAwait(false);

				if (icon is null)
				{
					return HostInvokeOutcome.Failed(ProtocolErrorCodes.InvalidPayload,
						$"No icon '{arguments.IconId}' is available to this device session.");
				}

				// A cache hit costs no bytes and so needs no transfer. Anything else does, and this test
				// host has no host.asset.* channel: reported as the capability being unavailable, exactly
				// as a real host without an asset sender reports it, rather than as a silent empty icon.
				return icon.NotModified
					? HostInvokeOutcome.Ok(new DevicesIconResult
					{
						ContentType = icon.ContentType, ETag = icon.ETag, ByteLength = 0, NotModified = true
					})
					: HostInvokeOutcome.Failed(ProtocolErrorCodes.CapabilityUnavailable,
						"This test host cannot transfer icon bytes to a plugin.");
			}

			case HostOperations.Devices.Close:
			{
				var arguments = Require<DevicesCloseArguments>(payload);
				if (context.Devices.ResolveSession(arguments.SessionId) is not { } session)
				{
					return UnknownSession();
				}

				session.Close();
				return HostInvokeOutcome.Ok((JsonElement?)null);
			}

			default:
			{
				var arguments = Require<DevicesUnregisterArguments>(payload);
				await context.Devices.UnregisterDeviceAsync(arguments.DeviceId, cancellationToken)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok((JsonElement?)null);
			}
		}
	}

	private static async Task<HostInvokeOutcome> FolderViewsAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (string.Equals(payload.Operation, HostOperations.FolderViews.Register, StringComparison.Ordinal))
		{
			var arguments = Require<FolderViewsRegisterArguments>(payload);
			var registration = await context.FolderViews
				.RegisterFolderViewAsync(ToDescriptor(arguments.FolderView), cancellationToken)
				.ConfigureAwait(false);

			return HostInvokeOutcome.Ok(new FolderViewsRegisterResult
			{
				FolderViewId = registration.FolderViewId, ProviderId = registration.ProviderId
			});
		}

		var unregister = Require<FolderViewsUnregisterArguments>(payload);
		await context.FolderViews
			.UnregisterFolderViewAsync(unregister.FolderViewId, cancellationToken)
			.ConfigureAwait(false);

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static FolderViewDescriptor ToDescriptor(FolderViewDescriptorDto dto)
		=> new(dto.Id, dto.Name, dto.Description, dto.Navigation, dto.HasConfiguration, dto.Metadata);

	private static async Task<HostInvokeOutcome> WidgetTypesAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (string.Equals(payload.Operation, HostOperations.WidgetTypes.Register, StringComparison.Ordinal))
		{
			var arguments = Require<WidgetTypesRegisterArguments>(payload);
			var registration = await context.WidgetTypes
				.RegisterWidgetTypeAsync(ToDescriptor(arguments.WidgetType), cancellationToken)
				.ConfigureAwait(false);

			return HostInvokeOutcome.Ok(new WidgetTypesRegisterResult
			{
				WidgetTypeId = registration.WidgetTypeId, ProviderId = registration.ProviderId
			});
		}

		var unregister = Require<WidgetTypesUnregisterArguments>(payload);
		await context.WidgetTypes
			.UnregisterWidgetTypeAsync(unregister.WidgetTypeId, cancellationToken)
			.ConfigureAwait(false);

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static WidgetTypeDescriptor ToDescriptor(WidgetTypeDescriptorDto dto)
		=> new(dto.Id,
			dto.Name,
			dto.Description,
			dto.DefaultData,
			dto.DataSchema,
			dto.HasConfiguration,
			dto.Metadata);

	private static async Task<HostInvokeOutcome> LayoutsAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (string.Equals(payload.Operation, HostOperations.Layouts.Register, StringComparison.Ordinal))
		{
			var arguments = Require<LayoutsRegisterArguments>(payload);
			var registration = await context.Layouts
				.RegisterLayoutAsync(ToDescriptor(arguments.Layout), cancellationToken)
				.ConfigureAwait(false);

			return HostInvokeOutcome.Ok(new LayoutsRegisterResult
			{
				LayoutId = registration.LayoutId, ProviderId = registration.ProviderId
			});
		}

		var unregister = Require<LayoutsUnregisterArguments>(payload);
		await context.Layouts.UnregisterLayoutAsync(unregister.LayoutId, cancellationToken).ConfigureAwait(false);
		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static LayoutDescriptor ToDescriptor(LayoutDescriptorDto dto)
		=> new(dto.Id,
			dto.Name,
			[.. dto.Regions.Select(ToRegion)],
			dto.Capabilities is { } capabilities
				? ToCapabilities(capabilities)
				: null,
			dto.Metadata);

	private static LayoutRegion ToRegion(LayoutRegionDto dto)
		=> new()
		{
			Id = dto.Id,
			Kind = dto.Kind,
			Name = dto.Name,
			Grid = dto.Grid is { } grid ? ToGrid(grid) : null,
			Count = dto.Count,
			Visuals = dto.Visuals is { } visuals ? ToVisuals(visuals) : null,
			Extra = dto.Extra
		};

	private static LayoutGrid ToGrid(LayoutGridDto dto)
		=> new()
		{
			Rows = dto.Rows,
			Columns = dto.Columns,
			IsConfigurable = dto.IsConfigurable,
			MinRows = dto.MinRows,
			MaxRows = dto.MaxRows,
			MinColumns = dto.MinColumns,
			MaxColumns = dto.MaxColumns,
			SupportsRuntimeResize = dto.SupportsRuntimeResize,
			KeySize = dto.KeySize is { } keySize ? new LayoutKeySize(keySize.Width, keySize.Height) : null
		};

	private static LayoutVisualCapabilities ToVisuals(LayoutVisualCapabilitiesDto dto)
		=> new()
		{
			StaticIcons = dto.StaticIcons,
			AnimatedIcons = dto.AnimatedIcons,
			Borders = dto.Borders,
			BackgroundColors = dto.BackgroundColors,
			TextLabels = dto.TextLabels,
			Transparency = dto.Transparency,
			MaxUpdatesPerSecond = dto.MaxUpdatesPerSecond
		};

	private static LayoutCapabilities ToCapabilities(LayoutCapabilitiesDto dto)
		=> new() { Visuals = dto.Visuals is { } visuals ? ToVisuals(visuals) : null, Extra = dto.Extra };

	private static HostInvokeOutcome UnknownSession()
		=> HostInvokeOutcome.Failed(ProtocolErrorCodes.SessionNotFound,
			"No open device session has that id.");

	private static async Task<HostInvokeOutcome> VariableValuesAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (string.Equals(payload.Operation, HostOperations.VariableValues.Value, StringComparison.Ordinal))
		{
			var arguments = Require<VariableValuesValueArguments>(payload);

			var values = arguments.Values
				.Select(value => VariableValue.Of(value.Id, FromDto(value.Reading)))
				.ToList();

			await context.VariableValues.PublishAsync(values, cancellationToken).ConfigureAwait(false);
			return HostInvokeOutcome.Ok((JsonElement?)null);
		}

		await context.VariableValues.InvalidateCatalogAsync(cancellationToken).ConfigureAwait(false);
		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static VariableReading FromDto(VariableReadingDto reading)
		=> new()
		{
			Value = FromDto(reading.Value), Min = reading.Min, Max = reading.Max, Step = reading.Step
		};

	private static object? FromDto(VariableValueDto value)
		=> value.Kind switch
		{
			"text" => value.Text,
			"number" => value.Number,
			"boolean" => value.Boolean,
			_ => null
		};

	private static DeviceDescriptor ToDescriptor(DeviceDescriptorDto dto)
		=> new(dto.Id,
			dto.Name,
			dto.Model,
			dto.Manufacturer,
			dto.LayoutReference,
			dto.Capabilities is { } capabilities
				? new DeviceCapabilities
				{
					KeyCount = capabilities.KeyCount,
					DialCount = capabilities.DialCount,
					DisplayCount = capabilities.DisplayCount,
					SupportsImages = capabilities.SupportsImages,
					SupportsText = capabilities.SupportsText,
					Extra = capabilities.Extra
				}
				: null,
			ToPresence(dto.Presence),
			dto.Metadata);

	private static DevicePresence ToPresence(string? presence)
		=> Enum.TryParse<DevicePresence>(presence, ignoreCase: true, out var parsed)
			? parsed
			: DevicePresence.Unknown;

	private static async Task<HostInvokeOutcome> VariablesAsync(FakeIntegrationContext context,
		HostInvokePayload payload)
	{
		switch (payload.Operation)
		{
			case HostOperations.Variables.List:
				return HostInvokeOutcome.Ok(await context.Variables.GetAllAsync().ConfigureAwait(false));

			case HostOperations.Variables.Get:
			{
				var arguments = Require<VariablesGetArguments>(payload);
				return HostInvokeOutcome.Ok(
					await context.Variables.GetByNameAsync(arguments.Name).ConfigureAwait(false));
			}

			case HostOperations.Variables.Create:
			{
				var arguments = Require<VariablesCreateArguments>(payload);
				var handle = await context.Variables.CreateAsync(arguments.Name,
						Enum.Parse<VariableType>(arguments.Type),
						arguments.InitialValue.HasValue ? (object?)arguments.InitialValue.Value : null,
						arguments.DecimalPlaces,
						arguments.DefinitionId)
					.ConfigureAwait(false);

				return HostInvokeOutcome.Ok(handle);
			}

			case HostOperations.Variables.Set:
			{
				var arguments = Require<VariablesSetArguments>(payload);
				await context.Variables
					.SetValueAsync(arguments.VariableId,
						arguments.Value.HasValue ? (object?)arguments.Value.Value : null)
					.ConfigureAwait(false);
				return HostInvokeOutcome.Ok((JsonElement?)null);
			}

			default:
			{
				var arguments = Require<VariablesDeleteArguments>(payload);
				await context.Variables.DeleteAsync(arguments.VariableId).ConfigureAwait(false);
				return HostInvokeOutcome.Ok((JsonElement?)null);
			}
		}
	}

	private static async Task<HostInvokeOutcome> UserVariablesAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (payload.Operation == HostOperations.UserVariables.Create)
		{
			var create = Require<UserVariablesCreateArguments>(payload);

			var created = await context.UserVariables.CreateAsync(create.Name,
					create.OwnerWidgetId,
					Enum.Parse<VariableType>(create.Type),
					create.InitialValue,
					create.DecimalPlaces,
					cancellationToken)
				.ConfigureAwait(false);

			return HostInvokeOutcome.Ok(created);
		}

		var arguments = Require<UserVariablesApplyArguments>(payload);

		var result = await context.UserVariables.ApplyAsync(arguments.Name,
				arguments.OwnerWidgetId,
				Enum.Parse<UserVariableOperation>(arguments.Operation),
				arguments.Value,
				cancellationToken)
			.ConfigureAwait(false);

		return HostInvokeOutcome.Ok(result);
	}

	private static async Task<HostInvokeOutcome> ConfigAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.Config.Entries:
				return HostInvokeOutcome.Ok(await context.Config.GetEntriesAsync(cancellationToken)
					.ConfigureAwait(false));

			case HostOperations.Config.GetString:
			{
				var arguments = Require<ConfigGetArguments>(payload);

				return HostInvokeOutcome.Ok(await context.Config
					.GetStringAsync(arguments.EntryId, arguments.Key, cancellationToken)
					.ConfigureAwait(false));
			}

			case HostOperations.Config.GetSecret:
			{
				var arguments = Require<ConfigGetArguments>(payload);

				return HostInvokeOutcome.Ok(await context.Config
					.GetSecretAsync(arguments.EntryId, arguments.Key, cancellationToken)
					.ConfigureAwait(false));
			}

			case HostOperations.Config.SetString:
			{
				var arguments = Require<ConfigSetStringArguments>(payload);
				await context.Config
					.SetStringAsync(arguments.EntryId, arguments.Key, arguments.Value, cancellationToken)
					.ConfigureAwait(false);
				return HostInvokeOutcome.Ok((JsonElement?)null);
			}

			default:
			{
				var arguments = Require<ConfigSetSecretArguments>(payload);
				await context.Config
					.SetSecretAsync(arguments.EntryId, arguments.Key, arguments.Value, cancellationToken)
					.ConfigureAwait(false);
				return HostInvokeOutcome.Ok((JsonElement?)null);
			}
		}
	}

	private static async Task<HostInvokeOutcome> DeckAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.Deck.ChangeFolder:
			{
				var arguments = Require<DeckChangeFolderArguments>(payload);
				await context.Deck.ChangeFolderAsync(arguments.FolderId, arguments.OriginClientId, cancellationToken)
					.ConfigureAwait(false);
				break;
			}

			case HostOperations.Deck.ChangeProfile:
			{
				var arguments = Require<DeckChangeProfileArguments>(payload);
				await context.Deck.ChangeProfileAsync(arguments.ProfileId, arguments.OriginClientId, cancellationToken)
					.ConfigureAwait(false);
				break;
			}

			case HostOperations.Deck.Parent:
			{
				var arguments = Require<DeckOriginArguments>(payload);
				await context.Deck.GoToParentAsync(arguments.OriginClientId, cancellationToken).ConfigureAwait(false);
				break;
			}

			default:
			{
				var arguments = Require<DeckOriginArguments>(payload);
				await context.Deck.GoBackAsync(arguments.OriginClientId, cancellationToken).ConfigureAwait(false);
				break;
			}
		}

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static async Task<HostInvokeOutcome> ScriptsAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		var arguments = Require<ScriptsRunArguments>(payload);

		var result = await context.Scripts
			.RunAsync(arguments.ScriptId,
				arguments.Inputs,
				arguments.OriginClientId,
				arguments.OwnerWidgetId,
				cancellationToken)
			.ConfigureAwait(false);

		return HostInvokeOutcome.Ok(result);
	}

	private static async Task<HostInvokeOutcome> WidgetsAsync(
		FakeIntegrationContext context,
		HostInvokePayload payload,
		int negotiatedVersion,
		CancellationToken cancellationToken)
	{
		var request = negotiatedVersion >= 2
			? FromWire(Require<WidgetsApplyArgumentsV2>(payload))
			: FromWire(Require<WidgetsApplyArgumentsV1>(payload));

		var applied = await context.Widgets.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
		return HostInvokeOutcome.Ok(applied);
	}

	private static WidgetAppearanceRequest FromWire(WidgetsApplyArgumentsV1 v1)
		=> new()
		{
			WidgetId = v1.WidgetId,
			Patch = ToPatch(v1.Patch),
#pragma warning disable CS0618 // Mapping the v1 wire shape's legacy selector back into the SDK request - this fake host's own tiny mirror of the real host's wire-compatibility shim.
			State = (WidgetStateSelector)v1.State,
#pragma warning restore CS0618
			ClearProperties =
				[.. v1.ClearProperties.Select(property => (WidgetAppearanceProperty)property)]
		};

	private static WidgetAppearanceRequest FromWire(WidgetsApplyArgumentsV2 v2)
		=> new()
		{
			WidgetId = v2.WidgetId,
			Patch = ToPatch(v2.Patch),
			StateIds = [.. v2.StateIds],
			ClearProperties =
				[.. v2.ClearProperties.Select(property => (WidgetAppearanceProperty)property)]
		};

	private static WidgetAppearancePatch ToPatch(WidgetAppearancePatchDto dto)
		=> new()
		{
			Label = dto.Label,
			BackgroundColor = dto.BackgroundColor,
			LabelColor = dto.LabelColor,
			IconId = dto.IconId,
			IconFit = dto.IconFit,
			IconZoom = dto.IconZoom,
			IconOffsetX = dto.IconOffsetX,
			IconOffsetY = dto.IconOffsetY,
			IconOpacity = dto.IconOpacity,
			FontFaceId = dto.FontFaceId,
			FontSize = dto.FontSize,
			TextAlign = dto.TextAlign,
			LabelPosition = dto.LabelPosition,
			BorderStyle = dto.BorderStyle,
			BorderColor = dto.BorderColor
		};

	private static HostInvokeOutcome Notifications(FakeIntegrationContext context, HostInvokePayload payload)
	{
		if (string.Equals(payload.Operation, HostOperations.Notifications.Notify, StringComparison.Ordinal))
		{
			context.Notifications.Notify(Require<Sdk.Notifications.UserNotificationRequest>(payload));
		}
		else
		{
			var arguments = Require<NotificationsDismissArguments>(payload);
			context.Notifications.Dismiss(arguments.Key);
		}

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static HostInvokeOutcome ActionInteractions(FakeActionInteractions interactions, HostInvokePayload payload)
	{
		if (string.Equals(payload.Operation,
			HostOperations.ActionInteractions.RequestItemPicker,
			StringComparison.Ordinal))
		{
			var arguments = Require<ActionInteractionsRequestItemPickerArguments>(payload);

			interactions.RequestItemPicker(arguments.OriginClientId,
				arguments.InstanceId,
				Enum.Parse<MusicPlayerCatalogItemKind>(arguments.Kind),
				arguments.Prompt);
		}
		else
		{
			var arguments = Require<ActionInteractionsRequestDevicePickerArguments>(payload);

			interactions.RequestDevicePicker(arguments.OriginClientId,
				arguments.InstanceId,
				arguments.StartPlayback,
				arguments.Prompt);
		}

		return HostInvokeOutcome.Ok((JsonElement?)null);
	}

	private static T Require<T>(HostInvokePayload payload)
	{
		if (payload.Arguments is not { } element)
		{
			throw new InvalidOperationException($"'{payload.Api}/{payload.Operation}' requires arguments.");
		}

		return element.Deserialize<T>(PluginProtocolJson.Options) ??
			throw new InvalidOperationException(
				$"'{payload.Api}/{payload.Operation}' arguments did not parse as {typeof(T).Name}.");
	}
}

/// <summary>What a <c>host.invoke</c> produced, before it is written onto a <c>host.result</c> envelope.</summary>
internal readonly struct HostInvokeOutcome
{
	public JsonElement? Data { get; private init; }

	public ProtocolError? Error { get; private init; }

	public static HostInvokeOutcome Ok(JsonElement? data) => new() { Data = data };

	public static HostInvokeOutcome Ok<T>(T value)
		=> new() { Data = JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options) };

	public static HostInvokeOutcome Failed(string code, string message)
		=> new() { Error = new ProtocolError { Code = code, Message = message, Retryable = false } };
}
