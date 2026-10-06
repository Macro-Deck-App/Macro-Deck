using System.Collections.Concurrent;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube;

internal sealed class YouTubeAccountConnection : IDisposable
{
	private readonly ILogger _logger;
	private readonly YouTubeEventEmitter? _emitter;
	private readonly IStreamChatSink? _chatSink;
	private readonly YouTubePollerOptions _options;
	private readonly Lock _sync = new();
	private readonly ConcurrentDictionary<(string LiveChatId, string ChannelId), string> _bans = new();
	private readonly HashSet<string> _ended = new(StringComparer.Ordinal);

	private volatile YouTubeAccountState _state = YouTubeAccountState.Unknown;
	private YouTubeStatePoller? _poller;
	private YouTubeChatPoller? _chat;
	private bool _authorizationLost;
	private bool _stopped;

	public YouTubeAccountConnection(
		YouTubeAccount account,
		YouTubeTokenProvider? tokens,
		IYouTubeOAuthClient? oauthClient,
		IYouTubeApiClient api,
		YouTubeQuotaBudget budget,
		YouTubeEventEmitter? emitter,
		ILogger logger,
		IStreamChatSink? chatSink = null,
		YouTubePollerOptions? options = null)
	{
		Account = account;
		Tokens = tokens;
		OAuthClient = oauthClient;
		Api = api;
		Budget = budget;
		_emitter = emitter;
		_logger = logger;
		_chatSink = chatSink;
		_options = options ?? new YouTubePollerOptions();
	}

	public YouTubeAccount Account { get; }

	public YouTubeTokenProvider? Tokens { get; }

	public IYouTubeOAuthClient? OAuthClient { get; }

	public IYouTubeApiClient Api { get; }

	internal YouTubePollerOptions Options => _options;

	public YouTubeQuotaBudget Budget { get; }

	public YouTubeAccountState State => _state;

	public bool NeedsReauthorization => Tokens?.NeedsReauthorization == true || _authorizationLost;

	public string? ActiveLiveChatId
	{
		get
		{
			lock (_sync)
			{
				return _chat is { HasEnded: false } chat ? chat.LiveChatId : null;
			}
		}
	}

	public void Start()
	{
		lock (_sync)
		{
			if (_stopped || _poller is not null)
			{
				return;
			}

			_poller = CreateStatePoller();
		}

		_poller.Start();
	}

	public YouTubeAccountState Merge(Func<YouTubeAccountState, YouTubeAccountState> update)
	{
		lock (_sync)
		{
			var next = update(_state);
			_state = next;
			return next;
		}
	}

	public void RememberBan(string liveChatId, string channelId, string banId)
		=> _bans[(liveChatId, channelId)] = banId;

	public string? FindBan(string liveChatId, string channelId)
		=> _bans.TryGetValue((liveChatId, channelId), out var banId) ? banId : null;

	public void ForgetBan(string liveChatId, string channelId) => _bans.TryRemove((liveChatId, channelId), out _);

	public void PostChat(ChatEvent chatEvent) => _chatSink?.Post(chatEvent);

	public void Dispose()
	{
		StopPollers();
		Tokens?.Dispose();
		OAuthClient?.Dispose();
		(Api as IDisposable)?.Dispose();
	}

	public async Task StopAsync()
	{
		StopPollers();
		if (Tokens is not null)
		{
			await Tokens.StopAsync();
			Tokens.Dispose();
		}

		OAuthClient?.Dispose();
		(Api as IDisposable)?.Dispose();
	}

	internal YouTubeStatePoller CreateStatePoller()
		=> new(Api,
			Budget,
			Account,
			Merge,
			OnTransition,
			OnPolled,
			OnAuthorizationLost,
			_logger,
			_options);

	internal void OnPolled(YouTubeAccountState state)
	{
		YouTubeChatPoller? started = null;
		YouTubeChatPoller? stopped = null;

		lock (_sync)
		{
			if (_stopped)
			{
				return;
			}

			var wanted = state.IsLive == true ? state.LiveChatId : null;
			if (_chat is not null && !string.Equals(_chat.LiveChatId, wanted, StringComparison.Ordinal))
			{
				stopped = _chat;
				_chat = null;
			}

			if (_chat is null && wanted is not null && !_ended.Contains(wanted))
			{
				_chat = CreateChatPoller(wanted);
				started = _chat;
			}
		}

		if (stopped is not null)
		{
			stopped.Dispose();
			_chatSink?.Post(new ChatConnectionChanged(Account.ChannelId, false));
		}

		if (started is not null)
		{
			_chatSink?.Post(new ChatConnectionChanged(Account.ChannelId, true));
			started.Start();
		}
	}

	internal YouTubeChatPoller CreateChatPoller(string liveChatId)
		=> new(Api,
			Budget,
			liveChatId,
			OnChatMessages,
			() => OnChatEnded(liveChatId),
			OnAuthorizationLost,
			_logger,
			_options);

	internal void OnChatMessages(IReadOnlyList<YouTubeChatMessage> messages, bool live)
	{
		foreach (var message in messages)
		{
			if (YouTubeChatMapper.ToChatEvent(Account.ChannelId, message) is { } chatEvent)
			{
				_chatSink?.Post(chatEvent);
			}

			if (live)
			{
				_emitter?.PublishChatEvent(Account, message);
			}
		}
	}

	internal void OnChatEnded(string liveChatId)
	{
		lock (_sync)
		{
			_ended.Add(liveChatId);
		}

		_chatSink?.Post(new ChatConnectionChanged(Account.ChannelId, false));
		_logger.Information("The YouTube live chat of {Channel} ended", Account.ChannelId);
	}

	internal void OnTransition(YouTubeLiveTransition transition)
	{
		if (transition.IsLive)
		{
			_emitter?.PublishStreamOnline(Account, transition.State);
		}
		else
		{
			_emitter?.PublishStreamOffline(Account);
		}
	}

	private void OnAuthorizationLost()
	{
		if (_authorizationLost)
		{
			return;
		}

		_authorizationLost = true;
		_logger.Warning("YouTube authorization lost for {Channel}", Account.ChannelId);
	}

	private void StopPollers()
	{
		YouTubeStatePoller? poller;
		YouTubeChatPoller? chat;

		lock (_sync)
		{
			_stopped = true;
			poller = _poller;
			chat = _chat;
			_poller = null;
			_chat = null;
		}

		poller?.Dispose();
		chat?.Dispose();
	}
}
