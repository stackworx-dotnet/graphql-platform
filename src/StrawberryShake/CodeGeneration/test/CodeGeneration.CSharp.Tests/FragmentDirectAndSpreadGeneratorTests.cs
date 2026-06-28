using static StrawberryShake.CodeGeneration.CSharp.GeneratorTestHelper;

namespace StrawberryShake.CodeGeneration.CSharp;

public class FragmentDirectAndSpreadGeneratorTests
{
    private const string Schema =
        "type Query { me: User } "
        + "type User { id: ID! name: String avatar: Image bestFriend: User } "
        + "type Image { url: String! width: Int height: Int }";

    private const string KeyExtension = "extend schema @key(fields: \"id\")";

    [Fact]
    public void Generate_Should_Succeed_When_CompositeField_Selected_Direct_And_Via_InlineFragment()
    {
        // arrange
        // avatar is selected both directly ({ url }) and inside an inline fragment ({ width }).

        // act & assert
        AssertResult(
            "query Q { me { avatar { url } ... on User { avatar { width } } } }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Succeed_When_CompositeField_Selected_Direct_And_Via_NamedFragment()
    {
        // arrange
        // avatar is selected both directly ({ url }) and via a named fragment spread ({ width }).

        // act & assert
        AssertResult(
            "query Q { me { avatar { url } ...UF } } fragment UF on User { avatar { width } }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Succeed_When_SelfReferencingField_Selected_Direct_And_Via_InlineFragment()
    {
        // arrange
        // bestFriend (a self-referencing User) is selected both directly and via an inline fragment.

        // act & assert
        AssertResult(
            "query Q { me { bestFriend { id } ... on User { bestFriend { name } } } }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Succeed_When_CompositeField_Selected_Direct_Twice()
    {
        // arrange
        // control: avatar selected twice directly must keep working.

        // act & assert
        AssertResult(
            "query Q { me { avatar { url } avatar { width } } }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Succeed_When_CompositeField_Selected_In_Two_InlineFragments()
    {
        // arrange
        // control: avatar selected only inside fragments must keep working.

        // act & assert
        AssertResult(
            "query Q { me { ... on User { avatar { url } } ... on User { avatar { width } } } }",
            Schema,
            KeyExtension);
    }
}
