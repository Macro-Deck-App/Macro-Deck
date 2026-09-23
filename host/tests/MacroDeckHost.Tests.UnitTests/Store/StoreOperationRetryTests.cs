using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreOperationRetryTests
{
	[TestCase(StoreOperationError.Incompatible)]
	[TestCase(StoreOperationError.RequiresNewerMacroDeck)]
	[TestCase(StoreOperationError.Unsupported)]
	[TestCase(StoreOperationError.PackageRemoved)]
	[TestCase(StoreOperationError.VersionNotFound)]
	[TestCase(StoreOperationError.UnsignedNotPermitted)]
	[TestCase(StoreOperationError.TrustDowngrade)]
	[TestCase(StoreOperationError.SignatureInvalid)]
	[TestCase(StoreOperationError.SignatureUntrusted)]
	[TestCase(StoreOperationError.MalformedPackage)]
	[TestCase(StoreOperationError.ArtifactTooLarge)]
	[TestCase(StoreOperationError.TestConsentRequired)]
	[TestCase(StoreOperationError.TestBuildMismatch)]
	public void A_failure_that_would_repeat_is_not_offered_for_retry(StoreOperationError error)
	{
		Assert.That(Failed(error).CanRetry, Is.False);
	}

	[TestCase(StoreOperationError.SignatureUnverifiable)]
	[TestCase(StoreOperationError.SignInRequired)]
	[TestCase(StoreOperationError.DownloadFailed)]
	[TestCase(StoreOperationError.ChecksumMismatch)]
	[TestCase(StoreOperationError.SizeMismatch)]
	[TestCase(StoreOperationError.RegistryUnavailable)]
	[TestCase(StoreOperationError.PackageNotFound)]
	[TestCase(StoreOperationError.InstallFailed)]
	[TestCase(StoreOperationError.Interrupted)]
	[TestCase(StoreOperationError.InstallBlockedByTakeover)]
	[TestCase(StoreOperationError.TestBuildUnavailable)]
	public void A_failure_that_trying_again_can_fix_is_offered_for_retry(StoreOperationError error)
	{
		Assert.That(Failed(error).CanRetry, Is.True);
	}

	[Test]
	public void A_cancelled_install_can_be_retried_and_a_completed_one_cannot()
	{
		Assert.Multiple(() =>
		{
			Assert.That((Failed(StoreOperationError.Cancelled) with { State = StoreOperationState.Cancelled }).CanRetry,
				Is.True);
			Assert.That((Failed(StoreOperationError.DownloadFailed) with
				{
					State = StoreOperationState.Completed,
					Error = null
				}).CanRetry,
				Is.False);
		});
	}

	private static StoreOperation Failed(StoreOperationError error) => new()
	{
		Id = Guid.CreateVersion7(),
		Kind = StoreOperationKind.Install,
		ExtensionKind = StoreExtensionKind.Plugin,
		PackageId = "com.acme.soundbox",
		Version = "1.0.0",
		DisplayName = "SoundBox",
		State = StoreOperationState.Failed,
		Error = error
	};
}
