using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Forms;
using System.Windows.Input;
using TUnit.Assertions;
using TUnit.Assertions.Should;
using TUnit.Assertions.Should.Extensions;
using WindowsInput;

namespace QPlayer.Tests;

[NotInParallel]
internal class CueListUITests
{
    private static Process? process;
    private static AutomationElement? rootElement;
    private static readonly Dictionary<CachedElementKey, AutomationElement> cachedElements = [];
    private static InputSimulator? input;
    private static KeyboardSimulator? kb;

    public record struct CachedElementKey(string? Name = null, string? Id = null, string? Type = null, AutomationElement? Parent = null);

    [Before(Class)]
    public static void SetupTests()
    {
        // Find QPlayer...
        var path = Environment.ProcessPath;
        while (path != null)
        {
            path = Path.GetDirectoryName(path);
            if (Path.GetFileName(path) == "QPlayer")
                break;
        }
        if (path == null)
            Assert.Fail("Couldn't find QPlayer executable!");
        path = Path.Combine(path!, @"QPlayer\bin\Debug\net10.0-windows\qplayer.exe");

        // Start an instance of QPlayer
        process = Process.Start(path);

        // Find the window
        for (int i = 0; i < 100; i++)
        {
            rootElement = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.AutomationIdProperty, "QPlayerMainWindow"));
            if (rootElement != null)
                break;
            Thread.Sleep(100);
        }

        if (rootElement == null)
            Assert.Fail("Couldn't find QPlayerWindow after 10 seconds!");

        input = new InputSimulator();
        kb = input.Keyboard as KeyboardSimulator;
    }

    [After(Class)]
    public static void TeardownTests()
    {
        process?.Kill(true);
    }

    /// <summary>
    /// Gets a UI element by name.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="type"></param>
    /// <param name="parent"></param>
    /// <param name="useCache"></param>
    /// <returns></returns>
    private static AutomationElement? GetByName(string name, string? type = null, AutomationElement? parent = null, bool useCache = true, bool findHidden = false)
    {
        if (useCache && cachedElements.TryGetValue(new(name, Type: type, Parent: parent), out var res))
            return res;

        Condition cond = new PropertyCondition(AutomationElement.NameProperty, name);
        if (type != null)
            cond = new AndCondition(cond, new PropertyCondition(AutomationElement.ClassNameProperty, type));
        parent ??= rootElement;

        if (findHidden)
        {
            var walker = new TreeWalker(cond);
            res = walker.GetFirstChild(parent);
        }
        else
        {
            res = parent?.FindFirst(TreeScope.Descendants, cond);
        }
        if (res != null)
            cachedElements.TryAdd(new(name, Type: type, Parent: parent), res);

        return res;
    }

    private static AutomationElement? GetById(string id, string? type = null, AutomationElement? parent = null, bool useCache = true)
    {
        if (useCache && cachedElements.TryGetValue(new(Id: id, Type: type, Parent: parent), out var res))
            return res;

        Condition cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
        if (type != null)
            cond = new AndCondition(cond, new PropertyCondition(AutomationElement.ClassNameProperty, type));
        parent ??= rootElement;

        res = parent?.FindFirst(TreeScope.Descendants, cond);
        if (res != null)
            cachedElements.TryAdd(new(Id: id, Type: type, Parent: parent), res);

        return res;
    }

    private static AutomationElement? GetLabeledControl(string label, AutomationElement? parent = null)
    {
        parent ??= rootElement;
        var labelElem = GetByName(label, parent: parent);
        Assert.NotNull(labelElem);
        return TreeWalker.ControlViewWalker.GetNextSibling(labelElem);
    }

    private static bool Invoke(AutomationElement element)
    {
        if (!element.TryGetCachedPattern(InvokePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern);

        var invoke = pattern as InvokePattern;
        invoke?.Invoke();
        return invoke != null;
    }

    private static bool Expand(AutomationElement element)
    {
        if (!element.TryGetCachedPattern(ExpandCollapsePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern);

        var invoke = pattern as ExpandCollapsePattern;
        invoke?.Expand();
        //Thread.Sleep(50);
        return invoke != null;
    }

    private static bool SelectItem(AutomationElement element, bool replace = true)
    {
        if (!element.TryGetCachedPattern(SelectionItemPattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern);

        if (pattern is not SelectionItemPattern invoke)
            return false;

        if (replace)
            invoke.Select();
        else
        {
            if (invoke.Current.IsSelected)
                invoke.RemoveFromSelection();
            else
                invoke.AddToSelection();
        }

        return true;
    }

    private static bool SetValue(AutomationElement element, string value)
    {
        if (!element.TryGetCachedPattern(ValuePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern);

        var invoke = pattern as ValuePattern;
        invoke?.SetValue(value);
        //Thread.Sleep(50);
        return invoke != null;
    }

    private static string? GetValue(AutomationElement element)
    {
        if (!element.TryGetCachedPattern(ValuePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern);

        if (pattern is not ValuePattern value)
            return null;

        return value.Current.Value;
    }

    private static bool InvokeKeybind(string keybind)
    {
        var groupBtn = GetByName($"Automation {keybind}", findHidden: true);
        if (groupBtn == null)
            return false;
        return Invoke(groupBtn);
    }

    private static void SetCueTextField(string property, string value)
    {
        var editor = GetByName("Selected Cue", "TabItem");
        Assert.NotNull(editor);
        SelectItem(editor);
        var textField = GetLabeledControl(property, editor);
        Assert.NotNull(textField);
        //var tb = GetById("TextBox", parent: nameControl);
        //Assert.NotNull(tb);
        SetValue(textField, value);
    }

    private static string? GetCueTextField(string property)
    {
        var editor = GetByName("Selected Cue", "TabItem");
        Assert.NotNull(editor);
        SelectItem(editor);
        var textField = GetLabeledControl(property, editor);
        Assert.NotNull(textField);
        //var tb = GetById("TextBox", parent: nameControl);
        // Assert.NotNull(tb);
        return GetValue(textField);
    }

    private static void NewProject()
    {
        var menu = GetByName("File");
        Assert.NotNull(menu);
        Expand(menu);
        menu = GetByName("New", "MenuItem");
        Assert.NotNull(menu);
        Invoke(menu);
    }

    private static void CreateCue(string? name = null, string type = "Sound Cue")
    {
        if (type == "Sound Cue")
        {
            var btn = GetByName("Automation Add Sound Cue", findHidden: true);
            Assert.NotNull(btn);
            Invoke(btn);
        }
        else
        {
            var menu = GetByName("Edit");
            Assert.NotNull(menu);
            Expand(menu);
            menu = GetByName("Create Cue", "MenuItem");
            Assert.NotNull(menu);
            Expand(menu);
            menu = GetByName($"Add {type}", "MenuItem");
            Assert.NotNull(menu);
            Invoke(menu);
        }

        if (name != null)
            SetCueTextField("Cue Name", name);
    }

    /// <summary>
    /// Selects a cue in the visual cue stack by name or by QID.
    /// </summary>
    /// <param name="identifier">The name/qid to search for.</param>
    /// <param name="byName">Whether the <paramref name="identifier"/> is a name or a qid.</param>
    /// <param name="replace">Whether the current selection should be replaced.</param>
    /// <returns></returns>
    private static bool SelectCue(string identifier, bool byName = false, bool replace = true)
    {
        var cueList = GetById("CueListControl");
        Assert.NotNull(cueList);
        var cond = new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, byName ? "Name" : "QID"),
            new PropertyCondition(ValuePattern.ValueProperty, identifier));
        var cueID = cueList.FindFirst(TreeScope.Descendants, cond);
        if (cueID == null)
            return false;
        var cue = TreeWalker.ContentViewWalker.GetParent(cueID);
        if (cue == null)
            return false;
        return SelectItem(cue, replace);
    }

    private static bool CheckCueNames(ICollection<string> names)
    {
        var cueList = GetById("CueListControl");
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, "Name");
        var fields = cueList.FindAll(TreeScope.Descendants, cond);

        if (names.Count != fields.Count) return false;

        using var namesEnum = names.GetEnumerator();
        foreach (var field in fields)
        {
            if (!namesEnum.MoveNext())
                break;
            var name = namesEnum.Current;
            var element = (AutomationElement)field;

            if (GetValue(element) != name)
                return false;
        }
        return true;
    }

    private static bool CreateTestFile()
    {
        NewProject();
        CreateCue("A"); // 1
        CreateCue("B"); // 2
        CreateCue("C"); // 3
        CreateCue("E"); // 4-1
        CreateCue("F"); // 4-2
        CreateCue("G"); // 4-3
        CreateCue("H"); // 5
        CreateCue("I"); // 6
        CreateCue("J"); // 7

        SelectCue("E", true, true);
        SelectCue("F", true, false);
        SelectCue("G", true, false);

        InvokeKeybind("Ctrl+G");
        
        SetCueTextField("Cue Name", "D");

        SelectCue("H", true);
        SetCueTextField("Cue ID", "5");
        SelectCue("I", true);
        SetCueTextField("Cue ID", "6");
        SelectCue("J", true);
        SetCueTextField("Cue ID", "7");

        return CheckCueNames(["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"]);
    }

    [Test]
    public async Task TestCreateTestCues()
    {
        await CreateTestFile().Should().BeTrue();
    }


    [Test]
    public async Task TestCreate()
    {
        CreateTestFile();

        SelectCue("A", true);
        CreateCue("A1");
        await CheckCueNames(["A", "A1", "B", "C", "D", "E", "F", "G", "H", "I", "J"]).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("1.1");

        SelectCue("D", true);
        CreateCue("D1");
        await CheckCueNames(["A", "A1", "B", "C", "D", "E", "F", "G", "D1", "H", "I", "J"]).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("4.1");

        SelectCue("G", true);
        CreateCue("G1");
        await CheckCueNames(["A", "A1", "B", "C", "D", "E", "F", "G", "G1", "D1", "H", "I", "J"]).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("4");

        SelectCue("J", true);
        CreateCue("J1");
        await CheckCueNames(["A", "A1", "B", "C", "D", "E", "F", "G", "G1", "D1", "H", "I", "J", "J1"]).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("8");
    }
}
