using System.Text.Json;
using StrawberryShake.CodeGeneration.CSharp.Builders;
using StrawberryShake.CodeGeneration.Descriptors.TypeDescriptors;
using StrawberryShake.CodeGeneration.Extensions;
using static StrawberryShake.CodeGeneration.Descriptors.NamingConventions;
using static StrawberryShake.CodeGeneration.Utilities.NameUtils;

namespace StrawberryShake.CodeGeneration.CSharp.Generators;

public partial class JsonResultBuilderGenerator
{
    private const string EntityId = "entityId";
    private const string Entity = "entity";

    private void AddUpdateEntityMethod(
        ClassBuilder classBuilder,
        MethodBuilder methodBuilder,
        INamedTypeDescriptor namedTypeDescriptor,
        HashSet<string> processed,
        bool isNonNull)
    {
        if (namedTypeDescriptor is InterfaceTypeDescriptor interfaceTypeDescriptor)
        {
            // If the type is an interface we first read the concrete __typename and
            // only parse the entity id for a member that is known to this client. A
            // member that is unknown (for example one added to the server after the
            // client was generated) degrades to the fallback instead of failing the
            // whole result.
            methodBuilder.AddCode(
                AssignmentBuilder
                    .New()
                    .SetLeftHandSide($"var {Typename}")
                    .SetRightHandSide(MethodCallBuilder
                        .Inline()
                        .SetMethodName(Obj, "Value", nameof(JsonElement.GetProperty))
                        .AddArgument(WellKnownNames.TypeName.AsStringToken())
                        .Chain(x => x.SetMethodName(nameof(JsonElement.GetString)))));

            foreach (var concreteType in interfaceTypeDescriptor.ImplementedBy)
            {
                methodBuilder
                    .AddEmptyLine()
                    .AddCode(CreateUpdateEntityByTypenameStatement(concreteType));
            }

            methodBuilder.AddEmptyLine();
            methodBuilder.AddCode(CreateUnknownTypeFallback(isNonNull));
        }
        else if (namedTypeDescriptor is ObjectTypeDescriptor objectTypeDescriptor)
        {
            methodBuilder.AddCode(CreateParseEntityIdStatement());
            methodBuilder.AddEmptyLine();

            BuildTryGetEntityIf(
                    CreateEntityType(
                        objectTypeDescriptor.Name,
                        objectTypeDescriptor.RuntimeType.NamespaceWithoutGlobal))
                .AddCode(CreateEntityConstructorCall(objectTypeDescriptor, false))
                .AddElse(CreateEntityConstructorCall(objectTypeDescriptor, true));

            methodBuilder.AddEmptyLine();
            methodBuilder.AddCode($"return {EntityId};");
        }

        AddRequiredDeserializeMethods(namedTypeDescriptor, classBuilder, processed);
    }

    private static ICode CreateParseEntityIdStatement()
        => CodeBlockBuilder
            .New()
            .AddCode(AssignmentBuilder
                .New()
                .SetLeftHandSide($"{TypeNames.EntityId} {EntityId}")
                .SetRightHandSide(
                    MethodCallBuilder
                        .Inline()
                        .SetMethodName(GetFieldName(IdSerializer), "Parse")
                        .AddArgument($"{Obj}.Value")))
            .AddCode(MethodCallBuilder
                .New()
                .SetMethodName(EntityIds, nameof(List<object>.Add))
                .AddArgument(EntityId));

    private IfBuilder CreateUpdateEntityStatement(
        ObjectTypeDescriptor concreteType)
    {
        var ifStatement = IfBuilder
            .New()
            .SetCondition(
                MethodCallBuilder
                    .Inline()
                    .SetMethodName(EntityId, "Name", nameof(string.Equals))
                    .AddArgument(concreteType.Name.AsStringToken())
                    .AddArgument(TypeNames.OrdinalStringComparison));

        var entityTypeName = CreateEntityType(
            concreteType.Name,
            concreteType.RuntimeType.NamespaceWithoutGlobal);

        var ifBuilder = BuildTryGetEntityIf(entityTypeName)
            .AddCode(CreateEntityConstructorCall(concreteType, false))
            .AddElse(CreateEntityConstructorCall(concreteType, true));

        return ifStatement
            .AddCode(ifBuilder)
            .AddEmptyLine();
    }

    private IfBuilder CreateUpdateEntityByTypenameStatement(
        ObjectTypeDescriptor concreteType)
    {
        var ifStatement = IfBuilder
            .New()
            .SetCondition(
                $"{Typename}?.Equals(\"{concreteType.Name}\", "
                + $"{TypeNames.OrdinalStringComparison}) ?? false");

        var entityTypeName = CreateEntityType(
            concreteType.Name,
            concreteType.RuntimeType.NamespaceWithoutGlobal);

        var ifBuilder = BuildTryGetEntityIf(entityTypeName)
            .AddCode(CreateEntityConstructorCall(concreteType, false))
            .AddElse(CreateEntityConstructorCall(concreteType, true));

        return ifStatement
            .AddCode(CreateParseEntityIdStatement())
            .AddEmptyLine()
            .AddCode(ifBuilder)
            .AddEmptyLine()
            .AddCode($"return {EntityId};");
    }

    private static ICode CreateEntityConstructorCall(
        ObjectTypeDescriptor objectType,
        bool assignDefault)
    {
        var propertyLookup = objectType.Properties.ToDictionary(x => x.Name);
        var fragments = objectType.Deferred.ToDictionary(t => t.FragmentIndicator);

        // include properties from fragments
        foreach (var fragment in fragments.Values)
        {
            foreach (var property in fragment.Class.Properties)
            {
                if (!propertyLookup.ContainsKey(property.Name))
                {
                    propertyLookup.Add(property.Name, EnsureDeferredFieldIsNullable(property));
                }
            }
        }

        var newEntity = MethodCallBuilder
            .Inline()
            .SetNew()
            .SetMethodName(objectType.EntityTypeDescriptor.RuntimeType.ToString());

        foreach (var property in
            objectType.EntityTypeDescriptor.Properties.Values)
        {
            if (propertyLookup.TryGetValue(property.Name, out var prop))
            {
                newEntity.AddArgument(BuildUpdateMethodCall(prop));
            }
            else if (fragments.TryGetValue(property.Name, out var frag))
            {
                newEntity.AddArgument(BuildFragmentMethodCall(frag));
            }
            else if (assignDefault)
            {
                newEntity.AddArgument("default!");
            }
            else
            {
                newEntity.AddArgument($"{Entity}.{property.Name}");
            }
        }

        return MethodCallBuilder
            .New()
            .SetMethodName(Session, "SetEntity")
            .AddArgument(EntityId)
            .AddArgument(newEntity);
    }

    private static IfBuilder BuildTryGetEntityIf(RuntimeTypeInfo entityType)
    {
        return IfBuilder
            .New()
            .SetCondition(MethodCallBuilder
                .Inline()
                .SetMethodName(Session, "CurrentSnapshot", "TryGetEntity")
                .AddArgument(EntityId)
                .AddOutArgument(Entity, entityType.ToString()));
    }

    private static PropertyDescriptor EnsureDeferredFieldIsNullable(PropertyDescriptor property)
    {
        if (property.Type.IsNonNull())
        {
            property = new PropertyDescriptor(
                property.Name,
                property.FieldName,
                property.Type.InnerType(),
                property.Description,
                PropertyKind.DeferredField);
        }

        return property;
    }
}
