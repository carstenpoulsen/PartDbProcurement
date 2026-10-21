using System.Text;

namespace PartDbProcurement.Core;

/// <summary>A node of a KiCad S-expression: (name item item ...). Items are strings or nested nodes.</summary>
public sealed class SNode
{
    public string Name { get; }
    public List<object> Items { get; } = new();

    public SNode(string name) => Name = name;

    public IEnumerable<SNode> Children(string name) =>
        Items.OfType<SNode>().Where(n => n.Name == name);

    public SNode? Child(string name) => Children(name).FirstOrDefault();

    /// <summary>First plain (non-node) value, e.g. (uuid "abc") -> "abc".</summary>
    public string? Value => Items.OfType<string>().FirstOrDefault();

    /// <summary>Value of a child node, e.g. node.ChildValue("in_bom") -> "yes".</summary>
    public string? ChildValue(string name) => Child(name)?.Value;

    /// <summary>True when a flag child such as (dnp yes) is set. Missing flags return the default.</summary>
    public bool Flag(string name, bool defaultValue)
    {
        var v = ChildValue(name);
        if (v == null) return defaultValue;
        return v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Minimal, fast parser for KiCad's S-expression file format (.kicad_sch).</summary>
public static class SExprParser
{
    public static SNode ParseFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static SNode Parse(string text)
    {
        int pos = 0;
        SkipWhitespace(text, ref pos);
        if (pos >= text.Length || text[pos] != '(')
            throw new FormatException("Not a KiCad S-expression file.");
        return ParseNode(text, ref pos);
    }

    private static SNode ParseNode(string t, ref int pos)
    {
        // At '('
        pos++;
        SkipWhitespace(t, ref pos);
        var name = ReadAtom(t, ref pos);
        var node = new SNode(name);
        while (true)
        {
            SkipWhitespace(t, ref pos);
            if (pos >= t.Length) throw new FormatException("Unexpected end of file (missing ')').");
            char c = t[pos];
            if (c == ')') { pos++; return node; }
            if (c == '(') node.Items.Add(ParseNode(t, ref pos));
            else if (c == '"') node.Items.Add(ReadString(t, ref pos));
            else node.Items.Add(ReadAtom(t, ref pos));
        }
    }

    private static void SkipWhitespace(string t, ref int pos)
    {
        while (pos < t.Length && char.IsWhiteSpace(t[pos])) pos++;
    }

    private static string ReadAtom(string t, ref int pos)
    {
        int start = pos;
        while (pos < t.Length && !char.IsWhiteSpace(t[pos]) && t[pos] != '(' && t[pos] != ')' && t[pos] != '"') pos++;
        return t.Substring(start, pos - start);
    }

    private static string ReadString(string t, ref int pos)
    {
        // At '"'
        pos++;
        var sb = new StringBuilder();
        while (pos < t.Length)
        {
            char c = t[pos++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && pos < t.Length)
            {
                char e = t[pos++];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
            }
            else sb.Append(c);
        }
        throw new FormatException("Unterminated string.");
    }
}
