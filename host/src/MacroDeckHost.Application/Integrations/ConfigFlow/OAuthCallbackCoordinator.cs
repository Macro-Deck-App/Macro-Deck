using System.Collections.Concurrent;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public sealed class OAuthCallbackCoordinator : IOAuthCallbackCoordinator
{
	public const string CallbackPath = "/api/integrations/oauth/callback";

	private readonly IUiTransport _transport;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

	public OAuthCallbackCoordinator(IUiTransport transport, ILogger logger)
	{
		_transport = transport;
		_logger = logger.ForContext<OAuthCallbackCoordinator>();
	}

	public string RedirectUri
	{
		get
		{
			var endpoint = ResolvedPublicEndpoints.Value.LocalClientEndpoint;
			var scheme = endpoint?.Ssl == true ? "https" : "http";
			var port = endpoint?.Port ?? ResolvedPublicEndpoints.Value.PublicPort;

			return $"{scheme}://127.0.0.1:{port}{CallbackPath}";
		}
	}

	public OAuthRegistration Register(Guid flowId)
	{
		var state = Guid.NewGuid().ToString("N");
		_pending[state] = new Pending(flowId);
		return new OAuthRegistration(RedirectUri, state);
	}

	public string? GetCode(string state)
		=> _pending.TryGetValue(state, out var pending) ? pending.Code : null;

	public async Task<bool> HandleCallbackAsync(
		string state,
		string? code,
		string? errorCode,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(state) || !_pending.TryGetValue(state, out var pending))
		{
			_logger.Warning("Received OAuth callback for unknown state");
			return false;
		}

		pending.Code = code;

		await _transport.Send(new ConfigFlowAuthorizedNotification
			{
				FlowId = pending.FlowId.ToString(),
				Success = errorCode is null && !string.IsNullOrEmpty(code),
				Error = errorCode
			},
			cancellationToken);

		return true;
	}

	public void Release(string state)
	{
		if (!string.IsNullOrEmpty(state))
		{
			_pending.TryRemove(state, out _);
		}
	}

	private sealed class Pending
	{
		public Pending(Guid flowId)
		{
			FlowId = flowId;
		}

		public Guid FlowId { get; }

		public string? Code { get; set; }
	}
}
