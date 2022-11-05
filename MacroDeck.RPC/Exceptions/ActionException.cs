using MacroDeck.RPC.Enum;

namespace MacroDeck.RPC.Exceptions;

public class ActionException : System.Exception
{
    public readonly ErrorCode Code;

    public ActionException() : this(ErrorCode.ActionGeneric, "Error occurred during RPC action")
    {
    }

    public ActionException(string message) 
        : this(ErrorCode.Generic, message)
    {
        
    }

    public ActionException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }
}