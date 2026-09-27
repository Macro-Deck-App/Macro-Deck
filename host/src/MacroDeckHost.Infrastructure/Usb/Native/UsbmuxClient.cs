using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed record UsbmuxDeviceEvent(bool Attached, int DeviceId, string? SerialNumber, bool Usb);

// usbmuxd's plist protocol: a 16 byte little-endian header (length, version 1, message 8, tag) and an
// XML property list. A successful Connect turns the socket into a raw tunnel to the device port.
internal static class UsbmuxProtocol
{
	public const int HeaderLength = 16;
	public const uint Version = 1;
	public const uint PlistMessage = 8;
	public const int MaxMessageLength = 1024 * 1024;
	public const ushort CompanionLinkPort = 8197;

	private const string ProgramName = "macro-deck";

	public static byte[] Encode(uint tag, IReadOnlyDictionary<string, object> plist)
	{
		var payload = UsbmuxPlist.Write(plist);
		var message = new byte[HeaderLength + payload.Length];
		BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)message.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), Version);
		BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(8), PlistMessage);
		BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(12), tag);
		payload.CopyTo(message.AsSpan(HeaderLength));
		return message;
	}

	public static Dictionary<string, object> Listen()
		=> Request("Listen");

	public static Dictionary<string, object> Connect(long deviceId, ushort port)
	{
		var request = Request("Connect");
		request["DeviceID"] = deviceId;
		request["PortNumber"] = (long)NetworkOrder(port);
		return request;
	}

	// usbmuxd reads PortNumber as a network byte order 16 bit value stored in a host integer.
	public static ushort NetworkOrder(ushort port) => BinaryPrimitives.ReverseEndianness(port);

	public static UsbmuxDeviceEvent? ParseDeviceEvent(IReadOnlyDictionary<string, object> message)
	{
		var type = message.GetValueOrDefault("MessageType") as string;
		if (type is not ("Attached" or "Detached") || message.GetValueOrDefault("DeviceID") is not long deviceId)
		{
			return null;
		}

		var properties = message.GetValueOrDefault("Properties") as IReadOnlyDictionary<string, object>;
		return new UsbmuxDeviceEvent(type == "Attached",
			(int)deviceId,
			properties?.GetValueOrDefault("SerialNumber") as string,
			properties?.GetValueOrDefault("ConnectionType") as string == "USB");
	}

	public static long? ResultNumber(IReadOnlyDictionary<string, object> message)
		=> message.GetValueOrDefault("MessageType") as string == "Result"
			? message.GetValueOrDefault("Number") as long?
			: null;

	private static Dictionary<string, object> Request(string messageType)
		=> new(StringComparer.Ordinal)
		{
			["MessageType"] = messageType,
			["ClientVersionString"] = ProgramName,
			["ProgName"] = ProgramName,
			["kLibUSBMuxVersion"] = 3L
		};
}

internal sealed class UsbmuxClient
{
	private readonly EndPoint _endpoint;

	public UsbmuxClient(EndPoint endpoint)
	{
		_endpoint = endpoint;
	}

	public static EndPoint DefaultEndpoint => OperatingSystem.IsWindows()
		? new IPEndPoint(IPAddress.Loopback, 27015)
		: new UnixDomainSocketEndPoint("/var/run/usbmuxd");

	public async Task ListenAsync(Action onListening,
		Action<UsbmuxDeviceEvent> onEvent,
		CancellationToken cancellationToken)
	{
		using var socket = await OpenAsync(cancellationToken);
		await SendAsync(socket, 1, UsbmuxProtocol.Listen(), cancellationToken);
		if (UsbmuxProtocol.ResultNumber(await ReceiveAsync(socket, cancellationToken)) != 0)
		{
			throw new IOException("usbmuxd refused to list devices.");
		}

		onListening();

		while (!cancellationToken.IsCancellationRequested)
		{
			if (UsbmuxProtocol.ParseDeviceEvent(await ReceiveAsync(socket, cancellationToken)) is { } deviceEvent)
			{
				onEvent(deviceEvent);
			}
		}
	}

	public async Task<Socket?> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken)
	{
		var socket = await OpenAsync(cancellationToken);
		try
		{
			await SendAsync(socket, 2, UsbmuxProtocol.Connect(deviceId, port), cancellationToken);
			if (UsbmuxProtocol.ResultNumber(await ReceiveAsync(socket, cancellationToken)) == 0)
			{
				return socket;
			}
		}
		catch
		{
			socket.Dispose();
			throw;
		}

		socket.Dispose();
		return null;
	}

	private async Task<Socket> OpenAsync(CancellationToken cancellationToken)
	{
		var socket = _endpoint is UnixDomainSocketEndPoint
			? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
			: new Socket(_endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private static async Task SendAsync(Socket socket,
		uint tag,
		IReadOnlyDictionary<string, object> plist,
		CancellationToken cancellationToken)
	{
		var message = UsbmuxProtocol.Encode(tag, plist);
		var sent = 0;
		while (sent < message.Length)
		{
			sent += await socket.SendAsync(message.AsMemory(sent), SocketFlags.None, cancellationToken);
		}
	}

	private static async Task<Dictionary<string, object>> ReceiveAsync(Socket socket,
		CancellationToken cancellationToken)
	{
		var header = new byte[UsbmuxProtocol.HeaderLength];
		await ReceiveExactlyAsync(socket, header, cancellationToken);
		var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
		if (length < UsbmuxProtocol.HeaderLength || length > UsbmuxProtocol.MaxMessageLength)
		{
			throw new IOException("usbmuxd sent a message with an invalid length.");
		}

		var payload = new byte[length - UsbmuxProtocol.HeaderLength];
		await ReceiveExactlyAsync(socket, payload, cancellationToken);
		return UsbmuxPlist.Read(payload);
	}

	private static async Task ReceiveExactlyAsync(Socket socket, Memory<byte> buffer, CancellationToken cancellationToken)
	{
		while (!buffer.IsEmpty)
		{
			var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
			if (read == 0)
			{
				throw new IOException("usbmuxd closed the connection.");
			}

			buffer = buffer[read..];
		}
	}
}
