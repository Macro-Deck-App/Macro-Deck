using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Feeds;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

internal sealed class FakeFeedServer : HttpMessageHandler
{
	private readonly Lock _sync = new();

	private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes =
		new(StringComparer.Ordinal);

	private readonly List<HttpRequestMessage> _requests = [];

	public IReadOnlyList<HttpRequestMessage> Requests
	{
		get
		{
			lock (_sync)
			{
				return [.. _requests];
			}
		}
	}

	public void Serve(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
	{
		lock (_sync)
		{
			_routes[url] = respond;
		}
	}

	public void ServeXml(string url, string xml, string? etag = null)
	{
		Serve(url,
			request =>
			{
				if (etag is not null && request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
				{
					return new HttpResponseMessage(HttpStatusCode.NotModified);
				}

				var response = new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml")
				};
				if (etag is not null)
				{
					response.Headers.ETag = new EntityTagHeaderValue(etag);
				}

				return response;
			});
	}

	public void Redirect(string url, string location, HttpStatusCode status = HttpStatusCode.Found)
	{
		Serve(url,
			_ =>
			{
				var response = new HttpResponseMessage(status);
				response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
				return response;
			});
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Func<HttpRequestMessage, HttpResponseMessage>? respond;
		lock (_sync)
		{
			_requests.Add(request);
			_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out respond);
		}

		return Task.FromResult(respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

internal sealed class FakeResolver : IFeedAddressResolver
{
	private readonly Dictionary<string, IPAddress[]> _hosts = new(StringComparer.OrdinalIgnoreCase);

	public FakeResolver Map(string host, params string[] addresses)
	{
		_hosts[host] = [.. addresses.Select(IPAddress.Parse)];
		return this;
	}

	public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
	{
		var bare = host.Trim('[', ']');
		if (IPAddress.TryParse(bare, out var literal))
		{
			return Task.FromResult<IReadOnlyList<IPAddress>>([literal]);
		}

		return Task.FromResult<IReadOnlyList<IPAddress>>(_hosts.TryGetValue(bare, out var mapped)
			? mapped
			: [IPAddress.Parse("93.184.216.34")]);
	}
}

internal sealed class MemoryStorage : IIntegrationStorage
{
	private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, string> Documents => _documents;

	public Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		if (!_documents.TryGetValue(key, out var content))
		{
			return Task.FromResult(IntegrationStorageReadResult.Missing);
		}

		return Task.FromResult(isValid is null || isValid(content)
			? IntegrationStorageReadResult.Found(content)
			: IntegrationStorageReadResult.Unreadable);
	}

	public Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		_documents[key] = content;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		_documents.Remove(key);
		return Task.CompletedTask;
	}
}

internal sealed class RecordingEvents : IEventPublisher
{
	private readonly Lock _sync = new();
	private readonly List<(string Id, IReadOnlyDictionary<string, object?> Payload)> _published = [];

	public IReadOnlyList<(string Id, IReadOnlyDictionary<string, object?> Payload)> Published
	{
		get
		{
			lock (_sync)
			{
				return [.. _published];
			}
		}
	}

	public IReadOnlyList<IReadOnlyDictionary<string, object?>> Of(string eventId)
	{
		return [.. Published.Where(entry => entry.Id == eventId).Select(entry => entry.Payload)];
	}

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		lock (_sync)
		{
			_published.Add((eventId, parameters ?? new Dictionary<string, object?>()));
		}
	}
}

