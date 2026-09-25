using System.Collections.Concurrent;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TO'LOV BIR MARTA O'TISHI kafolati — "Saqlash" ketma-ket bir necha marta bosilsa ham.
///
/// <para>Ilgari himoya faqat "oxirgi 5–6 soniyada AYNAN shu to'lov bormi" tekshiruvi edi. U
/// POYGAGA ochiq: bir zumda kelgan ikki so'rov IKKALASI ham tekshiruvdan (hali hech narsa
/// saqlanmagan) o'tib, ikki tranzaksiya va IKKI BARAVAR balans yozardi. Ikki qatlam qo'shildi:</para>
///
/// <list type="number">
/// <item><b>Qulf</b> (<see cref="LockAsync"/>) — bir o'quvchi (yoki bir turdagi moliya amali)
/// bo'yicha tekshiruv + yozish KETMA-KET bajariladi. Ikkinchi so'rov birinchisi SAQLANGACH
/// kiradi va uni "oxirgi soniyalardagi dublikat" sifatida topadi.</item>
/// <item><b>So'rov kaliti</b> (<see cref="Find"/>/<see cref="Remember"/>) — klient oyna ochilganda
/// bitta tasodifiy kalit yaratadi va shu oynadagi HAR urinishda uni yuboradi. Tarmoq sekin bo'lib
/// qayta yuborilgan so'rov (soniyalar oynasidan keyin ham) o'sha tranzaksiyani qaytaradi.</item>
/// </list>
///
/// <para>⚠️ Holat PROTSESS XOTIRASIDA: ilova bitta nusxada ishlaydi (docker compose, bitta
/// <c>app</c>). Bir nechta nusxaga o'tilsa qulf va kalitlar bazaga (unikal indeks) ko'chirilishi
/// SHART — aks holda turli nusxaga tushgan ikki so'rov yana ikki to'lov yozadi.</para>
/// </summary>
public static class PaymentIdempotency
{
    /// <summary>Kalit shuncha vaqt eslab turiladi (oyna ochiq turib qayta urinish uchun yetarli).</summary>
    public static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(30);

    /// <summary>Xotiradagi kalitlar chegarasi — oshsa eskilari tozalanadi (cheksiz o'smasin).</summary>
    public const int MaxKeys = 5000;

    /// <summary>Klient kalitining eng uzun ruxsat etilgan uzunligi (GUID = 36).</summary>
    public const int MaxKeyLength = 64;

    // Qulflar "bo'laklangan": har o'quvchiga alohida semafor saqlanmaydi (xotira o'smasin),
    // kalit xeshi bo'yicha 64 tadan biri olinadi. Ikki xil o'quvchi bitta bo'lakka tushsa
    // shunchaki navbat kutadi — to'g'rilik buzilmaydi.
    private const int Stripes = 64;
    private static readonly SemaphoreSlim[] Locks =
        Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly ConcurrentDictionary<string, (string TxId, DateTime At)> Seen = new();

    /// <summary><paramref name="scope"/> (masalan o'quvchi id'si) bo'yicha qulf. <c>using</c> bilan bo'shatiladi.</summary>
    public static async Task<IDisposable> LockAsync(string scope)
    {
        var sem = Locks[(uint)StringComparer.Ordinal.GetHashCode(scope) % Stripes];
        await sem.WaitAsync();
        return new Releaser(sem);
    }

    /// <summary>
    /// Klient kalitini tozalaydi: bo'sh, juda uzun yoki begona belgili kalit — <c>null</c>
    /// (ya'ni kalitsiz eski xatti-harakat; so'rov RAD ETILMAYDI — eski klient ham ishlasin).
    /// Natija <paramref name="scope"/> bilan birlashtiriladi: bir kalit boshqa o'quvchining
    /// tranzaksiyasini qaytara olmaydi.
    /// </summary>
    public static string? Key(string scope, string? requestId)
    {
        var k = (requestId ?? "").Trim();
        if (k.Length == 0 || k.Length > MaxKeyLength) return null;
        foreach (var ch in k)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_')) return null;
        return scope + ":" + k;
    }

    /// <summary>Shu kalit bilan allaqachon yozilgan tranzaksiya id'si (muddati o'tmagan bo'lsa).</summary>
    public static string? Find(string? key, DateTime nowUtc)
    {
        if (key is null || !Seen.TryGetValue(key, out var hit)) return null;
        if (nowUtc - hit.At > KeyTtl)
        {
            Seen.TryRemove(key, out _);
            return null;
        }
        return hit.TxId;
    }

    /// <summary>Kalitni yozilgan tranzaksiyaga bog'laydi (faqat MUVAFFAQIYATLI saqlashdan keyin).</summary>
    public static void Remember(string? key, string txId, DateTime nowUtc)
    {
        if (key is null) return;
        Seen[key] = (txId, nowUtc);
        if (Seen.Count <= MaxKeys) return;
        foreach (var kv in Seen)
            if (nowUtc - kv.Value.At > KeyTtl) Seen.TryRemove(kv.Key, out _);
        // Muddati o'tgani bo'lmasa ham chegara saqlanadi — eng eskilari tashlanadi.
        if (Seen.Count > MaxKeys)
            foreach (var kv in Seen.OrderBy(kv => kv.Value.At).Take(Seen.Count - MaxKeys))
                Seen.TryRemove(kv.Key, out _);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) sem.Release();
        }
    }
}
