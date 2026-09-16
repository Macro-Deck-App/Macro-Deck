using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("windows")]
internal sealed class WindowsVolumeService : IVolumeService
{
	private const int ClsCtxAll = 0x17;
	private const int RenderFlow = 0;
	private const int CaptureFlow = 1;
	private const int MultimediaRole = 1;
	private const int DeviceStateActive = 1;
	private const int StgmRead = 0;
	private const ushort VtLpwstr = 31;
	private const int ErrorNotFound = unchecked((int)0x80070490);
	private const int RpcServerUnavailable = unchecked((int)0x800706BA);
	private const int DeviceInvalidated = unchecked((int)0x88890004);

	private static readonly Guid _audioEndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;
	private static readonly PropertyKey _friendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

	private static readonly ILogger _logger = Log.ForContext<WindowsVolumeService>();

	private readonly object _gate = new();
	private readonly VolumeCallback _callback;

	private volatile Action? _changed;
	private volatile bool _armed;
	private IAudioEndpointVolume? _listened;
	private string? _listenedId;

	public WindowsVolumeService()
	{
		_callback = new VolumeCallback(this);
	}

	public bool IsSupported => true;

	public event Action? Changed
	{
		add
		{
			lock (_gate)
			{
				_changed += value;
				if (!_armed && _changed is not null)
				{
					Arm();
				}
			}
		}
		remove
		{
			lock (_gate)
			{
				_changed -= value;
				if (_armed && _changed is null)
				{
					_armed = false;
					StopListening();
				}
			}
		}
	}

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
	{
		var devices = new List<AudioDevice>();
		var enumerator = CreateEnumerator();
		try
		{
			AddDevices(enumerator, AudioFlow.Output, devices);
			AddDevices(enumerator, AudioFlow.Input, devices);
		}
		finally
		{
			Marshal.ReleaseComObject(enumerator);
		}

		return Task.FromResult<IReadOnlyList<AudioDevice>>(devices);
	}

	public Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(WithEndpoint(target,
			endpoint => Succeeded(endpoint.GetMasterVolumeLevelScalar(out var level))
				? Math.Clamp(level, 0f, 1f)
				: (float?)null));

