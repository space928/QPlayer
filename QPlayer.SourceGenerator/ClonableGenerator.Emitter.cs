using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.SourceGenerator;

public partial class ClonableGenerator
{
    /*
     This source generator generates the following code:

    public override UIElement Clone() => Clone(new Rectangle());

    public override UIElement Clone(UIElement target)
    {
        base.Clone(target);
        if (target is Rectangle t)
        {
            t.colour = colour;
            t.rounding = rounding;
        }
        return target;
    }
     
     */

    private static void Emit(SourceProductionContext context, UIClonableResult result)
    {
        if (result.Diagnostic != null)
            context.ReportDiagnostic(result.Diagnostic);
        if (result.ClonableClass is not UIClonableClass model)
            return;

        IndentedStringBuilder sb = new();

        Helpers.EmitFileHeader(sb, model.Namespace, model.EnableNullable, ["QPlayer.SourceGenerator", "ArgonUI.UIElements"]);

        sb.AppendLine($"{model.Accessibility.GetText()} partial class {model.ClassName}");
        using (sb.EnterCurlyBracket())
        {
            if (!model.IsAbstract)
            {
                sb.AppendLine($"public override UIElement Clone() => Clone(new {model.ClassName}());");
                sb.AppendLine();
            }
            sb.AppendLine("public override UIElement Clone(UIElement target)");
            using (sb.EnterCurlyBracket())
            {
                sb.AppendLine("base.Clone(target);");
                sb.AppendLine($"if (target is {model.ClassName} t)");
                using (sb.EnterCurlyBracket())
                {
                    foreach (var field in model.ClonableFields)
                    {
                        if (field.HasCloneMethod)
                            sb.AppendLine($"t.{field.FieldName} = {field.FieldName}.Clone();");
                        else
                            sb.AppendLine($"t.{field.FieldName} = {field.FieldName};");
                    }

                    if (model.EnableCustomClone)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"// Call the {model.ClassName}'s custom cloning code.");
                        sb.AppendLine("CustomClone(t);");
                    }
                }
                sb.AppendLine("return target;");
            }
        }

        var sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);

        context.AddSource($"{model.ClassName}_UIClonable.g.cs", sourceText);

        //EmitStylableClass(context, result);
    }
}
