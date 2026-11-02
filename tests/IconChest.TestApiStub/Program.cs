using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

int port = 0;
int parentPid = 0;
string mode = "ready";

for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        int.TryParse(args[++i], out port);
    }
    else if (args[i].Equals("--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        int.TryParse(args[++i], out parentPid);
    }
    else if (args[i].Equals("--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        mode = args[++i].ToLowerInvariant();
    }
}

// Mode: exit-early
if (mode == "exit-early")
{
    Console.Error.WriteLine("Simulated early fatal error: failed to initialize database or bind socket.");
    return 1;
}

// Parent watchdog
if (parentPid > 0)
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(250);
            try
            {
                var p = Process.GetProcessById(parentPid);
                if (p.HasExited) Environment.Exit(0);
            }
            catch
            {
                // Process does not exist
                Environment.Exit(0);
            }
        }
    });
}

// Start TcpListener HTTP server
var tcpListener = new TcpListener(IPAddress.Loopback, Math.Max(0, port));
tcpListener.Start();
port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;

_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            var socket = await tcpListener.AcceptSocketAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var stream = new NetworkStream(socket, ownsSocket: true);
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    var reqLine = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(reqLine)) return;

                    // Drain headers until blank line
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }

                    var parts = reqLine.Split(' ');
                    var path = parts.Length > 1 ? parts[1] : "/";

                    string body;
                    if (path == "/health" || (path == "/api/session" && mode != "ignore-shutdown"))
                    {
                        body = "{\"status\":\"ok\"}";
                    }
                    else if (path == "/api/system/shutdown" && mode != "ignore-shutdown")
                    {
                        body = "{\"status\":\"shutting_down\"}";
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            Environment.Exit(0);
                        });
                    }
                    else
                    {
                        body = "{\"status\":\"ok\"}";
                    }

                    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                    string responseHeader = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeader);

                    await stream.WriteAsync(headerBytes);
                    await stream.WriteAsync(bodyBytes);
                    await stream.FlushAsync();
                }
                catch {}
            });
        }
        catch
        {
            break;
        }
    }
});

// Mode: delayed
if (mode == "delayed")
{
    await Task.Delay(3000);
}

// Mode: never
if (mode != "never")
{
    Console.WriteLine($"ICONCHEST_READY http://127.0.0.1:{port}");
    Console.Out.Flush();
}

// Keep running until killed or exit
while (true)
{
    await Task.Delay(1000);
}
