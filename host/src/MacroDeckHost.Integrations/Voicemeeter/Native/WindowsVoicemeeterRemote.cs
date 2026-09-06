using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

[SupportedOSPlatform("windows")]
internal sealed class WindowsVoicemeeterRemote : IVoicemeeterRemote
{
	private const int StringBufferLength = 512;

	private static readonly ILogger _logger =
		IntegrationLog.For<WindowsVoicemeeterRemote>(VoicemeeterIntegration.IntegrationId);

	private readonly Lock _gate = new();

	private readonly nint _library;
	private readonly Exports? _exports;

	private bool _disposed;

	public WindowsVoicemeeterRemote()
	{
		var path = VoicemeeterInstallation.FindRemoteLibrary();
		if (path is null)
		{
			UnavailableReason = AppStrings.Integrations.Voicemeeter.Issues.NotInstalledReason();
			return;
		}

		if (!NativeLibrary.TryLoad(path, out _library))
		{
			UnavailableReason = AppStrings.Integrations.Voicemeeter.Issues.LibraryLoadFailedReason(path: path);
			return;
		}

		if (!Exports.TryBind(_library, out var exports, out var missingExport))
		{
			NativeLibrary.Free(_library);
			_library = nint.Zero;
			UnavailableReason =
				AppStrings.Integrations.Voicemeeter.Issues.LibraryMissingExportReason(export: missingExport);
			return;
		}

		_exports = exports;
		_logger.Information("Loaded the Voicemeeter remote library from {Path}", path);
	}

	public bool IsAvailable => _exports is not null;

	public LocalizedText? UnavailableReason { get; }

	public VoicemeeterResult Login() => Invoke(e => e.Login());

	public void Logout()
	{
		if (_exports is null)
		{
			return;
		}

		lock (_gate)
		{
			_ = _exports.Logout();
		}
	}

	public VoicemeeterResult RunVoicemeeter(int runType) => Invoke(e => e.RunVoicemeeter(runType));

	public VoicemeeterResult GetVoicemeeterType(out int rawType)
	{
		var value = 0;
		var result = Invoke(e => e.GetVoicemeeterType(out value));
		rawType = value;
		return result;
	}

	public VoicemeeterResult GetVoicemeeterVersion(out int packedVersion)
	{
		var value = 0;
		var result = Invoke(e => e.GetVoicemeeterVersion(out value));
		packedVersion = value;
		return result;
	}

	public bool IsParametersDirty() => InvokeRaw(e => e.IsParametersDirty()) > 0;

	public VoicemeeterResult GetParameter(string name, out float value)
	{
		var read = 0f;
		var result = Invoke(e => e.GetParameterFloat(Ansi(name), out read));
		value = read;
		return result;
	}

	public VoicemeeterResult GetParameter(string name, out string value)
	{
		var buffer = new char[StringBufferLength];
		var result = Invoke(e => e.GetParameterStringW(Ansi(name), buffer));
		value = result == VoicemeeterResult.Ok ? ReadNullTerminated(buffer) : string.Empty;
		return result;
	}

	public VoicemeeterResult SetParameter(string name, float value)
		=> Invoke(e => e.SetParameterFloat(Ansi(name), value));

	public VoicemeeterResult SetParameter(string name, string value)
		=> Invoke(e => e.SetParameterStringW(Ansi(name), NullTerminated(value)));

	public VoicemeeterResult RunScript(string script)
	{
		var raw = InvokeRaw(e => e.SetParametersW(NullTerminated(script)));
		if (raw > 0)
		{
			_logger.Warning("Voicemeeter rejected the script at line {Line}", raw);
			return VoicemeeterResult.Error;
		}

		return ToResult(raw);
	}

	public bool IsMacroButtonDirty() => InvokeRaw(e => e.MacroButtonIsDirty()) > 0;

	public VoicemeeterResult GetMacroButton(int button, VoicemeeterMacroButtonMode mode, out bool state)
	{
		var value = 0f;
		var result = Invoke(e => e.MacroButtonGetStatus(button, out value, (int)mode));
		state = value > 0.5f;
		return result;
	}

