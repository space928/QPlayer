using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace QPlayer.SourceGenerator;

public partial class ReactiveObjectGenerator
{
    internal class Parser
    {
        public IEnumerable<ReactiveObjectResult> Parse(EquatableArray<GeneratorAttributeSyntaxContext> sources)
        {
            foreach (var sourceClass in sources.GroupBy(x => x.TargetNode is PropertyDeclarationSyntax ? x.TargetNode?.Parent : x.TargetNode?.Parent?.Parent?.Parent)) // VariableDeclaratorSyntax -> VariableDeclarationSyntax -> FieldDeclarationSyntax -> TypeDeclarationSyntax
            {
                if (sourceClass.Key == null)
                    continue;
                var targetType = (TypeDeclarationSyntax)sourceClass.Key;
                var classSymbol = sourceClass.First().SemanticModel.GetDeclaredSymbol(targetType);
                if (classSymbol == null)
                    continue;

                // Check that the declaring type is partial
                if (!targetType.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    var diag = new DiagnosticInfo(DiagnosticDescriptors.MustBePartial, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                // Check that the declaring type is not generic
                if (classSymbol.TypeParameters.Length > 0)
                {
                    var diag = new DiagnosticInfo(DiagnosticDescriptors.CantBeGeneric, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                // Check that the class derives from BindableViewModel
                bool isReactive = false;
                bool isObservable = false;
                bool isBindable = false;
                var parent = classSymbol;
                do
                {
                    if (parent.Name == "BindableViewModel")
                    {
                        isObservable = isBindable = isReactive = true;
                        break;
                    }
                    if (parent.Name == "ObservableObject")
                    {
                        isObservable = isReactive = true;
                        break;
                    }
                    var ifaces = parent.Interfaces;
                    if (ifaces.Any(x => x.Name == "INotifyPropertyChanged"))
                    {
                        isReactive = true;
                        break;
                    }
                } while (!isReactive && (parent = parent.BaseType) != null);
                if (!isReactive)
                {
                    var diag = new DiagnosticInfo(DiagnosticDescriptors.MustDeriveFromReactiveObject, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }
                string? modelType = null;
                string? baseModelType = null;
                if (isBindable)
                {
                    baseModelType = parent!.TypeArguments[0].ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat);
                    if (classSymbol.GetAttributes().FirstOrDefault(x => x?.AttributeClass?.Name == nameof(ModelAttribute)) is AttributeData modelAttrib)
                    {
                        var args = modelAttrib.ConstructorArguments;
                        if (args.Length >= 1)
                            modelType = ((INamedTypeSymbol)args[0].Value!).ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    else
                    {
                        modelType = baseModelType;
                    }
                }

                var fields = ImmutableArray.CreateBuilder<ReactiveObjectField>();
                bool enableNullable = false;

                // Create fields
                foreach (var reactiveNode in sourceClass)
                {
                    if (reactiveNode.TargetSymbol is IPropertySymbol)
                    {
                        var (fld, nullable, diag) = ParseReactiveProp(targetType, classSymbol, reactiveNode);
                        if (diag != null)
                            yield return new(null, diag);
                        if (fld != null)
                            fields.Add(fld);
                        enableNullable |= nullable;
                    }
                    else if (reactiveNode.TargetSymbol is IFieldSymbol)
                    {
                        var (fld, nullable, diag) = ParseReactiveField(targetType, classSymbol, reactiveNode);
                        if (diag != null)
                            yield return new(null, diag);
                        if (fld != null)
                            fields.Add(fld);
                        enableNullable |= nullable;
                    }
                }

                ReactiveObjectClass reactiveObjectClass = new(
                    classSymbol.DeclaredAccessibility,
                    classSymbol.ContainingNamespace.ToString(),
                    classSymbol.ContainingAssembly.Name,
                    classSymbol.Name,
                    enableNullable,
                    isObservable,
                    modelType,
                    baseModelType,
                    fields.DrainToImmutable().AsEquatable());

                yield return new(reactiveObjectClass, null);
            }
        }

        private static (ReactiveObjectField? fld, bool nullable, DiagnosticInfo? diag)
            ParseReactiveProp(TypeDeclarationSyntax targetType, INamedTypeSymbol classSymbol, GeneratorAttributeSyntaxContext reactiveNode)
        {
            var fieldName = reactiveNode.TargetSymbol.Name;
            var varSyntax = (PropertyDeclarationSyntax)reactiveNode.TargetNode;
            var symbol = ((IPropertySymbol)reactiveNode.TargetSymbol);
            var type = symbol.Type;
            bool nullable = varSyntax.Type.Kind() == SyntaxKind.NullableType;
            var typeName = type.ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat);

            var propName = fieldName;
            string? docComment = reactiveNode.TargetSymbol.GetDocumentationCommentXml();

            // Parse attributes
            var attributes = reactiveNode.TargetSymbol.GetAttributes();
            DiagnosticInfo? diag = ParseAttributes(targetType, classSymbol, fieldName, ref propName, attributes,
                !varSyntax.IsKind(SyntaxKind.PartialKeyword), varSyntax.Identifier.GetLocation(),
                out ReactivePropertyParams? reactiveProps, out BindablePropertyParams? bindingProps);
            if (diag != null)
                return (null, nullable, diag);

            bool generateField = false;
            if (fieldName == propName)
            {
                if (!varSyntax.IsKind(SyntaxKind.PartialKeyword))
                {
                    return (null, nullable, new(DiagnosticDescriptors.PropAlreadyDefined, varSyntax.Identifier.GetLocation(), classSymbol.Name));
                }

                if (char.IsLower(propName[0]))
                    fieldName = char.ToLower(propName[0]) + propName[1..];
                else
                    fieldName = '_' + propName;

                generateField = true;
            }

            bool readOnly = symbol.IsReadOnly;
            ReactiveObjectField fld = new(typeName, fieldName, propName, docComment, generateField, readOnly, IsBindableType(type), reactiveProps!, bindingProps!);
            return (fld, nullable, null);
        }

        private static (ReactiveObjectField? fld, bool nullable, DiagnosticInfo? diag)
            ParseReactiveField(TypeDeclarationSyntax targetType, INamedTypeSymbol classSymbol, GeneratorAttributeSyntaxContext reactiveNode)
        {
            var fieldName = reactiveNode.TargetSymbol.Name;
            var varSyntax = (VariableDeclarationSyntax)reactiveNode.TargetNode.Parent!;
            var symbol = ((IFieldSymbol)reactiveNode.TargetSymbol);
            var type = symbol.Type;
            bool nullable = varSyntax.Type.Kind() == SyntaxKind.NullableType;
            var typeName = type.ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat);//type.ToString();
                                                                                                                  //if (nullable)
                                                                                                                  //    typeName = typeName + '?';
            var propName = FormatPropName(fieldName);
            string? docComment = reactiveNode.TargetSymbol.GetDocumentationCommentXml();

            // Parse attributes
            var attributes = reactiveNode.TargetSymbol.GetAttributes();
            DiagnosticInfo? diag = ParseAttributes(targetType, classSymbol, fieldName, ref propName, attributes, 
                false, varSyntax.GetLocation(),
                out ReactivePropertyParams? reactiveProps, out BindablePropertyParams? bindingProps);
            if (diag != null)
                return (null, nullable, diag);

            bool readOnly = symbol.IsReadOnly || symbol.IsConst;
            ReactiveObjectField fld = new(typeName, fieldName, propName, docComment, false, readOnly, IsBindableType(type), reactiveProps!, bindingProps!);
            return (fld, nullable, null);
        }

        private static DiagnosticInfo? ParseAttributes(TypeDeclarationSyntax targetType, INamedTypeSymbol classSymbol,
            string fieldName, ref string propName, ImmutableArray<AttributeData> attributes,
            bool fieldIsProp, Location location,
            out ReactivePropertyParams? reactiveProps, out BindablePropertyParams? bindingProps)
        {
            bindingProps = null;
            reactiveProps = null;
            string? getFunc = null;
            string? setAction = null;
            string? accessibility = null;
            bool getInline = false;
            bool setInline = false;
            string? propTemplate = null;
            bool privateSet = false;
            bool skipCompare = false;
            string? bindingVM2M = null;
            string? bindingM2VM = null;
            string? bindingPath = null;
            bool skipBinding = false;
            var reactiveDependants = ImmutableArray.CreateBuilder<string>();
            foreach (var attrib in attributes)
            {
                var args = attrib.ConstructorArguments;
                switch (attrib?.AttributeClass?.Name)
                {
                    case nameof(ReactiveAttribute):
                        if (args.Length >= 1 && args[0].Value is string propNameArg)
                            propName = propNameArg;
                        break;
                    case nameof(ChangesPropAttribute):
                        if (args.Length >= 1)
                            reactiveDependants.Add((string)args[0].Value!);
                        break;
                    case nameof(TemplatePropAttribute):
                        if (args.Length >= 1)
                            propTemplate = (string)args[0].Value!;
                        break;
                    case nameof(CustomGetAttribute):
                        if (args.Length >= 1)
                            getFunc = (string)args[0].Value!;
                        if (args.Length >= 2)
                            getInline = (bool)args[1].Value!;
                        break;
                    case nameof(CustomSetAttribute):
                        if (args.Length >= 1)
                            setAction = (string)args[0].Value!;
                        if (args.Length >= 2)
                            setInline = (bool)args[1].Value!;
                        break;
                    case nameof(CustomAccessibilityAttribute):
                        if (args.Length >= 1)
                            accessibility = (string)args[0].Value!;
                        break;
                    case nameof(ReadonlyAttribute):
                        privateSet = true;
                        break;
                    case nameof(SkipEqualityCheckAttribute):
                        skipCompare = true;
                        break;

                    case nameof(ModelCustomBindingAttribute):
                        if (args.Length >= 2)
                        {
                            bindingVM2M = (string)args[0].Value!;
                            bindingM2VM = (string)args[1].Value!;
                        }
                        break;
                    case nameof(ModelBindsToAttribute):
                        if (args.Length >= 1)
                            bindingPath = (string)args[0].Value!;
                        break;
                    case nameof(ModelSkipAttribute):
                        skipBinding = true;
                        break;
                    default:
                        break;
                }
            }

            if ((getFunc != null || setAction != null) && propTemplate != null)
            {
                return new(DiagnosticDescriptors.GetSetAndTemplate, location, classSymbol.Name);
            }

            if (bindingPath != null && (bindingVM2M != null || bindingM2VM != null))
            {
                return new(DiagnosticDescriptors.CustomBindingAndBindingPath, location, classSymbol.Name);
            }

            if (skipBinding && (bindingPath != null || bindingVM2M != null || bindingM2VM != null))
            {
                return new(DiagnosticDescriptors.SkipAndCustomBinding, location, classSymbol.Name);
            }

            if (!skipBinding && bindingPath == null)
            {
                bindingPath = fieldName;
                //modelType.GetMembers(bindingPath);
            }

            if (fieldIsProp)
                propTemplate = fieldName;

            reactiveProps = new(getFunc, getInline, setAction, setInline, propTemplate,
                accessibility, reactiveDependants.DrainToImmutable().AsEquatable(), 
                privateSet, skipCompare);
            bindingProps = new(bindingVM2M, bindingM2VM, bindingPath, skipBinding);
            return null;
        }

        private static bool IsBindableType(ITypeSymbol symbol)
        {
            var parent = symbol;
            do
            {
                if (parent.Name == "BindableViewModel")
                {
                    return true;
                }
            } while ((parent = parent.BaseType) != null);
            return false;
        }

        private static string FormatPropName(string fieldName)
        {
            if (char.IsLower(fieldName[0]))
                return $"{char.ToUpper(fieldName[0])}{fieldName[1..]}";

            return $"_{fieldName}";
        }
    }

    public record ReactiveObjectResult(ReactiveObjectClass? Class, DiagnosticInfo? Diagnostic);
    public record ReactiveObjectClass(Accessibility Accessibility, string Namespace, string Assembly, string ClassName,
        bool EnableNullable, bool IsObservable, string? ModelType, string? BaseModelType,
        EquatableArray<ReactiveObjectField> ReactiveFields);
    public record ReactiveObjectField(string FieldType, string FieldName, string PropName, string? DocComment, 
        bool GenerateField, bool IsReadOnly, bool IsBindable, ReactivePropertyParams ReactiveParams, 
        BindablePropertyParams BindableParams);
    public record ReactivePropertyParams(string? OnGetFunc, bool GetInline, string? OnSetAction, bool SetInline,
        string? PropTemplate, string? CustomAccessibility, EquatableArray<string> ReactiveDependants, 
        bool PrivateSet, bool SkipCompare);
    public record BindablePropertyParams(string? BindingVM2M, string? BindingM2VM, string? BindingPath, bool SkipBinding);
}
