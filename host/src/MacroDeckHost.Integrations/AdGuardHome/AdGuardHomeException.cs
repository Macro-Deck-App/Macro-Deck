using MacroDeckHost.Application.AdGuardHome;

namespace MacroDeckHost.Integrations.AdGuardHome;

internal sealed class AdGuardHomeException : Exception
{
	public AdGuardHomeException(AdGuardHomeConnection failure, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Failure = failure;
	}

	public AdGuardHomeConnection Failure { get; }
}
