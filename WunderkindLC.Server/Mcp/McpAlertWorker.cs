using System.Threading.Channels;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Infrastructure.Mcp;

namespace WunderkindLC.Server.Mcp;

/// <summary>
/// Fire-and-forget hand-off for superadmin alerts (docs/SPEC-mcp.md §6). <see cref="Enqueue"/> never blocks and
/// never throws, so the OAuth flow cannot fail or slow down because Telegram is slow or down: the worker sends.
/// Bounded (100) — under a flood the oldest pending alert is dropped rather than growing memory.
/// </summary>
public sealed class McpAlertQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<string> Reader => _channel.Reader;

    /// <summary>Queue one alert text. Returns <c>false</c> only if the queue is closed.</summary>
    public bool Enqueue(string text)
    {
        try { return _channel.Writer.TryWrite(text); }
        catch { return false; }
    }
}

/// <summary>
/// Delivers queued alerts to superadmins and runs the volume watch every <see cref="McpLimits.VolumeCheckInterval"/>:
/// a connection whose calls or rows read in the last 24 h exceed <see cref="McpLimits.VolumeCallsPerDay"/> /
/// <see cref="McpLimits.VolumeRowsPerDay"/> is flagged (<see cref="Domain.McpGrant.VolumeAlertAt"/>, at most once a
/// day — durable, so a restart does not repeat it), logged at warning level and reported on Telegram. The
/// connection is NOT revoked — the superadmin decides.
/// </summary>
public sealed class McpAlertWorker(
    McpAlertQueue queue, IServiceScopeFactory scopes, McpFeature feature, TelegramService telegram,
    ILogger<McpAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watch = Task.Run(() => VolumeLoopAsync(stoppingToken), stoppingToken);
        try
        {
            await foreach (var text in queue.Reader.ReadAllAsync(stoppingToken))
                await SendAsync(text, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        await watch.ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task SendAsync(string text, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await McpAlertSender.SendToSuperAdminsAsync(db, telegram, text, logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP alert delivery failed");
        }
    }

    private async Task VolumeLoopAsync(CancellationToken ct)
    {
        try
        {
            // First run a little after startup (migrations, MCP activation), then on the interval.
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            while (!ct.IsCancellationRequested)
            {
                if (feature.Enabled) await CheckVolumeAsync(ct);
                await Task.Delay(McpLimits.VolumeCheckInterval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task CheckVolumeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var a in await McpConnections.ClaimVolumeAlertsAsync(db, DateTime.UtcNow, ct))
            {
                logger.LogWarning(
                    "MCP volume alert: grant {GrantId} (user {UserId}) — {Calls} calls, {Rows} rows in 24 h",
                    a.GrantId, a.UserId, a.Calls, a.Rows);
                queue.Enqueue(McpAlertText.Volume(a.UserName, a.ClientName, a.Calls, a.Rows));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP volume watch failed");
        }
    }
}
