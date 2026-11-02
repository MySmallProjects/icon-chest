using System.Data.Common;
using System.Text;
using System.Text.Json;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Images;
using IconChest.Core.Logging;
using IconChest.Core.Query;
using IconChest.Core.Settings;

namespace IconChest.Cli;

public static class QueryCliCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        return subcommand switch
        {
            "validate" => HandleValidate(subArgs),
            "run" => HandleRun(subArgs),
            "explain" => HandleExplain(subArgs),
            "docs" => HandleDocs(subArgs),
            _ => UnknownSubcommand(subcommand)
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: iconchest query <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate \"<text>\" [--sql]                   Validate query syntax and semantics");
        Console.WriteLine("  run \"<text>\" [--sort field] [--dir asc|desc] [--limit n] Run search and display results table");
        Console.WriteLine("  explain \"<text>\"                            Show AST, generated SQL, parameters, and query plan");
        Console.WriteLine("  docs [--write]                              Print or rewrite docs/QUERY-LANGUAGE.md");
    }

    private static int UnknownSubcommand(string subcommand)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown query command: '{subcommand}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    private static int HandleValidate(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Query text argument is required.");
            return 1;
        }

        string text = args[0];
        bool showSql = args.Any(a => a.Equals("--sql", StringComparison.OrdinalIgnoreCase));

        var result = QueryEngine.ParseAndCompile(text);
        if (!result.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("INVALID QUERY");
            Console.ResetColor();

            foreach (var err in result.Errors)
            {
                PrintCaretError(text, err);
            }
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OK");
        Console.ResetColor();
        Console.WriteLine($"Normalised: {result.NormalizedText}");

        if (showSql && result.Query != null)
        {
            Console.WriteLine();
            Console.WriteLine($"SQL: {result.Query.WhereSql}");
            if (result.Query.Parameters.Count > 0)
            {
                Console.WriteLine("Parameters:");
                foreach (var (k, v) in result.Query.Parameters)
                {
                    Console.WriteLine($"  @{k} = {v ?? "NULL"}");
                }
            }
        }

        return 0;
    }

    private static void PrintCaretError(string text, QueryError err)
    {
        Console.WriteLine(text);
        int pos = Math.Clamp(err.Position, 0, text.Length);
        var caretLine = new string(' ', pos) + "^";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(caretLine);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($" {err.Code}: {err.Message}");
        if (!string.IsNullOrWhiteSpace(err.Suggestion))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($" (suggestion: {err.Suggestion})");
        }
        Console.ResetColor();
        Console.WriteLine();
    }

    private static int HandleRun(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Query text argument is required.");
            return 1;
        }

        string text = args[0];
        string sort = "filename";
        string dir = "asc";
        int limit = 50;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--sort", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                sort = args[++i];
            }
            else if (args[i].Equals("--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dir = args[++i];
            }
            else if (args[i].Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsedLimit)) limit = parsedLimit;
            }
        }

        var compResult = QueryEngine.ParseAndCompile(text);
        if (!compResult.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("INVALID QUERY");
            Console.ResetColor();
            foreach (var err in compResult.Errors)
            {
                PrintCaretError(text, err);
            }
            return 1;
        }

        var paths = new AppPaths();
        var settingsService = new SettingsService(paths);
        var db = new IconChestDatabase(settingsService);
        db.Migrate();
        var connectionFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var searchService = new ImageSearchService(connectionFactory);

        var request = new ImageSearchRequest
        {
            Sort = sort,
            Dir = dir,
            Limit = limit,
            CustomWhereClause = compResult.Query!.WhereSql,
            CustomWhereParameters = compResult.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
            Hidden = InclusionState.Include,
            Missing = InclusionState.Include,
            Duplicate = InclusionState.Include
        };

        var searchResult = searchService.Search(request);

        Console.WriteLine($"Found {searchResult.Total} matching image(s) (displaying {searchResult.Items.Count}):");
        Console.WriteLine();

        Console.WriteLine($"{"ID",-6} | {"File Name",-25} | {"Format",-6} | {"Width",-6} | {"Height",-6} | {"Size",-9} | {"Rating",-6} | {"Fav",-5}");
        Console.WriteLine(new string('-', 85));

        foreach (var item in searchResult.Items)
        {
            Console.WriteLine($"{item.Id,-6} | {Truncate(item.FileName, 25),-25} | {item.Format,-6} | {item.Width,-6} | {item.Height,-6} | {FormatBytes(item.SizeBytes),-9} | {item.Rating,-6} | {(item.IsFavourite ? "yes" : "no"),-5}");
        }

        return 0;
    }

    private static int HandleExplain(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: Query text argument is required.");
            return 1;
        }

        string text = args[0];
        var (ast, syntaxError) = QueryParser.Parse(text);
        if (syntaxError != null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SYNTAX ERROR");
            Console.ResetColor();
            PrintCaretError(text, syntaxError);
            return 1;
        }

        var validation = QueryValidator.Validate(ast);
        if (!validation.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SEMANTIC ERROR");
            Console.ResetColor();
            foreach (var err in validation.Errors)
            {
                PrintCaretError(text, err);
            }
            return 1;
        }

        var compiled = QueryCompiler.Compile(validation);

        Console.WriteLine("=== AST ===");
        Console.WriteLine(JsonSerializer.Serialize(ast, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();

        Console.WriteLine("=== SQL ===");
        Console.WriteLine(compiled.WhereSql);
        Console.WriteLine();

        Console.WriteLine("=== Parameters ===");
        foreach (var (k, v) in compiled.Parameters)
        {
            Console.WriteLine($"  @{k} = {v ?? "NULL"}");
        }
        Console.WriteLine();

        var paths = new AppPaths();
        var settingsService = new SettingsService(paths);
        var db = new IconChestDatabase(settingsService);
        db.Migrate();
        var connectionFactory = new DatabaseConnectionFactory(db.DatabasePath);
        using var conn = connectionFactory.CreateAndOpenConnection();

        try
        {
            string explainSql = $"EXPLAIN QUERY PLAN SELECT id FROM image WHERE {compiled.WhereSql};";
            var planRows = conn.Query<(int Id, int Parent, int NotUsed, string Detail)>(explainSql, compiled.Parameters).ToList();

            Console.WriteLine("=== EXPLAIN QUERY PLAN ===");
            foreach (var row in planRows)
            {
                Console.WriteLine($"{row.Id,-3} | {row.Parent,-3} | {row.Detail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EXPLAIN QUERY PLAN failed: {ex.Message}");
        }

        return 0;
    }

    private static int HandleDocs(string[] args)
    {
        bool write = args.Any(a => a.Equals("--write", StringComparison.OrdinalIgnoreCase));
        string markdown = QueryHelpGenerator.GenerateMarkdownDocumentation();

        if (write)
        {
            string docsPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "QUERY-LANGUAGE.md");
            Directory.CreateDirectory(Path.GetDirectoryName(docsPath)!);
            File.WriteAllText(docsPath, markdown, Encoding.UTF8);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Successfully generated and wrote {docsPath}");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(markdown);
        }

        return 0;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
