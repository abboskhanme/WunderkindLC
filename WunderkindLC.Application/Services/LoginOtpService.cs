using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Bot orqali so'raladigan bir martalik kirish kodi (parol o'rniga tezkor login). Statik — holat
/// saqlanmaydi, hamma narsa <see cref="LoginOtpCode"/> jadvalida (chaqiruvchi IAppDbContext beradi).
/// Kod 8 belgi (chalkash bo'lishi mumkin bo'lgan 0/O/1/I/L chiqarib tashlangan), 60 soniya amal
/// qiladi, bir marta ishlatiladi. So'rash chastotasi (5 daqiqada bir marta) chaqiruvchi tomonda
/// <see cref="LastRequestAtAsync"/> orqali tekshiriladi.
/// </summary>
public static class LoginOtpService
{
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int CodeLength = 8;

    public static readonly TimeSpan CodeTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Shu chat OXIRGI marta kod so'ragan payt (cooldown hisoblash uchun) — bo'lmasa null.</summary>
    public static async Task<DateTime?> LastRequestAtAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        return await db.LoginOtpCodes
            .Where(c => c.ChatId == chatId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Kriptografik tasodifiy 8 belgili kod (raqam+harf, chalkash belgilarsiz).</summary>
    private static string GenerateCode()
    {
        var sb = new StringBuilder(CodeLength);
        Span<byte> buf = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(buf);
        foreach (var b in buf) sb.Append(CodeAlphabet[b % CodeAlphabet.Length]);
        return sb.ToString();
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    /// <summary>Berilgan foydalanuvchi uchun yangi kod yaratadi — shu userga tegishli ESKI (hali
    /// ishlatilmagan) kodlar avtomatik bekor qilinadi (bir vaqtda faqat bitta kod amal qiladi).
    /// Qaytadi: ochiq (plaintext) kod matni — faqat SHU YERDA, DB'da faqat xeshi saqlanadi.</summary>
    public static async Task<string> IssueAsync(IAppDbContext db, string userId, long chatId, CancellationToken ct)
    {
        var stale = await db.LoginOtpCodes.Where(c => c.UserId == userId && !c.Used).ToListAsync(ct);
        foreach (var s in stale) s.Used = true;

        var code = GenerateCode();
        db.LoginOtpCodes.Add(new LoginOtpCode
        {
            UserId = userId,
            ChatId = chatId,
            CodeHash = Hash(code),
            CreatedAt = AppClock.Now,
            ExpiresAt = AppClock.Now.Add(CodeTtl),
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>Kodni tekshiradi va (to'g'ri/muddati o'tmagan/ishlatilmagan bo'lsa) DARHOL ishlatilgan
    /// deb belgilaydi (bir martalik — qayta urinib ko'rish ishlamaydi). Topilgan foydalanuvchi id'sini
    /// qaytaradi (null — kod noto'g'ri/eskirgan/ishlatilgan).</summary>
    public static async Task<string?> VerifyAndConsumeAsync(IAppDbContext db, string rawCode, CancellationToken ct)
    {
        var normalized = rawCode.Trim().ToUpperInvariant();
        if (normalized.Length == 0) return null;
        var hash = Hash(normalized);

        var entry = await db.LoginOtpCodes.FirstOrDefaultAsync(c => c.CodeHash == hash && !c.Used, ct);
        if (entry is null) return null;
        if (entry.ExpiresAt < AppClock.Now) return null;

        entry.Used = true;
        entry.ConsumedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        return entry.UserId;
    }
}
