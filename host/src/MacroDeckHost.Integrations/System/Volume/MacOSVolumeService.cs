using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("macos")]
internal sealed class MacOsVolumeService : IVolumeService
{
	private const string CoreAudioLibrary = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

	private const uint SystemObject = 1;
	private const uint UnknownDevice = 0;

	private const uint DefaultOutputDeviceSelector = 0x644F7574; // 'dOut'
	private const uint VolumeScalarSelector = 0x766F6C6D; // 'volm'
	private const uint MuteSelector = 0x6D757465; // 'mute'
	private const uint GlobalScope = 0x676C6F62; // 'glob'
	private const uint OutputScope = 0x6F757470; // 'outp'

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

	public Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(ReadVolume());

	public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default)
	{
		WriteVolume(Math.Clamp(level, 0f, 1f));
		return Task.CompletedTask;
	}

	public Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(ReadMute());

	public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
	{
		_ = TryWriteMute(mute);
		return Task.CompletedTask;
	}

	private void Arm()
	{
		_token = checked((IntPtr)Interlocked.Increment(ref _nextToken));
		_instances[_token] = this;
		_armed = true;

		var address = new AudioObjectPropertyAddress(DefaultOutputDeviceSelector, GlobalScope, MainElement);
		_ = AudioObjectAddPropertyListener(SystemObject, ref address, _listenerPointer, _token);
		ListenTo(ReadDefaultOutputDevice() ?? UnknownDevice);
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
				ListenTo(ReadDefaultOutputDevice() ?? UnknownDevice);
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

	private static float? ReadVolume()
	{
		if (ReadDefaultOutputDevice() is not { } device)
		{
			return null;
		}

		if (TryReadFloat(device, VolumeScalarSelector, MainElement, out var main))
		{
			return Math.Clamp(main, 0f, 1f);
		}

		var sum = 0f;
		var channels = 0;
		foreach (var element in _stereoElements)
		{
			if (TryReadFloat(device, VolumeScalarSelector, element, out var channel))
			{
				sum += channel;
				channels++;
			}
		}

		return channels == 0 ? null : Math.Clamp(sum / channels, 0f, 1f);
	}

	private static void WriteVolume(float level)
	{
		if (ReadDefaultOutputDevice() is not { } device)
		{
			return;
		}

		if (TryWriteFloat(device, VolumeScalarSelector, MainElement, level))
		{
			return;
		}

		foreach (var element in _stereoElements)
		{
			_ = TryWriteFloat(device, VolumeScalarSelector, element, level);
		}
	}

	private static bool? ReadMute()
	{
		if (ReadDefaultOutputDevice() is not { } device)
		{
			return null;
		}

		var address = new AudioObjectPropertyAddress(MuteSelector, OutputScope, MainElement);
		if (!HasProperty(device, ref address))
		{
			return null;
		}

		var size = (uint)sizeof(uint);
		return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out uint muted) == 0
			? muted != 0
			: null;
	}

	private static bool TryWriteMute(bool mute)
	{
		if (ReadDefaultOutputDevice() is not { } device)
		{
			return false;
		}

		var address = new AudioObjectPropertyAddress(MuteSelector, OutputScope, MainElement);
		if (!IsSettable(device, ref address))
		{
			return false;
		}

		var value = mute ? 1u : 0u;
		return AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, (uint)sizeof(uint), ref value) == 0;
	}

	private static uint? ReadDefaultOutputDevice()
	{
		var address = new AudioObjectPropertyAddress(DefaultOutputDeviceSelector, GlobalScope, MainElement);
		var size = (uint)sizeof(uint);
		var status = AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, out uint device);
		return status == 0 && device != UnknownDevice ? device : null;
	}

	private static bool TryReadFloat(uint device, uint selector, uint element, out float value)
	{
		value = 0f;
		var address = new AudioObjectPropertyAddress(selector, OutputScope, element);
		if (!HasProperty(device, ref address))
		{
			return false;
		}

		var size = (uint)sizeof(float);
		return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out value) == 0;
	}

	private static bool TryWriteFloat(uint device, uint selector, uint element, float value)
	{
		var address = new AudioObjectPropertyAddress(selector, OutputScope, element);
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
