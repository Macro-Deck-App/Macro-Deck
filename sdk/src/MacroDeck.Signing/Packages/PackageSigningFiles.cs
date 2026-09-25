using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

internal static class PackageSigningFiles
{
	public static bool IsSignatureMaterial(string entryFullName) =>
		entryFullName is PluginArtifactFiles.CertificateFileName
			or PluginArtifactFiles.CertificateSignatureFileName
			or PluginArtifactFiles.IssuerCertificateFileName
			or PluginArtifactFiles.IssuerCertificateSignatureFileName;
}
