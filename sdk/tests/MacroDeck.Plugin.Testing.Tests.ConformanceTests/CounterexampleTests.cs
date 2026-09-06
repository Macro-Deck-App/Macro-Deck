using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;
using MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// A check that can never fail is worse than no check. Each test here builds the misbehaving fixture with
/// exactly one <see cref="MisbehaviorFlags" /> token and proves the one check that behaviour targets
/// actually turns red - not merely that "some" check somewhere reacts.
///
/// <para>
/// One violator the issue names is deliberately not exercised here: "a custom <c>ICapabilityHandler</c>
/// that replies twice". Reading <c>CapabilityDispatcher.DispatchAsync</c> directly shows its single-reply
/// guarantee is enforced by an <c>Interlocked.Exchange</c> latch (<c>Invocation.TryComplete</c>) that runs
/// entirely inside the dispatcher - a handler only ever returns one <c>CapabilityInvocationResult</c> and
/// has no access to a second send. There is no public extensibility seam left to build this violator
/// through, so MDC0501 stays a regression guard (see <c>TimeoutAndCancellationChecks.cs</c>'s own remarks)
/// rather than something exercised here.
/// </para>
/// </summary>
[TestFixture]
internal sealed class CounterexampleTests
{
	[Test]
	public async Task IgnoringCancellationMakesDeadlineCheckFail()
		=> await AssertCheckFailsAsync("MDC0502", MisbehaviorFlags.IgnoreCancellation);

	[Test]
	public async Task DuplicateInstanceIdsMakeDuplicateProviderInstanceCheckFail()
		=> await AssertCheckFailsAsync("MDC0402", MisbehaviorFlags.DuplicateInstanceIds);

	[Test]
	public async Task InvalidInstanceIdMakesRuntimeInstanceIdCheckFail()
		=> await AssertCheckFailsAsync("MDC0103", MisbehaviorFlags.InvalidInstanceId);

	[Test]
	public async Task DuplicateVariableDefinitionIdsMakeDuplicateVariableCheckFail()
		=> await AssertCheckFailsAsync("MDC0403", MisbehaviorFlags.DuplicateVariableDefinitionIds);

	[Test]
	public async Task OversizeDescribeMakesMessageSizeCheckFail()
		=> await AssertCheckFailsAsync("MDC0305", MisbehaviorFlags.OversizeDescribe);

	[Test]
	public async Task NonIdempotentInitMakesNonResumeReconnectCheckFail()
		=> await AssertCheckFailsAsync("MDC0603", MisbehaviorFlags.NonIdempotentInit);

	[Test]
	public async Task BypassLoggingPipelineMakesErrorSurvivesFloodCheckFail()
		=> await AssertCheckFailsAsync("MDC0802", MisbehaviorFlags.BypassLoggingPipeline);

	[Test]
	public async Task DuplicateStateIdsMakeActionStateSnapshotCheckFail()
		=> await AssertCheckFailsAsync("MDC0309", MisbehaviorFlags.DuplicateStateIds);

	[Test]
	public async Task InvalidStateIdMakesActionStateIdCheckFail()
		=> await AssertCheckFailsAsync("MDC0310", MisbehaviorFlags.InvalidStateId);

	[Test]
	public async Task InconsistentIconSnapshotMakesActionIconSnapshotCheckFail()
		=> await AssertCheckFailsAsync("MDC0312", MisbehaviorFlags.InconsistentIconSnapshot);

	[Test]
	public async Task NonImageIconContentMediaTypeMakesActionIconContentCheckFail()
		=> await AssertCheckFailsAsync("MDC0313", MisbehaviorFlags.NonImageIconMediaType);

