using Mediator;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingMediator : IMediator
{
	public List<object> Published { get; } = new();

	public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
		where TNotification : INotification
	{
		if (notification is not null)
		{
			Published.Add(notification);
		}

		return default;
	}

	public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
	{
		Published.Add(notification);
		return default;
	}

	public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamRequest<TResponse> request,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamCommand<TResponse> command,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
		IStreamQuery<TResponse> query,
		CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();
}
