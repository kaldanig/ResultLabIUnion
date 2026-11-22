using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>A ENTITY_WORDS. It is created from validated parts, so creating one cannot fail.</summary>
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
    }

    public Guid Id { get; }
    public WidgetName Name { get; }

    private Widget(Guid id, WidgetName name)
    {
        Id = id;
        Name = name;
    }

    public static Widget Create(WidgetName name) => new(Guid.NewGuid(), name);
}
