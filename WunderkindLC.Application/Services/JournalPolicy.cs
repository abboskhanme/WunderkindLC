using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Jurnal tahrirlash siyosati — admin "Guruhlar → Jurnal boshqaruvi" oynasida belgilanadi,
/// <see cref="CenterMeta"/>'da saqlanadi. Admin jurnali ham, o'qituvchi ilovasi ham katakka
/// yozishdan OLDIN <see cref="CheckAsync"/> ni chaqiradi (yagona nazorat nuqtasi).
/// Kelajak sanalar HAR DOIM taqiqlangan (JournalService ichida) — siyosat faqat O'TGAN sanalarni cheklaydi.
/// </summary>
public static class JournalPolicy
{
    public const string ModeFree = "free";
    public const string ModeToday = "today";
    public const string ModeWindow = "window";

    /// <summary>Joriy siyosat (CenterMeta yo'q/buzuq bo'lsa — xavfsiz default: erkin).</summary>
    public static async Task<JournalPolicyDto> GetAsync(IAppDbContext db)
    {
        var m = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();
        var mode = m?.JournalEditMode is ModeToday or ModeWindow ? m.JournalEditMode : ModeFree;
        return new JournalPolicyDto(mode, m?.JournalRetroDays ?? 3,
            m?.JournalConductedOnly ?? false, m?.JournalApplyToAdmins ?? false,
            m?.SalaryRequireJournal ?? false, m?.SalaryGraceDays ?? 0,
            m?.JournalHideUnpaidPrevMonth ?? false, m?.JournalHideUnpaidAfterDay ?? false,
            // Buzuq/bo'sh (0) qiymat — standart 10-kunga tushadi (1 ga emas).
            m?.JournalUnpaidCutoffDay is int d and >= 1 and <= 28 ? d : 10);
    }

