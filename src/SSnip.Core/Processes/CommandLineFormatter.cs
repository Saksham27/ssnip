using System.Text;

namespace SSnip.Core.Processes;

/// <summary>
/// Formats an argument list as a Windows command line for display and copying. Quoting follows
/// the CommandLineToArgvW rules, so a pasted command splits back into the same arguments.
/// </summary>
public static class CommandLineFormatter
{
    public static string Format(string executable, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        AppendQuoted(builder, executable);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendQuoted(builder, argument);
        }

        return builder.ToString();
    }

    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var builder = new StringBuilder();
        AppendQuoted(builder, argument);
        return builder.ToString();
    }

    private static void AppendQuoted(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Backslashes before a quote are escaped, then the quote itself.
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            backslashes = 0;
            builder.Append(c);
        }

        // Trailing backslashes are doubled so they do not escape the closing quote.
        builder.Append('\\', backslashes * 2);
        builder.Append('"');
    }
}
