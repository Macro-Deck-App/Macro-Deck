using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Infrastructure.Native;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

[SupportedOSPlatform("linux")]
internal sealed class LinuxAvahiServiceAdvertiser : IServiceAdvertiser
{
	private const string ClientLibrary = "libavahi-client.so.3";
	private const string CommonLibrary = "libavahi-common.so.3";
	private const int ClientFlagNoFail = 2;
	private const int ClientStateRunning = 2;
	private const int ClientStateFailure = 100;
	private const int GroupStateCollision = 3;
	private const int GroupStateFailure = 4;
	private const int ProtocolInet = 0;

	private static readonly Lazy<bool> Loadable = new(() =>
		NativeLibrary.TryLoad(ClientLibrary, out var client) &&
		NativeLibrary.TryLoad(CommonLibrary, out _) &&
		NativeLibrary.TryGetExport(client, "avahi_entry_group_add_service_strlst", out _));

	private readonly StateCallback _clientCallback;
	private readonly StateCallback _groupCallback;
	private IntPtr _poll;
	private IntPtr _client;
	private IntPtr _group;
	private bool _pollStarted;
	private string? _wireName;
	private string? _alternativeName;
	private volatile bool _stale = true;
	private volatile bool _collision;
	private volatile bool _clientFailed;

	public LinuxAvahiServiceAdvertiser()
	{
		_clientCallback = OnClientState;
		_groupCallback = OnGroupState;
	}

	public event Action? StateChanged;

	public bool IsAvailable => Loadable.Value;

	public bool NeedsRepublish => _stale || _collision || _clientFailed;

	public void CheckLiveness()
	{
	}

	public void Publish(ServiceAdvertisement advertisement)
	{
		EnsureClient();
		_stale = true;

		// Avahi's threaded poll runs callbacks on its own thread. Every call from outside it has to hold
		// the poll lock, and the callbacks themselves must never take it.
		avahi_threaded_poll_lock(_poll);
		try
		{
			if (avahi_client_get_state(_client) != ClientStateRunning)
			{
				throw new InvalidOperationException("The Avahi daemon is not available");
			}

			var name = ResolveName(advertisement.WireName);
			if (_group != IntPtr.Zero)
			{
				_ = avahi_entry_group_free(_group);
			}

			_group = avahi_entry_group_new(_client, _groupCallback, IntPtr.Zero);
			if (_group == IntPtr.Zero)
			{
				throw Failure("avahi_entry_group_new", avahi_client_errno(_client));
			}

			AddServices(advertisement, name);
			_stale = false;
		}
		finally
		{
			avahi_threaded_poll_unlock(_poll);
		}
	}

	public void Withdraw()
	{
		if (_group == IntPtr.Zero)
		{
			return;
		}

		avahi_threaded_poll_lock(_poll);
		try
		{
			_ = avahi_entry_group_free(_group);
			_group = IntPtr.Zero;
		}
		finally
		{
			avahi_threaded_poll_unlock(_poll);
		}
	}

	public void Dispose()
	{
		Withdraw();
		if (_pollStarted)
		{
			_ = avahi_threaded_poll_stop(_poll);
		}

		if (_client != IntPtr.Zero)
		{
			avahi_client_free(_client);
			_client = IntPtr.Zero;
		}

		if (_poll != IntPtr.Zero)
		{
			avahi_threaded_poll_free(_poll);
			_poll = IntPtr.Zero;
		}

		GC.SuppressFinalize(this);
	}

	private void EnsureClient()
	{
		if (_client != IntPtr.Zero && !_clientFailed)
		{
			return;
		}

		if (_poll == IntPtr.Zero)
		{
			_poll = avahi_threaded_poll_new();
			if (_poll == IntPtr.Zero)
			{
				throw new InvalidOperationException("avahi_threaded_poll_new failed");
			}
		}

		avahi_threaded_poll_lock(_poll);
		try
		{
			if (_client != IntPtr.Zero)
			{
				avahi_client_free(_client);
				_client = IntPtr.Zero;
				_group = IntPtr.Zero;
			}

			_clientFailed = false;
			_client = avahi_client_new(avahi_threaded_poll_get(_poll),
				ClientFlagNoFail,
				_clientCallback,
				IntPtr.Zero,
				out var error);
			if (_client == IntPtr.Zero)
			{
				throw Failure("avahi_client_new", error);
			}
		}
		finally
		{
			avahi_threaded_poll_unlock(_poll);
		}

		if (!_pollStarted)
		{
			if (avahi_threaded_poll_start(_poll) < 0)
			{
				throw new InvalidOperationException("avahi_threaded_poll_start failed");
			}

			_pollStarted = true;
		}
	}

