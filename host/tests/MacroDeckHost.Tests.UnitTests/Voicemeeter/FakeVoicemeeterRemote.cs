using System.Globalization;
using MacroDeckHost.Integrations.Voicemeeter;
using MacroDeckHost.Integrations.Voicemeeter.Native;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

internal sealed class FakeVoicemeeterRemote : IVoicemeeterRemote
{
	private readonly Dictionary<string, float> _floats = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
	private readonly Dictionary<int, bool> _macroButtons = new();

	public bool IsAvailable { get; set; } = true;

	public LocalizedText? UnavailableReason { get; set; }

	public VoicemeeterEdition Edition { get; private set; } = VoicemeeterEdition.None;

	public List<VoicemeeterEdition> InstalledEditions { get; } = [];

	public int PackedVersion { get; set; } = (3 << 24) | (0 << 16) | (2 << 8) | 8;

	public bool ParametersDirty { get; set; }

	public bool MacroButtonsDirty { get; set; }

	public int LoginCount { get; private set; }

	public int LogoutCount { get; private set; }

	public int TypeQueries { get; private set; }

	public bool IsDisposed { get; private set; }

	public List<string> Writes { get; } = [];

	public List<string> Reads { get; } = [];

	public List<int> RunRequests { get; } = [];

	public FakeVoicemeeterRemote Run(VoicemeeterEdition edition)
	{
		Edition = edition;
		var layout = VoicemeeterLayout.For(edition);
		var buses = layout.BusAssignmentNames();

		for (var index = 0; index < layout.Strips; index++)
		{
			_strings[VoicemeeterParameters.Strip(index, VoicemeeterParameters.Label)] = string.Empty;
			_floats[VoicemeeterParameters.Strip(index, VoicemeeterParameters.Gain)] = 0f;
			_floats[VoicemeeterParameters.Strip(index, VoicemeeterParameters.Mute)] = 0f;
			_floats[VoicemeeterParameters.Strip(index, VoicemeeterParameters.Mono)] = 0f;

			if (index < layout.PhysicalStrips)
			{
				_floats[VoicemeeterParameters.Strip(index, VoicemeeterParameters.Solo)] = 0f;
			}

			foreach (var bus in buses)
			{
				_floats[VoicemeeterParameters.StripBusAssignment(index, bus)] = 0f;
			}
		}

		for (var index = 0; index < layout.Buses; index++)
		{
			_strings[VoicemeeterParameters.Bus(index, VoicemeeterParameters.Label)] = string.Empty;
			_floats[VoicemeeterParameters.Bus(index, VoicemeeterParameters.Gain)] = 0f;
			_floats[VoicemeeterParameters.Bus(index, VoicemeeterParameters.Mute)] = 0f;
			_floats[VoicemeeterParameters.Bus(index, VoicemeeterParameters.Mono)] = 0f;
			_floats[VoicemeeterParameters.Bus(index, VoicemeeterParameters.Eq)] = 0f;
		}

		for (var button = 0; button < 80; button++)
		{
			_macroButtons[button] = false;
		}

		return this;
	}

	public void Close() => Edition = VoicemeeterEdition.None;

	public void UserSets(string parameter, float value)
	{
		_floats[parameter] = value;
		ParametersDirty = true;
	}

	public void UserSets(string parameter, string value)
	{
		_strings[parameter] = value;
		ParametersDirty = true;
	}

	public void UserPressesMacroButton(int button, bool state)
	{
		_macroButtons[button] = state;
		MacroButtonsDirty = true;
	}

	public float Float(string parameter) => _floats.GetValueOrDefault(parameter);

	public VoicemeeterResult Login()
	{
		LoginCount++;
		return Edition == VoicemeeterEdition.None ? VoicemeeterResult.OkNotLaunched : VoicemeeterResult.Ok;
	}

	public void Logout() => LogoutCount++;

	public VoicemeeterResult RunVoicemeeter(int runType)
	{
		RunRequests.Add(runType);
		var matching = InstalledEditions.FirstOrDefault(edition =>
			VoicemeeterEditions.ToRunType(edition) == runType);

		if (matching == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.Error;
		}

		Run(matching);
		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult GetVoicemeeterType(out int rawType)
	{
		TypeQueries++;
		rawType = (int)Edition;
		return Edition == VoicemeeterEdition.None ? VoicemeeterResult.NoServer : VoicemeeterResult.Ok;
	}

	public VoicemeeterResult GetVoicemeeterVersion(out int packedVersion)
	{
		packedVersion = PackedVersion;
		return Edition == VoicemeeterEdition.None ? VoicemeeterResult.NoServer : VoicemeeterResult.Ok;
	}

	public bool IsParametersDirty()
	{
		var dirty = ParametersDirty;
		ParametersDirty = false;
		return dirty;
	}

	public VoicemeeterResult GetParameter(string name, out float value)
	{
		Reads.Add(name);
		value = 0f;
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		if (!_floats.TryGetValue(name, out value))
		{
			return VoicemeeterResult.UnknownParameter;
		}

		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult GetParameter(string name, out string value)
	{
		Reads.Add(name);
		value = string.Empty;
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		if (!_strings.TryGetValue(name, out value!))
		{
			value = string.Empty;
			return VoicemeeterResult.UnknownParameter;
		}

		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult SetParameter(string name, float value)
	{
		Writes.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={value}"));
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		_floats[name] = value;
		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult SetParameter(string name, string value)
	{
		Writes.Add($"{name}={value}");
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		_strings[name] = value;
		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult RunScript(string script)
	{
		Writes.Add(script);
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		foreach (var assignment in script.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
		{
			var parts = assignment.Split('=', 2);
			if (parts.Length == 2 &&
				float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			{
				_floats[parts[0].Trim()] = value;
			}
		}

		return VoicemeeterResult.Ok;
	}

	public bool IsMacroButtonDirty()
	{
		var dirty = MacroButtonsDirty;
		MacroButtonsDirty = false;
		return dirty;
	}

	public VoicemeeterResult GetMacroButton(int button, VoicemeeterMacroButtonMode mode, out bool state)
	{
		state = false;
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		state = _macroButtons.GetValueOrDefault(button);
		return VoicemeeterResult.Ok;
	}

	public VoicemeeterResult SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state)
	{
		Writes.Add(string.Create(CultureInfo.InvariantCulture, $"MacroButton[{button}].{mode}={(state ? 1 : 0)}"));
		if (Edition == VoicemeeterEdition.None)
		{
			return VoicemeeterResult.NoServer;
		}

		_macroButtons[button] = state;
		return VoicemeeterResult.Ok;
	}

	public void Dispose() => IsDisposed = true;
}
