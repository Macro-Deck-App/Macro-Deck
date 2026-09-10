using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

internal sealed class FakeWidgetIconInvalidator : IWidgetIconInvalidator
{
	public List<(string IntegrationId, string ActionId)> Invalidations { get; } = [];

	public void Invalidate(string integrationId, string actionId) => Invalidations.Add((integrationId, actionId));
}

internal sealed class FakeIntegrationRegistry : IIntegrationRegistry
{
	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
	{
		add { }
		remove { }
	}

	private readonly List<IIntegration> _integrations = [];
	private readonly HashSet<string> _disabled = [];

	public IReadOnlyList<IIntegration> Integrations => _integrations;

	public void Add(IIntegration integration) => _integrations.Add(integration);

	public IActionDefinition? FindAction(string integrationId, string actionId)
		=> _integrations.FirstOrDefault(i => i.Id == integrationId)?
			.Actions.FirstOrDefault(a => a.Id == actionId && a.RunsHere());

	public IActionDefinition? FindAction(QualifiedId id) => FindAction(id.OwnerId, id.LocalId);

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true)
	{
		var descriptors = new List<ActionDescriptor>();
		foreach (var integration in _integrations)
		{
			if (enabledOnly && !IsEnabled(integration.Id))
			{
				continue;
			}

			foreach (var action in integration.Actions)
			{
				if (!action.RunsHere())
				{
					continue;
				}

				if (QualifiedId.TryCreate(integration.Id,
					action.Id,
					OwnerIdKind.Package,
					LocalIdKind.Declared,
					out var id))
				{
					descriptors.Add(new ActionDescriptor(id, integration, action));
				}
			}
		}

		return descriptors;
	}

	public bool IsEnabled(string integrationId) => !_disabled.Contains(integrationId);

	public void SetEnabled(string integrationId, bool enabled)
	{
		if (enabled)
		{
			_disabled.Remove(integrationId);
		}
		else
		{
			_disabled.Add(integrationId);
		}
	}

	public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

	public Task<IntegrationRegistrationResult> RegisterAsync(
		IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
	{
		_integrations.Add(integration);
		return Task.FromResult(IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId)
	{
		var integration = _integrations.FirstOrDefault(i => i.Id == integrationId);
		if (integration is null)
		{
			return Task.FromResult(false);
		}

		_integrations.Remove(integration);
		return Task.FromResult(true);
	}
}

internal sealed class CapturingActionDefinition : IActionDefinition
{
	public string Id { get; init; } = "capture";
	public LocalizedText Name => "Capture";
	public LocalizedText Description => "Captures execution parameters";
	public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];
	public MacroDeckPlatform Platforms { get; init; } = MacroDeckPlatform.All;

	public IReadOnlyDictionary<string, object>? CapturedParameters { get; private set; }

	public string? CapturedOwnerWidgetId { get; private set; }

	public int ExecuteCount { get; private set; }

	public ActionResult Result { get; set; } = ActionResult.Success();

	public Exception? Throw { get; set; }

	public Action? OnExecuted { get; set; }

	public IActionExecutor CreateExecutor() => new CapturingExecutor(this);

	private sealed class CapturingExecutor : IActionExecutor
	{
		private readonly CapturingActionDefinition _owner;

		public CapturingExecutor(CapturingActionDefinition owner)
		{
			_owner = owner;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			_owner.CapturedParameters = context.Parameters;
			_owner.CapturedOwnerWidgetId = context.OwnerWidgetId;
			_owner.ExecuteCount++;
			_owner.OnExecuted?.Invoke();
			return _owner.Throw is { } exception
				? Task.FromException<ActionResult>(exception)
				: Task.FromResult(_owner.Result);
		}
	}
}

/// <summary>
/// A configured state-provider action instance. <see cref="ExecuteCount" /> stays zero across every
/// test that only reads <see cref="GetActionStateAsync" /> - reporting state must never itself run the
/// action's side effect (scenario C8).
/// </summary>
internal sealed class FakeStateProviderAction : IActionDefinition, IStateProviderActionDefinition
{
	public string Id { get; init; } = "provide-state";
	public LocalizedText Name { get; init; } = "Provide State";
	public LocalizedText Description => string.Empty;
	public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

