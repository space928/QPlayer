using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace QPlayer.SourceGenerator;

public partial class ReactiveObjectGenerator
{
    const bool LOG_PROP_CHANGE = false;

    private static void Emit(SourceProductionContext context, ReactiveObjectResult result)
    {
        if (result.Diagnostic != null)
            context.ReportDiagnostic(result.Diagnostic.ToDiagnostic());
        if (result.Class is not ReactiveObjectClass model)
            return;

        IndentedStringBuilder sb = new();

        Helpers.EmitFileHeader(sb, model.Namespace, model.EnableNullable || model.ModelType != null || !model.IsObservable, ["QPlayer.SourceGenerator"]);

        sb.AppendLine($"{model.Accessibility.GetText()} partial class {model.ClassName}");
        using (sb.EnterCurlyBracket())
        {
            foreach (var prop in model.ReactiveFields)
            {
                if (prop.GenerateField)
                {
                    sb.AppendLine($"///<summary>Backing field for <see cref='{prop.PropName}'/></summary>");
                    sb.AppendLine($"private {prop.FieldType} {prop.FieldName};");
                    sb.AppendLine();
                }

                // Generate a property
                if (!string.IsNullOrEmpty(prop.DocComment))
                    Helpers.PrintDocComment(sb, prop.DocComment!);
                else
                    sb.AppendLine($"///<summary>Reactive property for <see cref='{prop.FieldName}'/></summary>");
                if (prop.ReactiveParams.CustomAccessibility != null)
                    sb.AppendLine($"{prop.ReactiveParams.CustomAccessibility} {prop.FieldType} {prop.PropName}");
                else
                    sb.AppendLine($"public {prop.FieldType} {prop.PropName}");
                using (sb.EnterCurlyBracket())
                {
                    // Getter
                    if (prop.ReactiveParams.OnGetFunc != null)
                    {
                        if (prop.ReactiveParams.GetInline)
                            sb.AppendLine($"get => {prop.ReactiveParams.OnGetFunc};");
                        else
                            sb.AppendLine($"get => {prop.ReactiveParams.OnGetFunc}();");
                    }
                    else if (prop.ReactiveParams.PropTemplate is string propTemplate)
                    {
                        sb.AppendLine($"get => {propTemplate};");
                    }
                    else
                    {
                        sb.AppendLine($"get => {prop.FieldName};");
                    }

                    if (prop.IsReadOnly)
                        continue;

                    // Setter
                    if (prop.FieldType.EndsWith("?") || prop.ReactiveParams.PrivateSet) // A bit of a hack, but helps silence warnings for now.
                        sb.AppendLine($"[System.Diagnostics.CodeAnalysis.MemberNotNull(nameof({prop.FieldName}))]");
                    if (prop.ReactiveParams.PrivateSet)
                        sb.AppendLine($"private set");
                    else
                        sb.AppendLine($"set");
                    using (sb.EnterCurlyBracket())
                    {
                        //sb.AppendLine($"{prop.FieldName} = value;");
                        if (prop.ReactiveParams.OnSetAction != null)
                        {
                            sb.AppendLine($"OnPropertyChanging(\"{prop.PropName}\");");
                            if (prop.ReactiveParams.SetInline)
                                sb.AppendLine($"{prop.ReactiveParams.OnSetAction};");
                            else
                                sb.AppendLine($"{prop.ReactiveParams.OnSetAction}(value);");
                            sb.AppendLine($"OnPropertyChanged(\"{prop.PropName}\");");
                            if (LOG_PROP_CHANGE)
                                sb.AppendLine($"System.Diagnostics.Debug.WriteLine($\"[PropChange] {prop.PropName} = {{{prop.PropName}}}\\t\\t(<- {{new System.Diagnostics.StackTrace().GetFrame(1).GetMethod()}})\");");
                        }
                        else if (prop.ReactiveParams.PropTemplate is string propTemplate)
                        {
                            if (!prop.ReactiveParams.SkipCompare)
                            {
                                sb.AppendLine($"if ({propTemplate} == value)");
                                sb.AppendLine("    return;");
                            }
                            sb.AppendLine($"OnPropertyChanging(\"{prop.PropName}\");");
                            sb.AppendLine($"{propTemplate} = value;");
                            sb.AppendLine($"OnPropertyChanged(\"{prop.PropName}\");");
                            if (LOG_PROP_CHANGE)
                                sb.AppendLine($"System.Diagnostics.Debug.WriteLine($\"[PropChange] {prop.PropName} = {{{prop.PropName}}}\\t\\t(<- {{new System.Diagnostics.StackTrace().GetFrame(1).GetMethod()}})\");");
                        }
                        else
                        {
                            string prefix = string.Empty;
                            if (prop.FieldName == "value")
                                prefix = "this.";
                            if (prop.ReactiveParams.SkipCompare)
                            {
                                sb.AppendLine($"OnPropertyChanging(\"{prop.PropName}\");");
                                sb.AppendLine($"{prefix}{prop.FieldName} = value;");
                                sb.AppendLine($"OnPropertyChanged(\"{prop.PropName}\");");
                            }
                            else
                            {
                                sb.AppendLine($"if (!SetProperty(ref {prefix}{prop.FieldName}, value))");
                                sb.AppendLine("    return;");
                            }
                            if (LOG_PROP_CHANGE)
                                sb.AppendLine($"System.Diagnostics.Debug.WriteLine($\"[PropChange] {prop.PropName} = {{{prop.PropName}}}\\t\\t(<- {{new System.Diagnostics.StackTrace().GetFrame(1).GetMethod()}})\");");
                        }

                        // Traverse the tree of property dependencies to emit the required OnPropertyChanged notifications
                        HashSet<string> deps = [];
                        Queue<string> depsToSearch = new(prop.ReactiveParams.ReactiveDependants);
                        while (depsToSearch.Count > 0)
                        {
                            var dep = depsToSearch.Dequeue();
                            if (deps.Add(dep))
                            {
                                sb.AppendLine($"OnPropertyChanged(\"{dep}\");");
                                if (LOG_PROP_CHANGE)
                                    sb.AppendLine($"System.Diagnostics.Debug.WriteLine($\"[PropChange]    {dep} = {{{dep}}}\");");
                                // Follow the tree of dependencies if needed
                                if (model.ReactiveFields.FirstOrDefault(x => x.ReactiveParams.ReactiveDependants.Length > 0 && x.PropName == dep) is ReactiveObjectField sub)
                                {
                                    foreach (var subDep in sub.ReactiveParams.ReactiveDependants)
                                        depsToSearch.Enqueue(subDep);
                                }
                            }
                        }
                    }
                }
                sb.AppendLine();
            }

            if (!model.IsObservable)
            {
                // Implement the observable object methods
                sb.AppendLine("private void OnPropertyChanged(string? name)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine("PropertyChanged?.Invoke(this, new(name));");
                }
                sb.AppendLine();

                sb.AppendLine("private void OnPropertyChanging(string? name)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine("PropertyChanging?.Invoke(this, new(name));");
                }
                sb.AppendLine();

                sb.AppendLine("private bool SetProperty<T>(ref T field, T newValue, [global::System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine("if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, newValue))");
                    sb.AppendLine("    return false;");
                    sb.AppendLine();
                    sb.AppendLine("OnPropertyChanging(propertyName);");
                    sb.AppendLine("field = newValue;");
                    sb.AppendLine("OnPropertyChanged(propertyName);");
                    sb.AppendLine();
                    sb.AppendLine("return true;");
                }
                sb.AppendLine();
            }

