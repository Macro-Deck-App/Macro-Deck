using System.Diagnostics;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class ExecuteActionRequestMessageHandler
	: IUiTransportMessageHandler<ExecuteActionRequest, ExecuteActionResponse>
{
	private static readonly TimeSpan _executionTimeout = TimeSpan.FromSeconds(30);

	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IActionInteractions _interactions;
	private readonly IMusicPlayerPollNudge _musicPlayerPollNudge;
	private readonly IHostLockState _lockState;
	private readonly ILogger _logger;

	public ExecuteActionRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		IActionInteractions interactions,
		IMusicPlayerPollNudge musicPlayerPollNudge,
		IHostLockState lockState,
		ILogger logger)
	{
		_integrationRegistry = integrationRegistry;
		_interactions = interactions;
		_musicPlayerPollNudge = musicPlayerPollNudge;
		_lockState = lockState;
		_logger = logger.ForContext<ExecuteActionRequestMessageHandler>();
	}

	public async ValueTask<ExecuteActionResponse> Handle(
		ExecuteActionRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.IntegrationId) || string.IsNullOrWhiteSpace(request.ActionId))
		{
			return Fail("VALIDATION_ERROR", AppStrings.Errors.Integrations.AndActionIdRequired(), Guid.Empty);
		}

		if (_lockState.IsLocked)
		{
			return Fail(ActionExecutionErrorCodes.HostLocked, AppStrings.Errors.Common.HostLocked(), Guid.Empty);
		}

		if (!_integrationRegistry.IsEnabled(request.IntegrationId))
		{
			return Fail("INTEGRATION_DISABLED", AppStrings.Errors.Integrations.Disabled(), Guid.Empty);
		}

		var action = _integrationRegistry.FindAction(request.IntegrationId, request.ActionId);
		if (action is null)
		{
			return Fail("NOT_FOUND", AppStrings.Errors.Actions.NotFound(), Guid.Empty);
		}

		var parameters = ActionParameterLiteralCoercion.Build(action.Parameters, request.Parameters);

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(_executionTimeout);

		var executionId = Guid.NewGuid();
		var stopwatch = Stopwatch.StartNew();
		try
		{
			var executor = action.CreateExecutor();
			var result = await executor.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters,
				OriginClientId = request.ClientId,
				Interactions = _interactions,
				CancellationToken = timeoutSource.Token
			});
			stopwatch.Stop();

			if (result.Status == ActionResultStatus.Failed)
			{
				return Fail(result.ErrorCode ?? "ACTION_FAILED",
					ActionErrorSanitizer.ClampMessage(result.ErrorMessage),
					executionId,
					stopwatch.ElapsedMilliseconds);
			}

			var status = result.Status == ActionResultStatus.Accepted
				? ActionExecutionStatus.Accepted
				: ActionExecutionStatus.Succeeded;
			_musicPlayerPollNudge.NoteActionExecuted(request.IntegrationId);
			return new ExecuteActionResponse
			{
				Success = true,
				DurationMs = stopwatch.ElapsedMilliseconds,
				ExecutionId = executionId.ToString(),
				Status = status
			};
		}
		catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested &&
			!cancellationToken.IsCancellationRequested)
		{
			stopwatch.Stop();
			_logger.Warning("Test run of action {IntegrationId}.{ActionId} timed out after {Timeout}s",
				request.IntegrationId,
				request.ActionId,
				_executionTimeout.TotalSeconds);
			return Fail("TIMEOUT",
				AppStrings.Errors.Actions.ActionTimedOut(seconds: (int)_executionTimeout.TotalSeconds),
				executionId,
				stopwatch.ElapsedMilliseconds);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			_logger.Error(ex,
				"Test run of action {IntegrationId}.{ActionId} failed",
				request.IntegrationId,
				request.ActionId);
			return Fail("EXECUTION_ERROR", ex.Message, executionId, stopwatch.ElapsedMilliseconds);
		}
	}

	private static ExecuteActionResponse Fail(
		string code,
		LocalizedText message,
		Guid executionId,
		long durationMs = 0)
		=> new()
		{
			Success = false,
			DurationMs = durationMs,
			Error = new TransportError { Code = code, Message = message },
			ExecutionId = executionId.ToString(),
			Status = ActionExecutionStatus.Failed
		};
}
