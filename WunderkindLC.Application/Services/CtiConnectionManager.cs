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

    /// <summary>
    /// Agentni MAJBURAN uzadi: ilovaga <c>{action:"logout"}</c> yuboriladi (ilova tokenini o'chirib,
    /// login ekraniga qaytadi), so'ng ulanish yopiladi va ro'yxatdan olinadi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Agent o'chirilganda (<c>CtiController.UpdateAgent</c>) chaqiriladi. Ilgari o'chirish faqat
    /// YANGI loginni to'sardi — ishdan ketgan xodimning telefoni jonli ulanish (va 12 soatlik JWT)
    /// bilan qo'ng'iroq qilishda va ota-onalarga SMS yuborishda davom etardi.
    /// </remarks>
    public async Task<bool> DisconnectAsync(string agentId, string reason = "deactivated")
    {
        if (!_sockets.TryGetValue(agentId, out var socket)) return false;
        await SendAsync(agentId, new { action = "logout", reason });
        _sockets.TryRemove(agentId, out _);
        try
        {
            // CloseOutputAsync — faqat yopish kadrini yuboradi (handler'ning kutib turgan
            // ReceiveAsync'i bilan to'qnashmaydi). Ilova javob bermasa 5 s dan keyin uziladi.
            if (socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, cts.Token);
                _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
                {
                    try { if (socket.State != WebSocketState.Closed) socket.Abort(); } catch { }
                }, TaskScheduler.Default);
            }
            else socket.Abort();
        }
        catch { try { socket.Abort(); } catch { } }
        return true;
    }

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
