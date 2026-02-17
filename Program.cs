using System.Text;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return;
}

if (string.IsNullOrWhiteSpace(options.InputPath))
{
    Console.Error.WriteLine("Missing input path.");
    CliOptions.PrintHelp();
    return;
}

string fullPath;
try
{
    fullPath = Path.GetFullPath(options.InputPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Invalid path: {ex.Message}");
    return;
}

if (File.Exists(fullPath))
{
    ProcessFile(fullPath, options, new RunStats());
    return;
}

if (!Directory.Exists(fullPath))
{
    Console.Error.WriteLine($"Path does not exist: {fullPath}");
    return;
}

var stats = new RunStats();
foreach (var filePath in EnumerateCandidateFiles(fullPath, options.Recursive))
{
    ProcessFile(filePath, options, stats);
}

Console.WriteLine();
Console.WriteLine("Done.");
Console.WriteLine($"Files scanned : {stats.Scanned}");
Console.WriteLine($"Files changed : {stats.Modified}");
Console.WriteLine($"Files skipped : {stats.Skipped}");
Console.WriteLine($"Errors        : {stats.Errors}");

static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool recursive)
{
    var pending = new Stack<string>();
    pending.Push(rootPath);
    var ignoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build"
    };

    while (pending.Count > 0)
    {
        var current = pending.Pop();

        IEnumerable<string> subDirs;
        try
        {
            subDirs = Directory.EnumerateDirectories(current);
        }
        catch
        {
            continue;
        }

        if (recursive)
        {
            foreach (var dir in subDirs)
            {
                var name = Path.GetFileName(dir);
                if (!ignoredDirectories.Contains(name))
                {
                    pending.Push(dir);
                }
            }
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(current);
        }
        catch
        {
            continue;
        }

        foreach (var file in files)
        {
            yield return file;
        }
    }
}

static void ProcessFile(string filePath, CliOptions options, RunStats stats)
{
    stats.Scanned++;

    if (!LooksLikeTextFile(filePath))
    {
        stats.Skipped++;
        return;
    }

    string original;
    try
    {
        original = File.ReadAllText(filePath);
    }
    catch
    {
        stats.Skipped++;
        return;
    }

    var cleaned = CommentStripper.RemoveComments(original);
    if (cleaned == original)
    {
        return;
    }

    if (options.DryRun)
    {
        stats.Modified++;
        Console.WriteLine($"[dry-run] {filePath}");
        return;
    }

    try
    {
        if (options.Backup)
        {
            File.Copy(filePath, $"{filePath}.bak", overwrite: true);
        }

        File.WriteAllText(filePath, cleaned);
        stats.Modified++;
        Console.WriteLine($"[updated] {filePath}");
    }
    catch (Exception ex)
    {
        stats.Errors++;
        Console.Error.WriteLine($"[error] {filePath}: {ex.Message}");
    }
}

static bool LooksLikeTextFile(string filePath)
{
    const int maxProbe = 4096;
    byte[] buffer;
    int bytesRead;

    try
    {
        using var stream = File.OpenRead(filePath);
        buffer = new byte[maxProbe];
        bytesRead = stream.Read(buffer, 0, buffer.Length);
    }
    catch
    {
        return false;
    }

    if (bytesRead == 0)
    {
        return true;
    }

    int controlBytes = 0;
    for (int i = 0; i < bytesRead; i++)
    {
        var b = buffer[i];
        if (b == 0)
        {
            return false;
        }

        var isAllowedControl = b == 9 || b == 10 || b == 13;
        var isControl = b < 32 && !isAllowedControl;
        if (isControl)
        {
            controlBytes++;
        }
    }

    return ((double)controlBytes / bytesRead) < 0.15;
}

