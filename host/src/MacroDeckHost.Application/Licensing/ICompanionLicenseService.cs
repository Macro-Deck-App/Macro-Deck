using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;

namespace MacroDeckHost.Application.Licensing;

public interface ICompanionLicenseService
{
	Task<SyncCompanionLicenseResponse> SyncAsync(string connectionId,
		SyncCompanionLicenseRequest request,
		CancellationToken cancellationToken);

	Task<CompanionLicenseStatus> GetStatusAsync(CancellationToken cancellationToken);

	Task<CompanionLicenseStatus?> IssueTestLicenseAsync(CancellationToken cancellationToken);

	Task<CompanionLicenseStatus> RevokeTestLicenseAsync(CancellationToken cancellationToken);
}
