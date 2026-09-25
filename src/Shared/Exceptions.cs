namespace ECommerce.Shared.Exceptions;

/// <summary>
/// Base type for all domain/application exceptions the global exception middleware understands.
/// Anything not deriving from this is treated as an unhandled 500-level error.
/// </summary>
public abstract class AppException : Exception
{
    public abstract int StatusCode { get; }
    public abstract string ErrorCode { get; }

    protected AppException(string message) : base(message)
    {
    }
}

public sealed class NotFoundException : AppException
{
    public override int StatusCode => 404;
    public override string ErrorCode => "NOT_FOUND";

    public NotFoundException(string entityName, object key)
        : base($"{entityName} with id '{key}' was not found.")
    {
    }
}

public sealed class ValidationAppException : AppException
{
    public override int StatusCode => 400;
    public override string ErrorCode => "VALIDATION_ERROR";
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}

public sealed class UnauthorizedAppException : AppException
{
    public override int StatusCode => 401;
    public override string ErrorCode => "UNAUTHORIZED";

    public UnauthorizedAppException(string message = "Authentication is required.") : base(message)
    {
    }
}

public sealed class ForbiddenAppException : AppException
{
    public override int StatusCode => 403;
    public override string ErrorCode => "FORBIDDEN";

    public ForbiddenAppException(string message = "You do not have permission to perform this action.") : base(message)
    {
    }
}

public sealed class ConflictAppException : AppException
{
    public override int StatusCode => 409;
    public override string ErrorCode => "CONFLICT";

    public ConflictAppException(string message) : base(message)
    {
    }
}
