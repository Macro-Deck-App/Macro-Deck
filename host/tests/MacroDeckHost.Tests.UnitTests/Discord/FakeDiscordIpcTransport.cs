using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

internal sealed record FakeCommand(string Command, string? EventName, string? Nonce, string Payload);

internal sealed record FakeResponse(string? Data, int ErrorCode = 0, string ErrorMessage = "")
{
	public static FakeResponse Ok(string data = "{}") => new(data);

	public static FakeResponse Error(int code, string message = "rejected") => new(null, code, message);
}

internal sealed class FakeDiscordIpcTransport : IDiscordIpcTransport
{
	private readonly Channel<DiscordIpcFrame> _inbound = Channel.CreateUnbounded<DiscordIpcFrame>();

	public bool IsConnected { get; private set; }

	public string? Endpoint { get; private set; }

	public List<DiscordIpcFrame> Written { get; } = [];

	public List<FakeCommand> Commands { get; } = [];

	public Exception? ConnectException { get; set; }

	public bool AutoReady { get; set; } = true;

	public string ReadyData { get; set; } = """{"v":1,"user":{"id":"1","username":"tester"}}""";

	public Func<FakeCommand, FakeResponse?> Responder { get; set; } = _ => FakeResponse.Ok();

	public Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (ConnectException is not null)
		{
			return Task.FromException(ConnectException);
		}

		IsConnected = true;
		Endpoint = "fake";
		return Task.CompletedTask;
	}

	public Task WriteFrameAsync(
		DiscordRpcOpcode opcode,
		ReadOnlyMemory<byte> payload,
		CancellationToken cancellationToken)
	{
		var bytes = payload.ToArray();
		Written.Add(new DiscordIpcFrame(opcode, bytes));

		switch (opcode)
		{
			case DiscordRpcOpcode.Handshake when AutoReady:
				Push(DiscordRpcOpcode.Frame,
					$$"""{"cmd":"DISPATCH","evt":"READY","data":{{ReadyData}}}""");
				break;

			case DiscordRpcOpcode.Frame:
				Answer(bytes);
				break;

			case DiscordRpcOpcode.Handshake:
			case DiscordRpcOpcode.Close:
			case DiscordRpcOpcode.Ping:
			case DiscordRpcOpcode.Pong:
			default:
				break;
		}

		return Task.CompletedTask;
	}

	public async Task<DiscordIpcFrame?> ReadFrameAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _inbound.Reader.ReadAsync(cancellationToken);
		}
		catch (ChannelClosedException)
		{
			return null;
		}
	}

	public void Dispose()
	{
		IsConnected = false;
		_inbound.Writer.TryComplete();
	}

	public void Push(DiscordRpcOpcode opcode, string json)
		=> _inbound.Writer.TryWrite(new DiscordIpcFrame(opcode, Encoding.UTF8.GetBytes(json)));

	public void PushEvent(string eventName, string dataJson)
		=> Push(DiscordRpcOpcode.Frame, $$"""{"cmd":"DISPATCH","evt":"{{eventName}}","data":{{dataJson}}}""");

	public void Close() => _inbound.Writer.TryComplete();

	public IEnumerable<DiscordIpcFrame> WrittenWithOpcode(DiscordRpcOpcode opcode)
		=> Written.Where(f => f.Opcode == opcode);

	private void Answer(byte[] payload)
	{
		FakeCommand command;
		try
		{
			using var document = JsonDocument.Parse(payload);
			var root = document.RootElement;
			command = new FakeCommand(ReadString(root, "cmd") ?? string.Empty,
				ReadString(root, "evt"),
				ReadString(root, "nonce"),
				Encoding.UTF8.GetString(payload));
		}
		catch (JsonException)
		{
			return;
		}

		Commands.Add(command);

		var response = Responder(command);
		if (response is null || command.Nonce is null)
		{
			return;
		}

		Push(DiscordRpcOpcode.Frame,
			response.Data is not null
				? $$"""{"cmd":"{{command.Command}}","data":{{response.Data}},"nonce":"{{command.Nonce}}"}"""
				: $$"""
					{"cmd":"{{command.Command}}","evt":"ERROR","data":{"code":{{response.ErrorCode}},
					"message":"{{response.ErrorMessage}}"},"nonce":"{{command.Nonce}}"}
					""");
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
