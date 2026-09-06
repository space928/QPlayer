using QPlayer.Utilities;
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
//using WindowsInput;

namespace QPlayer.Tests;

[NotInParallel]
internal partial class CueListUITests
{
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

        // Kill any exisiting processes
        var existing = Process.GetProcessesByName("qplayer");
        if (existing.Length > 0)
        {
            foreach (var proc in existing)
                proc.Kill(true);
        }

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

        //input = new InputSimulator();
        //kb = input.Keyboard as KeyboardSimulator;
    }

    [After(Class)]
    public static void TeardownTests()
    {
        process?.Kill(true);
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

    [Test]
    public async Task TestMoves()
    {
        CreateTestFile();

        string[] startNames = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        string[] startIds = ["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "6", "7"];

        // Move none
        SelectCue("A", true);
        InvokeKeybind("Ctrl+Up");
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        SelectCue("J", true);
        InvokeKeybind("Ctrl+Down");
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        SelectCue("A", true);
        SelectCue("B", true, false);
        InvokeKeybind("Ctrl+Up");
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        SelectCue("J", true);
        SelectCue("I", true, false);
        InvokeKeybind("Ctrl+Down");
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        // Normal moves
        SelectCue("B", true);
        InvokeKeybind("Ctrl+Up");
        await GetCueTextField("Cue ID").Should().BeEqualTo("0.1");
        await GetCueTextField("Cue Name").Should().BeEqualTo("B");

        SelectCue("I", true);
        InvokeKeybind("Ctrl+Down");
        await GetCueTextField("Cue ID").Should().BeEqualTo("8");
        await GetCueTextField("Cue Name").Should().BeEqualTo("I");

        SelectCue("B", true);
        SelectCue("C", true, false);
        InvokeKeybind("Ctrl+Up");
        await SelectCue("B", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("0.1");
        await SelectCue("C", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("0.2");

        SelectCue("H", true);
        SelectCue("I", true, false);
        InvokeKeybind("Ctrl+Down");
        await SelectCue("H", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("8");
        await SelectCue("I", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("9");

        // Check undo/redo
        await CheckUndoRedo(4, startNames, startIds);
    }

    [Test]
    public async Task TestMovesInGroups()
    {
        CreateTestFile();

        string[] startNames = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        string[] startIds = ["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "6", "7"];

        // For testing, duplicate the group
        SelectCues(["H", "I", "J"], true);
        SetQIDs(["6", "7", "8"]); // Renumber the last three cues to make space for the duplicated group
        SelectCue("D", true); // Select the group
        InvokeKeybind("Ctrl+D"); // Duplicate
        SelectCues(["5", "5-1", "5-2", "5-3"]); // Select the duplicated cues
        SetNames(["D1", "E1", "F1", "G1"]); // Give them unique names

        startNames = ["A", "B", "C", "D", "E", "F", "G", "D1", "E1", "F1", "G1", "H", "I", "J"];
        startIds = ["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "5-1", "5-2", "5-3", "6", "7", "8"];
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        // Move out of one group
        SelectCue("4-1");
        SelectCue("4-3", replace: false);
        InvokeKeybind("Ctrl+Up");
        await SelectCue("E", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("3.1");
        await SelectCue("G", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("3.2");

        SelectCue("F", true);
        InvokeKeybind("Ctrl+Down");
        await GetCueTextField("Cue ID").Should().BeEqualTo("4.1");

        // Undo redo
        await CheckUndoRedo(2, startNames, startIds);
        InvokeKeybind("Ctrl+Z");
        InvokeKeybind("Ctrl+Z");

        // Move out of two groups
        SelectCues(["F", "F1"], true);
        InvokeKeybind("Ctrl+Up");
        await SelectCue("F", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("3.1");
        await SelectCue("F1", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("3.2");
        await CheckUndoRedo(1, startNames, startIds);

        // Move into group
        SelectCues(["H", "J"], true);
        InvokeKeybind("Ctrl+Shift+Up");
        await SelectCue("H", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("5-4");
        await SelectCue("J", true).Should().BeTrue();
        await GetCueTextField("Cue ID").Should().BeEqualTo("5-5");
        await CheckUndoRedo(1, startNames, startIds);
    }

    [Test]
    public async Task TestGroupUngroup()
    {
        CreateTestFile();

        string[] startNames = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"];
        string[] startIds = ["1", "2", "3", "4", "4-1", "4-2", "4-3", "5", "6", "7"];
        string[] groupedNames = ["A", "Group (3 cues)", "B", "E", "I", "C", "D", "F", "G", "H", "J"];
        string[] groupedIds = ["1", "2", "2-1", "2-2", "2-3", "3", "4", "4-2", "4-3", "5", "7"];

        // Group some non-contiguous cues
        SelectCue("B", true);
        SelectCue("E", true, false);
        SelectCue("I", true, false);
        InvokeKeybind("Ctrl+G");
        await CheckCueNames(groupedNames).Should().BeTrue();
        await CheckQIDs(groupedIds).Should().BeTrue();

        // Check undo/redo
        InvokeKeybind("Ctrl+Z");
        await CheckCueNames(startNames).Should().BeTrue();
        await CheckQIDs(startIds).Should().BeTrue();

        InvokeKeybind("Ctrl+Shift+Z");
        await CheckCueNames(groupedNames).Should().BeTrue();
        await CheckQIDs(groupedIds).Should().BeTrue();

        // Test ungroup contiguous, this should also delete the empty group at qid 2
        string[] ungroupedNames = ["A", "B", "E", "I", "C", "D", "F", "G", "H", "J"];
        string[] ungroupedIds = ["1", "2", "2.1", "2.2", "3", "4", "4-2", "4-3", "5", "7"];

        SelectCue("B", true);
        SelectCue("E", true, false);
        SelectCue("I", true, false);
        InvokeKeybind("Ctrl+Shift+G");
        await CheckCueNames(ungroupedNames).Should().BeTrue();
        await CheckQIDs(ungroupedIds).Should().BeTrue();

        InvokeKeybind("Ctrl+Z");
        await CheckCueNames(groupedNames).Should().BeTrue();
        await CheckQIDs(groupedIds).Should().BeTrue();

        InvokeKeybind("Ctrl+Shift+Z");
        await CheckCueNames(ungroupedNames).Should().BeTrue();
        await CheckQIDs(ungroupedIds).Should().BeTrue();

        // Test ungroup non-contiguous
        ungroupedNames = ["A", "B", "F", "Group (2 cues)", "E", "I", "C", "D", "G", "H", "J"];
        ungroupedIds = ["1", "1.1", "1.2", "2", "2-2", "3", "4", "4-3", "5", "7"];

        SelectCue("B", true);
        SelectCue("F", true, false);
        InvokeKeybind("Ctrl+Shift+G");
        await CheckCueNames(ungroupedNames).Should().BeTrue();
        await CheckQIDs(ungroupedIds).Should().BeTrue();

        InvokeKeybind("Ctrl+Z");
        await CheckCueNames(groupedNames).Should().BeTrue();
        await CheckQIDs(groupedIds).Should().BeTrue();

        InvokeKeybind("Ctrl+Shift+Z");
        await CheckCueNames(ungroupedNames).Should().BeTrue();
        await CheckQIDs(ungroupedIds).Should().BeTrue();
    }
}
