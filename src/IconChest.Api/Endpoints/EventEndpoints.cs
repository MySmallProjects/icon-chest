using System.Text.Json;
using IconChest.Core.Events;
using IconChest.Core.Thumbnails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (
            HttpContext context,
            ILibraryEventEmitter eventEmitter,
            IThumbnailQueue thumbnailQueue,
            CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            var writeLock = new SemaphoreSlim(1, 1);

            async Task SendEventAsync(string eventName, string data)
            {
                if (ct.IsCancellationRequested) return;

                await writeLock.WaitAsync(ct);
                try
                {
                    await context.Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
                catch
                {
                    // Client disconnected
                }
                finally
                {
                    writeLock.Release();
                }
            }

            async Task SendCommentAsync(string comment)
            {
                if (ct.IsCancellationRequested) return;

                await writeLock.WaitAsync(ct);
                try
                {
                    await context.Response.WriteAsync($": {comment}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
                catch
                {
                    // Client disconnected
                }
                finally
                {
                    writeLock.Release();
                }
            }

            Action onLibraryChanged = () =>
            {
                _ = SendEventAsync("library.changed", "{}");
            };

            Action<object> onScanProgress = (prog) =>
            {
                string json = JsonSerializer.Serialize(prog);
                _ = SendEventAsync("scan.progress", json);
            };

            Action<object> onScanCompleted = (res) =>
            {
                string json = JsonSerializer.Serialize(res);
                _ = SendEventAsync("scan.completed", json);
            };

            eventEmitter.LibraryChanged += onLibraryChanged;
            eventEmitter.ScanProgress += onScanProgress;
            eventEmitter.ScanCompleted += onScanCompleted;

            try
            {
                // Send initial connection greeting / heartbeat
                await SendCommentAsync("connected");

                long lastHeartbeatTicks = Environment.TickCount64;
                long lastThumbnailCheckTicks = Environment.TickCount64;

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);

                    long nowTicks = Environment.TickCount64;

                    // 1. Heartbeat every 15 seconds
                    if (nowTicks - lastHeartbeatTicks >= 15000)
                    {
                        lastHeartbeatTicks = nowTicks;
                        await SendCommentAsync("heartbeat");
                    }

                    // 2. Thumbnail status every 2 seconds if work is pending/running
                    if (nowTicks - lastThumbnailCheckTicks >= 2000)
                    {
                        lastThumbnailCheckTicks = nowTicks;
                        var st = thumbnailQueue.GetStatus();
                        if (st.Pending > 0 || st.Running > 0)
                        {
                            var thumbData = new
                            {
                                pending = st.Pending,
                                running = st.Running,
                                done = st.Done,
                                failed = st.Failed,
                                total = st.TotalJobs
                            };
                            await SendEventAsync("thumbnails.status", JsonSerializer.Serialize(thumbData));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Clean disconnect
            }
            finally
            {
                eventEmitter.LibraryChanged -= onLibraryChanged;
                eventEmitter.ScanProgress -= onScanProgress;
                eventEmitter.ScanCompleted -= onScanCompleted;
            }
        })
        .WithName("EventsStream")
        .WithSummary("Server-Sent Events stream for real-time library changes, scan progress and thumbnail status");
    }
}
