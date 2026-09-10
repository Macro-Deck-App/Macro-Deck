using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Network.Discovery;
using Serilog;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

[SupportedOSPlatform("windows")]
internal sealed class WindowsDnsServiceAdvertiser
	: PerInterfaceServiceAdvertiser<WindowsDnsServiceAdvertiser.Registration>
{
	private const string Library = "dnsapi.dll";
	private const uint QueryRequestVersion1 = 1;
	private const uint RequestPending = 9506;

	private static readonly TimeSpan DeregisterTimeout = TimeSpan.FromSeconds(2);

	private static readonly Lazy<bool> Loadable = new(() =>
		OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) &&
		NativeLibrary.TryLoad(Library, out var handle) &&
		NativeLibrary.TryGetExport(handle, "DnsServiceRegister", out _));

	private readonly ILogger _logger = Log.ForContext<WindowsDnsServiceAdvertiser>();
	private readonly ConcurrentDictionary<nint, bool> _failed = new();
	private readonly ConcurrentDictionary<nint, Registration> _registering = new();
	private readonly ConcurrentDictionary<nint, Registration> _deregistering = new();
	private readonly ConcurrentDictionary<nint, bool> _abandoned = new();
	private readonly ConcurrentDictionary<uint, bool> _loggedRejections = new();
	private readonly RegisterComplete _complete;
	private long _nextContext;

	public WindowsDnsServiceAdvertiser()
	{
		_complete = OnComplete;
	}

	public override bool IsAvailable => Loadable.Value;

	protected override Registration Register(ServiceAdvertisement service, AdvertisedInterface target)
	{
		var address = BitConverter.ToUInt32(target.Ipv4.GetAddressBytes());
		var instance = DnsServiceConstructInstance(
			$"{service.WireName}.{ServiceAdvertisementPlanner.ServiceType}.local",
			HostName(),
			ref address,
			IntPtr.Zero,
			(ushort)service.Port,
			0,
			0,
			(uint)service.Txt.Count,
			service.Txt.Select(entry => entry.Key).ToArray(),
			service.Txt.Select(entry => entry.Value).ToArray());
		if (instance == IntPtr.Zero)
		{
			throw new InvalidOperationException("DnsServiceConstructInstance failed");
		}

		var context = (nint)Interlocked.Increment(ref _nextContext);
		var request = Marshal.AllocHGlobal(Marshal.SizeOf<RegisterRequest>());
		Marshal.StructureToPtr(new RegisterRequest
			{
				Version = QueryRequestVersion1,
				InterfaceIndex = (uint)target.Index,
				ServiceInstance = instance,
				Callback = Marshal.GetFunctionPointerForDelegate(_complete),
				QueryContext = context
			},
			request,
			false);

		var registration = new Registration(instance, request, context);
		_registering[context] = registration;
		var status = DnsServiceRegister(request, IntPtr.Zero);
		if (status == RequestPending)
		{
			return registration;
		}

		_registering.TryRemove(context, out _);
		DnsServiceFreeInstance(instance);
		Marshal.FreeHGlobal(request);
		throw new InvalidOperationException($"DnsServiceRegister failed with status {status}");
	}

	protected override void Unregister(Registration registration)
	{
		// Windows may use the request and instance until each matching callback runs, so a callback that
		// never arrives leaks them on purpose rather than freeing them under the service.
		if (!registration.Registered.Task.Wait(DeregisterTimeout))
		{
			_abandoned[registration.Context] = true;
			if (!registration.Registered.Task.IsCompleted || !_abandoned.TryRemove(registration.Context, out _))
			{
				_logger.Warning("Windows has not confirmed a network discovery registration yet; it is withdrawn " +
					"as soon as it does");
				return;
			}
		}

		if (_failed.TryRemove(registration.Context, out _))
		{
			DnsServiceFreeInstance(registration.Instance);
			Marshal.FreeHGlobal(registration.Request);
			return;
		}

		_deregistering[registration.Context] = registration;
		var status = DnsServiceDeRegister(registration.Request, IntPtr.Zero);
		if (status == RequestPending && !registration.Deregistered.Task.Wait(DeregisterTimeout))
		{
			_logger.Warning("Windows did not confirm withdrawing a network discovery registration in time");
			return;
		}

		_deregistering.TryRemove(registration.Context, out _);
		DnsServiceFreeInstance(registration.Instance);
		Marshal.FreeHGlobal(registration.Request);
	}

	protected override bool IsAlive(Registration registration) => !_failed.ContainsKey(registration.Context);

	private void LogRejection(uint status)
	{
		if (!_loggedRejections.TryAdd(status, true))
		{
			_logger.Debug("Windows still rejects a network discovery registration with status {Status}", status);
			return;
		}

		_logger.Warning("Windows rejected a network discovery registration with status {Status}; it is retried " +
			"at the next check",
			status);
	}

	private static string HostName()
	{
		var host = Dns.GetHostName();
		return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? host : host + ".local";
	}

	private void OnComplete(uint status, IntPtr context, IntPtr instance)
	{
		if (instance != IntPtr.Zero)
		{
			DnsServiceFreeInstance(instance);
		}

		if (_registering.TryRemove(context, out var registered))
		{
			// A rejection waits for the next periodic check instead of waking the reconciler, so a lasting
			// conflict cannot turn into a probe loop on the network.
			if (status != 0)
			{
				_failed[context] = true;
				LogRejection(status);
			}

			registered.Registered.TrySetResult();

			if (_abandoned.TryRemove(context, out _))
			{
				_ = Task.Run(() => Unregister(registered));
			}

			return;
		}

		if (_deregistering.TryRemove(context, out var withdrawn))
		{
			withdrawn.Deregistered.TrySetResult();
		}
	}

	[DllImport(Library, CharSet = CharSet.Unicode)]
	private static extern IntPtr DnsServiceConstructInstance(string serviceName,
		string hostName,
		ref uint ipv4,
		IntPtr ipv6,
		ushort port,
		ushort priority,
		ushort weight,
		uint propertiesCount,
		[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
		string[] keys,
		[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
		string[] values);

	[DllImport(Library)]
	private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);

	[DllImport(Library)]
	private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);

	[DllImport(Library)]
	private static extern void DnsServiceFreeInstance(IntPtr instance);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate void RegisterComplete(uint status, IntPtr context, IntPtr instance);

	[StructLayout(LayoutKind.Sequential)]
	private struct RegisterRequest
	{
		public uint Version;
		public uint InterfaceIndex;
		public IntPtr ServiceInstance;
		public IntPtr Callback;
		public IntPtr QueryContext;
		public IntPtr Credentials;
		public int UnicastEnabled;
	}

	internal sealed record Registration(IntPtr Instance, IntPtr Request, nint Context)
	{
		public TaskCompletionSource Registered { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Deregistered { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
