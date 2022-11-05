using MacroDeck.RPC.Enum;

namespace MacroDeck.RPC.Exceptions;

public class ActionInvalidRequestException : ActionException
{
    public readonly string? ParamName;
    public readonly Type? ParamType;

    public ActionInvalidRequestException()
        : base(ErrorCode.InvalidParams, "Unexpected request format provided.")
    {
        
    }
    
    public ActionInvalidRequestException(string paramName, Type? paramType = null)
        : base(ErrorCode.InvalidParams, $"You must provide a \"{paramName}\" {(paramType?.Name ?? "")} node")
    {
        ParamName = paramName;
        ParamType = paramType;
    }
}