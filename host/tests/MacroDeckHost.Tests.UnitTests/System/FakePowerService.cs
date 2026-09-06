using MacroDeckHost.Integrations.System.Power;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

internal sealed class FakePowerService : IPowerService
{
	public const string UnsupportedReason = "the mechanism is missing here";

	public FakePowerService(bool supported = true)
	{
		IsSupported = supported;
	}

	public bool IsSupported { get; }

	public HashSet<PowerOperation> UnsupportedOperations { get; } = [];

	public PowerResult Result { get; set; } = PowerResult.Succeeded();

	public PowerOperation? LastOperation { get; private set; }

	public bool? LastForce { get; private set; }

	public bool Supports(PowerOperation operation) => IsSupported && !UnsupportedOperations.Contains(operation);

	public Task<PowerResult> ExecuteAsync(
		PowerOperation operation,
		bool force,
		CancellationToken cancellationToken = default)
	{
		if (!Supports(operation))
		{
			return Task.FromResult(PowerResult.Failed(UnsupportedReason, ActionErrorCodes.Unavailable));
		}

		LastOperation = operation;
		LastForce = force;
		return Task.FromResult(Result);
	}
}
