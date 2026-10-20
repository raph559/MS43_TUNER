using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>
/// The text a person can read in the sources: the string literals of C# files (comments skipped, escapes decoded, the
/// literal parts of interpolated strings, and the strings inside their holes) and the attribute values and text of
/// XAML files (comments skipped).
/// </summary>
internal static class SourceText
{
    public static string RepoRoot { get; } = FindRoot();

    /// <summary>The project's source files with these extensions, without bin and obj.</summary>
    public static IEnumerable<string> Files(string folder, params string[] extensions) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, folder), "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(path => path, StringComparer.Ordinal);

    public static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    public static IReadOnlyList<string> XamlTexts(string path)
    {
        var texts = new List<string>();
        foreach (var element in XDocument.Load(path).Descendants())
        {
            texts.AddRange(element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Select(attribute => attribute.Value));
            texts.AddRange(element.Nodes().OfType<XText>().Select(text => text.Value).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return texts;
    }

    public static IReadOnlyList<string> CSharpLiterals(string source)
    {
        var literals = new List<string>();
        var i = 0;
        ReadCode(source, ref i, literals, inHole: false);
        return literals;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MS43Logger.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("MS43Logger.sln not found above the test assembly.");
    }

    private static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    // Code up to the end of the file, or, inside an interpolation hole, up to the brace that closes it.
    private static void ReadCode(string s, ref int i, List<string> literals, bool inHole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && At(s, i + 1) == '/')
            {
                var end = s.IndexOf('\n', i);
                i = end < 0 ? s.Length : end;
            }
            else if (c == '/' && At(s, i + 1) == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
            }
            else if (c == '\'')
            {
                SkipChar(s, ref i);
            }
            else if (StringStarts(s, i))
            {
                ReadString(s, ref i, literals);
            }
            else
            {
                if (inHole && c == '{')
                {
                    depth++;
                }
                else if (inHole && c == '}')
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                }

                i++;
            }
        }
    }

    // A string starts at a quote, or at $ and @ prefixes followed by one; @ before a letter is a verbatim identifier.
    private static bool StringStarts(string s, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_'))
        {
            return false;
        }

        var j = i;
        while (j < s.Length && (s[j] == '$' || s[j] == '@'))
        {
            j++;
        }

        return At(s, j) == '"';
    }

    private static void SkipChar(string s, ref int i)
    {
        i++;
        while (i < s.Length && s[i] != '\'')
        {
            i += s[i] == '\\' ? 2 : 1;
        }

        i++;
    }

    private static void ReadString(string s, ref int i, List<string> literals)
    {
        var interpolated = false;
        var verbatim = false;
        while (s[i] != '"')
        {
            interpolated |= s[i] == '$';
            verbatim |= s[i] == '@';
            i++;
        }

        var quotes = 0;
        while (At(s, i + quotes) == '"')
        {
            quotes++;
        }

        var text = new StringBuilder();
        if (quotes >= 3)
        {
            // A raw string: everything up to the same run of quotes.
            var close = new string('"', quotes);
            var end = s.IndexOf(close, i + quotes, StringComparison.Ordinal);
            end = end < 0 ? s.Length : end;
            literals.Add(s[(i + quotes)..end]);
            i = Math.Min(s.Length, end + quotes);
            return;
        }

        i++;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                if (verbatim && At(s, i + 1) == '"')
                {
                    text.Append('"');
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            if (interpolated && c == '{')
            {
                if (At(s, i + 1) == '{')
                {
                    text.Append('{');
                    i += 2;
                    continue;
                }

                i++;
                ReadCode(s, ref i, literals, inHole: true);
                i++;
                continue;
            }

            if (interpolated && c == '}' && At(s, i + 1) == '}')
            {
                text.Append('}');
                i += 2;
                continue;
            }

            if (!verbatim && c == '\\')
            {
                i = Escape(s, i, text);
                continue;
            }

            text.Append(c);
            i++;
        }

        literals.Add(text.ToString());
    }

    // Decodes one escape at s[i] == '\\' and returns the index after it.
    private static int Escape(string s, int i, StringBuilder text)
    {
        var kind = At(s, i + 1);
        switch (kind)
        {
            case 'u':
                text.Append((char)int.Parse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return i + 6;
            case 'U':
                text.Append(char.ConvertFromUtf32(int.Parse(s.AsSpan(i + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                return i + 10;
            case 'x':
                var digits = 0;
                while (digits < 4 && Uri.IsHexDigit(At(s, i + 2 + digits)))
                {
                    digits++;
                }

                text.Append((char)int.Parse(s.AsSpan(i + 2, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return i + 2 + digits;
            case 'n':
                text.Append('\n');
                return i + 2;
            case 't':
                text.Append('\t');
                return i + 2;
            case 'r':
                text.Append('\r');
                return i + 2;
            case '0':
                text.Append('\0');
                return i + 2;
            default:
                text.Append(kind);
                return i + 2;
        }
    }
}
