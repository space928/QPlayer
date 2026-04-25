using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml;

namespace StarlightDocNet;

/*
 dotnet build QPlayer/QPlayer.csproj -p:GenerateDocumentationFile=true --configuration Release -p:DocumentationFile=$SolutionDir/api_docs.xml -p:NoWarn=1591
 */

internal class Program
{
    public static readonly List<APIAssembly> assemblies = [];
    public static APIClass? lastClass;
    private static readonly Lock logListLock = new();

    static void Main(string[] args)
    {
        Assert(args.Length >= 2, "Expected usage: dotnet run StarlightDocNet api_docs.xml path/to/dst/md/");

        string srcPath = args[0];
        string dstPath = args[1];

        using var tr = File.OpenText(srcPath);
        var doc = new XmlDocument();
        Log($"Loading xml...");
        doc.Load(tr);
        Log("Parsing xml...");

        var xml = doc.DocumentElement;
        Assert(xml?.Name == "doc");

        ParseRoot(xml!);
        CreatePages(dstPath);
    }

    public static void Assert(bool condition, string? msg = null, [CallerArgumentExpression(nameof(condition))] string? expr = null, [CallerMemberName] string caller = "")
    {
        if (condition)
            return;

        if (msg != null)
            Log(msg, LogLevel.Error, caller);
        else
            Log($"Assertion failed: {expr}", LogLevel.Error, caller);

#if DEBUG
        Debugger.Break();
#endif
        Environment.Exit(-1);
    }

