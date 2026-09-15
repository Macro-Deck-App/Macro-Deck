using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.System;

internal static partial class AudioDeviceVariables
{
	public const string OutputFlow = "output";
	public const string InputFlow = "input";

	// Each device costs two of the provider's 256 eager variables, so once the list is full the oldest
	// absent devices make room; a present device is never forgotten.
	public const int MaxKnownDevices = 100;

	// system_audio_output_ + slug + _volume_percent must fit the 64-character name limit; a slug that
	// needs a collision suffix is cut shorter so _<key> still fits.
	private const int SlugBudget = 29;
	private const int SuffixedSlugBudget = 20;

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(2);

	public static string FlowName(AudioFlow flow) => flow == AudioFlow.Output ? OutputFlow : InputFlow;

	public static string VolumeId(KnownAudioDevice device) => $"{IdPrefix(device)}-volume-percent";

	public static string MutedId(KnownAudioDevice device) => $"{IdPrefix(device)}-muted";

	public static IEnumerable<VariableDefinition> Declare(KnownAudioDevice device)
	{
		var output = device.Flow == OutputFlow;
		yield return VariableDefinition.Eager($"system_audio_{device.Flow}_{device.Slug}_volume_percent",
				VariableType.Numeric,
				0,
				_refreshInterval)
			with
			{
				Id = VolumeId(device),
				DisplayName = output
					? AppStrings.Integrations.System.Variables.DeviceOutputVolume(device: device.Name)
					: AppStrings.Integrations.System.Variables.DeviceInputVolume(device: device.Name),
				Unit = "%",
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = new VariableWriteCapability()
			};
		yield return VariableDefinition.Eager($"system_audio_{device.Flow}_{device.Slug}_muted",
				VariableType.Boolean,
				refreshInterval: _refreshInterval)
			with
			{
				Id = MutedId(device),
				DisplayName = output
					? AppStrings.Integrations.System.Variables.DeviceOutputMuted(device: device.Name)
					: AppStrings.Integrations.System.Variables.DeviceInputMuted(device: device.Name)
			};
	}

	public static bool TryParseId(string localId, out AudioFlow flow, out string key, out bool isVolume)
	{
		var match = IdRegex().Match(localId);
		flow = match.Groups[1].Value == "out" ? AudioFlow.Output : AudioFlow.Input;
		key = match.Groups[2].Value;
		isVolume = match.Groups[3].Value == "volume-percent";
		return match.Success;
	}

	public static IReadOnlyList<KnownAudioDevice> Valid(IReadOnlyList<KnownAudioDevice> stored)
	{
		var valid = new List<KnownAudioDevice>();
		foreach (var device in stored)
		{
			if (device.Flow is not (OutputFlow or InputFlow) ||
				string.IsNullOrEmpty(device.DeviceId) ||
				!KeyRegex().IsMatch(device.Key ?? string.Empty) ||
				!SlugRegex().IsMatch(device.Slug ?? string.Empty) ||
				valid.Any(other => other.Flow == device.Flow &&
					(other.DeviceId == device.DeviceId || other.Key == device.Key || other.Slug == device.Slug)))
			{
				continue;
			}

			valid.Add(device with { Name = string.IsNullOrEmpty(device.Name) ? device.DeviceId : device.Name });
		}

		return valid;
	}

	// Keys and slugs are assigned once and never recomputed: a renamed device or a later twin must not
	// move an existing variable to a new name, because templates reference variables by name.
	public static IReadOnlyList<KnownAudioDevice> Merge(
		IReadOnlyList<KnownAudioDevice> known,
		IReadOnlyList<AudioDevice> present)
	{
		var merged = new List<KnownAudioDevice>(known.Count);
		foreach (var device in known)
		{
			var current = present.FirstOrDefault(p => FlowName(p.Flow) == device.Flow && p.Id == device.DeviceId);
			merged.Add(current is null || current.Name == device.Name ? device : device with { Name = current.Name });
		}

		foreach (var device in present)
		{
			var flow = FlowName(device.Flow);
			if (merged.Any(k => k.Flow == flow && k.DeviceId == device.Id))
			{
				continue;
			}

			var key = UniqueKey(merged, flow, device.Id);
			var slug = Slug(device.Name);
			if (slug.Length == 0)
			{
				slug = key;
			}
			else if (merged.Any(k => k.Flow == flow && k.Slug == slug))
			{
				slug = $"{Truncate(slug, SuffixedSlugBudget)}_{key}";
			}

			merged.Add(new KnownAudioDevice(flow, device.Id, key, slug, device.Name));
		}

		for (var index = 0; merged.Count > MaxKnownDevices && index < merged.Count;)
		{
			var candidate = merged[index];
			if (present.Any(p => FlowName(p.Flow) == candidate.Flow && p.Id == candidate.DeviceId))
			{
				index++;
			}
			else
			{
				merged.RemoveAt(index);
			}
		}

		return merged;
	}

	public static string Slug(string name)
	{
		// The x keeps Sanitize from prefixing v_ to a name that starts with a digit.
		return Truncate(VariableNameSanitizer.Sanitize("x" + name)[1..].TrimStart('_'), SlugBudget);
	}

	private static string Truncate(string slug, int length)
		=> slug.Length <= length ? slug : slug[..length].TrimEnd('_');

	public static string ShortId(string deviceId) => Hash(deviceId)[..6];

	private static string UniqueKey(IReadOnlyList<KnownAudioDevice> known, string flow, string deviceId)
	{
		for (var salt = 0;; salt++)
		{
			var key = Hash(salt == 0 ? deviceId : $"{deviceId}#{salt.ToString(CultureInfo.InvariantCulture)}");
			if (!known.Any(k => k.Flow == flow && k.Key == key))
			{
				return key;
			}
		}
	}

	private static string Hash(string value)
	{
		var hash = 2166136261u;
		foreach (var character in value)
		{
			hash = (hash ^ character) * 16777619u;
		}

		return hash.ToString("x8", CultureInfo.InvariantCulture);
	}

	private static string IdPrefix(KnownAudioDevice device)
		=> $"system-audio-{(device.Flow == OutputFlow ? "out" : "in")}-{device.Key}";

	[GeneratedRegex(@"\Asystem-audio-(out|in)-([0-9a-f]{8})-(volume-percent|muted)\z")]
	private static partial Regex IdRegex();

	[GeneratedRegex(@"\A[0-9a-f]{8}\z")]
	private static partial Regex KeyRegex();

	[GeneratedRegex(@"\A[a-z0-9][a-z0-9_]*\z")]
	private static partial Regex SlugRegex();
}
