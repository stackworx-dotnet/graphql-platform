using static StrawberryShake.CodeGeneration.CSharp.GeneratorTestHelper;

namespace StrawberryShake.CodeGeneration.CSharp;

public class UploadMultipleFilesGeneratorTests
{
    [Fact]
    public void Generate_Should_CompileUniqueLocals_When_InputTypeHasMultipleUploadFields()
    {
        // arrange
        // an input type with more than one Upload field forces the file mapper
        // to emit several upload statements inside the same method scope.

        // act + assert
        AssertResult(
            @"query test($input: FileUpload!) {
                    upload(input: $input)
                }",
            @"type Query {
                    upload(input: FileUpload): String
                }

                input FileUpload {
                    metadata: String!
                    file: Upload
                    file2: Upload
                }

                scalar Upload
                ",
            "extend schema @key(fields: \"id\")");
    }

    [Fact]
    public void Generate_Should_EmitSingleMapper_When_InputTypeHasMultipleFieldsOfSameUploadInputType()
    {
        // arrange
        // an input type has two fields that point to the same nested upload input
        // type, so the file mapper for that nested type must only be generated once.

        // act + assert
        AssertResult(
            @"query test($input: CreateTaskInput!) {
                    upload(input: $input)
                }",
            @"type Query {
                    upload(input: CreateTaskInput): String
                }

                input CreateTaskInput {
                    title: String
                    uploadOne: FileUpload
                    uploadTwo: FileUpload
                }

                input FileUpload {
                    metadata: String!
                    file: Upload
                    file2: Upload
                }

                scalar Upload
                ",
            "extend schema @key(fields: \"id\")");
    }
}
