using MacroDeck.Sdk.Scripts;

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
