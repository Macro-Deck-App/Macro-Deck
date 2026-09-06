namespace MacroDeckHost.Domain.Entities;

public class PluginTrustRecordEntity : BaseEntity
{
	public required string PluginId { get; set; }

	public required string Version { get; set; }

	/// <summary>The trust tier this version was admitted at: <c>"Trusted"</c> or <c>"Unsigned"</c>. Nothing
	/// else is ever admitted - a signature failure never reaches installation.</summary>
	public required string AdmittedVerdict { get; set; }

	public string? CertificateId { get; set; }

	public DateTime InstalledAt { get; set; }
}
