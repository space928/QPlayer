using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.SourceGenerator;

public class IndentedStringBuilder
{
    private char[] chars = [];
    private int pos;
    private int indent;
    private int indentSize = 4;

    /// <summary>
    /// Gets or sets the current indentation level.
    /// </summary>
    public int IndentLevel { get => indent; set => indent = value; }
    /// <summary>
    /// Gets or sets the number of spaces per indentation level. (default: 4)
    /// </summary>
    public int IndentSize { get => indentSize; set => indentSize = value; }

    /// <summary>
    /// Clears the string buffer.
    /// </summary>
    public void Clear()
    {
        pos = 0;
    }

    /// <summary>
    /// Creates a new <see cref="IndentedCurlyBracket"/> instance which appends an opening curly brace and 
    /// increases the indent when created, and appends a closing brace and unindents when disposed.
    /// </summary>
    /// <example>
    /// using (sb.EnterCurlyBrace()
    /// {
    ///     sb.AppendLine("HelloWorld();");
    /// }
    /// 
    /// // Results in:
    /// // {
    /// //     HelloWorld();
    /// // }
    /// </example>
    /// <returns></returns>
    public IndentedCurlyBracket EnterCurlyBracket()
    {
        return new IndentedCurlyBracket(this);
    }

    /// <summary>
    /// Increases the indentation level by 1.
    /// </summary>
    /// <returns></returns>
    public IndentedStringBuilder Indent()
    {
        indent += indentSize;
        return this;
    }

    /// <summary>
    /// Decreases the indentation level by 1.
    /// </summary>
    /// <returns></returns>
    public IndentedStringBuilder UnIndent()
    {
        indent = Math.Max(0, indent - indentSize);
        return this;
    }

    /// <summary>
    /// Appends an empty line to the string buffer.
    /// </summary>
    /// <returns></returns>
    public IndentedStringBuilder AppendLine()
    {
        //AppendIndent();
        EnsureSpace(1);
        chars[pos++] = '\n';
        return this;
    }

    /// <summary>
    /// Appends the specified string to the buffer, adding a new line character to the end of it.
    /// </summary>
    /// <param name="text">The string to append to the buffer.</param>
    /// <returns></returns>
    public IndentedStringBuilder AppendLine(string text)
    {
        AppendIndent();
        EnsureSpace(text.Length + 1);
        text.CopyTo(0, chars, pos, text.Length);
        pos += text.Length;
        chars[pos++] = '\n';
        return this;
    }

    public IndentedStringBuilder Append(string text)
    {
        EnsureSpace(text.Length);
        text.CopyTo(0, chars, pos, text.Length);
        pos += text.Length;
        return this;
    }

    public IndentedStringBuilder Append(char c)
    {
        EnsureSpace(1);
        chars[pos++] = c;
        return this;
    }

    /// <summary>
    /// Appends spaces equalling the current indentation level to the buffer.
    /// </summary>
    /// <returns></returns>
    public IndentedStringBuilder AppendIndent()
    {
        EnsureSpace(indent);
        for (int i = 0; i < indent; i++)
            chars[pos++] = ' ';
        return this;
    }

    private void EnsureSpace(int extraSpace)
    {
        int capacity = extraSpace + pos;
        if (chars.Length < capacity)
        {
            char[] nChars = new char[NextPowerOf2((uint)capacity)];
            chars.CopyTo(nChars, 0);
            chars = nChars;
        }
    }

    private static uint NextPowerOf2(uint n)
    {
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        n++;
        return n;
    }

    public override string ToString()
    {
        return new(chars, 0, pos);
    }

    public readonly struct IndentedCurlyBracket : IDisposable
    {
        private readonly IndentedStringBuilder sb;

        internal IndentedCurlyBracket(IndentedStringBuilder sb)
        {
            this.sb = sb;
            sb.AppendLine("{").Indent();
        }

        public readonly void Dispose()
        {
            sb.UnIndent().AppendLine("}");
        }
    }
}
