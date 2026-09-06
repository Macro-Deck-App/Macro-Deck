using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class ExecuteActionButtonTriggerRequestMessageHandler
	: IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest, ExecuteActionButtonTriggerResponse>
{
	private readonly IFolderCache _folderCache;
	private readonly IProfileRegistry _profileRegistry;
	private readonly IHostLockState _lockState;
	private readonly IWidgetTriggerService _triggerService;
	private readonly IWidgetTypeRegistry _widgetTypes;

	public ExecuteActionButtonTriggerRequestMessageHandler(
		IFolderCache folderCache,
		IProfileRegistry profileRegistry,
		IHostLockState lockState,
		IWidgetTriggerService triggerService,
		IWidgetTypeRegistry widgetTypes)
	{
		_folderCache = folderCache;
		_profileRegistry = profileRegistry;
		_lockState = lockState;
		_triggerService = triggerService;
		_widgetTypes = widgetTypes;
	}

	public async ValueTask<ExecuteActionButtonTriggerResponse> Handle(
		ExecuteActionButtonTriggerRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.TriggerType))
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
					{ Code = "VALIDATION_ERROR", Message = AppStrings.Errors.ActionButtons.TriggerTypeRequired() }
			};
		}

		if (_lockState.IsLocked)
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			};
		}

		if (string.Equals(request.TriggerType, WidgetTriggerTypes.Event, StringComparison.OrdinalIgnoreCase))
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
				{
					Code = "VALIDATION_ERROR",
					Message = AppStrings.Errors.ActionButtons.EventTriggerNotClientExecutable()
				}
			};
		}

		if (_profileRegistry.IsVirtual(request.WidgetId))
		{
			try
			{
				var routed = await _profileRegistry.RouteWidgetInteraction(request.FolderId,
					request.WidgetId,
					new WidgetInteraction(request.TriggerType));

				return routed
					? new ExecuteActionButtonTriggerResponse
						{ Success = true, Status = ActionExecutionStatus.Succeeded }
					: new ExecuteActionButtonTriggerResponse
					{
						Success = false,
						Status = ActionExecutionStatus.Failed,
						Error = new TransportError
							{ Code = "NOT_FOUND", Message = AppStrings.Errors.Widgets.ProviderDidNotHandle() }
					};
			}
			catch (Exception ex)
			{
				var (code, message) = ActionErrorSanitizer.Sanitize(ex);
				return new ExecuteActionButtonTriggerResponse
				{
					Success = false,
					Status = ActionExecutionStatus.Failed,
					Error = new TransportError { Code = code, Message = message }
				};
			}
		}

		if (!Guid.TryParse(request.WidgetId, out var widgetId) || !Guid.TryParse(request.FolderId, out var folderId))
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
					{ Code = "VALIDATION_ERROR", Message = AppStrings.Errors.Widgets.InvalidWidgetOrFolderId() }
			};
		}

		var widget = _folderCache.GetFolderById(folderId)?.Widgets.FirstOrDefault(w => w.Id == widgetId);
		if (widget is null || widget.FolderId != folderId)
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError { Code = "NOT_FOUND", Message = AppStrings.Errors.Widgets.NotFound() }
			};
		}

		// A provider's widget carries no flows for the host to run - its interactions are the events its
		// own tree declares, dispatched over its UI session. A client that cannot tell reaches here
		// anyway: a tile whose tree claims no gesture falls through to this path, and the keyboard and
		// hardware routes have no tree in reach at all. So the press is nothing to do rather than a
		// failure, which is what stops every press of a plugin widget raising an error the user cannot act
		// on.
		if (_widgetTypes.TryResolve(widget.Type, out var entry) && !entry.IsBuiltIn)
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = true, Status = ActionExecutionStatus.Accepted
			};
		}

		if (widget.Type is not (WidgetTypeIds.ActionButton
			or WidgetTypeIds.MusicPlayer
			or WidgetTypeIds.Weather
			or WidgetTypeIds.HistoryGraph
			or WidgetTypeIds.Clock))
		{
			return new ExecuteActionButtonTriggerResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
					{ Code = "VALIDATION_ERROR", Message = AppStrings.Errors.Widgets.UnsupportedActionTriggers() }
			};
		}

		// The implicit short-press state advance and the bounded flow execution are shared with a UI
		// session's own press dispatch through IWidgetTriggerService - see its doc comment for the exact
		// ordering and failure-isolation guarantees.
		var dispatch = await _triggerService
			.ExecuteAsync(widget, request.TriggerType, request.ClientId, request.OriginDeviceId, cancellationToken)
			.ConfigureAwait(false);

		var result = dispatch.Result is { } flowResult
			? ActionExecutionDtoMapper.ToDto(flowResult)
			: new ActionExecutionResultDto
			{
				ExecutionId = dispatch.ExecutionId.ToString(),
				Status = ActionExecutionStatus.Accepted
			};

		return new ExecuteActionButtonTriggerResponse
		{
			Success = ActionExecutionDtoMapper.IsSuccess(result.Status),
			Error = result.Error,
			ExecutionId = result.ExecutionId,
			Status = result.Status,
			DurationMs = result.DurationMs,
			Actions = result.Actions
		};
	}
}
