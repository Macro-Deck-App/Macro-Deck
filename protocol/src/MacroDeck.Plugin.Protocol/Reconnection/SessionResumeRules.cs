namespace MacroDeck.Plugin.Protocol.Reconnection;

/// <summary>
/// Pure resolution of resume-versus-replace. A new connection presenting <c>resumeSessionId</c> inside
/// the resume window is a resume; one without it replaces the old session (close <c>4000</c>) - which
/// is also what makes <c>instanceId</c> meaningful under <c>MaxSessionsPerPlugin = 1</c>.
/// </summary>
public static class SessionResumeRules
{
	public static bool IsResumeAttempt(string? resumeSessionId) => !string.IsNullOrEmpty(resumeSessionId);

	public static bool CanResume(string? resumeSessionId, bool sessionExists, bool withinResumeWindow)
		=> IsResumeAttempt(resumeSessionId) && sessionExists && withinResumeWindow;
}
