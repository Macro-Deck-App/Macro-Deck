using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts;

[TestFixture]
internal sealed class PluginInstallErrorCompatibilityTests
{
	[TestCase(PluginInstallError.ArtifactNotFound, 0)]
	[TestCase(PluginInstallError.ArtifactTooLarge, 1)]
	[TestCase(PluginInstallError.InvalidArchive, 2)]
	[TestCase(PluginInstallError.UnsafeEntry, 3)]
	[TestCase(PluginInstallError.ArtifactLimitExceeded, 4)]
	[TestCase(PluginInstallError.ManifestMissing, 5)]
	[TestCase(PluginInstallError.ManifestInvalid, 6)]
	[TestCase(PluginInstallError.IdMismatch, 7)]
	[TestCase(PluginInstallError.Incompatible, 8)]
	[TestCase(PluginInstallError.HashMismatch, 9)]
	[TestCase(PluginInstallError.SignatureInvalid, 10)]
	[TestCase(PluginInstallError.AlreadyInstalled, 11)]
	[TestCase(PluginInstallError.StagingFailed, 12)]
	[TestCase(PluginInstallError.ActivationFailed, 13)]
	[TestCase(PluginInstallError.HealthValidationFailed, 14)]
	[TestCase(PluginInstallError.DependencyInUse, 15)]
	[TestCase(PluginInstallError.NotInstalled, 16)]
	[TestCase(PluginInstallError.Cancelled, 17)]
	[TestCase(PluginInstallError.Failed, 18)]
	[TestCase(PluginInstallError.SignatureUntrusted, 19)]
	[TestCase(PluginInstallError.SignatureRevoked, 20)]
	[TestCase(PluginInstallError.SignatureUnverifiable, 21)]
	[TestCase(PluginInstallError.UnsignedNotPermitted, 22)]
	[TestCase(PluginInstallError.TrustDowngrade, 23)]
	public void A_consumer_compiled_against_an_older_sdk_still_reads_every_error_it_knows(
		PluginInstallError error,
		int compiledValue)
	{
		Assert.That((int)error, Is.EqualTo(compiledValue));
	}
}
