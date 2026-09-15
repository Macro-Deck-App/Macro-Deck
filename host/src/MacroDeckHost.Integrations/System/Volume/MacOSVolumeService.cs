using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("macos")]
internal sealed class MacOsVolumeService : IVolumeService
{
	private const string CoreAudioLibrary = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
	private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	private const uint SystemObject = 1;
	private const uint UnknownDevice = 0;

	private const uint DefaultOutputDeviceSelector = 0x644F7574; // 'dOut'
	private const uint DefaultInputDeviceSelector = 0x64496E20; // 'dIn '
	private const uint DevicesSelector = 0x64657623; // 'dev#'
	private const uint DeviceUidSelector = 0x75696420; // 'uid '
	private const uint NameSelector = 0x6C6E616D; // 'lnam'
	private const uint StreamsSelector = 0x73746D23; // 'stm#'
	private const uint VolumeScalarSelector = 0x766F6C6D; // 'volm'
	private const uint MuteSelector = 0x6D757465; // 'mute'
	private const uint GlobalScope = 0x676C6F62; // 'glob'
	private const uint OutputScope = 0x6F757470; // 'outp'
	private const uint InputScope = 0x696E7074; // 'inpt'

	private const uint MainElement = 0;

	private static readonly uint[] _stereoElements = [1, 2];

	private static readonly AudioObjectPropertyAddress[] _deviceAddresses =
	[
		new(VolumeScalarSelector, OutputScope, MainElement),
		new(VolumeScalarSelector, OutputScope, 1),
		new(VolumeScalarSelector, OutputScope, 2),
		new(MuteSelector, OutputScope, MainElement)
	];

	private static readonly ILogger _logger = Log.ForContext<MacOsVolumeService>();

	// CoreAudio keeps this pointer for as long as any listener is registered, so the delegate has to stay
	// reachable for the process lifetime.
	private static readonly PropertyListener _listener = OnPropertyChanged;
	private static readonly IntPtr _listenerPointer = Marshal.GetFunctionPointerForDelegate(_listener);

	private static long _nextToken;
	private static readonly ConcurrentDictionary<IntPtr, MacOsVolumeService> _instances = new();

	private readonly object _gate = new();

