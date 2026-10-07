using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.SoundPad;

internal sealed class SoundPadConfigFlow : IConfigFlow
{
	private const string ConnectStepId = "connect";

	private const string WebsiteUrl = "https://www.leppsoft.com/soundpad/";

	private static readonly ILogger _logger =
		IntegrationLog.For<SoundPadConfigFlow>(SoundPadIntegration.IntegrationId);

	private readonly Func<ISoundPadClient> _clientFactory;
	private readonly Func<SoundPadConnection?> _liveConnection;

	internal SoundPadConfigFlow(Func<ISoundPadClient> clientFactory, Func<SoundPadConnection?> liveConnection)
	{
		_clientFactory = clientFactory;
		_liveConnection = liveConnection;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != ConnectStepId)
		{
			return ConfigFlowResult.Error(ConnectStep(), AppStrings.Integrations.SoundPad.Config.UnknownStep());
		}

		return await IsReachableAsync(cancellationToken)
			? ConfigFlowResult.Complete("SoundPad")
			: ConfigFlowResult.Error(ConnectStep(), AppStrings.Integrations.SoundPad.Config.NotReachable());
	}

	private async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
	{
		// SoundPad may serve one remote-control client at a time, so the running integration's own pipe
		// answers for it instead of a second client competing for the pipe.
		if (_liveConnection() is { IsConnected: true })
		{
			return true;
		}

		using var connection = new SoundPadConnection(_clientFactory);
		connection.Start();

		try
		{
			var version = await connection.RunAsync(c => c.GetVersionAsync(),
				cancellationToken,
				connectOnDemand: true);
			_logger.Information("SoundPad {Version} answered the setup check", version);
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Information(ex, "SoundPad did not answer the setup check");
			return false;
		}
	}

	private static ConfigFlowStep ConnectStep()
		=> new()
		{
			StepId = ConnectStepId,
			Title = AppStrings.Integrations.SoundPad.Config.Title(),
			Description = AppStrings.Integrations.SoundPad.Config.Description(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = AppStrings.Integrations.SoundPad.Config.StartSoundPad() },
				new ConfigFlowInstruction { Text = AppStrings.Integrations.SoundPad.Config.KeepRunning() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.SoundPad.Config.WebsiteLinkLabel(), Url = WebsiteUrl
				}
			],
			Fields = []
		};
}
