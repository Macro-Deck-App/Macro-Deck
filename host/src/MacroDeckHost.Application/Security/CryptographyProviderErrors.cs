using System.Security.Cryptography;

namespace MacroDeckHost.Application.Security;

public static class CryptographyProviderErrors
{
	// EPT_S_NOT_REGISTERED and RPC_S_SERVER_UNAVAILABLE: the CNG Key Isolation service cannot be reached.
	// NCrypt may report them as Win32 codes or as HRESULTs.
	private static readonly HashSet<int> _providerUnreachable =
		[1753, 1722, unchecked((int)0x800706D9), unchecked((int)0x800706BA)];

	public static bool IsProviderUnreachable(Exception exception)
	{
		var pending = new Stack<Exception>([exception]);
		while (pending.TryPop(out var current))
		{
			if (current is CryptographicException && _providerUnreachable.Contains(current.HResult))
			{
				return true;
			}

			if (current is AggregateException aggregate)
			{
				foreach (var inner in aggregate.InnerExceptions)
				{
					pending.Push(inner);
				}
			}
			else if (current.InnerException is { } inner)
			{
				pending.Push(inner);
			}
		}

		return false;
	}
}