	public VoicemeeterResult SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state)
		=> Invoke(e => e.MacroButtonSetStatus(button, state ? 1f : 0f, (int)mode));

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		lock (_gate)
		{
			if (_library != nint.Zero)
			{
				NativeLibrary.Free(_library);
			}
		}
	}

	private static byte[] Ansi(string value) => Encoding.ASCII.GetBytes(value + '\0');

	private static char[] NullTerminated(string value) => (value + '\0').ToCharArray();

	private static string ReadNullTerminated(char[] buffer)
	{
		var end = Array.IndexOf(buffer, '\0');
		return new string(buffer, 0, end < 0 ? buffer.Length : end);
	}

	private static VoicemeeterResult ToResult(int raw)
		=> Enum.IsDefined((VoicemeeterResult)raw) ? (VoicemeeterResult)raw : VoicemeeterResult.Error;

	private VoicemeeterResult Invoke(Func<Exports, int> call) => ToResult(InvokeRaw(call));

	private int InvokeRaw(Func<Exports, int> call)
	{
		var exports = _exports;
		if (exports is null || _disposed)
		{
			return (int)VoicemeeterResult.Unavailable;
		}

		lock (_gate)
		{
			if (_disposed)
			{
				return (int)VoicemeeterResult.Unavailable;
			}

			try
			{
				return call(exports);
			}
			catch (Exception ex) when (ex is SEHException or EntryPointNotFoundException)
			{
				_logger.Error(ex, "The Voicemeeter remote library faulted");
				return (int)VoicemeeterResult.Error;
			}
		}
	}

	private sealed class Exports
	{
		private Exports(nint library)
		{
			Login = Bind<NoArgs>(library, "VBVMR_Login");
			Logout = Bind<NoArgs>(library, "VBVMR_Logout");
			RunVoicemeeter = Bind<WithInt>(library, "VBVMR_RunVoicemeeter");
			GetVoicemeeterType = Bind<OutInt>(library, "VBVMR_GetVoicemeeterType");
			GetVoicemeeterVersion = Bind<OutInt>(library, "VBVMR_GetVoicemeeterVersion");
			IsParametersDirty = Bind<NoArgs>(library, "VBVMR_IsParametersDirty");
			GetParameterFloat = Bind<GetFloat>(library, "VBVMR_GetParameterFloat");
			GetParameterStringW = Bind<GetStringW>(library, "VBVMR_GetParameterStringW");
			SetParameterFloat = Bind<SetFloat>(library, "VBVMR_SetParameterFloat");
			SetParameterStringW = Bind<SetStringW>(library, "VBVMR_SetParameterStringW");
			SetParametersW = Bind<ScriptW>(library, "VBVMR_SetParametersW");
			MacroButtonIsDirty = Bind<NoArgs>(library, "VBVMR_MacroButton_IsDirty");
			MacroButtonGetStatus = Bind<MacroGet>(library, "VBVMR_MacroButton_GetStatus");
			MacroButtonSetStatus = Bind<MacroSet>(library, "VBVMR_MacroButton_SetStatus");
		}

		public NoArgs Login { get; }
		public NoArgs Logout { get; }
		public WithInt RunVoicemeeter { get; }
		public OutInt GetVoicemeeterType { get; }
		public OutInt GetVoicemeeterVersion { get; }
		public NoArgs IsParametersDirty { get; }
		public GetFloat GetParameterFloat { get; }
		public GetStringW GetParameterStringW { get; }
		public SetFloat SetParameterFloat { get; }
		public SetStringW SetParameterStringW { get; }
		public ScriptW SetParametersW { get; }
		public NoArgs MacroButtonIsDirty { get; }
		public MacroGet MacroButtonGetStatus { get; }
		public MacroSet MacroButtonSetStatus { get; }

		public static bool TryBind(nint library, out Exports? exports, out string? missingExport)
		{
			try
			{
				exports = new Exports(library);
				missingExport = null;
				return true;
			}
			catch (MissingExportException ex)
			{
				exports = null;
				missingExport = ex.Export;
				return false;
			}
		}

		private static TDelegate Bind<TDelegate>(nint library, string export)
			where TDelegate : Delegate
		{
			if (!NativeLibrary.TryGetExport(library, export, out var address))
			{
				throw new MissingExportException(export);
			}

			return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
		}
	}

	private sealed class MissingExportException : Exception
	{
		public MissingExportException(string export)
			: base($"The Voicemeeter remote library does not export '{export}'.")
		{
			Export = export;
		}

		public string Export { get; }
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int NoArgs();

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int WithInt(int value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int OutInt(out int value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetFloat(byte[] name, out float value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int SetFloat(byte[] name, float value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
	private delegate int GetStringW(byte[] name, char[] buffer);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
	private delegate int SetStringW(byte[] name, char[] value);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
	private delegate int ScriptW(char[] script);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int MacroGet(int button, out float value, int mode);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int MacroSet(int button, float value, int mode);
}
