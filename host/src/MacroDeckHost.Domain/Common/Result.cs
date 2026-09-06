namespace MacroDeckHost.Domain.Common;

public static class Result
{
	public static Result<TError> Ok<TError>()
		where TError : struct, Enum
		=> new(true, null, null);

	public static Result<TError> Fail<TError>(TError error, string? message = null)
		where TError : struct, Enum
		=> new(false, error, message);

	public static Result<TData, TError> Ok<TData, TError>(TData data)
		where TError : struct, Enum
		=> new(true, data, null, null);

	public static Result<TData, TError> Fail<TData, TError>(TError error, string? message = null)
		where TError : struct, Enum
		=> new(false, default, error, message);
}

public class Result<TError>
	where TError : struct, Enum
{
	public bool Success { get; }
	public TError? Error { get; }
	public string? ErrorMessage { get; }

	internal Result(bool success, TError? error, string? errorMessage)
	{
		Success = success;
		Error = error;
		ErrorMessage = errorMessage;
	}
}

public class Result<TData, TError>
	where TError : struct, Enum
{
	public bool Success { get; }
	public TData? Data { get; }
	public TError? Error { get; }
	public string? ErrorMessage { get; }

	internal Result(bool success, TData? data, TError? error, string? errorMessage)
	{
		Success = success;
		Data = data;
		Error = error;
		ErrorMessage = errorMessage;
	}
}
