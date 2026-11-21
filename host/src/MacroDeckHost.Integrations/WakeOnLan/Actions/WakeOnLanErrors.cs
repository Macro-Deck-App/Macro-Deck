using System.Net;
using System.Net.Sockets;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.WakeOnLan.Errors;

namespace MacroDeckHost.Integrations.WakeOnLan.Actions;

internal static class WakeOnLanErrors
{
	public static (string Code, LocalizedText Message) Describe(SocketException exception, IPEndPoint target)
	{
		var address = target.Address.ToString();
		return exception.SocketErrorCode switch
		{
			SocketError.AccessDenied => (ActionErrorCodes.PermissionDenied, Strings.SendDenied(address)),
			SocketError.HostUnreachable or SocketError.NetworkUnreachable => (ActionErrorCodes.NotConnected,
				Strings.Unreachable(address)),
			_ => (ActionErrorCodes.NotConnected, Strings.SendFailed(address))
		};
	}
}
