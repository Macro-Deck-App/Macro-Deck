namespace MacroDeckHost.Integrations.Meld.Protocol;

internal static class QWebChannelMessageTypes
{
	public const int Signal = 1;
	public const int PropertyUpdate = 2;
	public const int Init = 3;
	public const int Idle = 4;
	public const int Debug = 5;
	public const int InvokeMethod = 6;
	public const int ConnectToSignal = 7;
	public const int DisconnectFromSignal = 8;
	public const int SetProperty = 9;
	public const int Response = 10;
}
