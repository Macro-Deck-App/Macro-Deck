namespace MacroDeckHost.Application.Plugins.Trust;

public sealed record PluginTrustResult
{
	public required PluginTrustVerdict Verdict { get; init; }

	public string? CertificateId { get; init; }

	public string? Message { get; init; }

	public PluginRevocationStatus Revocation { get; init; } = PluginRevocationStatus.Unavailable;

	public bool IsTrusted => Verdict == PluginTrustVerdict.Trusted;

	public static PluginTrustResult Of(PluginTrustVerdict verdict,
		string? certificateId = null,
		string? message = null,
		PluginRevocationStatus revocation = PluginRevocationStatus.Unavailable)
	{
		return new PluginTrustResult
		{
			Verdict = verdict,
			CertificateId = certificateId,
			Message = message,
			Revocation = revocation
		};
	}
}

/// <summary>Verifies an on-disk, already-extracted plugin tree - whether it is staged under a temporary
/// install directory or already promoted to <c>plugins/&lt;id&gt;/versions/&lt;version&gt;/</c>. Installation
/// and launch both evaluate the identical extracted bytes through this one method, so the two can never
/// disagree about what "trusted" means.</summary>
public interface IPluginTrustEvaluator
{
	Task<PluginTrustResult> EvaluateInstalledAsync(string versionDirectory,
		CancellationToken cancellationToken = default);
}
