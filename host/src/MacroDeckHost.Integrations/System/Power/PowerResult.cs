using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.System.Power;

public readonly record struct PowerResult(bool Success, LocalizedText? FailureReason, string? ErrorCode = null)
{
	public static PowerResult Succeeded()
	{
		return new PowerResult(true, null);
	}

	public static PowerResult Failed(LocalizedText reason, string? errorCode = null)
	{
		return new PowerResult(false, reason, errorCode);
	}
}
