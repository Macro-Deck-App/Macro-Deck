using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SubmitConfigFlowStepRequestMessageHandler
	: IUiTransportMessageHandler<SubmitConfigFlowStepRequest, SubmitConfigFlowStepResponse>
{
	private readonly IConfigFlowManager _manager;

	public SubmitConfigFlowStepRequestMessageHandler(IConfigFlowManager manager)
	{
		_manager = manager;
	}

	public async ValueTask<SubmitConfigFlowStepResponse> Handle(
		SubmitConfigFlowStepRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FlowId, out var flowId))
		{
			return FlowNotFound();
		}

		var outcome = await _manager.SubmitAsync(flowId,
			request.StepId,
			request.Values,
			request.ClearedSecretFields,
			cancellationToken);

		if (!outcome.FlowFound)
		{
			return FlowNotFound();
		}

		return new SubmitConfigFlowStepResponse
		{
			Kind = MapKind(outcome.Kind),
			Step = outcome.Step is null ? null : ConfigFlowStepMapper.Map(outcome.Step),
			Message = outcome.Message,
			FieldErrors = outcome.FieldErrors is null
				? null
				: new Dictionary<string, LocalizedText>(outcome.FieldErrors),
			EntryId = outcome.EntryId?.ToString(),
			ExternalUrl = outcome.ExternalUrl,
			ResumeStepId = outcome.ResumeStepId
		};
	}

	private static SubmitConfigFlowStepResponse FlowNotFound()
		=> new()
		{
			Kind = ConfigFlowOutcomeKind.Error,
			Error = new TransportError
			{
				Code = "FLOW_NOT_FOUND",
				Message = AppStrings.Errors.Config.FlowExpired()
			}
		};

	private static ConfigFlowOutcomeKind MapKind(ConfigFlowResultKind kind)
		=> kind switch
		{
			ConfigFlowResultKind.Step => ConfigFlowOutcomeKind.Step,
			ConfigFlowResultKind.Error => ConfigFlowOutcomeKind.Error,
			ConfigFlowResultKind.Complete => ConfigFlowOutcomeKind.Complete,
			ConfigFlowResultKind.External => ConfigFlowOutcomeKind.External,
			_ => ConfigFlowOutcomeKind.Error
		};
}
