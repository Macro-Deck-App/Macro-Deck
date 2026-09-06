using MacroDeckHost.Application.Plugins.Compatibility;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record CompatibilityFindingBody(
	string DiagnosticId,
	string Source,
	string Severity,
	string Subject,
	string Guidance,
	string? DeprecatedIn,
	string? RemovedIn,
	string? Replacement,
	string? MigrationUrl);

public record PluginCompatibilityBody(
	string PluginId,
	string DisplayName,
	string State,
	string UsageSource,
	string? SdkVersion,
	int? NegotiatedProtocolVersion,
	bool UsageTruncated,
	IReadOnlyList<CompatibilityFindingBody> Findings);

public record GetPluginCompatibilityResponse(IReadOnlyList<PluginCompatibilityBody> Plugins);

[ApiController]
[Route("api/plugin-compatibility")]
public class PluginCompatibilityController : ControllerBase
{
	private readonly IPluginCompatibilityService _compatibility;

	public PluginCompatibilityController(IPluginCompatibilityService compatibility)
	{
		_compatibility = compatibility;
	}

	[HttpGet]
	public GetPluginCompatibilityResponse GetAll()
	{
		var plugins = _compatibility.Snapshot()
			.Select(snapshot => new PluginCompatibilityBody(snapshot.PluginId,
				snapshot.DisplayName,
				snapshot.Report.State,
				snapshot.Report.UsageSource,
				snapshot.Report.SdkVersion,
				snapshot.Report.NegotiatedProtocolVersion == 0 ? null : snapshot.Report.NegotiatedProtocolVersion,
				snapshot.Report.UsageTruncated,
				snapshot.Report.Findings.Select(finding => new CompatibilityFindingBody(finding.DiagnosticId,
						finding.Source,
						finding.Severity,
						finding.Subject,
						finding.Guidance,
						finding.DeprecatedIn,
						finding.RemovedIn,
						finding.Replacement,
						finding.MigrationUrl))
					.ToList()))
			.ToList();

		return new GetPluginCompatibilityResponse(plugins);
	}
}
