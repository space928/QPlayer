using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Collections.Immutable;

namespace QPlayer.SourceGenerator;

public partial class ClonableGenerator
{
    private class Parser
    {
        internal IEnumerable<UIClonableResult> Parse(EquatableArray<GeneratorAttributeSyntaxContext> generatorAttributeSyntaxContexts)
        {
            foreach (var source in generatorAttributeSyntaxContexts)
            {
                var targetType = (TypeDeclarationSyntax)source.TargetNode;
                var classSymbol = source.SemanticModel.GetDeclaredSymbol(targetType);
                if (classSymbol == null)
                    continue;

                // Check that the declaring type is partial
                if (!targetType.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    var diag = Diagnostic.Create(DiagnosticDescriptors.MustBePartial, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                // Check that the declaring type is not generic
                if (classSymbol.TypeParameters.Length > 0)
                {
                    var diag = Diagnostic.Create(DiagnosticDescriptors.CantBeGeneric, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                // Check that the class derives from UIElement
                bool isUIElement = classSymbol.Name == "UIElement";
                var parent = classSymbol;
                while (!isUIElement && (parent = parent.BaseType) != null)
                {
                    // TODO: Evil magic strings, not safe from refactoring
                    if (parent.Name == "UIElement")
                    {
                        isUIElement = true;
                        break;
                    }
                }
                if (!isUIElement)
                {
                    var diag = Diagnostic.Create(DiagnosticDescriptors.ClonableMustDeriveFromUIElement, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                bool callCustomCloner = false;
                var attributes = classSymbol.GetAttributes();
                foreach (var attrib in attributes)
                {
                    var args = attrib.ConstructorArguments;
                    switch (attrib?.AttributeClass?.Name)
                    {
                        case nameof(UIClonableAttribute):
                            if (args.Length >= 1)
                                callCustomCloner = (bool)args[0].Value!;
                            break;
                    }
                }

                // Find fields
                var fields = ImmutableArray.CreateBuilder<UIClonableField>();
                foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
                {
                    var fieldAttributes = field.GetAttributes();
                    bool isReactive = false;
                    bool isIncluded = false;
                    bool isExcluded = false;
                    bool canUseCloneMethod = false;
                    foreach (var attrib in fieldAttributes)
                    {
                        var args = attrib.ConstructorArguments;
                        switch (attrib?.AttributeClass?.Name)
                        {
                            case nameof(ReactiveAttribute):
                                isReactive = true;
                                break;
                            case nameof(UICloneableFieldAttribute):
                                isIncluded = true;
                                if (args.Length >= 1)
                                    canUseCloneMethod = (bool)args[0].Value!;
                                break;
                            case nameof(UICloneableSkipAttribute):
                                isExcluded = true;
                                break;
                        }
                    }

                    if (!isExcluded && (isReactive || isIncluded))
                    {
                        bool hasCloneMethod = false;
                        if (canUseCloneMethod)
                        {
                            // Check if the field's type defines a Clone() instance method
                            var fieldType = field.Type;
                            var cloneMethod = fieldType
                                .GetMembers()
                                .OfType<IMethodSymbol>()
                                .FirstOrDefault(x => x.Name == "Clone" && x.Parameters.Length == 0 && x.DeclaredAccessibility == Accessibility.Public);
                            hasCloneMethod = cloneMethod != null;

                            if (!hasCloneMethod)
                            {
                                // Check if an extension method for Clone() has been defined
                                var staticMethods = source.SemanticModel.LookupSymbols(source.TargetNode.SpanStart, fieldType, "Clone", true);
                                hasCloneMethod = !staticMethods.IsEmpty && ((IMethodSymbol)staticMethods[0]).Parameters.IsEmpty;
                            }
                        }

                        fields.Add(new(field.Name, hasCloneMethod));
                    }
                }

                UIClonableClass clonableClass = new(
                    classSymbol.DeclaredAccessibility,
                    classSymbol.ContainingNamespace.ToString(),
                    classSymbol.ContainingAssembly.Name,
                    classSymbol.Name,
                    classSymbol.IsAbstract, true, callCustomCloner,
                    fields.DrainToImmutable().AsEquatable());

                yield return new UIClonableResult(clonableClass, null);
            }
        }
    }

    private record UIClonableResult(UIClonableClass? ClonableClass, Diagnostic? Diagnostic);
    private record UIClonableClass(Accessibility Accessibility, string Namespace, string Assembly, string ClassName,
        bool IsAbstract, bool EnableNullable, bool EnableCustomClone, EquatableArray<UIClonableField> ClonableFields);
    private record UIClonableField(string FieldName, bool HasCloneMethod);
}
