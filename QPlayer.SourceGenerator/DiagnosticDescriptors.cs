using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.SourceGenerator;

internal static class DiagnosticDescriptors
{
    const string Category = "GenerateReactiveObject";

    public static DiagnosticDescriptor MustBePartial => new(
        "AR1001",
        "ReactiveAttribute annotated field's declaring type must be partial",
        "The field declared in type '{0}' annotated with ReactiveAttribute's class must be declared as partial.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor CantBeGeneric => new(
        "AR1002",
        "ReactiveAttribute annotated field's declaring type cannot be a generic type",
        "The field declared in type '{0}' annotated with ReactiveAttribute's class cannot be a generic type.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor MustDeriveFromReactiveObject => new(
        "AR1003",
        "ReactiveAttribute annotated field's declaring type must derive from 'QPlayer.BindableViewModel<>'",
        "The field declared in type '{0}' annotated with ReactiveAttribute's class must derive from 'QPlayer.BindableViewModel<>'.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor GetSetAndTemplate => new(
        "AR1004",
        "A reactive property can't specify a template property and a custom getter or setter",
        "The reactive property declared in type '{0}' must specify one of TemplatePropAttribute or Custom[Get/Set]Attribute.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor CustomBindingAndBindingPath => new(
        "AR1005",
        "A reactive property can't specify a custom binding function and a custom binding path",
        "The reactive property declared in type '{0}' must specify either ModelCustomBindingAttribute or ModelBindsToAttribute (or neither attribute).",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor SkipAndCustomBinding => new(
        "AR1006",
        "Reactive property will be skipped from model binding",
        "The reactive property declared in type '{0}' specifies both a ModelSkipAttribute and a ModelCustomBindingAttribute or ModelBindsToAttribute; the skip attribute takes priority and this property will be skipped.",
        Category,
        DiagnosticSeverity.Warning,
        true);

    public static DiagnosticDescriptor PropAlreadyDefined => new(
        "AR1007",
        "Reactive property is already defined, and is not being used as a template",
        "The reactive property declared in type '{0}' must be declared as partial for an automatic property implementation or must specify a property name to be used as a property template.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor AssemblyNotFound => new(
        "AR2004",
        "MergeStylesAttribute specified assembly couldn't be found",
        "The class '{0}' annotated with a MergeStylesAttribute specifies an assembly '{1}' which could not be found.",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static DiagnosticDescriptor ClonableMustDeriveFromUIElement => new(
        "AR3001",
        "UIClonableAttribute annotated type must derive from 'ArgonUI.UIElements.UIElement'",
        "The type '{0}' annotated with a UIClonableAttribute must derive from 'ArgonUI.UIElements.UIElement'.",
        Category,
        DiagnosticSeverity.Warning,
        true);
}

// Taken from: https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/
public sealed record DiagnosticInfo
{
    // Explicit constructor to convert Location into LocationInfo
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params object[]? msgArgs)
    {
        Descriptor = descriptor;
        Location = location is not null ? LocationInfo.CreateFrom(location) : null;
        DescriptorMsgArgs = msgArgs;
    }

    public DiagnosticDescriptor Descriptor { get; }
    public LocationInfo? Location { get; }
    public object[]? DescriptorMsgArgs { get; }

    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location?.ToLocation(), DescriptorMsgArgs);
}

// Taken from: https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/
public record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation()
        => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(SyntaxNode node)
        => CreateFrom(node.GetLocation());

    public static LocationInfo? CreateFrom(Location location)
    {
        if (location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

