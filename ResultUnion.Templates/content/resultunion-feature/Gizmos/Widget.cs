using ResultUnion;

namespace TargetNamespace.Gizmos;

#if (Crud)
/// <summary>
/// A ENTITY_WORDS. It is created from validated parts, so creating one cannot fail. A change returns a new instance
/// with the next <see cref="Version"/>: the store compares versions, so two clients cannot overwrite each other.
/// </summary>
#else
/// <summary>A ENTITY_WORDS. It is created from validated parts, so creating one cannot fail.</summary>
#endif
public sealed class Widget
{
    /// <summary>The errors about a ENTITY_WORDS as a whole; each code is declared once, here.</summary>
    public static class Errors
    {
        public static Error NotFound(Guid id) =>
            Error.Create("ENTITY_CODE.not_found", $"There is no ENTITY_WORDS with the id {id}.", ErrorType.NotFound)
                .WithArgument("id", id);

        public static Error NameTaken(WidgetName name) =>
            Error.Create("ENTITY_CODE.name_taken", $"The name '{name}' is taken.", ErrorType.Conflict)
                .WithArgument("name", name.Value);
#if (Grpc)

        public static Error InvalidId(string value) =>
            Error.Create("ENTITY_CODE.invalid_id", $"'{value}' is not a ENTITY_WORDS id.", ErrorType.Validation)
                .WithArgument("value", value);
#endif
#if (Crud)

        /// <summary>Someone changed it since the client read it: reload, then try again.</summary>
        public static Error ChangedConcurrently(Guid id, int version) =>
            Error.Create("ENTITY_CODE.changed_concurrently", $"The ENTITY_WORDS {id} changed since version {version} was read.", ErrorType.Concurrency)
                .WithArgument("id", id)
                .WithArgument("version", version);
#endif
    }

    public Guid Id { get; }
    public WidgetName Name { get; }
#if (Crud)
    public int Version { get; }

    private Widget(Guid id, WidgetName name, int version)
    {
        Id = id;
        Name = name;
        Version = version;
    }

    public static Widget Create(WidgetName name) => new(Guid.NewGuid(), name, 1);

    public Widget Rename(WidgetName name) => new(Id, name, Version + 1);
#else

    private Widget(Guid id, WidgetName name)
    {
        Id = id;
        Name = name;
    }

    public static Widget Create(WidgetName name) => new(Guid.NewGuid(), name);
#endif
#if (Grpc)

    /// <summary>An id as a gRPC client sends it: protobuf has no GUID type, so it arrives as text.</summary>
    public static Result<Guid> ParseId(string? input) =>
        Guid.TryParse(input, out var id) ? id : Errors.InvalidId(input ?? string.Empty);
#endif
}
