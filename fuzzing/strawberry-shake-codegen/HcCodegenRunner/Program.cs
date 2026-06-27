// StrawberryShake code-generation runner for the differential codegen fuzzer.
//
// Usage: HcCodegenRunner <schema.graphql> <extensions.graphql> <corpus.jsonl>
//
//   schema.graphql     : GraphQL SDL (type system), loaded once.
//   extensions.graphql : GraphQL SDL extensions, loaded once.
//   corpus.jsonl       : one JSON-encoded GraphQL operation string per non-empty
//                        line (the line is a JSON string, e.g.
//                        "query Q { me { id } }").
//
// For every corpus line (index i from 0) exactly one JSON object is written to
// stdout, aligned by i:
//   {"i":0,"parseError":null,"analyzeThrew":null,"genThrew":null,
//    "genErrors":[],"compiled":true,"compileErrors":[]}
//
// Per-line stages are each wrapped in try/catch; the run NEVER crashes on bad
// input. All diagnostics go to stderr; stdout is results only.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using HotChocolate;
using HotChocolate.Language;
using StrawberryShake.CodeGeneration;
using StrawberryShake.CodeGeneration.Analyzers;
using StrawberryShake.CodeGeneration.Analyzers.Models;
using StrawberryShake.CodeGeneration.CSharp;
using StrawberryShake.CodeGeneration.Utilities;

using HcCodegenRunner;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "usage: HcCodegenRunner <schema.graphql> <extensions.graphql> <corpus.jsonl>");
    return 2;
}

var schemaPath = args[0];
var extensionsPath = args[1];
var corpusPath = args[2];

// Compact JSON output. Default encoder escapes non-ASCII conservatively, which is
// fine for diffing error messages.
var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false
};

foreach (var (label, path) in new[]
         {
             ("schema", schemaPath),
             ("extensions", extensionsPath),
             ("corpus", corpusPath)
         })
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"{label} file not found: {path}");
        return 2;
    }
}

// --- Parse the type-system documents ONCE --------------------------------
//
// The schema + extensions are constant across the whole corpus. We parse them
// once up front (a failure here is fatal, not a per-line result), then re-wrap
// the cached DocumentNodes as GraphQLFiles for each operation. SchemaHelper.Load
// and DocumentAnalyzer are stateful/one-shot, so they are rebuilt per line.

DocumentNode schemaDoc;
DocumentNode extensionsDoc;
try
{
    schemaDoc = Utf8GraphQLParser.Parse(File.ReadAllText(schemaPath));
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: failed to parse schema:");
    Console.Error.WriteLine(ex);
    return 1;
}

try
{
    extensionsDoc = Utf8GraphQLParser.Parse(File.ReadAllText(extensionsPath));
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: failed to parse extensions:");
    Console.Error.WriteLine(ex);
    return 1;
}

// Sanity check that the schema/extensions actually load. This is not strictly
// required (each line rebuilds the schema), but failing fast here turns a
// misconfigured schema into a clear fatal error instead of N identical per-line
// analyzeThrew results.
try
{
    var typeSystemFiles = new List<GraphQLFile>
    {
        new("schema.graphql", schemaDoc),
        new("extensions.graphql", extensionsDoc)
    }.GetTypeSystemDocuments();

    _ = SchemaHelper.Load(typeSystemFiles, strictValidation: true, noStore: false);
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: failed to load schema into StrawberryShake:");
    Console.Error.WriteLine(ex);
    return 1;
}

// --- Stream the corpus ----------------------------------------------------

// Buffered stdout; UTF-8 without BOM; '\n' line endings.
var stdout = new StreamWriter(
    Console.OpenStandardOutput(),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    NewLine = "\n",
    AutoFlush = false
};

var i = 0;
using (stdout)
using (var reader = new StreamReader(corpusPath, Encoding.UTF8))
{
    string line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (line.Trim().Length == 0)
        {
            // Skip blank lines without consuming an index.
            continue;
        }

        var record = Evaluate(i, line, schemaDoc, extensionsDoc);
        stdout.WriteLine(JsonSerializer.Serialize(record, jsonOptions));
        i++;
    }

    stdout.Flush();
}

return 0;

// --- Per-line evaluation --------------------------------------------------

