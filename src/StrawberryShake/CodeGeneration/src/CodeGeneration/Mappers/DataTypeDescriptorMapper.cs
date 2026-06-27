using HotChocolate.Types;
using StrawberryShake.CodeGeneration.Analyzers.Models;
using StrawberryShake.CodeGeneration.Descriptors;
using StrawberryShake.CodeGeneration.Descriptors.TypeDescriptors;
using StrawberryShake.CodeGeneration.Extensions;
using static StrawberryShake.CodeGeneration.Utilities.NameUtils;

namespace StrawberryShake.CodeGeneration.Mappers;

public static class DataTypeDescriptorMapper
{
    public static void Map(ClientModel model, IMapperContext context)
    {
        context.Register(CollectDataTypes(model, context));
    }

    private static IEnumerable<DataTypeDescriptor> CollectDataTypes(
        ClientModel model,
        IMapperContext context)
    {
        var dataTypes = context.Types
            .OfType<ObjectTypeDescriptor>()
            .Where(x => x.IsData())
            .ToList();

        var unionTypes = model.Schema.Types
            .OfType<UnionType>()
            .ToList();

        // Index every type by its runtime type name so that component lookups below run in
        // O(1) instead of scanning the whole (potentially very large) type list per component.
        // A name can map to more than one type; the Single(...) semantics of the original
        // lookup are preserved at resolution time (see GetTypeByRuntimeName).
        var typesByRuntimeName =
            new Dictionary<string, RuntimeTypeMatch>(StringComparer.Ordinal);

        foreach (var type in context.Types)
        {
            var runtimeName = type.RuntimeType.Name;
            if (typesByRuntimeName.TryGetValue(runtimeName, out var match))
            {
                typesByRuntimeName[runtimeName] = match.WithAdditional();
            }
            else
            {
                typesByRuntimeName[runtimeName] = new RuntimeTypeMatch(type);
            }
        }

        var dataTypeInfos = new Dictionary<string, DataTypeInfo>(StringComparer.Ordinal);

        foreach (var dataType in dataTypes)
        {
            var objectType = model.Schema.Types.GetType<ObjectType>(dataType.Name);

            var abstractTypes = new List<ITypeDefinition>();
            abstractTypes.AddRange(unionTypes.Where(t => t.ContainsType(dataType.Name)));
            abstractTypes.AddRange(objectType.Implements);

            if (!dataTypeInfos.TryGetValue(dataType.Name, out var dataTypeInfo))
            {
                dataTypeInfo = new DataTypeInfo(dataType.Name, dataType.Description);
                dataTypeInfo.AbstractTypeParentName.AddRange(
                    abstractTypes.Select(abstractType => abstractType.Name));
                dataTypeInfos.Add(dataType.Name, dataTypeInfo);
            }

            dataTypeInfo.Components.Add(dataType.RuntimeType.Name);
        }

        var handledAbstractTypes = new HashSet<string>();

        foreach (var dataTypeInfo in dataTypeInfos.Values)
        {
            var implements = new List<string>();

            foreach (var abstractTypeName in dataTypeInfo.AbstractTypeParentName)
            {
                var dataTypeInterfaceName = GetInterfaceName(abstractTypeName);
                implements.Add(dataTypeInterfaceName);
                if (handledAbstractTypes.Add(dataTypeInterfaceName))
                {
                    yield return new DataTypeDescriptor(
                        dataTypeInterfaceName,
                        NamingConventions.CreateStateNamespace(context.Namespace),
                        Array.Empty<ComplexTypeDescriptor>(),
                        Array.Empty<string>(),
                        dataTypeInfo.Description,
                        true);
                }
            }

            yield return new DataTypeDescriptor(
                dataTypeInfo.Name,
                NamingConventions.CreateStateNamespace(context.Namespace),
                dataTypeInfo.Components
                    .Select(name => GetTypeByRuntimeName(typesByRuntimeName, name))
                    .OfType<ComplexTypeDescriptor>()
                    .ToList(),
                implements,
                dataTypeInfo.Description);
        }
    }

    private static INamedTypeDescriptor GetTypeByRuntimeName(
        Dictionary<string, RuntimeTypeMatch> typesByRuntimeName,
        string runtimeName)
    {
        // Mirror the previous Single(...) semantics: zero matches and more than one match
        // both throw, exactly as Enumerable.Single did over the full type list.
        if (!typesByRuntimeName.TryGetValue(runtimeName, out var match))
        {
            throw new InvalidOperationException(
                $"Sequence contains no matching element for runtime type '{runtimeName}'.");
        }

        if (match.Count > 1)
        {
            throw new InvalidOperationException(
                $"Sequence contains more than one matching element for runtime type "
                + $"'{runtimeName}'.");
        }

        return match.First;
    }

    private readonly struct RuntimeTypeMatch
    {
        public RuntimeTypeMatch(INamedTypeDescriptor first)
        {
            First = first;
            Count = 1;
        }

        private RuntimeTypeMatch(INamedTypeDescriptor first, int count)
        {
            First = first;
            Count = count;
        }

        public INamedTypeDescriptor First { get; }

        public int Count { get; }

        public RuntimeTypeMatch WithAdditional() => new(First, Count + 1);
    }

    private sealed class DataTypeInfo
    {
        public DataTypeInfo(string name, string? description)
        {
            Name = name;
            Components = [];
            AbstractTypeParentName = [];
            Description = description;
        }

        public string Name { get; }

        public string? Description { get; }

        public HashSet<string> Components { get; }

        public List<string> AbstractTypeParentName { get; }
    }
}
