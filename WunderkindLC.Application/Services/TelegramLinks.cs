using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINING TELEGRAM ULANISHLARI (<see cref="TelegramRegistration"/>) — qachon UZILADI
/// (<b>yagona joy</b>). Batafsil: <c>.claude/rules/messaging.md</c> «Telegram ulanishini uzish».
///
/// <para>Bot ulanishni faqat QO'SHADI: ota-ona kontaktini ulashganda raqami o'quvchining raqamiga
/// (<c>Student.Phone</c> yoki <c>ParentPhone</c> — sozlamaga qarab) mos kelsa yozuv paydo bo'ladi
/// (<c>TelegramBotService.CompleteAsync</c>). Ilgari uni olib tashlashning YO'LI YO'Q edi: ota-ona
/// raqami tuzatilsa ham ESKI chat qarz/davomat/baho xabarlarini olaverar va o'quvchi akkaunti uchun
/// bir martalik kirish kodini so'ray olardi.</para>
///
/// <para>Qoida: o'quvchining raqamlari (o'zi, asosiy ota-ona, ota, ona) O'ZGARGANDA — raqami
/// HECH BIRIGA mos kelmaydigan ulanishlar o'chiriladi (<see cref="PruneStaleAsync"/>). Raqami
/// noma'lum (bo'sh/qisqa) eski yozuvga tegilmaydi — uni admin profildan qo'lda uzadi.</para>
/// </summary>
public static class TelegramLinks
{
    /// <summary>Profilda ko'rsatiladigan bitta ulanish.</summary>
    /// <param name="PhoneMatches">Ulanish raqami o'quvchining JORIY raqamlaridan biriga mosmi
    /// (<c>false</c> — eskirgan ulanish, uzish tavsiya etiladi).</param>
    /// <param name="Relation">CRM upgrade P2.4: father · mother · parent · student · other (<c>""</c> = not backfilled).</param>
    /// <param name="Blocked">The chat blocked the bot (<see cref="TelegramRegistration.BlockedAt"/>).</param>
    public sealed record LinkDto(string Id, string ParentName, string Phone, string CreatedAt, bool PhoneMatches,
        string Relation = "", bool Blocked = false, string? BlockedAt = null);

    /// <summary>O'quvchining joriy raqamlari KALITLARI (oxirgi 9 raqam, <see cref="PhoneUtil.Key"/>).
    /// 7 raqamdan qisqasi raqam emas — kirmaydi.</summary>
    public static HashSet<string> PhoneKeys(Student s) =>
        new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone }
            .Select(PhoneUtil.Key)
            .Where(k => k.Length >= 7)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Raqamlar "imzosi" — tahrirdan OLDIN olinadi va keyin solishtiriladi (<see cref="PhonesChanged"/>).</summary>
    public static string PhonesSignature(Student s) =>
        string.Join("|", PhoneKeys(s).OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>Tahrir raqamlarni o'zgartirdimi (format farqi — "+998 90..." va "90..." — o'zgarish EMAS).</summary>
    public static bool PhonesChanged(string signatureBefore, Student after) =>
        !string.Equals(signatureBefore, PhonesSignature(after), StringComparison.Ordinal);

    /// <summary>
    /// Ulanish ESKIRGANMI: raqami ma'lum (kamida 7 raqam) va o'quvchining joriy raqamlaridan HECH
    /// BIRIGA mos kelmaydi. Raqami noma'lum yozuv eskirgan deb HISOBLANMAYDI (taxmin bilan uzilmaydi).
    /// Sof funksiya.
    /// </summary>
    public static bool IsStale(TelegramRegistration r, IReadOnlySet<string> currentKeys)
    {
        var key = PhoneUtil.Key(r.Phone);
        return key.Length >= 7 && !currentKeys.Contains(key);
    }

    /// <summary>
    /// Raqami o'quvchiga endi mos kelmaydigan ulanishlarni o'chiradi va (bo'lsa) BITTA audit qatori
    /// yozadi. <c>SaveChanges</c> QILMAYDI. Qaytaradi: o'chirilgan ulanishlar.
    /// </summary>
    /// <param name="context">Audit izohidagi kontekst ("raqam o'zgardi", "o'qituvchi tahriri").</param>
    public static async Task<List<TelegramRegistration>> PruneStaleAsync(
        IAppDbContext db, AuditService audit, Student s, string context, CancellationToken ct = default)
    {
        var keys = PhoneKeys(s);
        var stale = (await db.TelegramRegistrations.Where(r => r.StudentId == s.Id).ToListAsync(ct))
            .Where(r => IsStale(r, keys))
            .ToList();
        if (stale.Count == 0) return stale;

        db.TelegramRegistrations.RemoveRange(stale);
        audit.Record(AuditService.EntityStudent, s.Id, "update",
            $"Telegram ulanishi uzildi ({context}): " + string.Join(", ", stale.Select(Describe)) + $" ({s.FullName})",
            studentId: s.Id);
        return stale;
    }

    /// <summary>Profil uchun ro'yxat (eng yangisi tepada).</summary>
    public static async Task<List<LinkDto>> ListAsync(IAppDbContext db, Student s, CancellationToken ct = default)
    {
        var keys = PhoneKeys(s);
        return (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.StudentId == s.Id).ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAt)
            .Select(ToDto(keys))
            .ToList();
    }

    /// <summary>Profil qatori (raqam mosligi, qarindoshlik, blok belgisi bilan).</summary>
    public static Func<TelegramRegistration, LinkDto> ToDto(IReadOnlySet<string> keys) => r =>
        new LinkDto(r.Id, r.ParentName, PhoneUtil.Normalize(r.Phone),
            r.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), !IsStale(r, keys) && PhoneUtil.Key(r.Phone).Length >= 7,
            Messaging.TelegramRelations.Effective(r.Relation), r.BlockedAt is not null,
            r.BlockedAt?.ToString("yyyy-MM-ddTHH:mm:ss"));

    /// <summary>Audit/izoh uchun: "Ism (+998-90-...)".</summary>
    public static string Describe(TelegramRegistration r)
    {
        var phone = PhoneUtil.Normalize(r.Phone);
        var name = (r.ParentName ?? "").Trim();
        return name.Length == 0 ? (phone.Length == 0 ? "raqamsiz chat" : phone)
            : phone.Length == 0 ? name : $"{name} ({phone})";
    }
}
