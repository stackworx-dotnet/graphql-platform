using StrawberryShake.CodeGeneration.Analyzers.Models;
using Path = HotChocolate.Path;

namespace StrawberryShake.CodeGeneration.Analyzers;

internal class InterfaceTypeSelectionSetAnalyzer : SelectionSetAnalyzer
{
    public override OutputTypeModel Analyze(
        IDocumentAnalyzerContext context,
        FieldSelection fieldSelection,
        SelectionSetVariants selectionVariants)
    {
        var returnTypeFragmentName = FragmentHelper.GetReturnTypeName(fieldSelection);

        if (returnTypeFragmentName is null)
        {
            return AnalyzeWithDefaults(
                context,
                fieldSelection,
                selectionVariants);
        }

        return AnalyzeWithHoistedFragment(
            context,
            fieldSelection,
            selectionVariants,
            returnTypeFragmentName);
    }

    public OutputTypeModel AnalyzeOperation(
        IDocumentAnalyzerContext context,
        SelectionSetVariants selectionSetVariants)
    {
        var rootSelectionPath = Path.Root.Append(context.OperationName);

        var returnTypeFragment =
            FragmentHelper.CreateFragmentNode(
                context.OperationType,
                rootSelectionPath,
                selectionSetVariants.ReturnType);

        returnTypeFragment = FragmentHelper.RewriteForConcreteType(returnTypeFragment);

        var returnType =
            FragmentHelper.CreateInterface(
                context,
                returnTypeFragment,
                rootSelectionPath);

        FragmentHelper.CreateClass(
            context,
            returnTypeFragment,
            selectionSetVariants.ReturnType,
            returnType);

        return returnType;
    }

    private OutputTypeModel AnalyzeWithDefaults(
        IDocumentAnalyzerContext context,
        FieldSelection fieldSelection,
        SelectionSetVariants selectionVariants)
    {
        var returnTypeFragment =
            FragmentHelper.CreateFragmentNode(
                selectionVariants.ReturnType,
                fieldSelection.Path);

        var returnType =
            FragmentHelper.CreateInterface(
                context,
                returnTypeFragment,
                fieldSelection.Path);

        context.RegisterSelectionSet(
            returnType.Type,
            selectionVariants.ReturnType.SyntaxNode,
            returnType.SelectionSet);

        RegisterDuplicateSelectionSets(context, fieldSelection, returnType);

        foreach (var selectionSet in selectionVariants.Variants)
        {
            returnTypeFragment = FragmentHelper.CreateFragmentNode(
                selectionSet,
                fieldSelection.Path,
                appendTypeName: true);

            returnTypeFragment = FragmentHelper.RewriteForConcreteType(returnTypeFragment);

            var @interface =
                FragmentHelper.CreateInterface(
                    context,
                    returnTypeFragment,
                    fieldSelection.Path,
                    [returnType]);

            var @class =
                FragmentHelper.CreateClass(
                    context,
                    returnTypeFragment,
                    selectionSet,
                    @interface);

            context.RegisterSelectionSet(
                selectionSet.Type,
                selectionSet.SyntaxNode,
                @class.SelectionSet);
        }

        return returnType;
    }

    // A composite field can be selected more than once at the same level, for example both
    // directly and through a fragment. The selections are merged onto a single canonical field
    // syntax node, but the parent type model may reference one of the merged-away duplicates as
    // the property's syntax node. We register those duplicate sub-selection sets against the same
    // result type so the property type can still be resolved later.
    private static void RegisterDuplicateSelectionSets(
        IDocumentAnalyzerContext context,
        FieldSelection fieldSelection,
        OutputTypeModel returnType)
    {
        foreach (var duplicate in fieldSelection.Duplicates)
        {
            if (duplicate.SelectionSet is { } selectionSet)
            {
                context.RegisterSelectionSet(
                    returnType.Type,
                    selectionSet,
                    returnType.SelectionSet);
            }
        }
    }

    private OutputTypeModel AnalyzeWithHoistedFragment(
        IDocumentAnalyzerContext context,
        FieldSelection fieldSelection,
        SelectionSetVariants selectionVariants,
        string fragmentName)
    {
        var returnTypeFragment =
            FragmentHelper.CreateFragmentNode(
                selectionVariants.Variants[0],
                fieldSelection.Path,
                appendTypeName: true);

        returnTypeFragment = FragmentHelper.GetFragment(returnTypeFragment, fragmentName);

        if (returnTypeFragment is null)
        {
            throw ThrowHelper.ReturnFragmentDoesNotExist();
        }

        var returnType =
            FragmentHelper.CreateInterface(context, returnTypeFragment, fieldSelection.Path);

        context.RegisterSelectionSet(
            returnType.Type,
            selectionVariants.ReturnType.SyntaxNode,
            returnType.SelectionSet);

        RegisterDuplicateSelectionSets(context, fieldSelection, returnType);

        foreach (var selectionSet in selectionVariants.Variants)
        {
            returnTypeFragment = FragmentHelper.CreateFragmentNode(
                selectionSet,
                fieldSelection.Path,
                appendTypeName: true);

            returnTypeFragment = FragmentHelper.RewriteForConcreteType(returnTypeFragment);

            if (FragmentHelper.GetFragment(returnTypeFragment, fragmentName) is null)
            {
                throw ThrowHelper.FragmentMustBeImplementedByAllTypeFragments();
            }

            var @interface =
                FragmentHelper.CreateInterface(
                    context,
                    returnTypeFragment,
                    fieldSelection.Path,
                    [returnType]);

            var @class =
                FragmentHelper.CreateClass(
                    context,
                    returnTypeFragment,
                    selectionSet,
                    @interface);

            context.RegisterSelectionSet(
                selectionSet.Type,
                selectionSet.SyntaxNode,
                @class.SelectionSet);
        }

        return returnType;
    }
}
