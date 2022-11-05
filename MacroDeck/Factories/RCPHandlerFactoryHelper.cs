using System;
using System.Collections.Generic;
using System.Linq;
using CefSharp.DevTools.Profiler;
using SuchByte.MacroDeck.Interfaces;

namespace SuchByte.MacroDeck.Factories;

public class RCPHandlerFactoryHelper
{
    // private readonly IDeckService DeckService;
    // private readonly IPageService PageService;
    // private readonly IProfileService ProfileService;
    // private readonly IWidgetService WidgetService;
    private readonly IEnumerable<Type> handlers;
    
    public RCPHandlerFactoryHelper(/*Add IServices*/)
    {
        handlers =
            from type in typeof(RpcHandlerFactory).Assembly.GetTypes()
            where type.IsClass && !type.IsAbstract && 
                  type.IsAssignableTo(typeof(IRpcHandler))
            select type;
    }
    public IEnumerable<IRpcHandler> GetHandlers()
    {
        return handlers.Select(handler => (IRpcHandler)Activator.CreateInstance(handler, GetInitializationService(handler)));
    }

    private object GetInitializationService(Type handler)
    {
        // if (handler.IsAssignableTo(RpcProfileHandler))
        // {
        //     return ProfileService;
        // }
        // else if (handler.IsAssignableTo(RpcPageHandler))
        // {
        //     return PageService;
        // }
        // else if (handler.IsAssignableTo(RpcDeckHandler))
        // {
        //     return DeckService;
        // }
        // else if (handler.IsAssignableTo(RpcWidgetHandler))
        // {
        //     return WidgetService;
        // }
        return null;
    }
}