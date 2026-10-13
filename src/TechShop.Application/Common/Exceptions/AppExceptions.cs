namespace TechShop.Application.Common.Exceptions;

/// <summary>Maps to 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.")
{
    public string Entity { get; } = entity;
}

/// <summary>Maps to 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Maps to 400 with a field keyed error dictionary.</summary>
public sealed class ValidationFailedException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationFailedException ForField(string field, string message)
        => new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Maps to 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