static Result Evaluate(
    int i,
    string line,
    DocumentNode schemaDoc,
    DocumentNode extensionsDoc)
{
    // 0) Decode the corpus line (a JSON-encoded string) into the raw operation.
    string operation;
    try
    {
        operation = JsonSerializer.Deserialize<string>(line);
    }
    catch (Exception)
    {
        return new Result { I = i, ParseError = "CORPUS_LINE_NOT_JSON" };
    }

    if (operation is null)
    {
        return new Result { I = i, ParseError = "CORPUS_LINE_NULL" };
    }

    // 1) Parse the operation document. Only the operation's parse result is the
    //    `parseError` field (schema/extensions were parsed once up front).
    DocumentNode operationDoc;
    try
    {
        operationDoc = Utf8GraphQLParser.Parse(operation);
    }
    catch (Exception ex)
    {
        return new Result { I = i, ParseError = Describe(ex) };
    }

    // 2) Build the ClientModel (this is the StrawberryShake "analyze" stage). It
    //    can throw arbitrary exceptions or GraphQLException; capture either.
    ClientModel clientModel;
    try
    {
        var files = new List<GraphQLFile>
        {
            new("schema.graphql", schemaDoc),
            new("extensions.graphql", extensionsDoc),
            new("operation.graphql", operationDoc)
        };

        var typeSystemDocs = files.GetTypeSystemDocuments();
        var executableDocs = files.GetExecutableDocuments();

        var analyzer = new DocumentAnalyzer();
        analyzer.SetSchema(
            SchemaHelper.Load(typeSystemDocs, strictValidation: true, noStore: false));

        foreach (var doc in executableDocs.Select(f => f.Document))
        {
            analyzer.AddDocument(doc);
        }

        clientModel = analyzer.Analyze();
    }
    catch (Exception ex)
    {
        // Generation did not run -> compiled is null (n/a).
        return new Result { I = i, AnalyzeThrew = Describe(ex) };
    }

    // 3) Run the C# generator. Generate() may throw, or may surface errors in
    //    result.Errors without throwing.
    CSharpGeneratorResult genResult;
    try
    {
        genResult = CSharpGenerator.Generate(
            clientModel,
            new CSharpGeneratorSettings
            {
                Namespace = "Fuzz.Generated",
                ClientName = "FuzzClient",
                AccessModifier = AccessModifier.Public,
                StrictSchemaValidation = true
                // NoStore left at default (false) so entity/fragment codegen runs.
            });
    }
    catch (Exception ex)
    {
        return new Result { I = i, GenThrew = Describe(ex) };
    }

    var genErrors = genResult.Errors
        .Select(e => new GenError { Code = e.Code, Message = e.Message })
        .ToList();

    // Collect the generated C# source documents.
    var csharpSources = genResult.Documents
        .Where(d => d.Kind == SourceDocumentKind.CSharp)
        .Select(d => d.SourceText)
        .ToArray();

    // If generation produced errors or produced no C#, it did not "compile".
    if (genErrors.Count > 0 || csharpSources.Length == 0)
    {
        return new Result
        {
            I = i,
            GenErrors = genErrors,
            Compiled = false
        };
    }

    // 4) Compile the generated C# and keep only error-severity diagnostics.
    try
    {
        var diagnostics = CSharpCompiler.GetDiagnosticErrors(csharpSources)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var compileErrors = diagnostics
            .Select(d => new CompileError
            {
                Id = d.Id,
                Message = d.GetMessage(),
                Line = d.Location.GetLineSpan().StartLinePosition.Line
            })
            .ToList();

        return new Result
        {
            I = i,
            GenErrors = genErrors,
            Compiled = compileErrors.Count == 0,
            CompileErrors = compileErrors
        };
    }
    catch (Exception ex)
    {
        // The compile harness itself threw (e.g. reference resolution). Treat as
        // not compiled and record the failure as a compile error so the line is
        // never lost.
        return new Result
        {
            I = i,
            GenErrors = genErrors,
            Compiled = false,
            CompileErrors =
            [
                new CompileError { Id = "COMPILE_THREW", Message = Describe(ex), Line = -1 }
            ]
        };
    }
}

static string Describe(Exception ex)
{
    var message = FirstLine(ex.Message);
    return $"{ex.GetType().Name}: {message}";
}

static string FirstLine(string message)
{
    if (string.IsNullOrEmpty(message))
    {
        return message;
    }

    var idx = message.IndexOfAny(['\r', '\n']);
    return idx < 0 ? message : message[..idx];
}

// --- Output shape ---------------------------------------------------------

internal sealed class Result
{
    [JsonPropertyName("i")]
    public int I { get; init; }

    // null unless parsing the corpus line / operation failed. When non-null, the
    // remaining stages are skipped.
    [JsonPropertyName("parseError")]
    public string ParseError { get; init; }

    // null unless building the ClientModel threw.
    [JsonPropertyName("analyzeThrew")]
    public string AnalyzeThrew { get; init; }

    // null unless CSharpGenerator.Generate itself threw.
    [JsonPropertyName("genThrew")]
    public string GenThrew { get; init; }

    [JsonPropertyName("genErrors")]
    public List<GenError> GenErrors { get; init; } = [];

    // true only if generation produced C# AND there are zero error-severity
    // diagnostics. false if gen failed or compile errored. null if generation
    // never ran (parse/analyze/gen threw).
    [JsonPropertyName("compiled")]
    public bool? Compiled { get; init; }

    [JsonPropertyName("compileErrors")]
    public List<CompileError> CompileErrors { get; init; } = [];
}

internal sealed class GenError
{
    [JsonPropertyName("code")]
    public string Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; }
}

internal sealed class CompileError
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }
}
