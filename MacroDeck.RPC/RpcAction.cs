namespace MacroDeck.RPC;

public class RpcAction
{
    public delegate Task ReturnValueAsyncDelegate(object? result);
    public readonly ReturnValueAsyncDelegate ReturnValueCallbackAsync;
    public readonly Request Request;

    public RpcAction(Request request, ReturnValueAsyncDelegate returnValueCallbackAsync)
    {
        Request = request;
        ReturnValueCallbackAsync = returnValueCallbackAsync;
    }
}