	public ActionStateSnapshot? SnapshotToReturn { get; set; }
	public Task<ActionStateSnapshot?>? SnapshotTask { get; set; }
	public ActionResult Result { get; set; } = ActionResult.Success();

	public int ExecuteCount { get; private set; }

	public int GetActionStateCallCount { get; private set; }

	public IReadOnlyDictionary<string, object?>? LastParameters { get; private set; }

	public TimeSpan StatePollInterval { get; set; } = TimeSpan.FromSeconds(2);

	TimeSpan IStateProviderActionDefinition.StatePollInterval => StatePollInterval;

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		GetActionStateCallCount++;
		LastParameters = parameters;
		return SnapshotTask ?? Task.FromResult(SnapshotToReturn);
	}

	public IActionExecutor CreateExecutor() => new Executor(this);

	private sealed class Executor : IActionExecutor
	{
		private readonly FakeStateProviderAction _owner;

		public Executor(FakeStateProviderAction owner) => _owner = owner;

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			_owner.ExecuteCount++;
			return Task.FromResult(_owner.Result);
		}
	}
}

internal sealed class ThrowingActionDefinition : IActionDefinition
{
	public string Id { get; init; } = "explode";
	public LocalizedText Name { get; init; } = "Explode";
	public LocalizedText Description => "Always throws";
	public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

	public IActionExecutor CreateExecutor() => new ThrowingExecutor();

	private sealed class ThrowingExecutor : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			=> throw new InvalidOperationException("boom");
	}
}

internal sealed class PassthroughConditionEvaluator : IActionConditionEvaluator
{
	public EvaluatedSide ResolveSide(JsonElement value, VariableContext variables)
		=> new(null, string.Empty);

	public bool Evaluate(JsonElement left, string? op, JsonElement right, VariableContext variables) => false;

	public bool EvaluateExpression(JsonElement expression,
		VariableContext variables,
		Action<ConditionLeafOutcome>? onLeaf = null)
		=> false;

	public string? RenderTemplateString(string? raw, VariableContext variables)
		=> raw?.Replace("{{ test }}", "rendered", StringComparison.Ordinal);
}

internal sealed class FakeVariableTemplateRenderer : IVariableTemplateRenderer
{
	public Task<string> RenderAsync(string templateText, VariableScope contextScope, string? contextScopeRefId)
		=> Task.FromResult(templateText);

	public Task<VariableContext> CreateContextAsync(VariableScope contextScope, string? contextScopeRefId)
		=> Task.FromResult(VariableContext.Empty);

	public string Render(string templateText, VariableContext context) => templateText;
}

internal sealed class FakeSecretService : ISecretService
{
	private readonly Dictionary<Guid, string> _secrets = [];
	private readonly Dictionary<Guid, SecretKind> _kinds = [];

	public int Count => _secrets.Count;

	public Guid Store(string value, SecretKind kind = SecretKind.Password)
	{
		var id = Guid.NewGuid();
		_secrets[id] = value;
		_kinds[id] = kind;
		return id;
	}

	public Task<Guid> Create(string value, SecretKind kind) => Task.FromResult(Store(value, kind));

	public Task<bool> Replace(Guid id, string value)
	{
		if (!_secrets.ContainsKey(id))
		{
			return Task.FromResult(false);
		}

		_secrets[id] = value;
		return Task.FromResult(true);
	}

	public Task<bool> Delete(Guid id)
	{
		_kinds.Remove(id);
		return Task.FromResult(_secrets.Remove(id));
	}

	public Task<Guid?> Clone(Guid id)
		=> Task.FromResult(_secrets.TryGetValue(id, out var value)
			? Store(value, _kinds.GetValueOrDefault(id, SecretKind.Password))
			: (Guid?)null);

	public Task<string?> Reveal(Guid id) => Resolve(id);

	public Task<string?> Resolve(Guid id)
		=> Task.FromResult(_secrets.TryGetValue(id, out var value) ? value : null);

	public Task<SecretMaterial?> ExportForArchive(Guid id)
		=> Task.FromResult(_secrets.TryGetValue(id, out var value)
			? new SecretMaterial(_kinds.GetValueOrDefault(id, SecretKind.Password), value)
			: null);
}

internal sealed class FakeHostListenerState : IHostListenerState
{
	private readonly PublicEndpointSet? _publicEndpoints;

