using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Cti;

/// <summary>
/// CTI agent WebSocket ulanishini oxirigacha xizmat qiladi (token Program.cs'da tekshirilgach chaqiriladi).
/// Ulanishda agent onlayn belgilanadi; receive-loop ilova xabarlarini qayta ishlaydi
/// (<c>ack</c> → buyruq "acked", <c>presence</c> → LastSeenAt yangilanadi); uzilishda offlayn.
/// Scoped emas — DbContext'ni <see cref="IServiceScopeFactory"/> orqali qisqa scope'da oladi.
/// </summary>
public static class CtiWebSocketHandler
{
    public static async Task HandleAsync(
        WebSocket socket, string agentId, CtiConnectionManager manager,
        IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken appStopping)
    {
        manager.AddOrReplace(agentId, socket);
        await SetOnlineAsync(scopeFactory, agentId, online: true);

        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !appStopping.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, appStopping);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text || ms.Length == 0) continue;
                await HandleMessageAsync(scopeFactory, agentId, ms.ToArray(), logger);
            }
        }
        catch (OperationCanceledException) { /* ilova to'xtayapti — normal */ }
        catch (WebSocketException) { /* ulanish keskin uzildi — normal */ }
        catch (Exception ex) { logger.LogWarning(ex, "CTI WS xatosi (agent {AgentId})", agentId); }
        finally
        {
            manager.Remove(agentId, socket);
            await SetOnlineAsync(scopeFactory, agentId, online: false);
        }
    }

    /// <summary>Ilovadan kelgan JSON xabarni qayta ishlaydi: ack (buyruq yetkazildi) / presence.</summary>
    private static async Task HandleMessageAsync(
        IServiceScopeFactory scopeFactory, string agentId, byte[] payload, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            switch (type)
            {
                case "ack":
                    var commandId = root.TryGetProperty("commandId", out var c) ? c.GetString() ?? "" : "";
                    if (commandId.Length > 0)
                        await db.CtiCommandLogs.Where(l => l.Id == commandId)
                            .ExecuteUpdateAsync(up => up.SetProperty(l => l.Status, "acked"));
                    break;
                case "presence":
                    await db.CtiAgents.Where(a => a.Id == agentId)
                        .ExecuteUpdateAsync(up => up
                            .SetProperty(a => a.LastSeenAt, AppClock.Now)
                            .SetProperty(a => a.IsOnline, true));
                    break;
            }
        }
        catch (JsonException) { /* buzuq JSON — e'tiborsiz */ }
        catch (Exception ex) { logger.LogWarning(ex, "CTI WS xabarini qayta ishlashda xato"); }
    }

    private static async Task SetOnlineAsync(IServiceScopeFactory scopeFactory, string agentId, bool online)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.CtiAgents.Where(a => a.Id == agentId)
                .ExecuteUpdateAsync(up => up
                    .SetProperty(a => a.IsOnline, online)
                    .SetProperty(a => a.LastSeenAt, AppClock.Now));
        }
        catch { /* agent o'chirilgan bo'lishi mumkin — e'tiborsiz */ }
    }
}
