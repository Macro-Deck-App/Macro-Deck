namespace MacroDeckHost.Application.Ui.Transport;

public interface IUiTransportMessageHandler<in TRequest, TResponse>
	where TRequest : class
	where TResponse : class
{
	ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
