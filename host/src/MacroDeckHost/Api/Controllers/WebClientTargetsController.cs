using MacroDeck.Localization;
using MacroDeckHost.Application.ClientTargets;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record AdvanceWebClientTargetProvisioningBody(
	string StepId,
	IReadOnlyDictionary<string, string>? Input);

public class WebClientTargetDto
{
	public string TargetId { get; set; } = string.Empty;

	public bool CanProvision { get; set; }
}

public class WebClientTargetProvisioningValueDto
{
	public LocalizedText Label { get; set; }

	public string Value { get; set; } = string.Empty;
}

public class WebClientTargetProvisioningLinkDto
{
	public LocalizedText Label { get; set; }

	public string Url { get; set; } = string.Empty;
}

public class WebClientTargetProvisioningChoiceDto
{
	public string Value { get; set; } = string.Empty;

	public LocalizedText Label { get; set; }
}

public class WebClientTargetProvisioningFieldDto
{
	public string FieldId { get; set; } = string.Empty;

	public LocalizedText Label { get; set; }

	public LocalizedText Description { get; set; }

	public string DefaultValue { get; set; } = string.Empty;

	public IReadOnlyList<WebClientTargetProvisioningChoiceDto>? Choices { get; set; }
}

public class WebClientTargetProvisioningStepDto
{
	public string StepId { get; set; } = string.Empty;

	public LocalizedText Title { get; set; }

	public LocalizedText Description { get; set; }

	public IReadOnlyList<LocalizedText> Instructions { get; set; } = [];

	public IReadOnlyList<WebClientTargetProvisioningValueDto> Values { get; set; } = [];

	public IReadOnlyList<WebClientTargetProvisioningLinkDto> Links { get; set; } = [];

	public IReadOnlyList<WebClientTargetProvisioningFieldDto> Fields { get; set; } = [];

	public bool CanContinue { get; set; }
}

public class WebClientTargetProvisioningResultDto
{
	public string Kind { get; set; } = string.Empty;

	public WebClientTargetProvisioningStepDto? Step { get; set; }

	public LocalizedText Message { get; set; }
}

/// <summary>
/// Device provisioning for the packaged Web Client targets (issue #727).
///
/// Loopback-only, for the same reason first-run setup is (ADR 0003): these actions drive a physical
/// device attached to this machine and write to its filesystem, which is not something a client on
/// the network gets to ask for. The check is an explicit refusal rather than an authorization policy
/// because it has to hold even for an authenticated admin token issued to a remote client.
/// </summary>
[ApiController]
[Route("api/client-targets")]
public class WebClientTargetsController : ControllerBase
{
	private readonly WebClientTargetProvisioningRegistry _registry;

	public WebClientTargetsController(WebClientTargetProvisioningRegistry registry)
	{
		_registry = registry;
	}

	[HttpGet]
	public async Task<IActionResult> GetTargets(CancellationToken cancellationToken)
	{
		if (RefuseUnlessDesktop() is { } refusal)
		{
			return refusal;
		}

		var targets = new List<WebClientTargetDto>();
		foreach (var targetId in _registry.TargetIds.OrderBy(id => id, StringComparer.Ordinal))
		{
			var provisioner = _registry.Resolve(targetId)!;
			targets.Add(new WebClientTargetDto
			{
				TargetId = targetId,
				CanProvision = await provisioner.IsAvailableAsync(cancellationToken)
			});
		}

		return Ok(targets);
	}

	[HttpPost("{targetId}/provisioning/start")]
	public async Task<IActionResult> StartProvisioning(string targetId, CancellationToken cancellationToken)
	{
		if (RefuseUnlessDesktop() is { } refusal)
		{
			return refusal;
		}

		if (_registry.Resolve(targetId) is not { } provisioner)
		{
			return NotFound();
		}

		return Ok(ToDto(await provisioner.StartAsync(cancellationToken)));
	}

	[HttpPost("{targetId}/provisioning/advance")]
	public async Task<IActionResult> AdvanceProvisioning(
		string targetId,
		AdvanceWebClientTargetProvisioningBody body,
		CancellationToken cancellationToken)
	{
		if (RefuseUnlessDesktop() is { } refusal)
		{
			return refusal;
		}

		if (_registry.Resolve(targetId) is not { } provisioner)
		{
			return NotFound();
		}

		var input = body.Input ?? new Dictionary<string, string>();
		var result = await provisioner.AdvanceAsync(body.StepId, input, cancellationToken);
		return Ok(ToDto(result));
	}

	private ObjectResult? RefuseUnlessDesktop()
		=> LoopbackConnection.IsTrusted(HttpContext)
			? null
			: Problem(statusCode: StatusCodes.Status403Forbidden,
				title: "Device setup is only allowed from the desktop app.");

	private static WebClientTargetProvisioningResultDto ToDto(WebClientTargetProvisioningResult result)
		=> new()
		{
			Kind = result.Kind.ToString(),
			Step = result.Step is null ? null : ToDto(result.Step),
			Message = result.Message
		};

	private static WebClientTargetProvisioningStepDto ToDto(WebClientTargetProvisioningStep step)
		=> new()
		{
			StepId = step.StepId,
			Title = step.Title,
			Description = step.Description,
			Instructions = step.Instructions,
			Values = step.Values
				.Select(value => new WebClientTargetProvisioningValueDto { Label = value.Label, Value = value.Value })
				.ToList(),
			Links = step.Links
				.Select(link => new WebClientTargetProvisioningLinkDto { Label = link.Label, Url = link.Url })
				.ToList(),
			Fields = step.Fields
				.Select(field => new WebClientTargetProvisioningFieldDto
				{
					FieldId = field.FieldId,
					Label = field.Label,
					Description = field.Description,
					DefaultValue = field.DefaultValue,
					Choices = field.Choices?
						.Select(choice => new WebClientTargetProvisioningChoiceDto
						{
							Value = choice.Value,
							Label = choice.Label
						})
						.ToList()
				})
				.ToList(),
			CanContinue = step.CanContinue
		};
}
