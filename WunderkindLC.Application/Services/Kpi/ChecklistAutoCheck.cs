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
/// birorta ham kiruvchi qo'ng'iroq bo'lmagan bo'lsa, "tez javob berdi" bandi o'tadi).</para>
///
/// <para>Natija — faqat MASLAHAT: yozuvni (<c>ChecklistEntry</c>) chaqiruvchi yozadi va xodim
/// uni qo'lda ustidan belgilay oladi.</para>
/// </summary>
public static class ChecklistAutoCheck
{
    /* =========================================================================================
     *  KALITLAR
     * ========================================================================================= */

    /// <summary>Kechikkan topshiriq YO'Q (kun oxirida).</summary>
    public const string TasksOverdueZero = "auto:tasks.overdue_zero";
    /// <summary>Shu kunga muddati belgilangan topshiriqlarning HAMMASI yopilgan.</summary>
    public const string TasksDueDone = "auto:tasks.due_done";
    /// <summary>Kechagi javobsiz KIRUVCHI qo'ng'iroqlarning hammasiga qayta qo'ng'iroq qilingan.</summary>
    public const string CallsMissedReturned = "auto:calls.missed_returned";
    /// <summary>Kiruvchi qo'ng'iroqlarga TEZ javob berilgan (≤ 15 soniya, kamida 90%).</summary>
    public const string CallsAnswerSpeed = "auto:calls.answer_speed";
    /// <summary>Kecha darsi bo'lgan HAR guruhning jurnali to'ldirilgan.</summary>
    public const string JournalYesterdayFilled = "auto:journal.yesterday_filled";
    /// <summary>Ochiq lidlarning hammasida ochiq topshiriq bor ("vazifasiz sdelka = 0").</summary>
    public const string LeadsWithoutTaskZero = "auto:leads.without_task_zero";

