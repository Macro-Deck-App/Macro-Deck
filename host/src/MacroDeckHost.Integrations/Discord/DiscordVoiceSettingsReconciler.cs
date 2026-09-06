using System.Text.Json;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Integrations.Discord;

internal static class DiscordVoiceSettingsReconciler
{
	public static readonly string[] FieldOrder =
	[
		"mute",
		"deaf",
		"noise_suppression",
		"echo_cancellation",
		"automatic_gain_control",
		"qos",
		"input.volume",
		"output.volume",
		"mode.type"
	];

	private const double VolumeTolerance = 0.5;

	public static IReadOnlyList<DiscordUnappliedField> FindUnappliedFields(
		DiscordVoiceSettingsPatch patch,
		JsonElement response)
	{
		List<DiscordUnappliedField>? unapplied = null;

		CheckBool(patch.Mute, "mute");
		CheckBool(patch.Deaf, "deaf");
		CheckBool(patch.NoiseSuppression, "noise_suppression");
		CheckBool(patch.EchoCancellation, "echo_cancellation");
		CheckBool(patch.AutomaticGainControl, "automatic_gain_control");
		CheckBool(patch.Qos, "qos");
		CheckVolume(patch.Input?.Volume, "input.volume", "input");
		CheckVolume(patch.Output?.Volume, "output.volume", "output");
		CheckMode(patch.Mode?.Type);

		return unapplied ?? [];

		void CheckBool(bool? requested, string field)
		{
			if (requested is not { } target)
			{
				return;
			}

			var actual = DiscordStateMapper.ReadBool(response, field);
			if (actual is null)
			{
				Add(field, DiscordFieldMismatch.Omitted);
			}
			else if (actual != target)
			{
				Add(field, DiscordFieldMismatch.Unchanged);
			}
		}

		void CheckVolume(double? requested, string field, string objectProperty)
		{
			if (requested is not { } target)
			{
				return;
			}

			var actual = DiscordStateMapper.ReadNestedDouble(response, objectProperty, "volume");
			if (actual is null)
			{
				Add(field, DiscordFieldMismatch.Omitted);
			}
			else if (Math.Abs(actual.Value - target) > VolumeTolerance)
			{
				Add(field, DiscordFieldMismatch.Unchanged);
			}
		}

		void CheckMode(string? requested)
		{
			if (requested is null)
			{
				return;
			}

			var actual = DiscordStateMapper.ReadString(response, "mode", "type");
			if (actual is null)
			{
				Add("mode.type", DiscordFieldMismatch.Omitted);
			}
			else if (!string.Equals(actual, requested, StringComparison.Ordinal))
			{
				Add("mode.type", DiscordFieldMismatch.Unchanged);
			}
		}

		void Add(string field, DiscordFieldMismatch reason)
		{
			unapplied ??= [];
			unapplied.Add(new DiscordUnappliedField(field, reason));
		}
	}

	public static IEnumerable<string> NamedFields(DiscordVoiceSettingsPatch patch)
	{
		if (patch.Mute is not null)
		{
			yield return "mute";
		}

		if (patch.Deaf is not null)
		{
			yield return "deaf";
		}

		if (patch.NoiseSuppression is not null)
		{
			yield return "noise_suppression";
		}

		if (patch.EchoCancellation is not null)
		{
			yield return "echo_cancellation";
		}

		if (patch.AutomaticGainControl is not null)
		{
			yield return "automatic_gain_control";
		}

		if (patch.Qos is not null)
		{
			yield return "qos";
		}

		if (patch.Input?.Volume is not null)
		{
			yield return "input.volume";
		}

		if (patch.Output?.Volume is not null)
		{
			yield return "output.volume";
		}

		if (patch.Mode?.Type is not null)
		{
			yield return "mode.type";
		}
	}
}
