namespace MacroDeckHost.Infrastructure.Licensing;

internal sealed class CompanionLicenseVerificationUnavailableException : Exception
{
	public CompanionLicenseVerificationUnavailableException(Exception? innerException)
		: base("Companion licenses cannot be verified because the cryptography provider is unavailable.",
			innerException)
	{
	}
}