            if (model.BaseModelType != null)
            {
                GenerateBind(model, sb);
                GenerateSyncToModel(model, sb);
                GenerateSyncFromModel(model, sb);
            }
        }
        sb.AppendLine();

        var sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);

        context.AddSource($"{model.ClassName}_ReactiveObject.g.cs", sourceText);

        //EmitStylableClass(context, result);
    }

    private static void GenerateBind(ReactiveObjectClass model, IndentedStringBuilder sb)
    {
        sb.AppendLine($"public override void Bind({model.BaseModelType}? model)");
        using (sb.EnterCurlyBracket())
        {
            sb.AppendLine("if (model == boundModel)");
            sb.AppendLine("    return;");
            sb.AppendLine("base.Bind(model); // Recursively bind up the chain, allowing property changed events on the parent to be listened to");
            sb.AppendLine();
            sb.AppendLine("if (model == null)");
            sb.AppendLine("    PropertyChanged -= BindableViewModel_PropertyChanged;");
            sb.AppendLine("else");
            sb.AppendLine("    PropertyChanged += BindableViewModel_PropertyChanged;");
        }
        sb.AppendLine();

        sb.AppendLine("private void BindableViewModel_PropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)");
        using (sb.EnterCurlyBracket())
        {
            sb.AppendLine($"if (e.PropertyName is not string __prop || boundModel is not {model.ModelType} __model)");
            sb.AppendLine("    return;");
            sb.AppendLine();
            sb.AppendLine("switch (__prop)");
            using (sb.EnterCurlyBracket())
            {
                foreach (var prop in model.ReactiveFields)
                {
                    if (prop.BindableParams.SkipBinding || prop.IsReadOnly || prop.IsBindable)
                        continue;

                    sb.AppendIndent();
                    sb.Append($"case nameof({prop.PropName}): ");
                    if (prop.BindableParams.BindingVM2M is string vm2m)
                        sb.Append($"{vm2m}(this, __model); ");
                    else if (prop.BindableParams.BindingPath is string bindPath)
                        sb.Append($"__model.{bindPath} = {prop.PropName}; ");
                    sb.Append("break;");
                    sb.AppendLine();
                }
            }
        }
        sb.AppendLine();
    }

    private static void GenerateSyncToModel(ReactiveObjectClass model, IndentedStringBuilder sb)
    {
        sb.AppendLine($"protected override void OnSyncToModel()");
        using (sb.EnterCurlyBracket())
        {
            sb.AppendLine($"if (boundModel is not {model.ModelType} __model)");
            sb.AppendLine("    return;");
            sb.AppendLine();
            sb.AppendLine("base.OnSyncToModel();");
            sb.AppendLine();
            foreach (var prop in model.ReactiveFields)
            {
                if (prop.BindableParams.SkipBinding || prop.IsReadOnly)
                    continue;

                if (prop.BindableParams.BindingVM2M is string vm2m)
                {
                    sb.AppendLine($"{vm2m}(this, __model);");
                }
                else if (prop.BindableParams.BindingPath is string bindPath)
                {
                    if (prop.IsBindable)
                    {
                        sb.AppendLine($"__model.{bindPath} ??= new();");
                        sb.AppendLine($"{prop.PropName}.Bind(__model.{bindPath});");
                        sb.AppendLine($"{prop.PropName}.SyncToModel();");
                    }
                    else
                    {
                        sb.AppendLine($"__model.{bindPath} = {prop.PropName};");
                    }
                }
            }
        }
        sb.AppendLine();
    }

    private static void GenerateSyncFromModel(ReactiveObjectClass model, IndentedStringBuilder sb)
    {
        sb.AppendLine($"protected override void OnSyncFromModel()");
        using (sb.EnterCurlyBracket())
        {
            sb.AppendLine($"if (boundModel is not {model.ModelType} __model)");
            sb.AppendLine("    return;");
            sb.AppendLine();
            sb.AppendLine("base.OnSyncFromModel();");
            sb.AppendLine();
            foreach (var prop in model.ReactiveFields)
            {
                if (prop.BindableParams.SkipBinding || prop.IsReadOnly)
                    continue;

                if (prop.BindableParams.BindingM2VM is string m2vm)
                {
                    sb.AppendLine($"{m2vm}(this, __model);");
                }
                else if (prop.BindableParams.BindingPath is string bindPath)
                {
                    if (prop.IsBindable)
                    {
                        sb.AppendLine($"if(__model.{bindPath} == null)");
                        using (sb.EnterCurlyBracket())
                        {
                            sb.AppendLine("// Create a new default instance using the values in this view model.");
                            sb.AppendLine($"__model.{bindPath} = new();");
                            sb.AppendLine($"{prop.PropName}.Bind(__model.{bindPath});");
                            sb.AppendLine($"{prop.PropName}.SyncToModel();");
                        }
                        sb.AppendLine("else");
                        using (sb.EnterCurlyBracket())
                        {
                            sb.AppendLine($"{prop.PropName}.Bind(__model.{bindPath});");
                            sb.AppendLine($"{prop.PropName}.SyncFromModel();");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"{prop.PropName} = __model.{bindPath};");
                    }
                }
            }
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Emits a factory class for making stylable properties for a given UIElement.
    /// </summary>
    /// <example>
    /// /* Outputs the following code: */
    /// 
    /// // Factory method for a new stylable prop
    /// public StylableProp<Colour> BgColour(Colour value)
    /// {
    ///     return new(value, Apply_Background);
    /// }
    /// 
    /// private void Apply_Background(UIElement elem, StylableProp<Colour> prop)
    /// {
    ///     switch (elem)
    ///     {
    ///         case Button button:
    ///             {
    ///                 button.Background = prop.Value;
    ///                 break;
    ///             }
    ///         default:
    ///             throw ...
    /// 	}
    /// }
    /// </example>
    /// <param name="context"></param>
    /// <param name="result"></param>
    /*private static void EmitStylableClass(SourceProductionContext context, ReactiveObjectResult result)
    {
        if (result.Diagnostic != null)
            context.ReportDiagnostic(result.Diagnostic);
        if (result.Class is not ReactiveObjectClass model)
            return;

        IndentedStringBuilder sb = new();

        Helpers.EmitFileHeader(sb, model.Namespace, model.EnableNullable, ["System.Runtime.CompilerServices", "ArgonUI.SourceGenerator", "ArgonUI.Styling", "ArgonUI.UIElements"]);

        sb.AppendLine($"[GeneratedStyles(\"{model.Assembly}\", \"{model.ClassName}\")]");
        sb.AppendLine($"{model.Accessibility.GetText()} static partial class {model.ClassName}_Styles");
        using (sb.EnterCurlyBracket())
        {
            foreach (var prop in model.ReactiveFields)
            {
                if (prop.Stylable == null)
                    continue;

                // Generate a factory method
                if (!string.IsNullOrEmpty(prop.DocComment))
                    Helpers.PrintDocComment(sb, prop.DocComment!);
                sb.AppendLine("/// <remarks>This is a factory method for a stylable property.</remarks>");
                sb.AppendLine("/// <param name=\"value\">The initial value of the new stylable property.</param>");
                sb.AppendLine($"public static StylableProp<{prop.FieldType}> {prop.PropName}({prop.FieldType} value)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine($"return new(value, Apply_{prop.PropName}, \"{prop.PropName}\");");
                }
                sb.AppendLine();

                // Generate the Apply method
                sb.AppendLine($"[MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"private static void Apply_{prop.PropName}(UIElement elem, IStylableProperty prop)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine($"(({model.ClassName})elem).{prop.PropName} = ((StylableProp<{prop.FieldType}>)prop).Value;");
                }
                sb.AppendLine();
            }
        }

        var sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);

        context.AddSource($"{model.ClassName}_Styles.g.cs", sourceText);
    }*/
}
