using System.Globalization;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
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

    /// <summary>Kechikkan topshiriq YO'Q (kun boshida) — HR topshiriqlari (<c>HrTask</c>, 2026-10-21 gacha Kanban).</summary>
    public const string TasksOverdueZero = "auto:tasks.overdue_zero";
    /// <summary>Shu kunga muddati belgilangan HR topshiriqlarining HAMMASI yopilgan.</summary>
    public const string TasksDueDone = "auto:tasks.due_done";
    // ("auto:leads.without_task_zero" — 2026-10-21 dan QO'LDA: lid ↔ topshiriq bog'lanishi Kanban «Topshiriqlar»
    //  (WorkTask.Tags/LeadId) da edi, HR topshiriqlarida lid tushunchasi yo'q. Seed kaliti o'zgarmadi — qo'llanmagan
    //  kalit sinf izohidagi qoida bo'yicha jimgina qo'lda belgilanadigan bandga aylanadi.)
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
        TasksOverdueZero, TasksDueDone, JournalYesterdayFilled,
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

        /* ---------- HR topshiriqlari (bitta so'rov, ikkala kalit uchun) ---------- */
        if (wanted.Contains(TasksOverdueZero) || wanted.Contains(TasksDueDone))
        {
            // Kun chegaralari HR modulining O'Z vaqt mintaqasida (hr_settings) — "bugun" u bilan bir xil bo'lsin.
            var zone = HrValues.Zone(await db.HrSettings.AsNoTracking().Select(x => x.Timezone).FirstOrDefaultAsync(ct));
            var dayStart = HrValues.DayStart(day, zone);
            var nextDayStart = HrValues.DayStart(day.AddDays(1), zone);
            // Xodimning OCHIQ (open/returned) topshiriqlari muddati shu kun oxirigacha. HR qatori yo'q xodimda
            // topshiriq ham yo'q → ikkala band O'TADI (shubha xodim foydasiga).
            var openDue = await (from t in db.HrTasks.AsNoTracking()
                                 join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                                 where e.UserId == userId
                                       && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                                       && t.DueAt < nextDayStart
                                 select t.DueAt).ToListAsync(ct);

            // Kechikkan = muddati shu KUNDAN OLDIN va hamon ochiq. Shu kunga qo'yilgani hali kechikmagan
            // (kun tugamagan) — aks holda ertalab belgilangan cheklist har doim "bajarilmadi" bo'lardi.
            if (wanted.Contains(TasksOverdueZero))
                result[TasksOverdueZero] = !openDue.Any(d => d < dayStart);
            // Shu kunga muddati qo'yilganlarning hammasi yopilgan. Bironta ham bo'lmasa: bajariladigan ish yo'q edi → o'tadi.
            if (wanted.Contains(TasksDueDone))
                result[TasksDueDone] = !openDue.Any(d => d >= dayStart);
        }

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
