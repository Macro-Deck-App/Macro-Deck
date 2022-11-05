using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MacroDeck.RPC.Exceptions;

namespace SuchByte.MacroDeck.Interfaces;

public interface IRpcHandler
{
    string Command { get; }

    object? Do(JsonNode? args);


    [DoesNotReturn]
    protected static void ThrowIfParamsNull(JsonNode? args)
    {
        if (args is null)
        {
            throw new ActionInvalidRequestException("Params");
        }
    }
}