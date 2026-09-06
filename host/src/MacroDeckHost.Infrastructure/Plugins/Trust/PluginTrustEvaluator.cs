using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Packages;
using MacroDeckHost.Application.Plugins.Trust;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

public sealed class PluginTrustEvaluator : IPluginTrustEvaluator
{
	private readonly IPluginManifestReader _manifestReader;
	private readonly IPluginRevocationSource _revocationSource;
	private readonly PluginTrustOptions _options;
	private readonly ILogger _logger;

	public PluginTrustEvaluator(IPluginManifestReader manifestReader,
		IPluginRevocationSource revocationSource,
		PluginTrustOptions options,
		ILogger logger)
	{
		_manifestReader = manifestReader;
		_revocationSource = revocationSource;
		_options = options;
		_logger = logger.ForContext<PluginTrustEvaluator>();
	}

	public Task<PluginTrustResult> EvaluateInstalledAsync(string versionDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);

		return EvaluateCoreAsync(versionDirectory,
			ct => PackageVerifier.VerifyExtractedAsync(versionDirectory,
				SignablePackageFormat.Plugin,
				_manifestReader,
				_options.RootPublicKeyOverride,
				ct),
			cancellationToken);
	}

	private async Task<PluginTrustResult> EvaluateCoreAsync(string subject,
		Func<CancellationToken, Task<PackageVerifyResult>> verify,
		CancellationToken cancellationToken)
	{
		var verified = await verify(cancellationToken);

		if (!verified.Success)
		{
			var verdict = verified.Error!.Value.ToVerdict();
			PluginTrustLog.Evaluated(_logger, subject, verdict, certificateId: null);
			return PluginTrustResult.Of(verdict, certificateId: null, verified.Message);
		}

		var certificateId = verified.CertificateId!;
		var revocation = await CheckRevocation(certificateId, cancellationToken);

		// Unavailable never blocks (decision: no revocation feed ships yet, and failing closed on an
		// absent feed would refuse every signed plugin on every machine). Only an explicit Revoked answer
		// downgrades the verdict.
		var trustVerdict = revocation.Status == PluginRevocationStatus.Revoked
			? PluginTrustVerdict.Revoked
			: PluginTrustVerdict.Trusted;

		var result = PluginTrustResult.Of(trustVerdict, certificateId, revocation.Message, revocation.Status);
		PluginTrustLog.Evaluated(_logger, subject, trustVerdict, certificateId);
		return result;
	}

	private async Task<PluginRevocationResult> CheckRevocation(string certificateId,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _revocationSource.CheckAsync(certificateId, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			PluginTrustLog.RevocationCheckFailed(_logger, certificateId, ex);
			return new PluginRevocationResult(PluginRevocationStatus.Unavailable, null);
		}
	}
}
