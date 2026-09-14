using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Licensing;

public class SyncCompanionLicenseResponse
{
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public string? License { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public long? TrialStartedAt { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public IReadOnlyList<string> RevokedLicenseIds { get; set; } = [];
}
