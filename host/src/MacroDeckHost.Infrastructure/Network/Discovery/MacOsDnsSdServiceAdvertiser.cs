using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Infrastructure.Native;
using Serilog;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

[SupportedOSPlatform("macos")]
internal sealed class MacOsDnsSdServiceAdvertiser
	: PerInterfaceServiceAdvertiser<MacOsDnsSdServiceAdvertiser.Registration>
{
	private const string Library = "/usr/lib/libSystem.B.dylib";
	private const int NoError = 0;
	private const short PollIn = 0x1;
	private const short PollErr = 0x8;
	private const short PollHup = 0x10;

	private static readonly Lazy<bool> Loadable = new(() =>
		NativeLibrary.TryLoad(Library, out var handle) &&
		NativeLibrary.TryGetExport(handle, "DNSServiceRegister", out _));

	private readonly ILogger _logger = Log.ForContext<MacOsDnsSdServiceAdvertiser>();
	private readonly HashSet<IntPtr> _failed = [];
	private readonly RegisterReply _reply;

	public MacOsDnsSdServiceAdvertiser()
	{
		_reply = OnReply;
	}

	public override bool IsAvailable => Loadable.Value;

	// Replies are only ever dispatched here, on the caller's thread, so a reference is never deallocated
	// while mDNSResponder's client library is still processing it.
	public override void CheckLiveness()
	{
		foreach (var registration in Registrations)
		{
			var descriptor = DNSServiceRefSockFD(registration.Reference);
			if (descriptor < 0)
			{
				_failed.Add(registration.Reference);
				continue;
			}

			var poll = new PollDescriptor { Descriptor = descriptor, Events = PollIn };
			if (Poll(ref poll, 1, 0) <= 0 || (poll.ReturnedEvents & (PollIn | PollErr | PollHup)) == 0)
			{
				continue;
			}

			if (DNSServiceProcessResult(registration.Reference) != NoError)
			{
				_failed.Add(registration.Reference);
			}
		}
	}

	protected override Registration Register(ServiceAdvertisement service, AdvertisedInterface target)
	{
		var txt = DnsSdTxtRecord.Encode(service.Txt);
		var name = NativeUtf8.Alloc(service.WireName);
		var type = NativeUtf8.Alloc(ServiceAdvertisementPlanner.ServiceType);
		try
		{
			var port = BitConverter.IsLittleEndian
				? BinaryPrimitives.ReverseEndianness((ushort)service.Port)
				: (ushort)service.Port;
			var error = DNSServiceRegister(out var reference,
				0,
				(uint)target.Index,
				name,
				type,
				IntPtr.Zero,
				IntPtr.Zero,
				port,
				(ushort)txt.Length,
				txt,
				_reply,
				IntPtr.Zero);

			return error == NoError
				? new Registration(reference)
				: throw new InvalidOperationException($"DNSServiceRegister failed with error {error}");
		}
		finally
		{
			NativeUtf8.Free(name);
			NativeUtf8.Free(type);
		}
	}

	protected override void Unregister(Registration registration)
	{
		_failed.Remove(registration.Reference);
		DNSServiceRefDeallocate(registration.Reference);
	}

	protected override bool IsAlive(Registration registration) => !_failed.Contains(registration.Reference);

	private void OnReply(IntPtr reference,
		uint flags,
		int errorCode,
		IntPtr name,
		IntPtr registrationType,
		IntPtr domain,
		IntPtr context)
	{
		if (errorCode == NoError)
		{
			_logger.Debug("mDNSResponder registered {Name}", Marshal.PtrToStringUTF8(name));
			return;
		}

		_logger.Warning("mDNSResponder reported error {ErrorCode} for a network discovery registration", errorCode);
		_failed.Add(reference);
	}

	[DllImport(Library)]
	private static extern int DNSServiceRegister(out IntPtr reference,
		uint flags,
		uint interfaceIndex,
		IntPtr name,
		IntPtr registrationType,
		IntPtr domain,
		IntPtr host,
		ushort port,
		ushort txtLength,
		byte[] txtRecord,
		RegisterReply callback,
		IntPtr context);

	[DllImport(Library)]
	private static extern void DNSServiceRefDeallocate(IntPtr reference);

	[DllImport(Library)]
	private static extern int DNSServiceRefSockFD(IntPtr reference);

	[DllImport(Library)]
	private static extern int DNSServiceProcessResult(IntPtr reference);

	[DllImport(Library, EntryPoint = "poll")]
	private static extern int Poll(ref PollDescriptor descriptors, uint count, int timeout);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void RegisterReply(IntPtr reference,
		uint flags,
		int errorCode,
		IntPtr name,
		IntPtr registrationType,
		IntPtr domain,
		IntPtr context);

	[StructLayout(LayoutKind.Sequential)]
	private struct PollDescriptor
	{
		public int Descriptor;
		public short Events;
		public short ReturnedEvents;
	}

	internal sealed record Registration(IntPtr Reference);
}
