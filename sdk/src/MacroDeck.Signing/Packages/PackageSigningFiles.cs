using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

internal static class PackageSigningFiles
{
	// issuer.json and issuer.sig only become signature material for a certificate that names an issuer, so a
	// package signed before issuers existed keeps any declared file of that name as ordinary content.
	public static bool IsSignatureMaterial(string entryFullName, bool withIssuer) =>
		entryFullName is PluginArtifactFiles.CertificateFileName or PluginArtifactFiles.CertificateSignatureFileName ||
		(withIssuer && IsIssuerMaterial(entryFullName));

	public static bool IsIssuerMaterial(string entryFullName) =>
		entryFullName is PluginArtifactFiles.IssuerCertificateFileName
			or PluginArtifactFiles.IssuerCertificateSignatureFileName;
}
