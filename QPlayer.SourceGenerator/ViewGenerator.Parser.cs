using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Collections.Immutable;

namespace QPlayer.SourceGenerator;

public partial class ViewGeneratorGenerator
{
    private class Parser
    {
        internal IEnumerable<ViewResult> Parse(EquatableArray<GeneratorAttributeSyntaxContext> generatorAttributeSyntaxContexts)
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
                bool isBindable = false;
                var parent = classSymbol;
                do
                {
                    if (parent.Name == "BindableViewModel")
                    {
                        isBindable = true;
                        break;
                    }
                } while ((parent = parent.BaseType) != null);
                if (!isBindable)
                {
                    var diag = new DiagnosticInfo(DiagnosticDescriptors.MustDeriveFromReactiveObject, targetType.Identifier.GetLocation(), classSymbol.Name);
                    yield return new(null, diag);
                }

                // Find fields
                var fields = ImmutableArray.CreateBuilder<ViewProp>();
                foreach (var member in classSymbol.GetMembers())
                {
                    string propName = member.Name;
                    string propType;
                    string viewLabel = PrettyPrintPropName(propName);
                    EquatableArray<string>? enumValues = null;// = member.Type.SpecialType == SpecialType.System_Enum;
                    Range? range = null;
                    bool isKnob = false;
                    bool isSlider = false;
                    string? filePickerCmd = null;
                    string? btnCmd = null;
                    string? headingTitle = null;
                    string? tooltip = null;
                    bool isReadonly;
                    bool skip = false;

                    if (member is IPropertySymbol prop)
                    {
                        propType = prop.Type.Name;
                        isReadonly = prop.IsReadOnly;

                        if (prop.Type.SpecialType == SpecialType.System_Enum)
                        {
                            var members = prop.Type.GetMembers();
                        }
                    }
                    else if (member is IFieldSymbol field)
                    {
                        propType = field.Type.Name;
                        isReadonly = field.IsReadOnly;
                    }
                    else
                    {
                        continue;
                    }

                    var fieldAttributes = member.GetAttributes();
                    foreach (var attrib in fieldAttributes)
                    {
                        var args = attrib.ConstructorArguments;
                        switch (attrib?.AttributeClass?.Name)
                        {
                            case nameof(ReactiveAttribute):
                                if (args.Length >= 1 && !string.IsNullOrEmpty((string?)args[0].Value))
                                    propName = (string)args[0].Value!;
                                else
                                {
                                    if (char.IsLower(propName[0]))
                                        propName = char.ToUpper(propName[0]) + propName[1..];
                                    else
                                        propName = '_' + propName;
                                }
                                break;
                            case nameof(ViewLabelAttribute):
                                if (args.Length >= 1)
                                    viewLabel = (string)args[0].Value!;
                                break;
                            case nameof(SkipViewAttribute):
                                skip = true;
                                break;
                            case nameof(RangeAttribute):
                                double min = double.MinValue;
                                double max = double.MaxValue;
                                double rate = 0.1;
                                bool clamp = false;
                                if (args.Length >= 1)
                                    min = (double)args[0].Value!;
                                if (args.Length >= 2)
                                    max = (double)args[1].Value!;
                                if (args.Length >= 3)
                                    rate = (double)args[2].Value!;
                                if (args.Length >= 4)
                                    clamp = (bool)args[3].Value!;
                                range = new(min, max, rate, clamp);
                                break;
                            case nameof(KnobAttribute):
                                isKnob = true;
                                break;
                            case nameof(SliderAttribute):
                                isSlider = true;
                                break;
                            case nameof(FilePickerAttribute):
                                if (args.Length >= 1)
                                    filePickerCmd = (string)args[0].Value!;
                                break;
                            case nameof(ButtonAttribute):
                                if (args.Length >= 1)
                                    btnCmd = (string)args[0].Value!;
                                break;
                            case nameof(HeadingAttribute):
                                if (args.Length >= 1)
                                    headingTitle = (string)args[0].Value!;
                                break;
                            case nameof(TooltipAttribute):
                                if (args.Length >= 1)
                                    tooltip = (string)args[0].Value!;
                                break;

                        }
                    }

                    if (skip)
                        continue;

                    fields.Add(new(propName, propType, isReadonly, enumValues, range, isKnob, isSlider, filePickerCmd, btnCmd, headingTitle, tooltip, viewLabel));
                }

                ViewClass clonableClass = new(
                    classSymbol.DeclaredAccessibility,
                    classSymbol.ContainingNamespace.ToString(),
                    classSymbol.ContainingAssembly.Name,
                    classSymbol.Name,
                    classSymbol.IsAbstract, true,
                    fields.DrainToImmutable().AsEquatable());

                yield return new ViewResult(clonableClass, null);
            }
        }

        private static string PrettyPrintPropName(string propName)
        {
            StringBuilder sb = new(propName.Length);
            int pos = 0;
            sb.Append(char.ToUpper(propName[0]));
            pos++;

            bool wasUpper = true;
            for (; pos < propName.Length; pos++)
            {
                char c = propName[pos];
                bool upper = char.IsUpper(c) || char.IsNumber(c);
                if (upper && !wasUpper)
                    sb.Append(' ');
                sb.Append(c);
                wasUpper = upper;
            }

            return sb.ToString();
        }
    }

    private record ViewResult(ViewClass? ViewClass, DiagnosticInfo? Diagnostic);
    private record ViewClass(Accessibility Accessibility, string Namespace, string Assembly, string ClassName,
        bool IsAbstract, bool EnableNullable, EquatableArray<ViewProp> ViewProps);
    private record ViewProp(string PropName, string PropType, bool ReadOnly, EquatableArray<string>? EnumValues,
        Range? Range, bool IsKnob, bool IsSlider, string? FilePickerCmd, string? BtnCmd, string? HeadingTitle,
        string? Tooltip, string ViewLabel);
    private record Range(double Min, double Max, double Rate, bool Clamp);
}
