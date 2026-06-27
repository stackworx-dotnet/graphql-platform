// COPIED VERBATIM from
//   src/StrawberryShake/CodeGeneration/test/CodeGeneration.CSharp.Tests/CSharpCompiler.cs
// (the original is `internal`, so it cannot be referenced; it is copied here).
//
// The type-pinning fields and AppDomain reference resolution are kept EXACTLY as
// in the original: pinning forces the transport/runtime assemblies to be loaded
// into this AppDomain so that ResolveReferences() can hand their metadata to the
// Roslyn compilation. Only the namespace is changed to the runner's namespace.

using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using StrawberryShake; // EntityId lives in the root StrawberryShake namespace
                       // (StrawberryShake.Core). The original test project's root
                       // namespace put it in scope implicitly; the runner's does
                       // not, so it is imported explicitly here.
using StrawberryShake.Transport.Http;
using StrawberryShake.Transport.InMemory;
using StrawberryShake.Transport.WebSockets;

namespace HcCodegenRunner;

internal static class CSharpCompiler
{
    // we pin these types so that they are added to this assembly
#pragma warning disable CS0414, IDE0051, RCS1249
    private static readonly EntityId? s_entityId = null!;
    private static readonly JsonDocument? s_jsonDocument = null!;
    private static readonly HttpConnection? s_httpConnection = null!;
    private static readonly WebSocketConnection? s_webSocketConnection = null!;
    private static readonly ServiceCollection? s_serviceCollection = null!;
    private static readonly IHttpClientFactory? s_httpClientFactory = null!;
    private static readonly HttpClient? s_httpClient = null!;
    private static readonly InMemoryClient s_memoryClient = null!;
#pragma warning restore CS0414, IDE0051, RCS1249

    private static readonly CSharpCompilationOptions s_options =
        new(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug);

    private static readonly HashSet<string> s_excludedCodes =
    [
        "CS1702",
        "CS1701"
    ];

    public static IReadOnlyList<Diagnostic> GetDiagnosticErrors(params string[] sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        if (sourceText.Length == 0)
        {
            throw new ArgumentException(
                "The compiler needs at least one code unit in order "
                + "to create an assembly.");
        }

        var syntaxTree = new SyntaxTree[sourceText.Length];
        for (var i = 0; i < sourceText.Length; i++)
        {
            syntaxTree[i] = SyntaxFactory.ParseSyntaxTree(
                SourceText.From(sourceText[i]));
        }

        var assemblyName = $"_{Guid.NewGuid():N}.dll";

        var compilation = CSharpCompilation
            .Create(assemblyName, syntaxTree, Array.Empty<MetadataReference>(), s_options)
            // If we load the references not twice, some assemblies are missing.
            .WithReferences(ResolveReferences());

        return compilation.GetDiagnostics()
            .Where(x => !s_excludedCodes.Contains(x.Id))
            .ToList();
    }

    private static IEnumerable<MetadataReference> ResolveReferences()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(t => !t.IsDynamic && !string.IsNullOrEmpty(t.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));
    }
}
