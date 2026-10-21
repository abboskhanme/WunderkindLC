using System.Globalization;
using WunderkindLC.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// CHEKLIST BANDINI AVTOMATIK BELGILASH — <c>ChecklistTemplateItem.AutoCheckKey</c> dan
/// "shu xodim, shu kun" uchun HA/YO'Q javobiga xarita.
///
/// <para>⚠️ <b><see cref="Supported"/> da YO'Q kalit JIMGINA e'tiborsiz qoladi</b> va band oddiy
/// QO'LDA belgilanadigan bo'lib turaveradi. Bu ATAYIN: seed (<see cref="KpiChecklistSeed"/>)
/// Excel'dagi 66 bandning HAMMASI uchun kalit yozadi, avtomatlashtirish esa bosqichma-bosqich
/// qo'shiladi. Aks holda ikki yomon yo'ldan biri chiqardi — yo seed'ni yarim yozish (keyin
/// "bu bandni kim qo'shadi" degan savol), yo qo'llanmagan kalitni "bajarilmadi" deb belgilash
/// (xodim hech qachon qila olmaydigan bandda jarima).</para>
///
/// <para>⚠️ <b>SHUBHA XODIM FOYDASIGA.</b> Bu tekshiruvlar samaradorlik foiziga, u esa oylik
/// bonusga tushadi — ya'ni <c>false</c> qaytarish PUL ushlashga olib boradi. Shuning uchun
/// "hukm chiqarish uchun ma'lumot yo'q" holati HAR DOIM <c>true</c> (masalan o'sha kuni
/// birorta ham darsi bo'lgan guruh bo'lmasa, "jurnal to'ldirildi" bandi o'tadi).</para>
///
/// <para>Natija — faqat MASLAHAT: yozuvni (<c>ChecklistEntry</c>) chaqiruvchi yozadi va xodim
/// uni qo'lda ustidan belgilay oladi.</para>
/// </summary>
public static class ChecklistAutoCheck
{
    /* =========================================================================================
     *  KALITLAR
     * ========================================================================================= */

    // (Topshiriq bandlari — "auto:tasks.overdue_zero", "auto:tasks.due_done", "auto:leads.without_task_zero" —
    //  2026-10-21 dan QO'LDA: ularning manbai Kanban «Topshiriqlar» (WorkTask) edi, u olib tashlandi. Seed kalitlari
    //  o'zgarmadi — qo'llanmagan kalit sinf izohidagi qoida bo'yicha jimgina qo'lda belgilanadigan bandga aylanadi.)
    // (Qo'ng'iroq bandlari — "auto:calls.missed_returned", "auto:calls.answer_speed" — 2026-10 dan QO'LDA:
    //  ularning manbai Local Call agent-ilovasi edi, u olib tashlandi. Seed kalitlari o'zgarmadi —
    //  qo'llanmagan kalit sinf izohidagi qoida bo'yicha jimgina qo'lda belgilanadigan bandga aylanadi.)
    /// <summary>Kecha darsi bo'lgan HAR guruhning jurnali to'ldirilgan.</summary>
    public const string JournalYesterdayFilled = "auto:journal.yesterday_filled";

    /// <summary>
    /// HAQIQATDA qo'llangan kalitlar. Ro'yxatda YO'Q kalit — band qo'lda belgilanadi
    /// (sinf izohidagi ⚠️ ga qarang).
    /// </summary>
    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        JournalYesterdayFilled,
    };


    /* =========================================================================================
     *  KIRISH NUQTASI
     * ========================================================================================= */

    /// <summary>
    /// So'ralgan kalitlarni (<paramref name="keys"/>) shu xodim va shu kun uchun hisoblaydi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Javobda FAQAT <see cref="Supported"/> dagi kalitlar bo'ladi — qolganlari jimgina
    /// tushib qoladi (chaqiruvchi "javob yo'q" ni "qo'lda" deb tushunadi).
    ///
    /// ⚠️ Sana BUZUQ bo'lsa BO'SH lug'at qaytadi: buzuq sana bo'yicha hech narsani
    /// "bajarilmagan" deb belgilash mumkin emas.
    ///
    /// ⚠️ Har tekshiruv KERAKLI ma'lumotni BIR MARTA, TO'PLAB o'qiydi (N+1 yo'q): sahifa
    /// ochilganda 66 bandning hammasi birdan so'raladi.
    /// </remarks>
    public static async Task<Dictionary<string, bool>> RunAsync(
        IAppDbContext db, string userId, string date, IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);

        var wanted = keys.Where(k => !string.IsNullOrWhiteSpace(k) && Supported.Contains(k))
                         .Distinct(StringComparer.Ordinal)
                         .ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0) return result;
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return result;

        var prevIso = day.AddDays(-1).ToString("yyyy-MM-dd");

        /* ---------- Jurnal ---------- */
        if (wanted.Contains(JournalYesterdayFilled))
            result[JournalYesterdayFilled] = await JournalFilledAsync(db, prevIso, day.AddDays(-1), ct);

        return result;
    }

    /* =========================================================================================
     *  TEKSHIRUVLAR
     * ========================================================================================= */

    /// <summary>
    /// «Kechagi barcha guruhlar davomadi tekshirildi»: kecha DARSI BO'LGAN har bir guruhda
    /// jurnal yozuvi bormi.
    /// </summary>
    /// <remarks>
    /// ⚠️ "Darsi bor" — <c>Group.Days</c> (0 = Dushanba … 6 = Yakshanba) bo'yicha aniqlanadi;
    /// arxivlangan/bloklangan guruhlar va hali BOSHLANMAGAN yoki allaqachon TUGAGAN guruhlar
    /// hisobga kirmaydi (aks holda band har kuni "bajarilmadi" bo'lardi).
    ///
    /// ⚠️ Bitta ham dars bo'lmagan kun (dam olish) — o'tadi.
    /// </remarks>
    private static async Task<bool> JournalFilledAsync(
        IAppDbContext db, string dayIso, DateOnly day, CancellationToken ct)
    {
        // C# da DayOfWeek: Yakshanba = 0. Loyihada 0 = Dushanba.
        var dow = ((int)day.DayOfWeek + 6) % 7;

        var groups = await db.Classes
            .Where(g => !g.IsArchived && g.Status != "archived")
            .Select(g => new { g.Id, g.Days, g.StartDate, g.EndDate })
            .ToListAsync(ct);

        var due = groups
            .Where(g => g.Days.Contains(dow))
            .Where(g => string.IsNullOrEmpty(g.StartDate) || string.CompareOrdinal(g.StartDate!, dayIso) <= 0)
            .Where(g => string.IsNullOrEmpty(g.EndDate) || string.CompareOrdinal(g.EndDate!, dayIso) >= 0)
            .Select(g => g.Id)
            .ToList();
        if (due.Count == 0) return true;

        var filled = await db.JournalEntries
            .Where(j => j.Date == dayIso)
            .Select(j => j.ClassId)
            .Distinct()
            .ToListAsync(ct);

        var set = filled.ToHashSet(StringComparer.Ordinal);
        return due.All(set.Contains);
    }
}
