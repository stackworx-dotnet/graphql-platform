using static StrawberryShake.CodeGeneration.CSharp.GeneratorTestHelper;

namespace StrawberryShake.CodeGeneration.CSharp;

public class DuplicateFragmentSpreadGeneratorTests
{
    private const string Schema =
        "interface Node { id: ID! } "
        + "type Query { me: User node(id: ID!): Node pet: Pet } "
        + "type User implements Node { id: ID! name: String email: String bestFriend: User } "
        + "union Pet = Dog | Cat "
        + "type Dog { id: ID! name: String } "
        + "type Cat { id: ID! name: String }";

    private const string KeyExtension = "extend schema @key(fields: \"id\")";

    [Fact]
    public void Generate_Should_Compile_When_Same_Object_Fragment_Spread_Twice()
    {
        // arrange
        // spreading the same fragment twice must not list its interface twice (CS0528).

        // act & assert
        AssertResult(
            "query Q { me { ...UF ...UF } } fragment UF on User { id name }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Compile_When_Same_Interface_Fragment_Spread_Twice()
    {
        // arrange
        // duplicate spread on an interface-typed field must dedupe the base interface list.

        // act & assert
        AssertResult(
            "query Q { node(id: \"1\") { ...NF ...NF } } fragment NF on Node { id }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Compile_When_Single_Fragment_Spread()
    {
        // arrange
        // control: a single spread already works and must keep working.

        // act & assert
        AssertResult(
            "query Q { me { ...UF } } fragment UF on User { id name }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Compile_When_Two_Different_Fragments_Spread()
    {
        // arrange
        // control: two distinct fragments must both remain in the interface list.

        // act & assert
        AssertResult(
            "query Q { me { ...UF ...UG } } "
            + "fragment UF on User { id name } "
            + "fragment UG on User { email }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Compile_When_Same_Fragment_Spread_At_Different_Levels()
    {
        // arrange
        // control: the same fragment at different selection levels must each be emitted.

        // act & assert
        AssertResult(
            "query Q { me { ...UF bestFriend { ...UF } } } fragment UF on User { id name }",
            Schema,
            KeyExtension);
    }

    [Fact]
    public void Generate_Should_Compile_When_Same_Fragment_Spread_Twice_On_Union_Member()
    {
        // arrange
        // duplicate spread targeting a union member must also dedupe the base interface list.

        // act & assert
        AssertResult(
            "query Q { pet { ...DF ...DF } } fragment DF on Dog { name }",
            Schema,
            KeyExtension);
    }
}
