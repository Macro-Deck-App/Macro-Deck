using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Delegation.Protocol;

internal interface IDelegateClient : IDisposable
{
	Task ProbeAsync(Uri baseUrl, CancellationToken cancellationToken);

	Task<DelegateLoginResult> LoginAsync(Uri baseUrl,
		string username,
		string password,
		CancellationToken cancellationToken);

	Task<DelegateConnectionInfo> GetConnectionInfoAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<DelegateScriptSummary>> GetScriptsAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken);

	Task<DelegateRunResult> RunScriptAsync(Uri baseUrl,
		string token,
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<DelegateSharedVariable>> GetSharedVariablesAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken);

	Task<DelegateWriteResult> SetSharedVariableAsync(Uri baseUrl,
		string token,
		string name,
		string? value,
		CancellationToken cancellationToken);
}

internal sealed record DelegateLoginResult(string AccessToken, TimeSpan ExpiresIn);

internal sealed record DelegateConnectionInfo(string InstanceName);

internal sealed record DelegateScriptSummary(
	string Id,
	string Name,
	IReadOnlyList<ScriptInput> Inputs,
	bool RunsOnWidget = false);

/// <param name="AppliedInputs">
/// The input names the remote reported applying, or null when it did not report the field at all - which
/// is how a remote too old to know about inputs answers.
/// </param>
internal sealed record DelegateRunResult(
	bool Success,
	string? Error,
	string? Status,
	IReadOnlyList<string>? AppliedInputs = null);

internal sealed record DelegateSharedVariable(
	string Name,
	VariableType Type,
	string Value,
	bool Present,
	bool Available,
	bool CanWrite,
	bool CommitOnRelease = false,
	int? DecimalPlaces = null,
	string? Unit = null,
	double? Min = null,
	double? Max = null,
	double? Step = null);

internal sealed record DelegateWriteResult(bool Success, string? ErrorCode);
