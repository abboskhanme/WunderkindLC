using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CTI (Local Call) agent WebSocket ulanishlarini boshqaruvchi (singleton). Har agent (telefon)
/// bittadan jonli ulanishga ega — eski ulanish bo'lsa yopib almashtiriladi. Server→ilova buyruqlari
/// (masalan click-to-call <c>dial</c>, <c>send_sms</c>) shu yerdan yuboriladi. Application qatlamida
/// (Server emas) — chunki AutoMessageService kabi Application xizmatlari ham Local SMS yuborishi kerak.
/// </summary>
public class CtiConnectionManager
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    /// <summary>Agent ulanishini qo'shadi; eski ulanish bo'lsa uni yopib, yangisiga almashtiradi.</summary>
    public void AddOrReplace(string agentId, WebSocket socket)
    {
        if (_sockets.TryRemove(agentId, out var old) && old is not null)
        {
            try
            {
                if (old.State == WebSocketState.Open)
                    _ = old.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None);
                else old.Abort();
            }
            catch { /* eski ulanish allaqachon uzilgan bo'lishi mumkin */ }
        }
        _sockets[agentId] = socket;
    }

    /// <summary>Ulanishni ro'yxatdan olib tashlaydi (faqat AYNAN shu socket hali qayd etilgan bo'lsa —
    /// almashtirilgan eski ulanish yangi ulanishni o'chirib yubormasligi uchun).</summary>
    public void Remove(string agentId, WebSocket socket)
    {
        if (_sockets.TryGetValue(agentId, out var cur) && ReferenceEquals(cur, socket))
            _sockets.TryRemove(agentId, out _);
    }

    /// <summary>Agent hozir jonli ulanganmi (holat Open).</summary>
    public bool IsConnected(string agentId) =>
        _sockets.TryGetValue(agentId, out var s) && s.State == WebSocketState.Open;

    /// <summary>Agentga JSON xabar (text frame) yuboradi. Xato bo'lsa — false + ulanishni tozalaydi.</summary>
    public async Task<bool> SendAsync(string agentId, object payload)
    {
        if (!_sockets.TryGetValue(agentId, out var socket) || socket.State != WebSocketState.Open)
            return false;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            return true;
        }
        catch
        {
            Remove(agentId, socket);
            return false;
        }
    }
}
