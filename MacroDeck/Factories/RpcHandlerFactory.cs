using SuchByte.MacroDeck.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Factories;

public class RpcHandlerFactory : IRpcHandlerFactory
{
    private readonly Func<IEnumerable<IRpcHandler>> _factory;

    public RpcHandlerFactory(Func<IEnumerable<IRpcHandler>> factory)
    {
        _factory = factory;
    }

    public bool TryGet(string command, out IRpcHandler? rpcHandler)
    {
        rpcHandler = _factory().FirstOrDefault(x => x.Command.Equals(command, StringComparison.OrdinalIgnoreCase));
        return rpcHandler is not null;
    }
}