    /// <summary>Siyosatni saqlaydi (noto'g'ri qiymatlar xavfsiz defaultga tushiriladi) va yangisini qaytaradi.</summary>
    public static async Task<JournalPolicyDto> SaveAsync(IAppDbContext db, JournalPolicyDto req)
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null)
        {
            m = new CenterMeta();
            db.CenterMeta.Add(m);
        }
        m.JournalEditMode = req.EditMode is ModeToday or ModeWindow ? req.EditMode : ModeFree;
        m.JournalRetroDays = Math.Clamp(req.RetroDays, 1, 90);
        m.JournalConductedOnly = req.ConductedOnly;
        m.JournalApplyToAdmins = req.ApplyToAdmins;
        m.SalaryRequireJournal = req.SalaryRequireJournal;
        m.SalaryGraceDays = Math.Clamp(req.SalaryGraceDays, 0, 30);
        m.JournalHideUnpaidPrevMonth = req.HideUnpaidPrevMonth;
        m.JournalHideUnpaidAfterDay = req.HideUnpaidAfterDay;
        // 28 — har oyda (fevralda ham) mavjudligi kafolatlangan eng katta kun.
        m.JournalUnpaidCutoffDay = Math.Clamp(req.UnpaidCutoffDay, 1, 28);
        await db.SaveChangesAsync();
        return await GetAsync(db);
    }

    /// <summary>
    /// Katakka yozish/tozalashdan OLDIN chaqiriladi. null = ruxsat; aks holda foydalanuvchiga
    /// ko'rsatiladigan taqiq xabari (controller BadRequest bilan qaytaradi).
    /// <paramref name="skipConducted"/> — ommaviy davomat (darsni o'zi "o'tildi" qiladi) va
    /// tozalash uchun (o'tilmagan darsda o'chiriladigan yozuv bo'lmaydi).
    /// </summary>
    public static async Task<string?> CheckAsync(
        IAppDbContext db, string classId, string subjectId, string date, int period,
        bool isAdmin, bool skipConducted = false)
    {
        var p = await GetAsync(db);
        if (isAdmin && !p.ApplyToAdmins) return null;

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (p.EditMode == ModeToday && string.CompareOrdinal(date, today) < 0)
            return "Jurnal sozlamasi: baho/davomat faqat BUGUNGI kun uchun kiritiladi — eski sanalar yopiq";
        if (p.EditMode == ModeWindow)
        {
            var min = AppClock.Today.AddDays(-p.RetroDays).ToString("yyyy-MM-dd");
            if (string.CompareOrdinal(date, min) < 0)
                return $"Jurnal sozlamasi: faqat oxirgi {p.RetroDays} kun ichidagi darslarga kiritish mumkin";
        }

        if (p.ConductedOnly && !skipConducted)
        {
            var conducted = await db.LessonNotes.AnyAsync(n =>
                n.ClassId == classId && n.SubjectId == subjectId &&
                n.Date == date && n.Period == period && n.Conducted);
            if (!conducted)
                return "Bu dars hali \"o'tildi\" deb belgilanmagan — avval davomat qiling (sana ustunini bosib), so'ng baho qo'yiladi";
        }
        return null;
    }

    // ---------- TO'LOV "DARVOZASI" (to'lamagan o'quvchi o'qituvchi jurnalida ko'rinmasin) ----------

    /// <summary>O'qituvchi jurnaliga yozishga urinilganda qaytariladigan standart taqiq xabari.</summary>
    public const string PaymentHiddenMessage =
        "O'quvchi to'lov qilmagani uchun jurnalda ko'rinmaydi — to'lovdan keyin qatori qaytadi";

    /// <summary>
    /// YAGONA QOIDA: shu o'quvchi to'lov holatiga ko'ra O'QITUVCHI jurnalida yashirilishi kerakmi.
    /// <para>Bu MUZLATISH EMAS — a'zolik ham, hisob-kitob ham odatdagidek davom etadi; faqat qator
    /// ko'rinmaydi va o'qituvchi unga yoza olmaydi. To'lov kelishi bilan qator O'Z-O'ZIDAN qaytadi
    /// (hech qanday qo'lda amal kerak emas).</para>
    /// <para>DIQQAT: qoida BUGUNGI holatga qarab ishlaydi, KO'RILAYOTGAN oyga emas — ya'ni qarzdor
    /// o'quvchi ESKI oy jurnalida ham ko'rinmaydi, to'lagach esa HAMMA oyda birdaniga qaytadi.
    /// Shu sabab <paramref name="currentMonth"/> va <paramref name="today"/> — joriy sana
    /// (<c>AppClock</c>), jurnal ochilgan oy emas.</para>
    /// <para>Ikkala sozlama ham o'chiq bo'lsa — HECH KIM yashirilmaydi (eski xatti-harakat).</para>
    /// </summary>
    /// <param name="p">Joriy siyosat (<see cref="GetAsync"/>).</param>
    /// <param name="b">Shu GURUH bo'yicha balans ma'lumoti (<see cref="GroupBalanceService.DetailedForGroupAsync"/>).</param>
    /// <param name="currentMonth">Joriy oy "yyyy-MM" (<c>TuitionService.CurrentMonth()</c>).</param>
    /// <param name="today">Bugungi kun raqami (1-31).</param>
    /// <returns><c>Hidden</c> — yashirilsinmi; <c>Reason</c> — "prevMonth" | "cutoff" | "".</returns>
    public static (bool Hidden, string Reason) PaymentGate(
        JournalPolicyDto p, GroupBalanceService.GroupBalanceInfo b, string currentMonth, int today)
    {
        // (1) O'TGAN oy(lar)dan qarz: eng eski qarz oyi joriy oydan OLDIN bo'lsa.
        if (p.HideUnpaidPrevMonth && b.OldestDebtMonth != ""
            && string.CompareOrdinal(b.OldestDebtMonth, currentMonth) < 0)
            return (true, "prevMonth");
        // (2) JORIY oy qarzi + belgilangan kun kelgan (yoki o'tgan).
        if (p.HideUnpaidAfterDay && b.DebtThisMonth && today >= p.UnpaidCutoffDay)
            return (true, "cutoff");
        return (false, "");
    }

    /// <summary>Shu guruhda to'lov "darvozasi" bo'yicha YASHIRILGAN o'quvchilar ro'yxati
    /// (o'qituvchi yozish yo'llari uchun). Ikkala sozlama ham o'chiq bo'lsa — bo'sh to'plam
    /// (ortiqcha so'rov ham qilinmaydi).</summary>
    public static async Task<HashSet<string>> PaymentHiddenStudentsAsync(
        IAppDbContext db, string classId, IEnumerable<string> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        if (ids.Count == 0) return hidden;
        var p = await GetAsync(db);
        if (!p.HideUnpaidPrevMonth && !p.HideUnpaidAfterDay) return hidden;

        var balances = await GroupBalanceService.DetailedForGroupAsync(db, classId, ids);
        var currentMonth = TuitionService.CurrentMonth();
        var today = AppClock.Today.Day;
        foreach (var id in ids)
        {
            var (isHidden, _) = PaymentGate(p, balances.GetValueOrDefault(id), currentMonth, today);
            if (isHidden) hidden.Add(id);
        }
        return hidden;
    }
}
