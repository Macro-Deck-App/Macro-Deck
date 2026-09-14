namespace MacroDeckHost.Application.Licensing;

public class CompanionLicenseStatus
{
	public bool Licensed { get; set; }

	public string? LicenseId { get; set; }

	public string? Source { get; set; }

	public string? KeyId { get; set; }

	public long? IssuedAt { get; set; }

	public bool IsTest { get; set; }

	public bool TestLicenseStored { get; set; }
}
