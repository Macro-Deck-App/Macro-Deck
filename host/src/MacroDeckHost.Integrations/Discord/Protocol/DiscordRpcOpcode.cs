namespace MacroDeckHost.Integrations.Discord.Protocol;

internal enum DiscordRpcOpcode
{
	Handshake = 0,
	Frame = 1,
	Close = 2,
	Ping = 3,
	Pong = 4
}
