// HotChocolate validation runner for the differential validation fuzzer.
//
// Usage: HcValidationRunner <schema.graphql> <corpus.jsonl>
//
//   schema.graphql : GraphQL SDL, loaded schema-first.
//   corpus.jsonl   : one JSON-encoded query string per non-empty line.
//
// For every corpus line (index i from 0) exactly one JSON object is written to
// stdout, aligned by i:
//   {"i":<int>,"parseError":<bool>,"valid":<bool>,"errorCount":<int>,"messages":[<=5 strings]}
//
// Mirrors the graphql-js reference runner (node-runner.mjs) so the two outputs
// can be diffed line-for-line. All diagnostics go to stderr; stdout is results only.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Validation;

// AddDefaultRules lives in the Microsoft.Extensions.DependencyInjection namespace.
using Microsoft.Extensions.DependencyInjection;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: HcValidationRunner <schema.graphql> <corpus.jsonl>");
    return 2;
}

var schemaPath = args[0];
var corpusPath = args[1];

// Compact JSON, exact key names matching the JS reference runner. Non-ASCII
// characters in error messages are escaped conservatively (default encoder).
var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false
};

if (!File.Exists(schemaPath))
{
    Console.Error.WriteLine($"schema file not found: {schemaPath}");
    return 2;
}

if (!File.Exists(corpusPath))
{
    Console.Error.WriteLine($"corpus file not found: {corpusPath}");
    return 2;
}

// --- Build schema + validator ONCE ---------------------------------------

ISchemaDefinition schema;
try
{
    var sdl = File.ReadAllText(schemaPath);
    schema = BuildSchema(sdl);
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: failed to load schema:");
    Console.Error.WriteLine(ex);
    return 1;
}

DocumentValidator validator;
try
{
    validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
}
catch (Exception ex)
{
    Console.Error.WriteLine("FATAL: failed to build validator:");
    Console.Error.WriteLine(ex);
    return 1;
}

// --- Stream the corpus ----------------------------------------------------

// Buffered stdout; UTF-8 without BOM; '\n' line endings to match the JS runner.
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
            // Skip blank lines without consuming an index, matching the JS runner
            // which filters empty lines before enumerating.
            continue;
        }

        var record = Evaluate(i, line, schema, validator);
        stdout.WriteLine(JsonSerializer.Serialize(record, jsonOptions));
        i++;
    }

    stdout.Flush();
}

return 0;

// --- Helpers --------------------------------------------------------------

static Result Evaluate(int i, string line, ISchemaDefinition schema, DocumentValidator validator)
{
    // 1) Decode the corpus line (a JSON-encoded string) into the raw query.
    string query;
    try
    {
        query = JsonSerializer.Deserialize<string>(line);
    }
    catch (Exception)
    {
        return new Result
        {
            I = i,
            ParseError = true,
            Valid = false,
            ErrorCount = 0,
            Messages = ["CORPUS_LINE_NOT_JSON"]
        };
    }

    if (query is null)
    {
        return new Result
        {
            I = i,
            ParseError = true,
            Valid = false,
            ErrorCount = 0,
            Messages = ["CORPUS_LINE_NULL"]
        };
    }

    // 2) Parse the GraphQL document.
    DocumentNode document;
    try
    {
        document = Utf8GraphQLParser.Parse(query);
    }
    catch (SyntaxException ex)
    {
        return new Result
        {
            I = i,
            ParseError = true,
            Valid = false,
            ErrorCount = 0,
            Messages = [FirstLine(ex.Message)]
        };
    }
    catch (Exception ex)
    {
        // Any other parser failure is still a parse error for diffing purposes.
        return new Result
        {
            I = i,
            ParseError = true,
            Valid = false,
            ErrorCount = 0,
            Messages = [FirstLine(ex.Message)]
        };
    }

    // 3) Validate against the full default rule set.
    try
    {
        var result = validator.Validate(schema, document);
        var messages = new List<string>(5);
        foreach (var error in result.Errors)
        {
            if (messages.Count == 5)
            {
                break;
            }

            messages.Add(error.Message);
        }

        return new Result
        {
            I = i,
            ParseError = false,
            Valid = !result.HasErrors,
            ErrorCount = result.Errors.Count,
            Messages = messages.ToArray()
        };
    }
    catch (Exception ex)
    {
        // Validation itself threw (should be rare). Treat as invalid, signal -1.
        return new Result
        {
            I = i,
            ParseError = false,
            Valid = false,
            ErrorCount = -1,
            Messages = ["VALIDATE_THREW: " + FirstLine(ex.Message)]
        };
    }
}

static ISchemaDefinition BuildSchema(string sdl)
{
    // Validation-only schema:
    //   - StrictValidation=false: relax spec-completeness checks; we only need a
    //     schema the validator can read, not an executable one.
    //   - EnableDefer/EnableStream: match graphql-js v17 so @defer/@stream are known.
    //   - .Use(next => next): a no-op field middleware so fields have a pipeline and
    //     the schema builds without resolvers.
    //   - AnyType("GeoPoint"): bind the SDL's custom scalar to a permissive scalar
    //     that accepts any literal, mirroring graphql-js's treatment of a custom
    //     scalar with no parseLiteral (no input-literal validation). Without a
    //     binding the type initializer cannot resolve references to GeoPoint.
    return SchemaBuilder.New()
        .AddDocumentFromString(sdl)
        .AddType(new AnyType("GeoPoint"))
        .Use(next => next)
        .ModifyOptions(o =>
        {
            o.StrictValidation = false;
            o.EnableDefer = true;
            o.EnableStream = true;
        })
        .Create();
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

    [JsonPropertyName("parseError")]
    public bool ParseError { get; init; }

    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; init; }

    [JsonPropertyName("messages")]
    public string[] Messages { get; init; } = [];
}
