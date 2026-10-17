using MicroORM.Mapping;
using MicroORM.TypeHandlers;
using System.Text.Json;

namespace MicroORM.Configuration;

/// <summary>Configures a <see cref="Sessions.MicroDb"/>.</summary>
public sealed class MicroOptions
{
    private readonly Dictionary<Type, EntityConfiguration> _entities = [];

    private readonly Dictionary<Type, ITypeHandler> _typeHandlers = [];

    /// <summary>Gets or sets how type and property names become table and column names. Defaults to <see cref="NamingConvention.AsIs"/>.</summary>
    public NamingConvention Naming { get; set; } = NamingConvention.AsIs;

    /// <summary>Gets or sets the command timeout in seconds; <see langword="null"/> keeps the provider default.</summary>
    public int? CommandTimeout { get; set; }

    /// <summary>Gets or sets the command timeout in seconds; <see langword="null"/> keeps the provider default.</summary>
    public Action<CommandLog>? Log { get; set; }

    /// <summary>Overrides mapping for <typeparamref name="T"/> with a fluent builder.</summary>
    /// <example><code>options.Entity&lt;User&gt;(e =&gt; e.ToTable("people").Column(u =&gt; u.Name, "full_name"));</code></example>
    public MicroOptions Entity<T>(Action<EntityBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure, nameof(configure));
        if (!_entities.TryGetValue(typeof(T), out var existing))
        {
            existing = new EntityBuilder<T>();
            _entities[typeof(T)] = existing;
        }

        configure((EntityBuilder<T>)existing);
        return this;
    }

    /// <summary>
    /// Registers a handler that converts <typeparamref name="T"/> to and from a provider value.
    /// Properties of type <typeparamref name="T"/> become mapped columns.
    /// </summary>
    public MicroOptions AddTypeHandler<T>(TypeHandler<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));
        _typeHandlers[typeof(T)] = handler;
        return this;
    }

    /// <summary>
    /// Registers a value object or strongly typed ID with a pair of conversion functions.
    /// </summary>
    /// <example><code>options.AddTypeHandler&lt;Email, string&gt;(e =&gt; e.Address, s =&gt; new Email(s));</code></example>
    public MicroOptions AddTypeHandler<T, TProvider>(Func<T, TProvider> toProvider, Func<TProvider, T> fromProvider)
    {
        ArgumentNullException.ThrowIfNull(toProvider, nameof(toProvider));
        ArgumentNullException.ThrowIfNull(fromProvider, nameof(fromProvider));

        return AddTypeHandler(new DelegateTypeHandler<T, TProvider>(toProvider, fromProvider));
    }

    /// <summary>Stores <typeparamref name="T"/> as JSON text in its column.</summary>
    public MicroOptions AddJsonColumn<T>(JsonSerializerOptions? serializerOptions = null) =>
       AddTypeHandler(new JsonTypeHandler<T>(serializerOptions));

    internal IReadOnlyDictionary<Type, EntityConfiguration> Entities => _entities;

    internal IReadOnlyDictionary<Type, ITypeHandler> TypeHandlers => _typeHandlers;
}
