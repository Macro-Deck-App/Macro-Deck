using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public sealed record ConfigFlowStartOutcome(
	bool Supported,
	Guid FlowId,
	ConfigFlowStep? Step,
	string? ErrorMessage = null,
	bool SupportsConfigUi = false,
	int ConfigUiModelVersion = 0,
	IReadOnlyDictionary<string, JsonElement>? InitialValues = null,
	IReadOnlyCollection<string>? StoredSecretFields = null);

public sealed record ConfigFlowSubmitOutcome(
	bool FlowFound,
	ConfigFlowResultKind Kind,
	ConfigFlowStep? Step,
	LocalizedText Message,
	IReadOnlyDictionary<string, LocalizedText>? FieldErrors,
	Guid? EntryId,
	string? ExternalUrl = null,
	string? ResumeStepId = null);

/// <summary>Just enough of a started flow's identity for the UI open path to resolve where its tree
/// lives, without exposing the flow's stored values.</summary>
public sealed record ConfigFlowActiveFlowInfo(string IntegrationId, IConfigFlow Flow);

public interface IConfigFlowManager
{
	Task<ConfigFlowStartOutcome> StartAsync(string integrationId, CancellationToken cancellationToken);

	Task<ConfigFlowStartOutcome> StartAsync(
		string integrationId,
		string? title,
		Guid? entryId,
		CancellationToken cancellationToken);

	bool TryGetActiveFlow(Guid flowId, out ConfigFlowActiveFlowInfo? info);

	Task<ConfigFlowSubmitOutcome> SubmitAsync(
		Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken);

	Task<ConfigFlowSubmitOutcome> SubmitAsync(
		Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		IReadOnlyCollection<string> clearedSecretFields,
		CancellationToken cancellationToken);

	Task AbandonAsync(Guid flowId, CancellationToken cancellationToken);
}
