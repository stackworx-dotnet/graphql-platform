using HotChocolate.Types;
using HotChocolate.Utilities;
using StrawberryShake.CodeGeneration.Analyzers.Models;
using StrawberryShake.CodeGeneration.Descriptors.TypeDescriptors;
using static System.StringComparer;

namespace StrawberryShake.CodeGeneration.Mappers;

public static partial class TypeDescriptorMapper
{
    private static void CollectInputTypes(
        ClientModel model,
        IMapperContext context,
        Dictionary<string, InputTypeDescriptorModel> typeDescriptors)
    {
        foreach (var inputType in model.InputObjectTypes)
        {
            if (!typeDescriptors.ContainsKey(inputType.Name))
            {
                var descriptorModel = new InputTypeDescriptorModel(
                    inputType,
                    new InputObjectTypeDescriptor(
                        inputType.Type.Name,
                        new(inputType.Type.Name, context.Namespace),
                        inputType.HasUpload,
                        inputType.Description));

                typeDescriptors.Add(inputType.Name, descriptorModel);
            }
        }
    }

    private static void AddInputTypeProperties(
        Dictionary<string, InputTypeDescriptorModel> typeDescriptors,
        Dictionary<string, INamedTypeDescriptor> leafTypeDescriptors)
    {
        // Index the descriptors by their GraphQL type name so that resolving a
        // field's input type is a constant-time lookup instead of a linear scan
        // per field. The descriptors are keyed by class name (Model.Name) which
        // can differ from the GraphQL type name (Model.Type.Name) used here, so a
        // dedicated index is required. First insertion wins, mirroring the prior
        // First() enumeration order.
        var descriptorsByTypeName =
            new Dictionary<string, INamedTypeDescriptor>(typeDescriptors.Count, Ordinal);
        foreach (var typeDescriptorModel in typeDescriptors.Values)
        {
            descriptorsByTypeName.TryAdd(
                typeDescriptorModel.Model.Type.Name,
                typeDescriptorModel.Descriptor);
        }

        foreach (var typeDescriptorModel in typeDescriptors.Values)
        {
            var properties = new List<PropertyDescriptor>();

            foreach (var field in typeDescriptorModel.Model.Fields)
            {
                INamedTypeDescriptor? fieldType;
                var namedType = field.Type.NamedType();

                if (namedType.IsScalarType() || namedType.IsEnumType())
                {
                    fieldType = leafTypeDescriptors[namedType.Name];
                }
                else
                {
                    fieldType = GetInputTypeDescriptor(
                        field.Type.NamedType(),
                        descriptorsByTypeName);
                }

                properties.Add(
                    new PropertyDescriptor(
                        field.Name,
                        field.Field.Name,
                        BuildFieldType(
                            field.Type,
                            fieldType),
                        field.Description));
            }

            typeDescriptorModel.Descriptor.CompleteProperties(properties);
        }
    }

    private static INamedTypeDescriptor GetInputTypeDescriptor(
        ITypeDefinition fieldNamedType,
        Dictionary<string, INamedTypeDescriptor> descriptorsByTypeName)
    {
        return descriptorsByTypeName[fieldNamedType.Name];
    }
}
