using MacroDeck.RPC;
using SuchByte.MacroDeck.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Server.V3;

public class RpcDispatcher : IObserver<RpcAction>
{
    private readonly IRpcHandlerFactory _rpcHandlerFactory;

    public RpcDispatcher(
        IRpcHandlerFactory rpcHandlerFactory
    )
    {
        _rpcHandlerFactory = rpcHandlerFactory;
    }

    public void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public void OnError(Exception error)
    {
        throw new NotImplementedException();
    }

    public void OnNext(RpcAction action)
    {
        if (!_rpcHandlerFactory.TryGet(action.Request.Method, out var rpcHandler)) return;
        var result = rpcHandler!.Do(action.Request.ParamsNode);
        _ = action.ReturnValueCallbackAsync(result);
    }
}