	private volatile Action? _changed;
	private volatile bool _armed;
	private IntPtr _token;
	private uint _listenedDevice = UnknownDevice;

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
					Disarm();
				}
			}
		}
	}

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<AudioDevice>>(ListDevices());

	public Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(target) is { } device ? ReadVolume(device, Scope(target.Flow)) : null);

	public Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(target) is { } device &&
			WriteVolume(device, Scope(target.Flow), Math.Clamp(level, 0f, 1f)));

	public Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(target) is { } device ? ReadMute(device, Scope(target.Flow)) : null);

	public Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(target) is { } device && TryWriteMute(device, Scope(target.Flow), mute));

	private void Arm()
	{
		_token = checked((IntPtr)Interlocked.Increment(ref _nextToken));
		_instances[_token] = this;
		_armed = true;

		var address = new AudioObjectPropertyAddress(DefaultOutputDeviceSelector, GlobalScope, MainElement);
		_ = AudioObjectAddPropertyListener(SystemObject, ref address, _listenerPointer, _token);
		ListenTo(ReadDefaultDevice(AudioFlow.Output) ?? UnknownDevice);
	}

	private void Disarm()
	{
		_armed = false;

		var address = new AudioObjectPropertyAddress(DefaultOutputDeviceSelector, GlobalScope, MainElement);
		_ = AudioObjectRemovePropertyListener(SystemObject, ref address, _listenerPointer, _token);
		ListenTo(UnknownDevice);
		_instances.TryRemove(_token, out _);
	}

	private void Retarget()
	{
		lock (_gate)
		{
			if (_armed)
			{
				ListenTo(ReadDefaultDevice(AudioFlow.Output) ?? UnknownDevice);
			}
		}
	}

	private void ListenTo(uint device)
	{
		if (_listenedDevice != UnknownDevice)
		{
			foreach (var entry in _deviceAddresses)
			{
				var address = entry;
				_ = AudioObjectRemovePropertyListener(_listenedDevice, ref address, _listenerPointer, _token);
			}
		}

		_listenedDevice = device;
		if (device == UnknownDevice)
		{
			return;
		}

		foreach (var entry in _deviceAddresses)
		{
			var address = entry;
			if (HasProperty(device, ref address))
			{
				_ = AudioObjectAddPropertyListener(device, ref address, _listenerPointer, _token);
			}
		}
	}

	private static int OnPropertyChanged(uint objectId, uint addressCount, IntPtr addresses, IntPtr clientData)
	{
		try
		{
			if (!_instances.TryGetValue(clientData, out var service) || !service._armed)
			{
				return 0;
			}

			// Moving the listeners locks the gate, and a CoreAudio callback must never block on it.
			if (objectId == SystemObject)
			{
				ThreadPool.QueueUserWorkItem(static s => s.Retarget(), service, preferLocal: false);
			}

			service._changed?.Invoke();
		}
		catch (Exception e)
		{
			// Runs inside a CoreAudio call frame: an exception unwinding into native code is undefined behaviour.
			_logger.Error(e, "The macOS volume change callback failed");
		}

		return 0;
	}

	private static List<AudioDevice> ListDevices()
	{
		var defaultOutput = ReadDefaultDevice(AudioFlow.Output);
		var defaultInput = ReadDefaultDevice(AudioFlow.Input);
		var devices = new List<AudioDevice>();

		foreach (var device in ReadDeviceIds())
		{
			if (ReadString(device, DeviceUidSelector) is not { Length: > 0 } uid)
			{
				continue;
			}

			var name = ReadString(device, NameSelector) ?? uid;
			if (HasStreams(device, OutputScope))
			{
				devices.Add(new AudioDevice(uid, name, AudioFlow.Output, device == defaultOutput));
			}

			if (HasStreams(device, InputScope))
			{
				devices.Add(new AudioDevice(uid, name, AudioFlow.Input, device == defaultInput));
			}
		}

		return devices;
	}

	private static uint? Resolve(AudioTarget target)
	{
		if (target.DeviceId is null)
		{
			return ReadDefaultDevice(target.Flow);
		}

		var scope = Scope(target.Flow);
		foreach (var device in ReadDeviceIds())
		{
			if (HasStreams(device, scope) && ReadString(device, DeviceUidSelector) == target.DeviceId)
			{
				return device;
			}
		}

		return null;
	}

	private static uint Scope(AudioFlow flow) => flow == AudioFlow.Output ? OutputScope : InputScope;

	private static uint[] ReadDeviceIds()
	{
		var address = new AudioObjectPropertyAddress(DevicesSelector, GlobalScope, MainElement);
		if (AudioObjectGetPropertyDataSize(SystemObject, ref address, 0, IntPtr.Zero, out var size) != 0 || size == 0)
		{
			return [];
		}

		var ids = new uint[size / sizeof(uint)];
		return AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, ids) == 0
			? ids[..(int)(size / sizeof(uint))]
			: [];
	}

	private static bool HasStreams(uint device, uint scope)
	{
		var address = new AudioObjectPropertyAddress(StreamsSelector, scope, MainElement);
		return AudioObjectGetPropertyDataSize(device, ref address, 0, IntPtr.Zero, out var size) == 0 && size > 0;
	}

	private static string? ReadString(uint device, uint selector)
	{
		var address = new AudioObjectPropertyAddress(selector, GlobalScope, MainElement);
		var size = (uint)IntPtr.Size;
		if (AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out IntPtr text) != 0 ||
			text == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var length = CFStringGetLength(text);
			var buffer = new char[length];
			CFStringGetCharacters(text, new CFRange(0, length), buffer);
			return new string(buffer);
		}
		finally
		{
			CFRelease(text);
		}
	}

	private static float? ReadVolume(uint device, uint scope)
	{
		if (TryReadFloat(device, scope, MainElement, out var main))
		{
			return Math.Clamp(main, 0f, 1f);
		}

		var sum = 0f;
		var channels = 0;
		foreach (var element in _stereoElements)
		{
			if (TryReadFloat(device, scope, element, out var channel))
			{
				sum += channel;
				channels++;
			}
		}

		return channels == 0 ? null : Math.Clamp(sum / channels, 0f, 1f);
	}

	private static bool WriteVolume(uint device, uint scope, float level)
	{
		if (TryWriteFloat(device, scope, MainElement, level))
		{
			return true;
		}

		var written = false;
		foreach (var element in _stereoElements)
		{
			written |= TryWriteFloat(device, scope, element, level);
		}

		return written;
	}

	private static bool? ReadMute(uint device, uint scope)
	{
		var address = new AudioObjectPropertyAddress(MuteSelector, scope, MainElement);
		if (!HasProperty(device, ref address))
		{
			return null;
		}

		var size = (uint)sizeof(uint);
		return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out uint muted) == 0
			? muted != 0
			: null;
	}

	private static bool TryWriteMute(uint device, uint scope, bool mute)
	{
		var address = new AudioObjectPropertyAddress(MuteSelector, scope, MainElement);
		if (!IsSettable(device, ref address))
		{
			return false;
		}

		var value = mute ? 1u : 0u;
		return AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, (uint)sizeof(uint), ref value) == 0;
	}

	private static uint? ReadDefaultDevice(AudioFlow flow)
	{
		var selector = flow == AudioFlow.Output ? DefaultOutputDeviceSelector : DefaultInputDeviceSelector;
		var address = new AudioObjectPropertyAddress(selector, GlobalScope, MainElement);
		var size = (uint)sizeof(uint);
		var status = AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, out uint device);
		return status == 0 && device != UnknownDevice ? device : null;
	}

	private static bool TryReadFloat(uint device, uint scope, uint element, out float value)
	{
		value = 0f;
		var address = new AudioObjectPropertyAddress(VolumeScalarSelector, scope, element);
		if (!HasProperty(device, ref address))
		{
			return false;
		}

		var size = (uint)sizeof(float);
		return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out value) == 0;
	}

	private static bool TryWriteFloat(uint device, uint scope, uint element, float value)
	{
		var address = new AudioObjectPropertyAddress(VolumeScalarSelector, scope, element);
		if (!IsSettable(device, ref address))
		{
			return false;
		}

		return AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, (uint)sizeof(float), ref value) == 0;
	}

	private static bool HasProperty(uint device, ref AudioObjectPropertyAddress address)
		=> AudioObjectHasProperty(device, ref address) != 0;

	private static bool IsSettable(uint device, ref AudioObjectPropertyAddress address)
		=> HasProperty(device, ref address) &&
			AudioObjectIsPropertySettable(device, ref address, out var settable) == 0 &&
			settable != 0;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int PropertyListener(uint objectId, uint addressCount, IntPtr addresses, IntPtr clientData);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectAddPropertyListener(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		IntPtr listener,
		IntPtr clientData);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectRemovePropertyListener(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		IntPtr listener,
		IntPtr clientData);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectGetPropertyDataSize(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		out uint dataSize);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectGetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		ref uint dataSize,
		out uint data);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectGetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		ref uint dataSize,
		out float data);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectGetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		ref uint dataSize,
		out IntPtr data);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectGetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		ref uint dataSize,
		[Out] uint[] data);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectSetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		uint dataSize,
		ref uint data);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectSetPropertyData(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		uint qualifierDataSize,
		IntPtr qualifierData,
		uint dataSize,
		ref float data);

	[DllImport(CoreAudioLibrary)]
	private static extern byte AudioObjectHasProperty(uint objectId, ref AudioObjectPropertyAddress address);

	[DllImport(CoreAudioLibrary)]
	private static extern int AudioObjectIsPropertySettable(
		uint objectId,
		ref AudioObjectPropertyAddress address,
		out byte settable);

	[DllImport(CoreFoundationLibrary)]
	private static extern nint CFStringGetLength(IntPtr text);

	[DllImport(CoreFoundationLibrary, CharSet = CharSet.Unicode)]
	private static extern void CFStringGetCharacters(IntPtr text, CFRange range, [Out] char[] buffer);

	[DllImport(CoreFoundationLibrary)]
	private static extern void CFRelease(IntPtr value);

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct CFRange
	{
		public readonly nint Location;
		public readonly nint Length;

		public CFRange(nint location, nint length)
		{
			Location = location;
			Length = length;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct AudioObjectPropertyAddress
	{
		public uint Selector;
		public uint Scope;
		public uint Element;

		public AudioObjectPropertyAddress(uint selector, uint scope, uint element)
		{
			Selector = selector;
			Scope = scope;
			Element = element;
		}
	}
}