    public static void Log(object message, LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "")
    {
#if !DEBUG
        if (level <= LogLevel.Debug)
            return;
#endif

        lock (logListLock)
        {
            var time = DateTime.Now;
            string messageString = message?.ToString() ?? "null";
            string msg = $"[{level}] [{time}] [{caller}] {messageString}";
            Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    private static void ParseRoot(XmlNode xml)
    {
        foreach (XmlNode child in xml.ChildNodes)
        {
            switch (child.Name)
            {
                case "assembly":
                    assemblies.Add(APIAssembly.Create(child));
                    break;
                case "members":
                    Assert(assemblies.Count > 0);
                    var asm = assemblies[^1];
                    foreach (XmlNode xmlMember in child.ChildNodes)
                    {
                        if (xmlMember.Name == "#comment")
                            continue;
                        var mem = CreateMember(xmlMember);
                        if (mem != null)
                            asm.members.Add(mem);
                        else
                            Log($"Couldn't create member for node: {xmlMember.Name}", LogLevel.Warning);
                    }
                    break;
                case "#comment":
                    break;
                default:
                    Log($"Unexpected xml node in documetation: {child}", LogLevel.Warning);
                    break;
            }
        }
    }

    private static APIMember? CreateMember(XmlNode xml)
    {
        Assert(xml.Name == "member");
        var memName = xml.Attributes?.GetNamedItem("name")?.Value;
        Assert(memName != null);
        Assert(memName!.Length > 0);
        switch (memName[0])
        {
            case 'T':
                return APIClass.Create(xml, memName);
            case 'M':
                return APIMethod.Create(xml, memName);
            case 'P':
                break;
            case 'F':
                break;
            case 'E':
                break;
            default:
                Assert(false, $"Unrecognised member type {memName}!");
                return null;
        }
        return null;
    }

    private static void CreatePages(string basePath)
    {
        foreach (var asm in assemblies)
            asm.CreatePage(basePath);
    }
}

internal class APIAssembly(string name)
{
    public string name = name;
    public readonly List<APIMember> members = [];

    public static APIAssembly Create(XmlNode xml)
    {
        string name = string.Empty;
        foreach (XmlNode child in xml.ChildNodes)
            if (child.Name == "name")
                name = child.InnerText;

        return new(name);
    }

    public void CreatePage(string basePath)
    {
        basePath = Path.Combine(basePath, name);
        Directory.CreateDirectory(basePath);
        var index = Path.Combine(basePath, "index.mdx");
        var sb = MDStringBuilder.Shared.Clear();
        sb.MetaHeader(name, null, -1);
        File.WriteAllText(index, sb);

        foreach (var child in members)
        {
            child.CreatePage(basePath);
        }
    }
}

internal abstract class APIMember(string name)
{
    public string name = name;
    public string? type;
    public string? summary;
    public string? remarks;

    protected virtual void ParseMemberNode(XmlNode xml)
    {
        switch (xml.Name)
        {
            case "summary":
                summary = xml.InnerText;
                break;
            case "remarks":
                remarks = xml.InnerText;
                break;
            default:
                Program.Log($"Unrecognised member node: {xml.Name}", Program.LogLevel.Warning);
                break;
        }
    }

    public virtual void CreatePage(string basePath)
    {
        var split = name.LastIndexOf('.') + 1;
        var index = Path.Combine(basePath, $"{name[split..]}.mdx");
        var sb = MDStringBuilder.Shared.Clear();
        sb.MetaHeader(name);
        WriteContents(sb);
        File.WriteAllText(index, sb);
    }

    public virtual void WriteContents(MDStringBuilder sb)
    {
        if (summary != null)
        {
            sb.Line(summary);
        }
        if (remarks != null)
        {
            sb.H2("Remarks");
            sb.Line(remarks);
        }
    }
}

internal class APIClass(string name) : APIMember(name)
{
    public readonly List<APIMember> members = [];

    public static APIClass Create(XmlNode xml, string name)
    {
        var node = new APIClass(name);
        foreach (XmlNode child in xml.ChildNodes)
        {
            node.ParseMemberNode(child);
        }

        return node;
    }

    public override void CreatePage(string basePath)
    {
        base.CreatePage(basePath);
        foreach (var child in members)
        {
            child.CreatePage(basePath);
        }
    }

    public override void WriteContents(MDStringBuilder sb)
    {
        base.WriteContents(sb);

        using var table = sb.Table();
        table.HeaderCell("Members");
        foreach (var mem in members)
            table.Cell(mem.name);
    }
}

internal class APIMethod(string name) : APIMember(name)
{
    public readonly List<APIParam> @params = [];
    public readonly List<APIException> exceptions = [];
    public APIReturns? returns;

    public static APIMethod Create(XmlNode xml, string name)
    {
        var node = new APIMethod(name);
        foreach (XmlNode child in xml.ChildNodes)
        {
            node.ParseMemberNode(child);
        }

        return node;
    }

    protected override void ParseMemberNode(XmlNode xml)
    {
        switch (xml.Name)
        {
            case "param":
                @params.Add(APIParam.Create(xml));
                break;
            case "returns":
                returns = new(xml.InnerText);
                break;
            case "exception":
                exceptions.Add(new(xml.InnerText));
                break;
            case "typeparam":
                break;
            case "inheritdoc":
                break;
            default:
                base.ParseMemberNode(xml);
                break;
        }
    }

    public override void WriteContents(MDStringBuilder sb)
    {
        base.WriteContents(sb);

        if (returns != null)
        {
            sb.H2("Returns");
            returns.WriteContents(sb);
        }
        if (exceptions.Count > 0)
        {
            sb.H2("Exceptions");
            using var exTab = sb.Table();
            exTab.HeaderCell("Type");
            exTab.HeaderCell("Summary");
            foreach (var ex in exceptions)
            {
                exTab.Cell(ex.name);
                exTab.Cell(ex.summary ?? string.Empty);
            }
        }
        sb.H2("Parameters");
        using var table = sb.Table();
        table.HeaderCell("Name");
        table.HeaderCell("Description");
        foreach (var par in @params)
        {
            table.Cell(par.name);
            table.Cell(par.summary ?? string.Empty);
        }
    }
}

internal class APIProperty(string name) : APIMember(name)
{

}

internal class APIField(string name) : APIMember(name)
{

}

internal class APIParam(string name) : APIMember(name)
{
    public static APIParam Create(XmlNode xml)
    {
        var memName = xml.Attributes?.GetNamedItem("name")?.Value ?? string.Empty;
        var p = new APIParam(memName);
        p.summary = xml.InnerText;
        return p;
    }
}

internal class APIReturns(string name) : APIMember(name)
{

}

internal class APIException(string name) : APIMember(name)
{

}
