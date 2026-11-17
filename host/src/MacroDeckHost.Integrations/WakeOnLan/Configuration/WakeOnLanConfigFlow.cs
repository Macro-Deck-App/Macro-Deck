using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.WakeOnLan.Config;

namespace MacroDeckHost.Integrations.WakeOnLan.Configuration;

internal sealed class WakeOnLanConfigFlow : IConfigFlow
{
	private const string DeviceStepId = "device";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return Task.FromResult(ConfigFlowResult.Step(DeviceStep(context)));
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != DeviceStepId)
		{
			return Task.FromResult(ConfigFlowResult.Error(DeviceStep(context), Strings.UnknownStep()));
		}

		var name = (context as IConfigFlowEntryContext)?.EntryTitle
			?? (input.GetValueOrDefault(WakeOnLanConfigKeys.Name) as string ?? string.Empty).Trim();
		var mac = input.GetValueOrDefault(WakeOnLanConfigKeys.MacAddress) as string;
		var address = input.GetValueOrDefault(WakeOnLanConfigKeys.BroadcastAddress) as string;
		var port = WakeOnLanDevice.ParsePort(input.GetValueOrDefault(WakeOnLanConfigKeys.Port));
		var packets = WakeOnLanDevice.ParsePackets(input.GetValueOrDefault(WakeOnLanConfigKeys.PacketCount));
		var delay = WakeOnLanDevice.ParseDelay(input.GetValueOrDefault(WakeOnLanConfigKeys.PacketDelay));

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[WakeOnLanConfigKeys.Name] = AppStrings.Errors.Config.TitleRequired();
		}

		if (!WakeOnLanDevice.TryParseMac(mac, out var macBytes))
		{
			fieldErrors[WakeOnLanConfigKeys.MacAddress] = Strings.MacInvalid();
		}

		if (!WakeOnLanDevice.TryParseBroadcastAddress(address, out var broadcast))
		{
			fieldErrors[WakeOnLanConfigKeys.BroadcastAddress] = Strings.BroadcastInvalid();
		}

		if (port is null)
		{
			fieldErrors[WakeOnLanConfigKeys.Port] = Strings.PortInvalid();
		}

		if (packets is null)
		{
			fieldErrors[WakeOnLanConfigKeys.PacketCount] = Strings.PacketsInvalid();
		}

		if (delay is null)
		{
			fieldErrors[WakeOnLanConfigKeys.PacketDelay] = Strings.DelayInvalid();
		}

		if (fieldErrors.Count > 0)
		{
			return Task.FromResult(ConfigFlowResult.Error(DeviceStep(context), fieldErrors: fieldErrors));
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[WakeOnLanConfigKeys.MacAddress] = ConfigFlowValue.Plain(WakeOnLanDevice.FormatMac(macBytes)),
			[WakeOnLanConfigKeys.BroadcastAddress] = ConfigFlowValue.Plain(broadcast.ToString()),
			[WakeOnLanConfigKeys.Port] = ConfigFlowValue.Plain(Format(port!.Value)),
			[WakeOnLanConfigKeys.PacketCount] = ConfigFlowValue.Plain(Format(packets!.Value)),
			[WakeOnLanConfigKeys.PacketDelay] = ConfigFlowValue.Plain(Format(delay!.Value))
		};

		return Task.FromResult(ConfigFlowResult.Complete(name, values));
	}

	private static string Format(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}

	private static ConfigFlowStep DeviceStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(WakeOnLanConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: AppStrings.Integrations.Detail.ConfigurationNamePlaceholder(),
				required: true));
		}

		fields.AddRange([
			ActionParameter.Text(WakeOnLanConfigKeys.MacAddress,
				Strings.MacLabel(),
				Strings.MacDescription(),
				"AA:BB:CC:DD:EE:FF",
				required: true),
			ActionParameter.Text(WakeOnLanConfigKeys.BroadcastAddress,
				Strings.BroadcastLabel(),
				Strings.BroadcastDescription(),
				defaultValue: WakeOnLanDevice.DefaultBroadcastAddress),
			ActionParameter.Number(WakeOnLanConfigKeys.Port,
				Strings.PortLabel(),
				Strings.PortDescription(),
				WakeOnLanDevice.MinPort,
				WakeOnLanDevice.MaxPort,
				1,
				WakeOnLanDevice.DefaultPort),
			ActionParameter.Number(WakeOnLanConfigKeys.PacketCount,
				Strings.PacketsLabel(),
				Strings.PacketsDescription(),
				WakeOnLanDevice.MinPackets,
				WakeOnLanDevice.MaxPackets,
				1,
				WakeOnLanDevice.DefaultPackets),
			ActionParameter.Number(WakeOnLanConfigKeys.PacketDelay,
				Strings.DelayLabel(),
				Strings.DelayDescription(),
				WakeOnLanDevice.MinDelayMilliseconds,
				WakeOnLanDevice.MaxDelayMilliseconds,
				1,
				WakeOnLanDevice.DefaultDelayMilliseconds)
		]);

		return new ConfigFlowStep
		{
			StepId = DeviceStepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Fields = fields
		};
	}
}