	private string ResolveName(string wireName)
	{
		if (wireName != _wireName)
		{
			_wireName = wireName;
			_alternativeName = null;
		}

		if (_collision)
		{
			_collision = false;
			var alternative = NativeUtf8.Alloc(_alternativeName ?? wireName);
			try
			{
				var renamed = avahi_alternative_service_name(alternative);
				_alternativeName = Marshal.PtrToStringUTF8(renamed);
				avahi_free(renamed);
			}
			finally
			{
				NativeUtf8.Free(alternative);
			}
		}

		return _alternativeName ?? wireName;
	}

	private void AddServices(ServiceAdvertisement advertisement, string name)
	{
		var namePointer = NativeUtf8.Alloc(name);
		var typePointer = NativeUtf8.Alloc(ServiceAdvertisementPlanner.ServiceType);
		var entries = advertisement.Txt.Select(entry => NativeUtf8.Alloc($"{entry.Key}={entry.Value}")).ToArray();
		var txt = avahi_string_list_new_from_array(entries, entries.Length);
		try
		{
			foreach (var target in advertisement.Interfaces)
			{
				var result = avahi_entry_group_add_service_strlst(_group,
					target.Index,
					ProtocolInet,
					0,
					namePointer,
					typePointer,
					IntPtr.Zero,
					IntPtr.Zero,
					(ushort)advertisement.Port,
					txt);
				if (result < 0)
				{
					throw Failure("avahi_entry_group_add_service_strlst", result);
				}
			}

			var committed = avahi_entry_group_commit(_group);
			if (committed < 0)
			{
				throw Failure("avahi_entry_group_commit", committed);
			}
		}
		finally
		{
			avahi_string_list_free(txt);
			foreach (var entry in entries)
			{
				NativeUtf8.Free(entry);
			}

			NativeUtf8.Free(namePointer);
			NativeUtf8.Free(typePointer);
		}
	}

	private void OnClientState(IntPtr client, int state, IntPtr userData)
	{
		if (state == ClientStateFailure)
		{
			_clientFailed = true;
		}
		else
		{
			_stale = true;
		}

		StateChanged?.Invoke();
	}

	private void OnGroupState(IntPtr group, int state, IntPtr userData)
	{
		if (state == GroupStateCollision)
		{
			_collision = true;
			StateChanged?.Invoke();
		}
		else if (state == GroupStateFailure)
		{
			// Left to the next periodic check rather than retried at once, so a lasting failure cannot loop.
			_stale = true;
		}
	}

	private static InvalidOperationException Failure(string operation, int error)
		=> new($"{operation} failed: {Marshal.PtrToStringUTF8(avahi_strerror(error))}");

	[DllImport(CommonLibrary)]
	private static extern IntPtr avahi_threaded_poll_new();

	[DllImport(CommonLibrary)]
	private static extern IntPtr avahi_threaded_poll_get(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern int avahi_threaded_poll_start(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern int avahi_threaded_poll_stop(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern void avahi_threaded_poll_free(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern void avahi_threaded_poll_lock(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern void avahi_threaded_poll_unlock(IntPtr poll);

	[DllImport(CommonLibrary)]
	private static extern IntPtr avahi_string_list_new_from_array(IntPtr[] array, int length);

	[DllImport(CommonLibrary)]
	private static extern void avahi_string_list_free(IntPtr list);

	[DllImport(CommonLibrary)]
	private static extern IntPtr avahi_alternative_service_name(IntPtr name);

	[DllImport(CommonLibrary)]
	private static extern void avahi_free(IntPtr pointer);

	[DllImport(CommonLibrary)]
	private static extern IntPtr avahi_strerror(int error);

	[DllImport(ClientLibrary)]
	private static extern IntPtr avahi_client_new(IntPtr poll,
		int flags,
		StateCallback callback,
		IntPtr userData,
		out int error);

	[DllImport(ClientLibrary)]
	private static extern void avahi_client_free(IntPtr client);

	[DllImport(ClientLibrary)]
	private static extern int avahi_client_get_state(IntPtr client);

	[DllImport(ClientLibrary)]
	private static extern int avahi_client_errno(IntPtr client);

	[DllImport(ClientLibrary)]
	private static extern IntPtr avahi_entry_group_new(IntPtr client, StateCallback callback, IntPtr userData);

	[DllImport(ClientLibrary)]
	private static extern int avahi_entry_group_free(IntPtr group);

	[DllImport(ClientLibrary)]
	private static extern int avahi_entry_group_commit(IntPtr group);

	[DllImport(ClientLibrary)]
	private static extern int avahi_entry_group_add_service_strlst(IntPtr group,
		int interfaceIndex,
		int protocol,
		int flags,
		IntPtr name,
		IntPtr type,
		IntPtr domain,
		IntPtr host,
		ushort port,
		IntPtr txt);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void StateCallback(IntPtr source, int state, IntPtr userData);
}
