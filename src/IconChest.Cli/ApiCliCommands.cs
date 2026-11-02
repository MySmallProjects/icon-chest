using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IconChest.Core.Common;
using IconChest.Core.Settings;

namespace IconChest.Cli;

public static class ApiCliCommands
{
    private class CliSession
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:45300";
        public string Cookie { get; set; } = string.Empty;
    }

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var sub = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        return sub switch
        {
            "login" => await HandleLoginAsync(subArgs),
            "get" => await HandleRequestAsync(HttpMethod.Get, subArgs),
            "post" => await HandleRequestAsync(HttpMethod.Post, subArgs),
            "patch" => await HandleRequestAsync(HttpMethod.Patch, subArgs),
            "put" => await HandleRequestAsync(HttpMethod.Put, subArgs),
            "delete" => await HandleRequestAsync(HttpMethod.Delete, subArgs),
            "events" => await HandleEventsAsync(subArgs),
            _ => UnknownSubcommand(sub)
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("IconChest Dev CLI - API commands");
        Console.WriteLine();
        Console.WriteLine("Usage: iconchest api <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  login [--url <url>] [--secret <secret>] [--password <password>]");
        Console.WriteLine("    Log in to API, saving session cookie with 0600 file permissions.");
        Console.WriteLine("  get <path> [--url <url>]");
        Console.WriteLine("    Send GET request to API (e.g. iconchest api get /api/system/info)");
        Console.WriteLine("  post <path> [--body <json|@file>] [--url <url>]");
        Console.WriteLine("    Send POST request to API");
        Console.WriteLine("  patch <path> [--body <json|@file>] [--url <url>]");
        Console.WriteLine("    Send PATCH request to API");
        Console.WriteLine("  put <path> [--body <json|@file>] [--url <url>]");
        Console.WriteLine("    Send PUT request to API");
        Console.WriteLine("  delete <path> [--url <url>]");
        Console.WriteLine("    Send DELETE request to API");
        Console.WriteLine("  events [--url <url>]");
        Console.WriteLine("    Connect to SSE event stream and print live events");
    }

    private static int UnknownSubcommand(string sub)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown api subcommand: '{sub}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    private static string GetSessionFilePath()
    {
        var tempDir = Path.GetTempPath();
        var user = Environment.UserName;
        var sanitizedUser = string.Join("_", user.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(tempDir, $".iconchest_session_{sanitizedUser}.json");
    }

    private static async Task<CliSession?> LoadSessionAsync()
    {
        var path = GetSessionFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<CliSession>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveSessionAsync(CliSession session)
    {
        var path = GetSessionFilePath();
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string GetDefaultUrl()
    {
        try
        {
            var paths = new AppPaths();
            var settingsService = new SettingsService(paths);
            var port = settingsService.Current.Api.PortPreference;
            if (port > 0) return $"http://127.0.0.1:{port}";
        }
        catch
        {
            // fallback
        }
        return "http://127.0.0.1:45300";
    }

    private static async Task<int> HandleLoginAsync(string[] args)
    {
        string? url = null;
        string? secret = null;
        string? password = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--url" && i + 1 < args.Length)
            {
                url = args[++i];
            }
            else if (args[i] == "--secret" && i + 1 < args.Length)
            {
                secret = args[++i];
            }
            else if (args[i] == "--password" && i + 1 < args.Length)
            {
                password = args[++i];
            }
        }

        url ??= GetDefaultUrl();
        url = url.TrimEnd('/');

        if (string.IsNullOrEmpty(secret) && string.IsNullOrEmpty(password))
        {
            secret = Environment.GetEnvironmentVariable("ICONCHEST_API_SECRET");
            if (string.IsNullOrEmpty(secret))
            {
                secret = "dev-session-secret-change-in-production";
            }
        }

        using var client = new HttpClient();
        object payload = !string.IsNullOrEmpty(secret)
            ? new { secret }
            : new { password };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync($"{url}/api/session", content);
        if (!response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Login failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                Console.Error.WriteLine(body);
            }
            Console.ResetColor();
            return 1;
        }

        string? cookieHeader = null;
        if (response.Headers.TryGetValues("Set-Cookie", out var cookieValues))
        {
            foreach (var cv in cookieValues)
            {
                if (cv.StartsWith("iconchest_session=", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = cv.IndexOf(';');
                    cookieHeader = endIdx > 0 ? cv[..endIdx] : cv;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(cookieHeader))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Login succeeded but no 'iconchest_session' cookie was returned.");
            Console.ResetColor();
            return 1;
        }

        var session = new CliSession
        {
            BaseUrl = url,
            Cookie = cookieHeader
        };
        await SaveSessionAsync(session);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully logged in to {url}");
        Console.WriteLine($"Session cookie saved to {GetSessionFilePath()}");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> HandleRequestAsync(HttpMethod method, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine($"Error: path required. Usage: iconchest api {method.Method.ToLowerInvariant()} <path> [options]");
            return 1;
        }

        var path = args[0];
        string? url = null;
        string? body = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--url" && i + 1 < args.Length)
            {
                url = args[++i];
            }
            else if (args[i] == "--body" && i + 1 < args.Length)
            {
                body = args[++i];
            }
        }

        var session = await LoadSessionAsync();
        url ??= session?.BaseUrl ?? GetDefaultUrl();
        url = url.TrimEnd('/');

        if (!path.StartsWith('/')) path = "/" + path;
        var fullUri = new Uri($"{url}{path}");

        using var handler = new HttpClientHandler();
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(method, fullUri);
        if (session != null && !string.IsNullOrEmpty(session.Cookie))
        {
            request.Headers.Add("Cookie", session.Cookie);
        }

        // Set Origin header to match server host for CSRF check
        request.Headers.Add("Origin", $"{fullUri.Scheme}://{fullUri.Authority}");

        if (body != null)
        {
            string bodyText = body;
            if (body.StartsWith('@') && File.Exists(body[1..]))
            {
                bodyText = await File.ReadAllTextAsync(body[1..]);
            }
            request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Request failed: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}");
            Console.ResetColor();
        }

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(pretty);
            }
            catch
            {
                Console.WriteLine(responseText);
            }
        }

        return response.IsSuccessStatusCode ? 0 : 1;
    }

    private static async Task<int> HandleEventsAsync(string[] args)
    {
        string? url = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--url" && i + 1 < args.Length)
            {
                url = args[++i];
            }
        }

        var session = await LoadSessionAsync();
        url ??= session?.BaseUrl ?? GetDefaultUrl();
        url = url.TrimEnd('/');

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/api/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (session != null && !string.IsNullOrEmpty(session.Cookie))
        {
            request.Headers.Add("Cookie", session.Cookie);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: No active session found. If authentication fails, run 'iconchest api login' first.");
            Console.ResetColor();
        }

        Console.WriteLine($"Connecting to event stream at {url}/api/events... (Press Ctrl+C to stop)");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Failed to connect: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"SSE connection rejected: {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.Error.WriteLine("Authentication failed. Please run 'iconchest api login' first.");
            }
            else
            {
                try
                {
                    var errDetail = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(errDetail))
                    {
                        Console.Error.WriteLine($"Details: {errDetail}");
                    }
                }
                catch { }
            }
            Console.ResetColor();
            return 1;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        string currentEvent = "message";
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line["event: ".Length..].Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line["data: ".Length..];
                    var time = DateTime.UtcNow.ToString("O");
                    Console.WriteLine($"[{time}] {currentEvent}: {data}");
                    currentEvent = "message";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }

        Console.WriteLine("Event stream disconnected.");
        return 0;
    }
}
