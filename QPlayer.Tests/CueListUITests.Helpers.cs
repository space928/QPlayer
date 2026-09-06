using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using TUnit.Assertions.Should;
using TUnit.Assertions.Should.Extensions;

namespace QPlayer.Tests;

internal partial class CueListUITests
{
    private static Process? process;
    private static AutomationElement? rootElement;
    private static readonly Dictionary<CachedElementKey, AutomationElement> cachedElements = [];
    private static bool multipleSelected = false;

    public record struct CachedElementKey(string? Name = null, string? Id = null, string? Type = null, AutomationElement? Parent = null);

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

        uint start = (uint)Environment.TickCount;
        do
        {
            if (findHidden)
            {
                var walker = new TreeWalker(cond);
                res = walker.GetFirstChild(parent);
            }
            else
            {
                res = parent?.FindFirst(TreeScope.Descendants, cond);
            }
        }
        while (res == null && Environment.TickCount - start < 1000);

        if (res != null)
            cachedElements.AddOrUpdate(new(name, Type: type, Parent: parent), res);

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

        uint start = (uint)Environment.TickCount;
        do
        {
            res = parent?.FindFirst(TreeScope.Descendants, cond);
        }
        while (res == null && Environment.TickCount - start < 1000);

        if (res != null)
            cachedElements.AddOrUpdate(new(Id: id, Type: type, Parent: parent), res);

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
        //Thread.Yield();
        return invoke != null;
    }

    private static bool Expand(AutomationElement element)
    {
        if (!element.TryGetCachedPattern(ExpandCollapsePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern);

        var invoke = pattern as ExpandCollapsePattern;
        invoke?.Expand();
        //Thread.Yield();
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
            if (IsMultiSelected(element))
                invoke.RemoveFromSelection();
            else
                invoke.AddToSelection();
        }
        //Thread.Yield();

        return true;
    }

    private static bool IsSelected(AutomationElement element)
    {
        if (!element.TryGetCachedPattern(SelectionItemPattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern);

        if (pattern is not SelectionItemPattern sel)
            return false;

        return sel.Current.IsSelected;
    }

    private static bool IsMultiSelected(AutomationElement element)
    {
        return element.Current.ItemStatus == "MultiSelected";
    }

    private static bool SetValue(AutomationElement element, string value)
    {
        if (!element.TryGetCachedPattern(ValuePattern.Pattern, out var pattern))
            element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern);

        var invoke = pattern as ValuePattern;
        invoke?.SetValue(value);
        //Thread.Yield();
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

        AutomationElement? unsavedChangesDialogue = null;
        Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
        cond = new AndCondition(cond, new PropertyCondition(AutomationElement.NameProperty, "Unsaved Changes"));
        for (int i = 0; i < 20; i++)
        {
            unsavedChangesDialogue = rootElement?.FindFirst(TreeScope.Children, cond);
            if (unsavedChangesDialogue != null)
                break;
            Thread.Sleep(50);
        }
        if (unsavedChangesDialogue != null
            && unsavedChangesDialogue.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "No")) is AutomationElement button)
            Invoke(button);
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
        if (replace)
        {
            if (multipleSelected)
                InvokeKeybind("Esc");
            multipleSelected = false;
        }
        else
        {
            multipleSelected = true;
        }

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

    private static void SelectCues(IEnumerable<string> identifiers, bool byName = false, bool replace = true)
    {
        if (replace && multipleSelected)
            InvokeKeybind("Esc");
        multipleSelected = true;

        var cueList = GetById("CueListControl");
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, byName ? "Name" : "QID");
        var cueIDs = cueList.FindAll(TreeScope.Descendants, cond);
        if (cueIDs == null || cueIDs.Count == 0)
            return;

        HashSet<string> targetIDs = [.. identifiers];
        bool first = true;
        foreach (var cueID in cueIDs)
        {
            if (cueID is AutomationElement qidElem
                && GetValue(qidElem) is string qid
                && targetIDs.Contains(qid))
            {
                var cue = TreeWalker.ContentViewWalker.GetParent(qidElem);
                if (cue == null)
                    continue;
                SelectItem(cue, replace && first);
                first = false;
            }
        }
    }

    private static IEnumerable<AutomationElement> GetSelectedCues()
    {
        var cueList = GetById("CueListControl", useCache: false);
        Assert.NotNull(cueList);
        //var cond = new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty, "Name");
        var cdcs = cueList.FindAll(TreeScope.Children, Condition.TrueCondition);

        multipleSelected = false;
        int i = 0;
        foreach (var cdc in cdcs)
        {
            var element = (AutomationElement)cdc;

            if (IsMultiSelected(element))
            {
                yield return element;
                i++;
                if (i > 1)
                    multipleSelected = true;
            }
        }
    }

    private static IEnumerable<string> GetCueNames()
    {
        var cueList = GetById("CueListControl", useCache: false);
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, "Name");
        var fields = cueList.FindAll(TreeScope.Descendants, cond);

        foreach (var field in fields)
        {
            var element = (AutomationElement)field;

            var elemName = GetValue(element);
            if (elemName != null)
                yield return elemName;
        }
    }

    private static IEnumerable<string> GetQIDs()
    {
        var cueList = GetById("CueListControl", useCache: false);
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, "QID");
        var fields = cueList.FindAll(TreeScope.Descendants, cond);

        foreach (var field in fields)
        {
            var element = (AutomationElement)field;

            var elemName = GetValue(element);
            if (elemName != null)
                yield return elemName;
        }
    }

    /// <summary>
    /// Sets the QIDs of the selected items.
    /// </summary>
    /// <param name="qids"></param>
    private static void SetQIDs(IEnumerable<string> qids)
    {
        var selected = GetSelectedCues().ToArray();
        InvokeKeybind("Esc");
        foreach (var (qid, cue) in qids.Zip(selected))
        {
            SelectItem(cue);
            SetCueTextField("Cue ID", qid);
        }
        foreach (var sel in selected)
            SelectItem(sel, false);
    }

    /// <summary>
    /// Sets the QIDs of the selected items.
    /// </summary>
    /// <param name="qids"></param>
    private static void SetNames(IEnumerable<string> names)
    {
        var selected = GetSelectedCues().ToArray();
        InvokeKeybind("Esc");
        foreach (var (name, cue) in names.Zip(selected))
        {
            SelectItem(cue);
            SetCueTextField("Cue Name", name);
        }
        foreach (var sel in selected)
            SelectItem(sel, false);
    }

    private static bool CheckCueNames(string[] names)
    {
        var cueList = GetById("CueListControl", useCache: false);
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, "Name");
        var fields = cueList.FindAll(TreeScope.Descendants, cond);

        if (names.Length != fields.Count)
        {
            var targetNames = string.Join(',', fields.Cast<AutomationElement>().Select(GetValue));
            Assert.Fail($"Cue count does not match expected cue count. Found: {fields.Count} expected: {names.Length} ({targetNames})");
            return false;
        }

        int i = 0;
        foreach (var field in fields)
        {
            if (i >= names.Length)
                break;
            var name = names[i];
            var element = (AutomationElement)field;

            var elemName = GetValue(element);
            if (elemName != name)
            {
                Assert.Fail($"Cue with name {elemName} does not match the expected name {name} (@ index {i})");
                return false;
            }
            i++;
        }
        return true;
    }

    private static bool CheckQIDs(string[] qids)
    {
        var cueList = GetById("CueListControl", useCache: false);
        Assert.NotNull(cueList);
        var cond = new PropertyCondition(AutomationElement.NameProperty, "QID");
        var fields = cueList.FindAll(TreeScope.Descendants, cond);

        if (qids.Length != fields.Count)
        {
            var targetNames = string.Join(',', fields.Cast<AutomationElement>().Select(GetValue));
            Assert.Fail($"Cue count does not match expected cue count. Found: {fields.Count} expected: {qids.Length} ({targetNames})");
            return false;
        }

        int i = 0;
        foreach (var field in fields)
        {
            if (i >= qids.Length)
                break;
            var qid = qids[i];
            var element = (AutomationElement)field;

            var elemQid = GetValue(element);
            if (elemQid != qid)
            {
                Assert.Fail($"Cue with QID {elemQid} does not match the expected QID {qid} (@ index {i})");
                return false;
            }
            i++;
        }
        return true;
    }

    /// <summary>
    /// Undoes the specified number of actions, checks that the resulting cues match the names/qids given. 
    /// Then redoes the specified number of actions and checks that the state has been restored correctly.
    /// </summary>
    /// <param name="numToUndo"></param>
    /// <param name="expectedNames"></param>
    /// <param name="expectedQIDs"></param>
    /// <returns></returns>
    private static async Task CheckUndoRedo(int numToUndo, string[] expectedNames, string[] expectedQIDs)
    {
        var currNames = GetCueNames().ToArray();
        var currIds = GetQIDs().ToArray();

        for (int i = 0; i < numToUndo; i++)
            InvokeKeybind("Ctrl+Z");
        await CheckCueNames(expectedNames).Should().BeTrue();
        await CheckQIDs(expectedQIDs).Should().BeTrue();

        for (int i = 0; i < numToUndo; i++)
            InvokeKeybind("Ctrl+Shift+Z");
        await CheckCueNames(currNames).Should().BeTrue();
        await CheckQIDs(currIds).Should().BeTrue();
    }

    /// <summary>
    /// Creates a simple test file containing the following cues:<br/>
    /// <c>["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"]</c><br/>
    /// With the following QIDs:<br/>
    /// <c>["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "6", "7"]</c><br/>
    /// Cue 4 is a group cue, all others are sound cues.
    /// </summary>
    /// <param name="allowRetry">Sometimes unpredictable UI automation behaviours can result in tests 
    /// failling. This option allows this method to retry creating the test file once if it wasn't 
    /// correctly created the first time.</param>
    /// <returns></returns>
    private static bool CreateTestFile(bool allowRetry = true)
    {
        string[] expectedNames = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        string[] expectedQIDs = ["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "6", "7"];

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

        bool res = CheckCueNames(expectedNames) && CheckQIDs(expectedQIDs);
        if (!res && allowRetry)
            return CreateTestFile(false);
        return res;
    }
}
