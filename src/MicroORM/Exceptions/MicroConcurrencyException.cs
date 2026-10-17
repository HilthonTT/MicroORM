namespace MicroORM.Exceptions;

/// <summary>
/// Thrown when an update or delete of a versioned entity matched no row: another writer changed
/// or deleted the row since it was read. Reload the entity and retry, or report the conflict.
/// </summary>
public sealed class MicroConcurrencyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MicroConcurrencyException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public MicroConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public MicroConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal MicroConcurrencyException(string message, object entity)
        : base(message) => Entity = entity;

    /// <summary>Gets the entity whose write was rejected.</summary>
    public object? Entity { get; }
}
