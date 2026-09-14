using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;

namespace MacroDeckHost.Application.Licensing;

public interface IPlatformLicenseClient
{
	Task<string?> IssueCompanionLicenseAsync(CompanionLicenseProof proof, CancellationToken cancellationToken);
}
