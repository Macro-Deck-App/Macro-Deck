using MacroDeckHost.Application.Actions.Options;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public class AudioDevicesOptionsSource : IHostOptionsSource
{
	public string Id => "system.audio-devices";

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		// Platform-specific device enumeration (CoreAudio/WASAPI) follows; empty list until then,
		// so the contract for integrations stays stable.
		return Task.FromResult(new DynamicOptionsResult
		{
			Options = [],
			AllowsCustomValue = true
		});
	}
}
