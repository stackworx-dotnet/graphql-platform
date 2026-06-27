using static StrawberryShake.CodeGeneration.CSharp.GeneratorTestHelper;

namespace StrawberryShake.CodeGeneration.CSharp;

public class UnionTypeGeneratorTests
{
    private const string Schema =
        "union Animal = Dog | Cat "
        + "type Dog { id: ID! name: String barkVolume: Int } "
        + "type Cat { id: ID! name: String meowVolume: Int } "
        + "type Query { animal: Animal }";

    private const string SchemaExtensions = "extend schema @key(fields: \"id\")";

    [Fact]
    public void UnionMemberClasses_Should_ContainSelectedFields_When_SelectedViaInlineFragments()
    {
        // arrange
        const string query =
            """
            query GetAnimal {
                animal {
                    ... on Dog { name barkVolume }
                    ... on Cat { name meowVolume }
                }
            }
            """;

        // act
        var source = GenerateUnionClientSource(query);

        // assert
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Dog", "Name", "BarkVolume");
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Cat", "Name", "MeowVolume");
    }

    [Fact]
    public void UnionMemberClasses_Should_ContainSelectedFields_When_SelectedViaNamedFragmentOnUnion()
    {
        // arrange
        // The named fragment is declared on the union type itself and contains the
        // member inline fragments. This is the shape from issue 5992.
        const string query =
            """
            query GetAnimal {
                animal {
                    ... AnimalDetails
                }
            }

            fragment AnimalDetails on Animal {
                ... on Dog { name barkVolume }
                ... on Cat { name meowVolume }
            }
            """;

        // act
        var source = GenerateUnionClientSource(query);

        // assert
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Dog", "Name", "BarkVolume");
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Cat", "Name", "MeowVolume");
    }

    [Fact]
    public void UnionMemberClasses_Should_ContainSelectedFields_When_SelectedViaNamedFragmentsOnMembers()
    {
        // arrange
        // Each named fragment is declared on a concrete union member.
        const string query =
            """
            query GetAnimal {
                animal {
                    ... DogDetails
                    ... CatDetails
                }
            }

            fragment DogDetails on Dog { name barkVolume }

            fragment CatDetails on Cat { name meowVolume }
            """;

        // act
        var source = GenerateUnionClientSource(query);

        // assert
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Dog", "Name", "BarkVolume");
        AssertMemberClassContainsProperties(source, "GetAnimal_Animal_Cat", "Name", "MeowVolume");
    }

    private static string GenerateUnionClientSource(string query)
    {
        var clientModel = CreateClientModel([query, Schema, SchemaExtensions]);

        var result = CSharpGenerator.Generate(
            clientModel,
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
                .Where(t => t.Kind == SourceDocumentKind.CSharp)
                .Select(t => t.SourceText));
    }

    private static void AssertMemberClassContainsProperties(
        string source,
        string className,
        params string[] propertyNames)
    {
        var classIndex = source.IndexOf(
            $"public partial class {className}",
            StringComparison.Ordinal);

        Assert.True(
            classIndex >= 0,
            $"Generated source does not contain class `{className}`.");

        var classBody = source[classIndex..];
        var nextClassIndex = classBody.IndexOf(
            "public partial class ",
            startIndex: "public partial class ".Length,
            StringComparison.Ordinal);

        if (nextClassIndex >= 0)
        {
            classBody = classBody[..nextClassIndex];
        }

        foreach (var propertyName in propertyNames)
        {
            Assert.True(
                classBody.Contains($" {propertyName} {{", StringComparison.Ordinal)
                || classBody.Contains($" {propertyName} =>", StringComparison.Ordinal),
                $"Generated class `{className}` is missing property `{propertyName}`. "
                + $"Class body:\n{classBody}");
        }
    }
}
