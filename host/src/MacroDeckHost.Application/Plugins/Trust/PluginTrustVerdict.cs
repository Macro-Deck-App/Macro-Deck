using System.Diagnostics.CodeAnalysis;

namespace MacroDeckHost.Application.Plugins.Trust;

public enum PluginTrustVerdict
{
	// Deliberately NOT member 0: an unmapped or default-initialised value must never read as trusted.
	[SuppressMessage("Naming",
		"CA1720:Identifier contains type name",
		Justification = "'Unsigned' names the trust tier, not the numeric type; renaming it would obscure " +
			"the exact vocabulary the security model is written in.")]
	Unsigned = 1,
	Trusted,
	Malformed,
	SignatureInvalid,
	ContentMismatch,
	UntrustedRoot,
	WrongCertificatePurpose,
	CertificateNotValidAtSignature,
	Revoked,
	RevocationUnavailable,
	VerificationUnavailable
}
