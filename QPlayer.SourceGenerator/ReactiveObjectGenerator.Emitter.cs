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
#pragma warning disable CS0162
    const bool LOG_PROP_CHANGE = false;

    private static void Emit(SourceProductionContext context, ReactiveObjectResult result)
    {
        if (result.Diagnostic != null)
            context.ReportDiagnostic(result.Diagnostic.ToDiagnostic());
        if (result.Class is not ReactiveObjectClass model)
            return;

        IndentedStringBuilder sb = new();

        Helpers.EmitFileHeader(sb, model.Namespace, model.EnableNullable || model.ModelType != null || !model.IsObservable, ["QPlayer.SourceGenerator"]);

        sb.AppendLine("// Suppress any 'member may be null when exiting' warnings");
        sb.AppendLine("#pragma warning disable CS8774");
        sb.AppendLine();

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

                if (prop.ReactiveParams.CachePropNotif)
                {
                    sb.AppendLine($"private static readonly global::System.ComponentModel.PropertyChangingEventArgs {prop.FieldName}_ChangingEventArgs = new(\"{prop.PropName}\");");
                    sb.AppendLine($"private static readonly global::System.ComponentModel.PropertyChangedEventArgs {prop.FieldName}_ChangedEventArgs = new(\"{prop.PropName}\");");
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
                            GeneratePropChanging(sb, prop);
                            if (prop.ReactiveParams.SetInline)
                                sb.AppendLine($"{prop.ReactiveParams.OnSetAction};");
                            else
                                sb.AppendLine($"{prop.ReactiveParams.OnSetAction}(value);");
                            GeneratePropChanged(sb, prop);
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
                            GeneratePropChanging(sb, prop);
                            if (!prop.ReactiveParams.NoUndo && model.BaseModelType != null)
                                sb.AppendLine($"global::QPlayer.ViewModels.UndoManager.RegisterAction(nameof({prop.PropName}), this, {propTemplate}, value);");
                            sb.AppendLine($"{propTemplate} = value;");
                            GeneratePropChanged(sb, prop);
                            if (LOG_PROP_CHANGE)
                                sb.AppendLine($"System.Diagnostics.Debug.WriteLine($\"[PropChange] {prop.PropName} = {{{prop.PropName}}}\\t\\t(<- {{new System.Diagnostics.StackTrace().GetFrame(1).GetMethod()}})\");");
                        }
                        else
                        {
                            string prefix = string.Empty;
                            if (prop.FieldName == "value")
                                prefix = "this.";
                            if (!prop.ReactiveParams.SkipCompare)
                            {
                                sb.AppendLine($"if ({prefix}{prop.FieldName} == value)");
                                sb.AppendLine("    return;");
                            }
                            GeneratePropChanging(sb, prop);
                            if (!prop.ReactiveParams.NoUndo && model.BaseModelType != null)
                                sb.AppendLine($"global::QPlayer.ViewModels.UndoManager.RegisterAction(nameof({prop.PropName}), this, {prefix}{prop.FieldName}, value);");
                            sb.AppendLine($"{prefix}{prop.FieldName} = value;");
                            GeneratePropChanged(sb, prop);
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

                sb.AppendLine("private void OnPropertyChanged(global::System.ComponentModel.PropertyChangedEventArgs args)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine("PropertyChanged?.Invoke(this, args);");
                }
                sb.AppendLine();

                sb.AppendLine("private void OnPropertyChanging(global::System.ComponentModel.PropertyChangingEventArgs args)");
                using (sb.EnterCurlyBracket())
                {
                    sb.AppendLine("PropertyChanging?.Invoke(this, args);");
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

            sb.AppendLine("private bool SetPropertyCached<T>(ref T field, T newValue, global::System.ComponentModel.PropertyChangingEventArgs argsChanging, global::System.ComponentModel.PropertyChangedEventArgs argsChanged)");
            using (sb.EnterCurlyBracket())
            {
                sb.AppendLine("if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, newValue))");
                sb.AppendLine("    return false;");
                sb.AppendLine();
                sb.AppendLine("OnPropertyChanging(argsChanging);");
                sb.AppendLine("field = newValue;");
                sb.AppendLine("OnPropertyChanged(argsChanged);");
                sb.AppendLine();
                sb.AppendLine("return true;");
            }
            sb.AppendLine();

            if (model.BaseModelType != null)
            {
                GenerateBind(model, sb);
                GenerateSyncToModel(model, sb);
                GenerateSyncFromModel(model, sb);
            }
        }
        sb.AppendLine();

        sb.AppendLine("#pragma warning restore CS8774");
        sb.AppendLine();

        var sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);

        context.AddSource($"{model.ClassName}_ReactiveObject.g.cs", sourceText);

        //EmitStylableClass(context, result);
    }

    private static void GeneratePropChanging(IndentedStringBuilder sb, ReactiveObjectField prop)
    {
        if (prop.ReactiveParams.CachePropNotif)
            sb.AppendLine($"OnPropertyChanging({prop.FieldName}_ChangingEventArgs);");
        else
            sb.AppendLine($"OnPropertyChanging(\"{prop.PropName}\");");
    }

    private static void GeneratePropChanged(IndentedStringBuilder sb, ReactiveObjectField prop)
    {
        if (prop.ReactiveParams.CachePropNotif)
            sb.AppendLine($"OnPropertyChanged({prop.FieldName}_ChangedEventArgs);");
        else
            sb.AppendLine($"OnPropertyChanged(\"{prop.PropName}\");");
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
}

#pragma warning restore CS0162
