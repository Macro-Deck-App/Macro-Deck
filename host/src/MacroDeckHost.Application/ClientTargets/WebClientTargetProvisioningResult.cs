using MacroDeck.Localization;

namespace MacroDeckHost.Application.ClientTargets;

public enum WebClientTargetProvisioningResultKind
{
	Step,
	Error,
	Complete
}

/// <summary>Outcome of starting or advancing a target's provisioning walkthrough (issue #727).</summary>
public sealed class WebClientTargetProvisioningResult
{
	private WebClientTargetProvisioningResult(WebClientTargetProvisioningResultKind kind)
	{
		Kind = kind;
	}

	public WebClientTargetProvisioningResultKind Kind { get; }

	/// <summary>The step to display. Set for <see cref="WebClientTargetProvisioningResultKind.Step"/> and Error.</summary>
	public WebClientTargetProvisioningStep? Step { get; private init; }

	/// <summary>Why the step could not be completed. Error only.</summary>
	public LocalizedText Message { get; private init; }

	public static WebClientTargetProvisioningResult Next(WebClientTargetProvisioningStep step)
		=> new(WebClientTargetProvisioningResultKind.Step) { Step = step };

	/// <summary>Re-displays <paramref name="step"/> with an explanation of what is still missing.</summary>
	public static WebClientTargetProvisioningResult Failed(WebClientTargetProvisioningStep step, LocalizedText message)
		=> new(WebClientTargetProvisioningResultKind.Error) { Step = step, Message = message };

	public static WebClientTargetProvisioningResult Done(WebClientTargetProvisioningStep step)
		=> new(WebClientTargetProvisioningResultKind.Complete) { Step = step };
}
