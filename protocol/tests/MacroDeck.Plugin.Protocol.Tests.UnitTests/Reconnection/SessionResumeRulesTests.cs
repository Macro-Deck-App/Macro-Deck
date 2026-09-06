using MacroDeck.Plugin.Protocol.Reconnection;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Reconnection;

[TestFixture]
public class SessionResumeRulesTests
{
	[Test]
	public void A_missing_resume_session_id_is_not_a_resume_attempt()
		=> Assert.That(SessionResumeRules.IsResumeAttempt(null), Is.False);

	[Test]
	public void An_empty_resume_session_id_is_not_a_resume_attempt()
		=> Assert.That(SessionResumeRules.IsResumeAttempt(string.Empty), Is.False);

	[Test]
	public void A_present_resume_session_id_is_a_resume_attempt()
		=> Assert.That(SessionResumeRules.IsResumeAttempt("0f8fad5b-d9cb-7f5b-9165-70867728950e"), Is.True);

	[Test]
	public void Can_resume_requires_a_present_id_an_existing_session_and_being_within_the_window()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SessionResumeRules.CanResume("0f8fad5b-d9cb-7f5b-9165-70867728950e",
					sessionExists: true,
					withinResumeWindow: true),
				Is.True);
			Assert.That(SessionResumeRules.CanResume(null, sessionExists: true, withinResumeWindow: true), Is.False);
			Assert.That(SessionResumeRules.CanResume("0f8fad5b-d9cb-7f5b-9165-70867728950e",
					sessionExists: false,
					withinResumeWindow: true),
				Is.False);
			Assert.That(SessionResumeRules.CanResume("0f8fad5b-d9cb-7f5b-9165-70867728950e",
					sessionExists: true,
					withinResumeWindow: false),
				Is.False);
		});
	}

	[Test]
	public void A_connection_without_a_resume_session_id_can_never_resume_which_is_what_makes_it_a_replace()
	{
		// A new connection presenting no resumeSessionId replaces the old session (close 4000).
		// SessionResumeRules only encodes the resume-versus-not precondition; the actual close
		// belongs to #107's transport.
		Assert.Multiple(() =>
		{
			Assert.That(SessionResumeRules.IsResumeAttempt(null), Is.False);
			Assert.That(SessionResumeRules.CanResume(null, sessionExists: true, withinResumeWindow: true), Is.False);
		});
	}

	[Test]
	public void Can_resume_is_false_outside_the_resume_window_even_with_a_valid_id_and_an_existing_session()
		=> Assert.That(SessionResumeRules.CanResume("0f8fad5b-d9cb-7f5b-9165-70867728950e",
				sessionExists: true,
				withinResumeWindow: false),
			Is.False);
}