	public Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default)
		=> Task.FromResult(WithEndpoint(target,
			endpoint =>
			{
				var context = Guid.Empty;
				return (bool?)Succeeded(endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context));
			}) ?? false);

	public Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(WithEndpoint(target,
			endpoint => Succeeded(endpoint.GetMute(out var mute)) ? mute : (bool?)null));

	public Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default)
		=> Task.FromResult(WithEndpoint(target,
			endpoint =>
			{
				var context = Guid.Empty;
				return (bool?)Succeeded(endpoint.SetMute(mute, ref context));
			}) ?? false);

	private void Arm()
	{
		_armed = true;
		try
		{
			if (GetEndpointVolume(AudioTarget.DefaultOutput) is { } endpoint)
			{
				Marshal.ReleaseComObject(endpoint);
			}
		}
		catch (COMException e)
		{
			_logger.Debug(e, "Could not listen for Windows volume changes yet");
		}
	}

	private T? WithEndpoint<T>(AudioTarget target, Func<IAudioEndpointVolume, T?> use)
		where T : struct
	{
		var endpoint = GetEndpointVolume(target);
		if (endpoint is null)
		{
			return null;
		}

		try
		{
			return use(endpoint);
		}
		finally
		{
			Marshal.ReleaseComObject(endpoint);
		}
	}

	private IAudioEndpointVolume? GetEndpointVolume(AudioTarget target)
	{
		var enumerator = CreateEnumerator();
		try
		{
			IMMDevice device;
			var hr = target.DeviceId is { } id
				? enumerator.GetDevice(id, out device)
				: enumerator.GetDefaultAudioEndpoint(DataFlow(target.Flow), MultimediaRole, out device);
			if (IsUnavailable(hr))
			{
				return null;
			}

			Marshal.ThrowExceptionForHR(hr);
			try
			{
				if (target.DeviceId is not null &&
					(device.GetState(out var state) != 0 || state != DeviceStateActive))
				{
					return null;
				}

				// Only the default output is listened to; resolving any other target must not move the
				// listener, or system_volume_percent would stop receiving change notifications.
				if (target == AudioTarget.DefaultOutput)
				{
					FollowDefaultEndpoint(device);
				}

				var iid = _audioEndpointVolumeIid;
				hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance);
				return Succeeded(hr) ? (IAudioEndpointVolume)instance : null;
			}
			finally
			{
				Marshal.ReleaseComObject(device);
			}
		}
		finally
		{
			Marshal.ReleaseComObject(enumerator);
		}
	}

	private static void AddDevices(IMMDeviceEnumerator enumerator, AudioFlow flow, List<AudioDevice> devices)
	{
		var dataFlow = DataFlow(flow);
		var defaultId = ReadDefaultId(enumerator, dataFlow);
		var hr = enumerator.EnumAudioEndpoints(dataFlow, DeviceStateActive, out var collection);
		if (!Succeeded(hr))
		{
			return;
		}

		try
		{
			if (!Succeeded(collection.GetCount(out var count)))
			{
				return;
			}

			for (uint index = 0; index < count; index++)
			{
				if (collection.Item(index, out var device) != 0)
				{
					continue;
				}

				try
				{
					if (device.GetId(out var id) == 0)
					{
						devices.Add(new AudioDevice(id, ReadFriendlyName(device) ?? id, flow, id == defaultId));
					}
				}
				finally
				{
					Marshal.ReleaseComObject(device);
				}
			}
		}
		finally
		{
			Marshal.ReleaseComObject(collection);
		}
	}

	private static string? ReadDefaultId(IMMDeviceEnumerator enumerator, int dataFlow)
	{
		if (enumerator.GetDefaultAudioEndpoint(dataFlow, MultimediaRole, out var device) != 0)
		{
			return null;
		}

		try
		{
			return device.GetId(out var id) == 0 ? id : null;
		}
		finally
		{
			Marshal.ReleaseComObject(device);
		}
	}

	private static string? ReadFriendlyName(IMMDevice device)
	{
		if (device.OpenPropertyStore(StgmRead, out var store) != 0)
		{
			return null;
		}

		try
		{
			var key = _friendlyNameKey;
			if (store.GetValue(ref key, out var value) != 0)
			{
				return null;
			}

			try
			{
				return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
			}
			finally
			{
				_ = PropVariantClear(ref value);
			}
		}
		finally
		{
			Marshal.ReleaseComObject(store);
		}
	}

	private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();

	private static int DataFlow(AudioFlow flow) => flow == AudioFlow.Output ? RenderFlow : CaptureFlow;

	// A stopped Windows Audio service answers with an unreachable RPC server, and an unplugged endpoint
	// with not found or invalidated. None of them is a reason for a volume action to throw.
	private static bool IsUnavailable(int hr) => hr is ErrorNotFound or RpcServerUnavailable or DeviceInvalidated;

	private static bool Succeeded(int hr)
	{
		if (IsUnavailable(hr))
		{
			return false;
		}

		Marshal.ThrowExceptionForHR(hr);
		return true;
	}

	private void FollowDefaultEndpoint(IMMDevice device)
	{
		if (!_armed || device.GetId(out var id) != 0)
		{
			return;
		}

		lock (_gate)
		{
			if (!_armed || id == _listenedId)
			{
				return;
			}

			StopListening();

			var iid = _audioEndpointVolumeIid;
			if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) != 0)
			{
				return;
			}

			var endpoint = (IAudioEndpointVolume)instance;
			if (endpoint.RegisterControlChangeNotify(_callback) != 0)
			{
				_logger.Debug("Could not register for Windows volume change notifications");
				Marshal.ReleaseComObject(endpoint);
				return;
			}

			_listened = endpoint;
			_listenedId = id;
		}
	}

	private void StopListening()
	{
		if (_listened is null)
		{
			return;
		}

		_ = _listened.UnregisterControlChangeNotify(_callback);
		Marshal.ReleaseComObject(_listened);
		_listened = null;
		_listenedId = null;
	}

	[DllImport("ole32.dll")]
	private static extern int PropVariantClear(ref PropVariant value);

	[ComVisible(true)]
	private sealed class VolumeCallback : IAudioEndpointVolumeCallback
	{
		private readonly WindowsVolumeService _owner;

		public VolumeCallback(WindowsVolumeService owner)
		{
			_owner = owner;
		}

		public int OnNotify(IntPtr notifyData)
		{
			try
			{
				if (_owner._armed)
				{
					_owner._changed?.Invoke();
				}
			}
			catch (Exception e)
			{
				_logger.Error(e, "The Windows volume change callback failed");
			}

			return 0;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct PropertyKey
	{
		public readonly Guid FormatId;
		public readonly uint PropertyId;

		public PropertyKey(Guid formatId, uint propertyId)
		{
			FormatId = formatId;
			PropertyId = propertyId;
		}
	}

	// Native PROPVARIANT: a VARTYPE, three reserved words, then a union as wide as two pointers.
	[StructLayout(LayoutKind.Sequential)]
	private struct PropVariant
	{
		public ushort Type;
		public ushort Reserved1;
		public ushort Reserved2;
		public ushort Reserved3;
		public IntPtr Pointer;
		public IntPtr Extra;
	}

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private sealed class MMDeviceEnumerator;

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

		[PreserveSig]
		int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
	}

	[ComImport]
	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceCollection
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int Item(uint index, out IMMDevice device);
	}

	[ComImport]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		[PreserveSig]
		int Activate(
			ref Guid iid,
			int clsCtx,
			IntPtr activationParams,
			[MarshalAs(UnmanagedType.IUnknown)] out object instance);

		[PreserveSig]
		int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

		[PreserveSig]
		int GetState(out int state);
	}

	[ComImport]
	[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int GetAt(uint index, out PropertyKey key);

		[PreserveSig]
		int GetValue(ref PropertyKey key, out PropVariant value);

		[PreserveSig]
		int SetValue(ref PropertyKey key, ref PropVariant value);

		[PreserveSig]
		int Commit();
	}

	[ComImport]
	[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolumeCallback
	{
		[PreserveSig]
		int OnNotify(IntPtr notifyData);
	}

	[ComImport]
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

		[PreserveSig]
		int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

		[PreserveSig]
		int GetChannelCount(out uint channelCount);

		[PreserveSig]
		int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

		[PreserveSig]
		int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

		[PreserveSig]
		int GetMasterVolumeLevel(out float levelDb);

		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float level);

		[PreserveSig]
		int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

		[PreserveSig]
		int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

		[PreserveSig]
		int GetChannelVolumeLevel(uint channel, out float levelDb);

		[PreserveSig]
		int GetChannelVolumeLevelScalar(uint channel, out float level);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

		[PreserveSig]
		int GetVolumeStepInfo(out uint step, out uint stepCount);

		[PreserveSig]
		int VolumeStepUp(ref Guid eventContext);

		[PreserveSig]
		int VolumeStepDown(ref Guid eventContext);

		[PreserveSig]
		int QueryHardwareSupport(out uint hardwareSupportMask);

		[PreserveSig]
		int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
	}
}
