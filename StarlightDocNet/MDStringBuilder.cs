using System;
using System.Collections.Generic;
using System.Text;

namespace StarlightDocNet;

public class MDStringBuilder
{
    protected readonly StringBuilder sb = new();

    public readonly static MDStringBuilder Shared = new();

    public MDStringBuilder Clear()
    {
        sb.Clear();
        return this;
    }

    public override string ToString()
    {
        return sb.ToString();
    }

    public static implicit operator string(MDStringBuilder md) => md.ToString();

    public MDStringBuilder Add(char c)
    {
        sb.Append(c);
        return this;
    }

    public MDStringBuilder Add(string s)
    {
        sb.Append(s);
        return this;
    }

    public MDStringBuilder Add(ref StringBuilder.AppendInterpolatedStringHandler s)
    {
        sb.Append(ref s);
        return this;
    }

    public MDStringBuilder Add(int x)
    {
        sb.Append(x);
        return this;
    }

    public MDStringBuilder Add(float x)
    {
        sb.Append(x);
        return this;
    }

    public MDStringBuilder Line(string s)
    {
        sb.AppendLine(s);
        return this;
    }

    public MDStringBuilder Line(char c)
    {
        sb.Append(c).AppendLine();
        return this;
    }

    public MDStringBuilder Line(int x)
    {
        sb.Append(x).AppendLine();
        return this;
    }

    public MDStringBuilder Line(float x)
    {
        sb.Append(x).AppendLine();
        return this;
    }

    public MDStringBuilder Line(ref StringBuilder.AppendInterpolatedStringHandler s)
    {
        sb.AppendLine(ref s);
        return this;
    }

    public MDStringBuilder H1(string s)
    {
        sb.Append("# ").AppendLine(s);
        return this;
    }

    public MDStringBuilder H2(string s)
    {
        sb.Append("## ").AppendLine(s);
        return this;
    }

    public MDStringBuilder H3(string s)
    {
        sb.Append("### ").AppendLine(s);
        return this;
    }

    public MDStringBuilder H4(string s)
    {
        sb.Append("### ").AppendLine(s);
        return this;
    }

    public MDStringBuilder Code(string s)
    {
        sb.Append('`').Append(s).Append('`');
        return this;
    }

    public MDStringBuilder Italic(string s)
    {
        sb.Append('*').Append(s).Append('*');
        return this;
    }

    public MDStringBuilder Bold(string s)
    {
        sb.Append("**").Append(s).Append("**");
        return this;
    }

    public MDStringBuilder Link(string s)
    {
        sb.Append(s);
        return this;
    }

    public MDStringBuilder Link(string url, string text)
    {
        sb.Append('[').Append(text).Append("](").Append(url).Append(')');
        return this;
    }

    public MDStringBuilder Image(string img, string alt)
    {
        sb.Append("![").Append(alt).Append("](").Append(img).Append(')');
        return this;
    }

    public MDStringBuilder Image(string img, string alt, string url)
    {
        sb.Append("[![").Append(alt).Append("](").Append(img).Append(")](").Append(url).Append(')');
        return this;
    }

    public MDStringBuilder MetaHeader(string title, string? description = null, int? order = null)
    {
        sb.AppendLine("---");
        sb.Append("title: ").AppendLine(title);
        if (description != null)
            sb.Append("description: ").AppendLine(description);
        if (order != null)
            sb.Append("order: ").AppendLine(order.Value.ToString());
        sb.AppendLine("---").AppendLine();
        return this;
    }

    public CodeBlockBuilder CodeBlock(string? language = null) => new(sb, language);

    public TableBuilder Table() => new(this);

    public readonly struct CodeBlockBuilder : IDisposable
    {
        private readonly StringBuilder sb;

        public CodeBlockBuilder(StringBuilder sb, string? language)
        {
            this.sb = sb;
            if (language == null)
                sb.AppendLine("```");
            else
                sb.Append("```").Append(language).AppendLine();
        }

        public readonly void Dispose()
        {
            sb.AppendLine("```");
        }
    }

    public struct TableBuilder : IDisposable
    {
        private readonly MDStringBuilder md;
        private readonly StringBuilder sb;
        private bool firstRow = false;
        private int nCols = 0;
        private int col = 0;

        public TableBuilder(MDStringBuilder md)
        {
            this.md = md;
            this.sb = md.sb;
            firstRow = true;
        }

        public MDStringBuilder HeaderCell(string s)
        {
            Program.Assert(firstRow);
            sb.Append(" | ").Append(s);
            nCols++;
            return md;
        }

        public MDStringBuilder Cell(string s)
        {
            if (firstRow)
            {
                firstRow = false;
                sb.AppendLine(" | ");
                sb.Append(" |");
                for (int i = 0; i < nCols; i++)
                    sb.Append("---|");
                sb.AppendLine();
            }
            sb.Append(" | ").Append(s);
            col++;
            if (col == nCols)
            {
                col = 0;
                sb.AppendLine(" | ");
            }

            return md;
        }

        public void Dispose()
        {
            for (; col < nCols;)
                Cell("  ");
            sb.AppendLine();
        }
    }
}
