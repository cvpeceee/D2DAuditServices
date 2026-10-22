namespace D2DAudit.Application;

public readonly record struct Result<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(string error) => new(default, error);
}
