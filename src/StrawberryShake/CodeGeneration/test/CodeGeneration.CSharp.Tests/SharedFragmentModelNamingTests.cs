namespace StrawberryShake.CodeGeneration.CSharp;

/// <summary>
/// Characterizing tests for ChilliCream/graphql-platform#6873.
/// The same named fragment spread across two operations currently generates
/// two separate, divergently named per-operation models instead of a single
/// shared model named after the fragment. These tests lock in the current
/// behaviour so that a future fix has a clear, intentional target to change.
/// </summary>
public class SharedFragmentModelNamingTests
{
    private const string Schema =
        "type Query { student(id: Int!): Student students: [Student] } "
        + "type Student { id: ID! name: String age: Int school: School } "
        + "type School { id: ID! name: String }";

    private const string Extensions = "extend schema @key(fields: \"id\")";

    private const string Operations =
        """
        query GetStudentById($id: Int!) {
            student(id: $id) {
                ...MyStudent
            }
        }

        query GetStudents {
            students {
                ...MyStudent
            }
        }

        fragment MyStudent on Student {
            id
            name
            age
            school {
                ...MySchool
            }
        }

        fragment MySchool on School {
            id
            name
        }
        """;

    [Fact]
    public void SameFragmentInTwoOperations_Should_GenerateDivergentPerOperationStudentModels_When_Spread()
    {
        // arrange
        var source = GenerateClientSource();

        // act
        var hasGetStudentByIdStudentClass =
            source.Contains("class GetStudentById_Student_Student");
        var hasGetStudentsStudentClass =
            source.Contains("class GetStudents_Students_Student");
        var hasSharedFragmentClass =
            ContainsTypeDeclaration(source, "class", "MyStudent");

        // assert
        // Documents the gap: the same `MyStudent` fragment yields two separate,
        // operation-scoped data classes and NO single shared `MyStudent` class.
        Assert.True(
            hasGetStudentByIdStudentClass,
            "Expected the operation-scoped class 'GetStudentById_Student_Student'.");
        Assert.True(
            hasGetStudentsStudentClass,
            "Expected the operation-scoped class 'GetStudents_Students_Student'.");
        Assert.False(
            hasSharedFragmentClass,
            "Today no single shared 'MyStudent' data class is generated; "
            + "if this fails the shared-fragment behaviour may have been fixed.");
    }

    [Fact]
    public void SameFragmentInTwoOperations_Should_GenerateDivergentNestedSchoolModels_When_Spread()
    {
        // arrange
        var source = GenerateClientSource();

        // act
        var hasGetStudentByIdSchoolClass =
            source.Contains("class GetStudentById_Student_School_School");
        var hasGetStudentsSchoolClass =
            source.Contains("class GetStudents_Students_School_School");
        var hasSharedFragmentClass =
            ContainsTypeDeclaration(source, "class", "MySchool");

        // assert
        // The nested `MySchool` fragment also diverges into two operation-scoped
        // data classes with no single shared `MySchool` class.
        Assert.True(
            hasGetStudentByIdSchoolClass,
            "Expected the operation-scoped class 'GetStudentById_Student_School_School'.");
        Assert.True(
            hasGetStudentsSchoolClass,
            "Expected the operation-scoped class 'GetStudents_Students_School_School'.");
        Assert.False(
            hasSharedFragmentClass,
            "Today no single shared 'MySchool' data class is generated; "
            + "if this fails the shared-fragment behaviour may have been fixed.");
    }

    [Fact]
    public void SameFragmentInTwoOperations_Should_GenerateSingleSharedFragmentInterfaces_When_Spread()
    {
        // arrange
        var source = GenerateClientSource();

        // act
        var myStudentInterfaceCount =
            CountTypeDeclarations(source, "interface", "IMyStudent");
        var mySchoolInterfaceCount =
            CountTypeDeclarations(source, "interface", "IMySchool");

        // assert
        // The fragment INTERFACES are already shared (one each), which is the half
        // of #6873 that works. The data classes that implement them are not.
        Assert.Equal(1, myStudentInterfaceCount);
        Assert.Equal(1, mySchoolInterfaceCount);
    }

    [Fact]
    public void SameFragmentInTwoOperations_Should_TypeNestedSchoolPropertyByPerOperationInterface_When_Spread()
    {
        // arrange
        var source = GenerateClientSource();

        // act
        var perOperationSchoolPropertyCount = CountOccurrences(
            source,
            "IGetStudentById_Student_School? School { get; }");
        var sharedFragmentSchoolPropertyCount = CountOccurrences(
            source,
            "IMySchool? School { get; }");

        // assert
        // The student model's `school` property is typed by an operation-scoped
        // interface (IGetStudentById_Student_School) rather than the nested
        // fragment interface (IMySchool). This is the "randomly assigned type"
        // behaviour described in #6873.
        Assert.True(
            perOperationSchoolPropertyCount > 0,
            "Expected at least one 'school' property typed by a per-operation interface.");
        Assert.Equal(0, sharedFragmentSchoolPropertyCount);
    }

    private static string GenerateClientSource()
    {
        // CSharpGenerator.Generate(IEnumerable<string>) treats its arguments as
        // file paths, so the GraphQL documents are written to a temporary folder.
        var dir = Path.Combine(
            Path.GetTempPath(),
            "ss_frag_naming_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var schemaFile = Path.Combine(dir, "schema.graphql");
            var extensionsFile = Path.Combine(dir, "schema.extensions.graphql");
            var operationsFile = Path.Combine(dir, "operations.graphql");

            File.WriteAllText(schemaFile, Schema);
            File.WriteAllText(extensionsFile, Extensions);
            File.WriteAllText(operationsFile, Operations);

            var result = CSharpGenerator.Generate(
                [schemaFile, extensionsFile, operationsFile],
                new CSharpGeneratorSettings
                {
                    Namespace = "Foo.Bar",
                    ClientName = "FooClient",
                    AccessModifier = AccessModifier.Public
                });

            Assert.False(
                result.Errors.Any(),
                "It is expected that the result has no generator errors!");

            return string.Join(
                "\n",
                result.Documents
                    .Where(d => d.Kind == SourceDocumentKind.CSharp)
                    .Select(d => d.SourceText));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool ContainsTypeDeclaration(string source, string keyword, string typeName) =>
        CountTypeDeclarations(source, keyword, typeName) > 0;

    private static int CountTypeDeclarations(string source, string keyword, string typeName)
    {
        // Matches "<keyword> <typeName>" only when <typeName> is a whole token,
        // so "interface IMyStudent" does not match "interface IMyStudentFoo".
        var count = 0;

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            var marker = keyword + " " + typeName;
            var index = line.IndexOf(marker, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var afterIndex = index + marker.Length;

            if (afterIndex >= line.Length || !IsIdentifierChar(line[afterIndex]))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