	// Settable, not init-only: a test that exercises the public port changing under a running
	// coordinator has to move it without rebuilding the fake.
	private int _publicPort = BuildConfig.DefaultPublicPort;
	private readonly bool _publicListenerAvailable = true;

	public PublicEndpointSet PublicEndpoints
	{
		get => _publicEndpoints ??
			(_publicListenerAvailable
				? PublicEndpointSet.HttpOnly(_publicPort)
				: PublicEndpointSet.HttpOnly(_publicPort).WithoutHttp());
		init => _publicEndpoints = value;
	}

	public int PublicPort
	{
		get => _publicEndpoints?.PublicPort ?? _publicPort;
		set => _publicPort = value;
	}

	public bool PublicPortOverriddenByEnvironment { get; init; }

	public int? RefusedConfiguredPublicPort { get; init; }

	public int? LoopbackPort { get; set; }

	public bool PublicListenerAvailable
	{
		get => _publicEndpoints?.HasPublicListener ?? _publicListenerAvailable;
		init => _publicListenerAvailable = value;
	}

	public PublicTlsFailure TlsFailure { get; init; }

	public PublicTlsRejection TlsRejection { get; init; }

	public string? ActiveCertificateFingerprint { get; init; }

	public void SetLoopbackPort(int port) => LoopbackPort = port;
}

internal sealed class FakePublicTlsCertificateStore : IPublicTlsCertificateStore
{
	private (string CertificatePem, string PrivateKeyPem, PublicTlsCertificateInfo Info)? _stored;
	private (GeneratedCertificate Material, PublicTlsCertificateInfo Info)? _authority;

	public bool HasCertificate => _stored is not null;

	public PublicTlsCertificateInfo? ReadInfo() => _stored?.Info;

	public string? ReadCertificatePem() => _stored?.CertificatePem;

	public PublicTlsCertificateResolution LoadServerCertificate()
	{
		if (_stored is not { } stored)
		{
			return new PublicTlsCertificateResolution(null, PublicTlsFailure.NotConfigured);
		}

		var certificate = X509Certificate2.CreateFromPem(stored.CertificatePem, stored.PrivateKeyPem);
		return new PublicTlsCertificateResolution(certificate, PublicTlsFailure.None);
	}

	public PublicTlsCertificateInfo Save(string certificatePem, string privateKeyPem, PublicTlsCertificateSource source)
	{
		var result = PublicTlsCertificateValidator.Validate(certificatePem, privateKeyPem, source);
		if (!result.Valid || result.Certificate is null)
		{
			throw new ArgumentException(result.Error?.ToString() ?? "The certificate/key pair is invalid.");
		}

		_stored = (certificatePem, privateKeyPem, result.Certificate);
		return result.Certificate;
	}

	public PublicTlsCertificateInfo? ReadAuthorityInfo() => _authority?.Info;

	public string? ReadAuthorityCertificatePem() => _authority?.Material.CertificatePem;

	public PublicTlsAuthorityResolution LoadAuthority()
		=> _authority is { } authority
			? new PublicTlsAuthorityResolution(authority.Material, PublicTlsFailure.None)
			: new PublicTlsAuthorityResolution(null, PublicTlsFailure.NotConfigured);

	public PublicTlsCertificateInfo SaveAuthority(string certificatePem, string privateKeyPem)
	{
		using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
		var info = new PublicTlsCertificateInfo(certificate.Subject,
			certificate.GetCertHashString(HashAlgorithmName.SHA256),
			certificate.NotBefore,
			certificate.NotAfter,
			PublicTlsCertificateSource.LocalCa);

		_authority = (new GeneratedCertificate(certificatePem, privateKeyPem), info);
		return info;
	}

	public void Delete() => _stored = null;
}

internal sealed class NullWidgetVariableCloner : IWidgetVariableCloner
{
	public Task<IReadOnlyList<WidgetVariableSnapshot>> Snapshot(Guid widgetId)
		=> Task.FromResult<IReadOnlyList<WidgetVariableSnapshot>>([]);

	public Task Restore(Guid widgetId, IReadOnlyList<WidgetVariableSnapshot> variables) => Task.CompletedTask;

	public Task Clone(Guid sourceWidgetId, Guid targetWidgetId) => Task.CompletedTask;
}
