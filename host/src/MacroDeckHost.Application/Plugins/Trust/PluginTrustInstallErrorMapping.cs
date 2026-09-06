using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeckHost.Application.Plugins.Trust;

public static class PluginTrustInstallErrorMapping
{
	/// <summary>Maps a non-trusted, non-permitted verdict to the <see cref="PluginInstallError" /> the
	/// installer reports for it. Never called for <see cref="PluginTrustVerdict.Trusted" /> or a permitted
	/// <see cref="PluginTrustVerdict.Unsigned" /> - both are successes, not errors.</summary>
	public static PluginInstallError ToInstallError(this PluginTrustVerdict verdict) => verdict switch
	{
		PluginTrustVerdict.Malformed => PluginInstallError.SignatureInvalid,
		PluginTrustVerdict.SignatureInvalid => PluginInstallError.SignatureInvalid,
		PluginTrustVerdict.ContentMismatch => PluginInstallError.SignatureInvalid,

		PluginTrustVerdict.UntrustedRoot => PluginInstallError.SignatureUntrusted,
		PluginTrustVerdict.WrongCertificatePurpose => PluginInstallError.SignatureUntrusted,
		PluginTrustVerdict.CertificateNotValidAtSignature => PluginInstallError.SignatureUntrusted,

		PluginTrustVerdict.Revoked => PluginInstallError.SignatureRevoked,

		PluginTrustVerdict.VerificationUnavailable => PluginInstallError.SignatureUnverifiable,
		PluginTrustVerdict.RevocationUnavailable => PluginInstallError.SignatureUnverifiable,

		PluginTrustVerdict.Unsigned => PluginInstallError.UnsignedNotPermitted,

		_ => PluginInstallError.Failed
	};
}
