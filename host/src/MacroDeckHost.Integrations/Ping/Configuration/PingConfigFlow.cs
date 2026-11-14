using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping.Config;

namespace MacroDeckHost.Integrations.Ping.Configuration;

public sealed class PingConfigFlow : IConfigFlow
{
	private const string TargetStepId = "target";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return Task.FromResult(ConfigFlowResult.Step(TargetStep(context)));
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(stepId == TargetStepId
			? SubmitTarget(input, context)
			: ConfigFlowResult.Error(TargetStep(context), Strings.UnknownStep()));
	}

	private static ConfigFlowResult SubmitTarget(IReadOnlyDictionary<string, object?> input, IConfigFlowContext context)
	{
		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = entryTitle ?? (input.GetValueOrDefault(PingConfigKeys.Name) as string ?? string.Empty).Trim();
		var host = PingTargetSettings.NormalizeHost(input.GetValueOrDefault(PingConfigKeys.Host) as string);
		var interval = ReadOrDefault(input,
			PingConfigKeys.IntervalSeconds,
			PingTargetSettings.DefaultIntervalSeconds);
		var timeout = ReadOrDefault(input,
			PingConfigKeys.TimeoutMilliseconds,
			PingTargetSettings.DefaultTimeoutMilliseconds);
		var threshold = ReadOrDefault(input,
			PingConfigKeys.FailureThreshold,
			PingTargetSettings.DefaultFailureThreshold);

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[PingConfigKeys.Name] = Strings.EnterName();
		}

		if (host is null)
		{
			fieldErrors[PingConfigKeys.Host] = Strings.EnterHost();
		}

		if (interval is not { } seconds
			|| seconds is < PingTargetSettings.MinimumIntervalSeconds or > PingTargetSettings.MaximumIntervalSeconds)
		{
			fieldErrors[PingConfigKeys.IntervalSeconds] = Strings.IntervalOutOfRange(
				Number(PingTargetSettings.MinimumIntervalSeconds),
				Number(PingTargetSettings.MaximumIntervalSeconds));
		}

		if (timeout is not { } milliseconds
			|| milliseconds is < PingTargetSettings.MinimumTimeoutMilliseconds
				or > PingTargetSettings.MaximumTimeoutMilliseconds)
		{
			fieldErrors[PingConfigKeys.TimeoutMilliseconds] = Strings.TimeoutOutOfRange(
				Number(PingTargetSettings.MinimumTimeoutMilliseconds),
				Number(PingTargetSettings.MaximumTimeoutMilliseconds));
		}
		else if (interval is { } wait && milliseconds >= wait * 1000)
		{
			fieldErrors[PingConfigKeys.TimeoutMilliseconds] = Strings.TimeoutNotBelowInterval();
		}

		if (threshold is not { } failures
			|| failures is < PingTargetSettings.MinimumFailureThreshold
				or > PingTargetSettings.MaximumFailureThreshold)
		{
			fieldErrors[PingConfigKeys.FailureThreshold] = Strings.FailureThresholdOutOfRange(
				Number(PingTargetSettings.MinimumFailureThreshold),
				Number(PingTargetSettings.MaximumFailureThreshold));
		}

		if (fieldErrors.Count > 0 || host is null)
		{
			return ConfigFlowResult.Error(TargetStep(context), fieldErrors: fieldErrors);
		}

		return ConfigFlowResult.Complete(name,
			new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
			{
				[PingConfigKeys.Host] = ConfigFlowValue.Plain(host)
			});
	}

	private static string Number(int value)
	{
		return value.ToString(CultureInfo.CurrentCulture);
	}

	private static int? ReadOrDefault(IReadOnlyDictionary<string, object?> input, string key, int fallback)
	{
		var value = input.GetValueOrDefault(key);
		return value is null or "" ? fallback : PingTargetSettings.ReadWholeNumber(value);
	}

	private static ConfigFlowStep TargetStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(PingConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.NamePlaceholder(),
				required: true));
		}

		fields.AddRange([
			ActionParameter.Text(PingConfigKeys.Host,
				Strings.HostLabel(),
				Strings.HostDescription(),
				"192.168.1.10",
				null,
				true),
			ActionParameter.Number(PingConfigKeys.IntervalSeconds,
				Strings.IntervalLabel(),
				Strings.IntervalDescription(),
				PingTargetSettings.MinimumIntervalSeconds,
				PingTargetSettings.MaximumIntervalSeconds,
				1,
				PingTargetSettings.DefaultIntervalSeconds),
			ActionParameter.Number(PingConfigKeys.TimeoutMilliseconds,
				Strings.TimeoutLabel(),
				Strings.TimeoutDescription(),
				PingTargetSettings.MinimumTimeoutMilliseconds,
				PingTargetSettings.MaximumTimeoutMilliseconds,
				100,
				PingTargetSettings.DefaultTimeoutMilliseconds),
			ActionParameter.Number(PingConfigKeys.FailureThreshold,
				Strings.FailureThresholdLabel(),
				Strings.FailureThresholdDescription(),
				PingTargetSettings.MinimumFailureThreshold,
				PingTargetSettings.MaximumFailureThreshold,
				1,
				PingTargetSettings.DefaultFailureThreshold)
		]);

		return new ConfigFlowStep
		{
			StepId = TargetStepId,
			Title = Strings.TargetTitle(),
			Description = Strings.TargetDescription(),
			Fields = fields
		};
	}
}
