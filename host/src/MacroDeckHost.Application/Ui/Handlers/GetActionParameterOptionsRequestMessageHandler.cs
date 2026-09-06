using System.Text.Json;
using MacroDeck.Localization;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetActionParameterOptionsRequestMessageHandler
	: IUiTransportMessageHandler<GetActionParameterOptionsRequest, GetActionParameterOptionsResponse>
{
	private static readonly TimeSpan _providerTimeout = TimeSpan.FromSeconds(10);

	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IEventRegistry _eventRegistry;
	private readonly Dictionary<string, IHostOptionsSource> _hostOptionsSources;
	private readonly ILogger _logger;

	public GetActionParameterOptionsRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		IEventRegistry eventRegistry,
		IEnumerable<IHostOptionsSource> hostOptionsSources,
		ILogger logger)
	{
		_integrationRegistry = integrationRegistry;
		_eventRegistry = eventRegistry;
		_logger = logger.ForContext<GetActionParameterOptionsRequestMessageHandler>();

		_hostOptionsSources = new Dictionary<string, IHostOptionsSource>(StringComparer.OrdinalIgnoreCase);
		foreach (var source in hostOptionsSources)
		{
			if (!_hostOptionsSources.TryAdd(source.Id, source))
			{
				_logger.Error("Duplicate options source id '{OptionsSourceId}' declared by {SourceType}; ignoring it",
					source.Id,
					source.GetType().FullName);
			}
		}
	}

	public async ValueTask<GetActionParameterOptionsResponse> Handle(
		GetActionParameterOptionsRequest request,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(request.EventId))
		{
			return await HandleEventParameter(request, cancellationToken);
		}

		if (string.IsNullOrWhiteSpace(request.ActionId) && !string.IsNullOrWhiteSpace(request.OptionsSourceId))
		{
			return await HandleHostOptionsSource(request, cancellationToken);
		}

		var action = _integrationRegistry.FindAction(request.IntegrationId, request.ActionId);
		if (action is null)
		{
			return ErrorResponse("ACTION_NOT_FOUND",
				AppStrings.Errors.Actions.ActionNotFound(integrationId: request.IntegrationId,
					actionId: request.ActionId));
		}

		var parameter = FindParameter(action.Parameters, request.ParameterName);
		if (parameter is null)
		{
			return ErrorResponse("PARAMETER_NOT_FOUND",
				AppStrings.Errors.Actions.ParameterNotFoundOnAction(parameterName: request.ParameterName,
					actionId: request.ActionId));
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_providerTimeout);

		try
		{
			var result = await ResolveOptions(action, parameter, request, timeoutCts.Token);
			if (result is null)
			{
				return ErrorResponse("NO_OPTIONS_PROVIDER",
					AppStrings.Errors.Actions.NoDynamicOptionsProvider(parameterName: request.ParameterName));
			}

			if (!result.Error.IsEmpty)
			{
				return ErrorResponse("OPTIONS_UNAVAILABLE", result.Error);
			}

			return Map(RestrictToWidgetTypes(result, parameter.WidgetTypes));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ErrorResponse("OPTIONS_TIMEOUT",
				AppStrings.Errors.Actions.OptionsProviderTimeout(parameterName: request.ParameterName));
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Options provider for {Integration}.{Action}/{Parameter} failed",
				request.IntegrationId,
				request.ActionId,
				request.ParameterName);
			return ErrorResponse("OPTIONS_FAILED", ex.Message);
		}
	}

	private async ValueTask<GetActionParameterOptionsResponse> HandleHostOptionsSource(
		GetActionParameterOptionsRequest request,
		CancellationToken cancellationToken)
	{
		if (!_hostOptionsSources.TryGetValue(request.OptionsSourceId!, out var source))
		{
			return ErrorResponse("OPTIONS_SOURCE_NOT_FOUND",
				AppStrings.Errors.Actions.OptionsSourceNotFound(optionsSourceId: request.OptionsSourceId));
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_providerTimeout);

		try
		{
			var result = await source.GetOptionsAsync(request.Filter, timeoutCts.Token);
			return Map(RestrictToWidgetTypes(result, request.WidgetTypes ?? []));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ErrorResponse("OPTIONS_TIMEOUT",
				AppStrings.Errors.Actions.OptionsSourceTimeout(optionsSourceId: request.OptionsSourceId));
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Options source {OptionsSourceId} failed", request.OptionsSourceId);
			return ErrorResponse("OPTIONS_FAILED", ex.Message);
		}
	}

	/// <summary>
	/// Keeps only the options whose <c>type</c> metadata is one of <paramref name="widgetTypes" />. An
	/// option without that metadata does not come from a widget source, so a declared restriction drops
	/// it rather than letting it through unchecked.
	/// </summary>
	private static DynamicOptionsResult RestrictToWidgetTypes(
		DynamicOptionsResult result,
		IReadOnlyList<string> widgetTypes)
	{
		if (widgetTypes.Count == 0)
		{
			return result;
		}

		var allowed = new HashSet<string>(widgetTypes, StringComparer.OrdinalIgnoreCase);

		return new DynamicOptionsResult
		{
			Options = result.Options
				.Where(option => option.Metadata is not null &&
					option.Metadata.TryGetValue("type", out var type) &&
					allowed.Contains(type))
				.ToList(),
			AllowsCustomValue = result.AllowsCustomValue,
			CacheSeconds = result.CacheSeconds
		};
	}

	private async ValueTask<GetActionParameterOptionsResponse> HandleEventParameter(
		GetActionParameterOptionsRequest request,
		CancellationToken cancellationToken)
	{
		var descriptor = _eventRegistry.Find(request.EventId!);
		if (descriptor is null)
		{
			return ErrorResponse("EVENT_NOT_FOUND", AppStrings.Errors.Events.NotFoundWithId(eventId: request.EventId));
		}

		// Condition authoring resolves options for a *payload* parameter, and an event may declare the
		// same name in both lists with different metadata, so the caller says which list it means.
		var isPayload = string.Equals(request.EventParameterKind,
			EventParameterKinds.Payload,
			StringComparison.OrdinalIgnoreCase);

		var declared = isPayload
			? descriptor.Definition.PayloadParameters
			: descriptor.Definition.ConfigurationParameters;

		var parameter = FindParameter(declared, request.ParameterName);
		if (parameter is null)
		{
			return ErrorResponse("PARAMETER_NOT_FOUND",
				AppStrings.Errors.Actions.ParameterNotFoundOnEvent(parameterName: request.ParameterName,
					eventId: request.EventId));
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_providerTimeout);

		try
		{
			DynamicOptionsResult? result;
			if (parameter.OptionsSourceId is not null)
			{
				if (!_hostOptionsSources.TryGetValue(parameter.OptionsSourceId, out var source))
				{
					throw new InvalidOperationException($"Unknown options source '{parameter.OptionsSourceId}'");
				}

				result = await source.GetOptionsAsync(request.Filter, timeoutCts.Token);
			}
			else if (_eventRegistry.FindProvider(request.EventId!) is IDynamicEventOptionsProvider provider)
			{
				result = await provider.GetEventOptionsAsync(new EventOptionsContext
					{
						EventId = LocalEventId(request.EventId!),
						ParameterName = request.ParameterName,
						Filter = request.Filter,
						CurrentParameters = ConvertParameters(request.CurrentParameters)
					},
					timeoutCts.Token);
			}
			else
			{
				return ErrorResponse("NO_OPTIONS_PROVIDER",
					AppStrings.Errors.Actions.NoDynamicOptionsProvider(parameterName: request.ParameterName));
			}

			if (!result.Error.IsEmpty)
			{
				return ErrorResponse("OPTIONS_UNAVAILABLE", result.Error);
			}

			return Map(DropFilterSentinel(RestrictToWidgetTypes(result, parameter.WidgetTypes), isPayload));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ErrorResponse("OPTIONS_TIMEOUT",
				AppStrings.Errors.Actions.OptionsProviderTimeout(parameterName: request.ParameterName));
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Options provider for event {EventId}/{Parameter} failed",
				request.EventId,
				request.ParameterName);
			return ErrorResponse("OPTIONS_FAILED", ex.Message);
		}
	}

	/// <summary>
	/// Drops an empty-valued option from a payload parameter's list. An options source shared with a
	/// configuration filter may offer one as its "any" affordance - ADB's "Default device" - but an
	/// occurrence always carries a real value, so offering it would author a condition that can never
	/// hold.
	/// </summary>
	private static DynamicOptionsResult DropFilterSentinel(DynamicOptionsResult result, bool isPayload)
	{
		if (!isPayload || !result.Options.Any(option => option.Value.Length == 0))
		{
			return result;
		}

		return new DynamicOptionsResult
		{
			Options = result.Options.Where(option => option.Value.Length > 0).ToList(),
			AllowsCustomValue = result.AllowsCustomValue,
			CacheSeconds = result.CacheSeconds
		};
	}

	private static string LocalEventId(string qualifiedEventId)
		=> QualifiedId.TryParse(qualifiedEventId, out var id) ? id.LocalId : qualifiedEventId;

	private static GetActionParameterOptionsResponse Map(DynamicOptionsResult result)
		=> new()
		{
			Options = result.Options
				.Select(o => new ActionParameterOptionDto
				{
					Value = o.Value,
					Label = o.Label,
					Metadata = o.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value)
				})
				.ToList(),
			AllowsCustomValue = result.AllowsCustomValue,
			CacheSeconds = result.CacheSeconds
		};

	private async Task<DynamicOptionsResult?> ResolveOptions(
		IActionDefinition action,
		ActionParameter parameter,
		GetActionParameterOptionsRequest request,
		CancellationToken cancellationToken)
	{
		if (parameter.OptionsSourceId is not null)
		{
			if (!_hostOptionsSources.TryGetValue(parameter.OptionsSourceId, out var source))
			{
				throw new InvalidOperationException($"Unknown options source '{parameter.OptionsSourceId}'");
			}

			return await source.GetOptionsAsync(request.Filter, cancellationToken);
		}

		if (action is IDynamicOptionsActionDefinition dynamicAction)
		{
			var context = new DynamicOptionsContext
			{
				ParameterName = request.ParameterName,
				Filter = request.Filter,
				CurrentParameters = ConvertParameters(request.CurrentParameters)
			};

			return await dynamicAction.GetDynamicOptionsAsync(context, cancellationToken);
		}

		return null;
	}

	private static ActionParameter? FindParameter(IReadOnlyList<ActionParameter> parameters, string name)
	{
		foreach (var parameter in parameters)
		{
			if (parameter.Name == name)
			{
				return parameter;
			}

			if (parameter.Children is not null)
			{
				var child = FindParameter(parameter.Children, name);
				if (child is not null)
				{
					return child;
				}
			}

			if (parameter.ItemTemplate is not null)
			{
				var item = FindParameter([parameter.ItemTemplate], name);
				if (item is not null)
				{
					return item;
				}
			}
		}

		return null;
	}

	private static Dictionary<string, object?> ConvertParameters(
		Dictionary<string, JsonElement>? parameters)
	{
		if (parameters is null)
		{
			return new Dictionary<string, object?>();
		}

		return parameters.ToDictionary(pair => pair.Key,
			pair => ConvertValue(pair.Value));
	}

	private static object? ConvertValue(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			JsonValueKind.Array => element.EnumerateArray().Select(ConvertValue).ToList(),
			_ => element.GetRawText()
		};
	}

	private static GetActionParameterOptionsResponse ErrorResponse(string code, LocalizedText message)
	{
		return new GetActionParameterOptionsResponse
		{
			Error = new TransportError { Code = code, Message = message }
		};
	}
}
