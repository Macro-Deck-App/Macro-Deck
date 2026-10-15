namespace MacroDeckHost.Integrations.Discord.Protocol;

internal readonly record struct DiscordIpcFrame(DiscordRpcOpcode Opcode, byte[] Payload);
