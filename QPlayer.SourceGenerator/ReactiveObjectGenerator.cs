using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace QPlayer.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public partial class ReactiveObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();
        }
#endif 

        var parser = new Parser();

        var pipeline = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: typeof(ReactiveAttribute).FullName,
            predicate: static (syntaxNode, cancellationToken) => syntaxNode is VariableDeclaratorSyntax || syntaxNode is PropertyDeclarationSyntax,
            transform: static (context, cancellationToken) => context // TODO: Parsing should happen here, so that parsed syntax can be cached
        );

        
        var pipelineParsed = pipeline.Collect().Select((x, _) => parser.Parse(x.AsEquatable()).ToImmutableArray().AsEquatable());

        context.RegisterSourceOutput(pipelineParsed, ExecuteEmitReactiveObject);
    }

    private static void ExecuteEmitReactiveObject(SourceProductionContext context, EquatableArray<ReactiveObjectResult> sources)
    {
        //context.ReportDiagnostic(Diagnostic.Create(new("AR1000", "ReactiveObjectAttribute test", $"{sources.Length}", "TEST", DiagnosticSeverity.Warning, true), null));
        if (sources.Length == 0)
            return;

        foreach (var parsed in sources)
            Emit(context, parsed);
    }
}
