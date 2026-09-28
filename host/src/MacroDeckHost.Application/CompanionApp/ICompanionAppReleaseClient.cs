namespace MacroDeckHost.Application.CompanionApp;

public interface ICompanionAppReleaseClient
{
	Task<CompanionAppRelease?> GetLatestAsync(CancellationToken cancellationToken);

	Task<CompanionApkDownloadOutcome> DownloadAsync(CompanionAppRelease release,
		string destinationPath,
		CancellationToken cancellationToken);
}

public enum CompanionApkDownloadOutcome
{
	Downloaded,
	DownloadFailed,
	VerificationFailed
}
