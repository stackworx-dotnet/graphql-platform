using HotChocolate.Language;
using HotChocolate.Types;
using Path = HotChocolate.Path;

namespace StrawberryShake.CodeGeneration.Analyzers;

public class FieldSelection
{
    public FieldSelection(
        IOutputFieldDefinition field,
        FieldNode syntaxNode,
        Path path,
        bool isConditional = true,
        IReadOnlyList<FieldNode>? duplicates = null)
    {
        ResponseName = syntaxNode.Alias?.Value ?? syntaxNode.Name.Value;
        Field = field;
        SyntaxNode = syntaxNode;
        IsConditional = isConditional;
        Path = path;
        Duplicates = duplicates ?? [];
    }

    public string ResponseName { get; }

    public IOutputFieldDefinition Field { get; }

    public FieldNode SyntaxNode { get; }

    public Path Path { get; }

    public bool IsConditional { get; }

    /// <summary>
    /// The other field syntax nodes that select the same response name at this level and were
    /// merged into this selection. They carry the same field but a potentially different
    /// sub-selection set, for example when a field is selected both directly and through a
    /// fragment.
    /// </summary>
    public IReadOnlyList<FieldNode> Duplicates { get; }

    public FieldSelection WithPath(Path path) =>
        new(Field, SyntaxNode, path, IsConditional, Duplicates);
}