    /// <summary>
    /// HAQIQATDA qo'llangan kalitlar. Ro'yxatda YO'Q kalit — band qo'lda belgilanadi
    /// (sinf izohidagi ⚠️ ga qarang).
    /// </summary>
    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TasksOverdueZero, TasksDueDone, CallsMissedReturned,
        CallsAnswerSpeed, JournalYesterdayFilled, LeadsWithoutTaskZero,
    };

    /// <summary>Kiruvchi qo'ng'iroqqa "tez javob" chegarasi (soniya) — Excel'dagi «3 gudok».</summary>
    public const int AnswerSpeedSeconds = 15;

    /// <summary>Tez javob berilgan qo'ng'iroqlarning kerakli ULUSHI.</summary>
    public const double AnswerSpeedShare = 0.90;

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

        var iso = day.ToString("yyyy-MM-dd");
        var prevIso = day.AddDays(-1).ToString("yyyy-MM-dd");

        /* ---------- Topshiriqlar (bitta so'rov, ikkala kalit uchun) ---------- */
        if (wanted.Contains(TasksOverdueZero) || wanted.Contains(TasksDueDone)
                                              || wanted.Contains(LeadsWithoutTaskZero))
        {
            var openTasks = await db.WorkTasks
                .Where(t => t.AssigneeId == userId && !t.IsArchived && t.CompletedAt == null)
                .ToListAsync(ct);

            if (wanted.Contains(TasksOverdueZero))
            {
                // Kechikkan = muddati BUGUNDAN OLDIN va hamon yopilmagan. Bugunga qo'yilgani
                // hali kechikmagan (kun tugamagan) — aks holda ertalab belgilangan cheklist
                // har doim "bajarilmadi" bo'lardi.
                result[TasksOverdueZero] = !openTasks.Any(
                    t => !string.IsNullOrEmpty(t.DueDate)
                         && string.CompareOrdinal(t.DueDate!, iso) < 0);
            }

            if (wanted.Contains(TasksDueDone))
            {
                // Shu kunga muddati qo'yilgan topshiriqlar — hammasi yopilgan bo'lishi kerak.
                // Bironta ham bo'lmasa: bajariladigan ish yo'q edi → o'tadi.
                result[TasksDueDone] = !openTasks.Any(t => t.DueDate == iso);
            }

            if (wanted.Contains(LeadsWithoutTaskZero))
                result[LeadsWithoutTaskZero] = await LeadsHaveTasksAsync(db, userId, openTasks, ct);
        }

        /* ---------- Qo'ng'iroqlar ---------- */
        if (wanted.Contains(CallsMissedReturned))
            result[CallsMissedReturned] = await MissedReturnedAsync(db, day, ct);

        if (wanted.Contains(CallsAnswerSpeed))
            result[CallsAnswerSpeed] = await AnswerSpeedAsync(db, userId, day, ct);

        /* ---------- Jurnal ---------- */
        if (wanted.Contains(JournalYesterdayFilled))
            result[JournalYesterdayFilled] = await JournalFilledAsync(db, prevIso, day.AddDays(-1), ct);

        return result;
    }

    /* =========================================================================================
     *  TEKSHIRUVLAR
     * ========================================================================================= */

    /// <summary>
    /// «Vazifasiz sdelka = 0»: xodimning har bir OCHIQ lidiga ochiq topshiriq biriktirilganmi.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Loyihada <c>Lead</c> ↔ <c>WorkTask</c> uchun ALOHIDA ustun YO'Q</b> — bog'lanish
    /// topshiriqning <c>Tags</c> ro'yxatiga lid Id'sini qo'yish orqali ifodalanadi.
    ///
    /// ⚠️ Shuning uchun: agar xodimning ochiq topshiriqlaridan BIRONTASI ham lidga
    /// bog'lanmagan bo'lsa, bu "bog'lanish umuman ishlatilmayapti" degani va band
    /// <b>O'TADI</b> (fail-open). Aks holda bog'lanishni hech kim yozmagani uchun HAR KUNI
    /// hamma xodimda "bajarilmadi" chiqar va samaradorlik foizi (ya'ni oylik bonus) sabab
    /// yo'q joyda pasayardi. Bog'lanishdan FOYDALANAYOTGAN xodim uchun esa tekshiruv haqiqiy
    /// ishlaydi: bittasi bog'langan bo'lsa, qolgan ochiq lidlar ham bog'langan bo'lishi kerak.
    /// </remarks>
    private static async Task<bool> LeadsHaveTasksAsync(
        IAppDbContext db, string userId, List<Domain.WorkTask> openTasks, CancellationToken ct)
    {
        var openLeadIds = await db.Leads
            .Where(l => l.AssigneeUserId == userId && l.ConvertedStudentId == null && l.ClosedAt == null)
            .Select(l => l.Id)
            .ToListAsync(ct);
        if (openLeadIds.Count == 0) return true;

        var leadSet = openLeadIds.ToHashSet(StringComparer.Ordinal);
        var linked = openTasks.SelectMany(t => t.Tags)
                              .Where(leadSet.Contains)
                              .ToHashSet(StringComparer.Ordinal);

        if (linked.Count == 0) return true;   // bog'lanish ishlatilmayapti — hukm chiqarmaymiz
        return leadSet.All(linked.Contains);
    }

    /// <summary>
    /// «Kechagi javobsiz qo'ng'iroqlar qaytarildi»: KECHAGI har bir javobsiz KIRUVCHI
    /// qo'ng'iroq uchun o'sha raqamga KEYINROQ chiquvchi qo'ng'iroq bo'lganmi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Xodim kesimida EMAS, MARKAZ bo'yicha: javobsiz kiruvchi qo'ng'iroqda operator
    /// umuman yo'q (hech kim ko'tarmagan), ya'ni uni kimgadir biriktirib bo'lmaydi. Band
    /// «kechagi javobsizlar qaytarildimi» degan JAMOA intizomi haqida.
    ///
    /// ⚠️ Raqamlar <c>PhoneUtil.Key</c> (oxirgi 9 raqam) bilan solishtiriladi — bazada
    /// formatlar aralash ("+998 90 …", "998…", "90…").
    /// </remarks>
    private static async Task<bool> MissedReturnedAsync(IAppDbContext db, DateOnly day, CancellationToken ct)
    {
        var prev = day.AddDays(-1);
        var from = prev.ToDateTime(TimeOnly.MinValue);
        var to = day.AddDays(1).ToDateTime(TimeOnly.MinValue);   // kecha + bugun

        var calls = await db.Calls
            .Where(c => c.StartedAt >= from && c.StartedAt < to)
            .Select(c => new { c.Direction, c.Status, c.PhoneNumber, c.StartedAt })
            .ToListAsync(ct);

        var missed = calls
            .Where(c => c.Direction == "inbound" && c.Status == "no_answer"
                        && DateOnly.FromDateTime(c.StartedAt) == prev)
            .ToList();
        if (missed.Count == 0) return true;

        var outbound = calls.Where(c => c.Direction == "outbound").ToList();
        foreach (var m in missed)
        {
            var key = PhoneUtil.Key(m.PhoneNumber);
            if (key.Length == 0) continue;   // raqamsiz yozuv bo'yicha hukm chiqarmaymiz
            var returned = outbound.Any(o => o.StartedAt > m.StartedAt && PhoneUtil.Key(o.PhoneNumber) == key);
            if (!returned) return false;
        }
        return true;
    }

    /// <summary>
    /// «Kiruvchi qo'ng'iroqlar 3-gudokgacha olindi»: javob berilgan kiruvchi qo'ng'iroqlarning
    /// kamida 90% i <see cref="AnswerSpeedSeconds"/> soniya ichida ko'tarilganmi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Xodimga biriktirilgan qo'ng'iroqlar VA operatori KO'RSATILMAGANLAR birga olinadi:
    /// kiruvchi qo'ng'iroq ko'pincha operator id'siz keladi (kim ko'targani provayder
    /// hodisasidan har doim ham aniqlanmaydi). Faqat "o'zinikini" sanasak, ro'yxat deyarli
    /// har doim bo'sh bo'lib, tekshiruv ma'nosini yo'qotardi.
    ///
    /// ⚠️ JAVOB BERILMAGAN qo'ng'iroqlar maxrajga KIRMAYDI — ular boshqa bandning
    /// (<see cref="CallsMissedReturned"/>) mavzusi; bu yerda "qanchalik TEZ ko'tarildi"
    /// so'ralyapti.
    /// </remarks>
    private static async Task<bool> AnswerSpeedAsync(
        IAppDbContext db, string userId, DateOnly day, CancellationToken ct)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = day.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var rows = await db.Calls
            .Where(c => c.Direction == "inbound" && c.AnsweredAt != null
                        && c.StartedAt >= from && c.StartedAt < to
                        && (c.OperatorUserId == userId || c.OperatorUserId == null || c.OperatorUserId == ""))
            .Select(c => new { c.StartedAt, c.AnsweredAt })
            .ToListAsync(ct);

        if (rows.Count == 0) return true;   // baholanadigan qo'ng'iroq yo'q

        var fast = rows.Count(r => (r.AnsweredAt!.Value - r.StartedAt).TotalSeconds <= AnswerSpeedSeconds);
        return (double)fast / rows.Count >= AnswerSpeedShare;
    }

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
