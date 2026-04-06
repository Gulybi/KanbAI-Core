namespace KanbAI_Core.DTOs;

public record ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ApiResponse Ok(string? message = null)
        => new() { Success = true, Message = message };

    public static ApiResponse Fail(string message)
        => new() { Success = false, Message = message };

    public static ApiResponse Fail(IEnumerable<string> errors)
        => new() { Success = false, Errors = errors.ToList().AsReadOnly() };
}

public record ApiResponse<T> : ApiResponse
{
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message };

    public new static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };

    public new static ApiResponse<T> Fail(IEnumerable<string> errors)
        => new() { Success = false, Errors = errors.ToList().AsReadOnly() };
}
