using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("windows")]
internal sealed class WindowsVolumeService : IVolumeService
{
	private const int ClsCtxAll = 0x17;
	private const int ErrorNotFound = unchecked((int)0x80070490);
	private const int RpcServerUnavailable = unchecked((int)0x800706BA);

	private static readonly Guid _audioEndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;

	public bool IsSupported => true;

	public Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default)
	{
		var endpoint = GetEndpointVolume();
		if (endpoint is null)
		{
			return Task.FromResult<float?>(null);
		}

		try
		{
			Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var level));
			return Task.FromResult<float?>(Math.Clamp(level, 0f, 1f));
		}
		finally
		{
			Marshal.ReleaseComObject(endpoint);
		}
	}

	public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default)
	{
		var endpoint = GetEndpointVolume();
		if (endpoint is null)
		{
			return Task.CompletedTask;
		}

		try
		{
			var context = Guid.Empty;
			Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context));
		}
		finally
		{
			Marshal.ReleaseComObject(endpoint);
		}

		return Task.CompletedTask;
	}

	public Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default)
	{
		var endpoint = GetEndpointVolume();
		if (endpoint is null)
		{
			return Task.FromResult<bool?>(null);
		}

		try
		{
			Marshal.ThrowExceptionForHR(endpoint.GetMute(out var mute));
			return Task.FromResult<bool?>(mute);
		}
		finally
		{
			Marshal.ReleaseComObject(endpoint);
		}
	}

	public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
	{
		var endpoint = GetEndpointVolume();
		if (endpoint is null)
		{
			return Task.CompletedTask;
		}

		try
		{
			var context = Guid.Empty;
			Marshal.ThrowExceptionForHR(endpoint.SetMute(mute, ref context));
		}
		finally
		{
			Marshal.ReleaseComObject(endpoint);
		}

		return Task.CompletedTask;
	}

	private static IAudioEndpointVolume? GetEndpointVolume()
	{
		var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
		try
		{
			var hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
			// A stopped Windows Audio service answers with an unreachable RPC server rather than a
			// missing endpoint. Either way there is no endpoint to read or set, and a machine with the
			// service off is not a machine where volume actions should throw.
			if (hr is ErrorNotFound or RpcServerUnavailable)
			{
				return null;
			}

			Marshal.ThrowExceptionForHR(hr);
			try
			{
				var iid = _audioEndpointVolumeIid;
				Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance));
				return (IAudioEndpointVolume)instance;
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

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private sealed class MMDeviceEnumerator;

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
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
	}

	[ComImport]
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(IntPtr notify);

		[PreserveSig]
		int UnregisterControlChangeNotify(IntPtr notify);

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
