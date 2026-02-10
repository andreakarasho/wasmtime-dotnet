using System.Text.RegularExpressions;

namespace Wasmtime.SourceGenerator;

public class StringUtils
{
    private static readonly Regex SeparatorRegex = new(@"[-.\[\]](.)?", RegexOptions.Compiled);

    /// <summary>
    /// Change the name to a valid C# name.
    /// </summary>
    /// <example>
    /// 'foo-bar' becomes 'FooBar'
    /// '[constructor]system' becomes 'CtorSystem'
    /// '[method]system.add-commands' becomes 'SystemAddCommands'
    /// '[resource-drop]system' becomes 'DropSystem'
    /// </example>
    /// <param name="name">WIT name</param>
    /// <param name="uppercaseFirst">If <c>true</c>, the first character will be uppercased.</param>
    /// <returns>C# name</returns>
    public static string GetName(string name, bool uppercaseFirst = true)
    {
        // Replace ABI prefixes before regex processing
        if (name.StartsWith("[constructor]"))
        {
            name = "ctor-" + name.Substring("[constructor]".Length);
        }
        else if (name.StartsWith("[method]"))
        {
            name = name.Substring("[method]".Length);
        }
        else if (name.StartsWith("[resource-drop]"))
        {
            name = "drop-" + name.Substring("[resource-drop]".Length);
        }

        var result = SeparatorRegex.Replace(name, static m =>
        {
            var g = m.Groups[1];
            return g.Success ? g.Value.ToUpperInvariant() : string.Empty;
        });

        if (string.IsNullOrEmpty(result))
        {
            return "_";
        }

        if (!uppercaseFirst)
        {
            return result;
        }

        return char.ToUpperInvariant(result[0]) + result.Substring(1);
    }
}