	/// <summary>
	/// The documented silent foot-gun (B7): a plugin that overrides <c>ASPNETCORE_URLS</c> before building
	/// never becomes an observable conformance subject at all. <c>MacroDeckTestHost.LaunchAsync</c> fails
	/// outright while probing the address its launcher reserved - the same symptom a real supervisor's
	/// health probe would see. There is no check id for this: a check only ever runs once a session already
	/// exists, and this subject never reaches one.
	/// </summary>
	[Test]
	public async Task SelfSetUrlsMakesTheSubjectUnreachable()
	{
		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable());
		spec.Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["MACRODECK_MISBEHAVE"] = MisbehaviorFlags.SelfSetUrls
		};

		var subject = ConformanceSubject.Executable(spec);
		var runner = new ConformanceRunner();

		try
		{
			// CatchAsync, not ThrowsAsync: ThrowsAsync<Exception> demands the exact type
			// System.Exception, never a subtype, and the real failure here is the documented one -
			// PluginTestTimeoutException, from LaunchAsync giving up on a health probe that never
			// answers because the plugin is listening somewhere else entirely. Asserting "some
			// exception" is the point; which concrete type is not.
			Assert.CatchAsync<Exception>(async () => await runner.RunAsync(subject));
		}
		finally
		{
			await subject.DisposeAsync();
		}
	}

	/// <summary>
	/// Not a check turning red: MDC0204 manages its own two-launch dance and always shares one state
	/// directory by construction, so nothing external can make it observe two different ones. This instead
	/// proves the underlying fact MDC0204 depends on is real - without a shared state directory, a
	/// self-registering subject genuinely does register again on every start.
	/// </summary>
	[Test]
	public async Task FreshStateDirectoryPerStartProducesTwoRegistrations()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		using var firstStateDirectory = new TempStateDirectory();
		var firstBuilder = MacroDeckPlugin.CreatePlugin();
		WellBehavedPluginComposition.Configure(firstBuilder);
		firstBuilder.Configuration[$"{PluginHostOptions.SectionName}:StateDirectory"] = firstStateDirectory.Path;

		await using (var first = await host.HostAsync(firstBuilder, PluginTestCredentials.SelfRegistering))
		{
			await host.WaitForSessionAsync();
			_ = first;
		}

		using var secondStateDirectory = new TempStateDirectory();
		var secondBuilder = MacroDeckPlugin.CreatePlugin();
		WellBehavedPluginComposition.Configure(secondBuilder);
		secondBuilder.Configuration[$"{PluginHostOptions.SectionName}:StateDirectory"] = secondStateDirectory.Path;

		await using (var second = await host.HostAsync(secondBuilder, PluginTestCredentials.SelfRegistering))
		{
			await host.WaitForSessionAsync();
			_ = second;
		}

		Assert.That(host.Registrations.Count,
			Is.EqualTo(2),
			"Two starts under two different state directories should register twice - proof that MDC0204's own " +
			"shared-directory mechanism is what makes 'registers exactly once across two runs' a meaningful assertion.");
	}

	/// <summary>
	/// A check that can never fail is worthless - MDC0206 included. A subject with pairing disabled and
	/// no enrollment token can never obtain a credential at all, so the session this check itself opens
	/// with <see cref="PluginTestCredentials.Pairing" /> never completes its handshake.
	/// </summary>
	[Test]
	public async Task PairingDisabledWithNoEnrollmentTokenMakesInteractivePairingCheckFail()
	{
		var subject = ConformanceSubject.InProcess(builder =>
		{
			WellBehavedPluginComposition.Configure(builder);
			builder.Configuration[$"{PluginHostOptions.SectionName}:PairingEnabled"] = "false";
		});

		var runner = new ConformanceRunner(new ConformanceOptions { Ids = ["MDC0206"] });

		ConformanceReport report;

		try
		{
			report = await runner.RunAsync(subject);
		}
		finally
		{
			await subject.DisposeAsync();
		}

		var outcome = report.Results.Single();

		Assert.That(outcome.Id, Is.EqualTo("MDC0206"));

		Assert.That(outcome.Result.Outcome,
			Is.EqualTo(ConformanceOutcome.Failed),
			() => $"Expected pairing disabled with no enrollment token to make MDC0206 fail, but it reported " +
				$"{outcome.Result.Outcome}" +
				(outcome.Result.SkipReason is { Length: > 0 } reason ? $" ({reason})" : string.Empty) +
				".");
	}

	private static async Task AssertCheckFailsAsync(string checkId, string flag)
	{
		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable());
		spec.Environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["MACRODECK_MISBEHAVE"] = flag };

		var subject = ConformanceSubject.Executable(spec);
		var runner = new ConformanceRunner(new ConformanceOptions { Ids = [checkId] });

		ConformanceReport report;

		try
		{
			report = await runner.RunAsync(subject);
		}
		finally
		{
			await subject.DisposeAsync();
		}

		var outcome = report.Results.Single();

		Assert.That(outcome.Id, Is.EqualTo(checkId));

		Assert.That(outcome.Result.Outcome,
			Is.EqualTo(ConformanceOutcome.Failed),
			() => $"Expected '{flag}' to make {checkId} fail, but it reported {outcome.Result.Outcome}" +
				(outcome.Result.SkipReason is { Length: > 0 } reason ? $" ({reason})" : string.Empty) +
				".");
	}
}
