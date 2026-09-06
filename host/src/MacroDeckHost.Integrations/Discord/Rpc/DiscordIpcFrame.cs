namespace MacroDeckHost.Integrations.Discord.Rpc;

internal readonly record struct DiscordIpcFrame(DiscordRpcOpcode Opcode, byte[] Payload);