file sealed class CliOptions
{
    public string? InputPath { get; private init; }
    public bool Recursive { get; private init; } = true;
    public bool Backup { get; private init; }
    public bool DryRun { get; private init; }
    public bool ShowHelp { get; private init; }

    public static CliOptions Parse(string[] args)
    {
        string? inputPath = null;
        bool recursive = true;
        bool backup = false;
        bool dryRun = false;
        bool showHelp = false;

        foreach (var raw in args)
        {
            var arg = raw.Trim();
            if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--no-recursive", StringComparison.OrdinalIgnoreCase))
            {
                recursive = false;
                continue;
            }

            if (arg.Equals("--backup", StringComparison.OrdinalIgnoreCase))
            {
                backup = true;
                continue;
            }

            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (inputPath is null)
            {
                inputPath = arg;
            }
        }

        return new CliOptions
        {
            InputPath = inputPath,
            Recursive = recursive,
            Backup = backup,
            DryRun = dryRun,
            ShowHelp = showHelp
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("CommentCleaner");
        Console.WriteLine("Remove comments from a single file or an entire folder.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  CommentCleaner <path> [--dry-run] [--backup] [--no-recursive]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  CommentCleaner \"C:\\code\\script.lua\"");
        Console.WriteLine("  CommentCleaner \"C:\\code\\project\" --backup");
        Console.WriteLine("  CommentCleaner \"C:\\code\\project\" --dry-run");
    }
}

file sealed class RunStats
{
    public int Scanned { get; set; }
    public int Modified { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
}

file static class CommentStripper
{
    private enum State
    {
        Normal,
        SingleQuoteString,
        DoubleQuoteString,
        BacktickString,
        VerbatimString,
        LineComment,
        CStyleBlockComment,
        HtmlBlockComment,
        LuaBlockComment,
        PascalBlockComment
    }

    public static string RemoveComments(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var output = new StringBuilder(input.Length);
        var state = State.Normal;
        var lineStart = true;
        var commentStartedOnEmptyLine = false;
        var suppressNextNewline = false;
        int i = 0;

        while (i < input.Length)
        {
            char c = input[i];
            char next = i + 1 < input.Length ? input[i + 1] : '\0';
            char next2 = i + 2 < input.Length ? input[i + 2] : '\0';
            char next3 = i + 3 < input.Length ? input[i + 3] : '\0';

            switch (state)
            {
                case State.Normal:
                    if (c == '\r' || c == '\n')
                    {
                        if (suppressNextNewline)
                        {
                            if (c == '\r' && next == '\n')
                            {
                                i += 2;
                            }
                            else
                            {
                                i++;
                            }

                            suppressNextNewline = false;
                            lineStart = true;
                            continue;
                        }

                        output.Append(c);
                        i++;
                        lineStart = true;
                        continue;
                    }

                    if (c == '@' && next == '"')
                    {
                        output.Append(c);
                        output.Append(next);
                        i += 2;
                        lineStart = false;
                        state = State.VerbatimString;
                        continue;
                    }

                    if (c == '"')
                    {
                        output.Append(c);
                        i++;
                        lineStart = false;
                        state = State.DoubleQuoteString;
                        continue;
                    }

                    if (c == '\'')
                    {
                        output.Append(c);
                        i++;
                        lineStart = false;
                        state = State.SingleQuoteString;
                        continue;
                    }

                    if (c == '`')
                    {
                        output.Append(c);
                        i++;
                        lineStart = false;
                        state = State.BacktickString;
                        continue;
                    }

                    if (c == '/' && next == '/')
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        i += 2;
                        state = State.LineComment;
                        continue;
                    }

                    if (c == '/' && next == '*')
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        i += 2;
                        state = State.CStyleBlockComment;
                        continue;
                    }

                    if (c == '<' && next == '!' && next2 == '-' && next3 == '-')
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        i += 4;
                        state = State.HtmlBlockComment;
                        continue;
                    }

                    if (c == '-' && next == '-' && IsLikelyLineCommentStart(input, i, lineStart))
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        if (next2 == '[' && next3 == '[')
                        {
                            i += 4;
                            state = State.LuaBlockComment;
                            continue;
                        }

                        i += 2;
                        state = State.LineComment;
                        continue;
                    }

                    if ((c == '#' || c == ';') && lineStart)
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        i += 1;
                        state = State.LineComment;
                        continue;
                    }

                    if (c == '(' && next == '*')
                    {
                        TrimLineEndingWhitespace(output);
                        commentStartedOnEmptyLine = lineStart;
                        i += 2;
                        state = State.PascalBlockComment;
                        continue;
                    }

                    output.Append(c);
                    i++;
                    if (!char.IsWhiteSpace(c))
                    {
                        lineStart = false;
                    }

                    continue;

                case State.LineComment:
                    if (c == '\r' || c == '\n')
                    {
                        if (!commentStartedOnEmptyLine)
                        {
                            if (c == '\r' && next == '\n')
                            {
                                output.Append("\r\n");
                                i += 2;
                            }
                            else
                            {
                                output.Append(c);
                                i++;
                            }
                        }
                        else
                        {
                            if (c == '\r' && next == '\n')
                            {
                                i += 2;
                            }
                            else
                            {
                                i++;
                            }
                        }

                        lineStart = true;
                        state = State.Normal;
                        continue;
                    }

                    i++;
                    continue;

                case State.CStyleBlockComment:
                    if (c == '\r' || c == '\n')
                    {
                        if (!commentStartedOnEmptyLine)
                        {
                            output.Append(c);
                        }
                    }

                    if (c == '*' && next == '/')
                    {
                        i += 2;
                        if (commentStartedOnEmptyLine)
                        {
                            suppressNextNewline = true;
                            lineStart = true;
                        }

                        state = State.Normal;
                        continue;
                    }

                    i++;
                    continue;

                case State.HtmlBlockComment:
                    if (c == '\r' || c == '\n')
                    {
                        if (!commentStartedOnEmptyLine)
                        {
                            output.Append(c);
                        }
                    }

                    if (c == '-' && next == '-' && next2 == '>')
                    {
                        i += 3;
                        if (commentStartedOnEmptyLine)
                        {
                            suppressNextNewline = true;
                            lineStart = true;
                        }

                        state = State.Normal;
                        continue;
                    }

                    i++;
                    continue;

                case State.LuaBlockComment:
                    if (c == '\r' || c == '\n')
                    {
                        if (!commentStartedOnEmptyLine)
                        {
                            output.Append(c);
                        }
                    }

                    if (c == ']' && next == ']')
                    {
                        i += 2;
                        if (commentStartedOnEmptyLine)
                        {
                            suppressNextNewline = true;
                            lineStart = true;
                        }

                        state = State.Normal;
                        continue;
                    }

                    i++;
                    continue;

                case State.PascalBlockComment:
                    if (c == '\r' || c == '\n')
                    {
                        if (!commentStartedOnEmptyLine)
                        {
                            output.Append(c);
                        }
                    }

                    if (c == '*' && next == ')')
                    {
                        i += 2;
                        if (commentStartedOnEmptyLine)
                        {
                            suppressNextNewline = true;
                            lineStart = true;
                        }

                        state = State.Normal;
                        continue;
                    }

                    i++;
                    continue;

                case State.SingleQuoteString:
                    output.Append(c);
                    if (c == '\\' && next != '\0')
                    {
                        output.Append(next);
                        i += 2;
                        continue;
                    }

                    if (c == '\'')
                    {
                        state = State.Normal;
                    }

                    i++;
                    continue;

                case State.DoubleQuoteString:
                    output.Append(c);
                    if (c == '\\' && next != '\0')
                    {
                        output.Append(next);
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        state = State.Normal;
                    }

                    i++;
                    continue;

                case State.BacktickString:
                    output.Append(c);
                    if (c == '\\' && next != '\0')
                    {
                        output.Append(next);
                        i += 2;
                        continue;
                    }

                    if (c == '`')
                    {
                        state = State.Normal;
                    }

                    i++;
                    continue;

                case State.VerbatimString:
                    output.Append(c);
                    if (c == '"' && next == '"')
                    {
                        output.Append(next);
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        state = State.Normal;
                    }

                    i++;
                    continue;
            }
        }

        return output.ToString();
    }

    private static bool IsLikelyLineCommentStart(string input, int index, bool lineStart)
    {
        if (lineStart)
        {
            return true;
        }

        if (index <= 0)
        {
            return true;
        }

        var previous = input[index - 1];
        return char.IsWhiteSpace(previous);
    }

    private static void TrimLineEndingWhitespace(StringBuilder output)
    {
        int i = output.Length - 1;
        while (i >= 0 && (output[i] == ' ' || output[i] == '\t'))
        {
            i--;
        }

        output.Length = i + 1;
    }
}