internal sealed class FeedsTestContext : IIntegrationContext, IIntegrationConfig, IVariableApi
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];

	public FeedsTestContext(MemoryStorage? storage = null)
	{
		Storage = storage ?? new MemoryStorage();
	}

	public RecordingEvents RecordedEvents { get; } = new();

	public IIntegrationStorage Storage { get; }

	public IUiResourceRegistry UiResources { get; } =
		new BuiltInUiResources(FeedsIntegration.IntegrationId, new UiResourceStore(), typeof(FeedsIntegration));

	public IIntegrationConfig Config => this;

	public IVariableApi Variables => this;

	public IEventPublisher Events => RecordedEvents;

	public IUserVariableApi UserVariables => throw new NotSupportedException();

	public IDeckNavigator Deck => throw new NotSupportedException();

	public IScriptApi Scripts => throw new NotSupportedException();

	public IWidgetApi Widgets => throw new NotSupportedException();

	public IUserNotifier Notifications => throw new NotSupportedException();

	public Guid AddFeed(
		string title,
		string url,
		string key,
		Action<Dictionary<string, string?>>? configure = null,
		Guid? id = null)
	{
		var entryId = id ?? Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));
		var values = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[FeedConfigKeys.Url] = url,
			[FeedConfigKeys.VariableKey] = key
		};
		configure?.Invoke(values);
		foreach (var (name, value) in values)
		{
			_strings[(entryId, name)] = value;
		}

		return entryId;
	}

	public void Set(Guid entryId, string key, string? value)
	{
		_strings[(entryId, key)] = value;
	}

	public void Remove(Guid entryId)
	{
		_entries.RemoveAll(entry => entry.Id == entryId);
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([.. _entries]);
	}

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_strings.GetValueOrDefault((entryId, key)));
	}

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<string?>(null);
	}

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		_strings[(entryId, key)] = value;
		return Task.CompletedTask;
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		return Task.FromResult<IReadOnlyList<VariableHandle>>([]);
	}

	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		return Task.FromResult<VariableHandle?>(null);
	}

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		throw new NotSupportedException();
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		throw new NotSupportedException();
	}

	public Task DeleteAsync(Guid variableId)
	{
		return Task.CompletedTask;
	}
}

internal sealed class RecordingOrigin : IUiEventOrigin
{
	private readonly Lock _sync = new();
	private readonly List<Uri> _openedUrls = [];
	private readonly List<ModalDefinition> _modals = [];

	public bool OpensUrls { get; init; } = true;

	public string? ClientId => "client-1";

	public IReadOnlyList<Uri> OpenedUrls
	{
		get
		{
			lock (_sync)
			{
				return [.. _openedUrls];
			}
		}
	}

	public IReadOnlyList<ModalDefinition> Modals
	{
		get
		{
			lock (_sync)
			{
				return [.. _modals];
			}
		}
	}

	public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(true);
	}

	public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_modals.Add(modal);
		}

		return Task.FromResult(true);
	}

	public Task<ModalResult<T>> ShowModalAsync<T>(ModalDefinition modal, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(ModalResult.FromCancellation<T>());
	}

	public Task<bool> OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
	{
		if (!OpensUrls)
		{
			return Task.FromResult(false);
		}

		lock (_sync)
		{
			_openedUrls.Add(url);
		}

		return Task.FromResult(true);
	}
}

internal static class FeedXml
{
	public static string Rss(params (string Guid, string Title, string Link, string PubDate)[] items)
	{
		return Rss(items.Select(item => Item(item.Guid, item.Title, item.Link, item.PubDate)).ToArray());
	}

	public static string Rss(IReadOnlyList<string> items)
	{
		return $"""
				<?xml version="1.0" encoding="utf-8"?>
				<rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
				  <channel>
				    <title>Example News</title>
				    <link>https://news.example.com/</link>
				    {string.Join("\n", items)}
				  </channel>
				</rss>
				""";
	}

	public static string Item(string guid, string title, string link, string pubDate, string extra = "")
	{
		return $"""
				<item>
				  <guid isPermaLink="false">{guid}</guid>
				  <title>{title}</title>
				  <link>{link}</link>
				  <pubDate>{pubDate}</pubDate>
				  {extra}
				</item>
				""";
	}
}

internal static class FeedsTestClients
{
	public static FeedClient Create(FakeFeedServer server, FakeResolver? resolver = null, TimeProvider? time = null)
	{
		return new FeedClient(_ => server, resolver ?? new FakeResolver(), time ?? TimeProvider.System);
	}
}
