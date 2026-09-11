using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Maoshni JURNALGA bog'lash uchun hisob: har (oy, guruh) kesimida rejadagi darslar soni va ulardan
/// nechtasi jurnalda "o'tildi" (<see cref="LessonNote.Conducted"/>) deb belgilangani.
///
/// Sozlama: <see cref="CenterMeta.SalaryRequireJournal"/> (Guruhlar → "Jurnal boshqaruvi").
/// Yoqilgan bo'lsa <see cref="SalaryLedger"/> o'qituvchining shu oydagi maoshini
/// (belgilangan darslar ÷ rejadagi darslar) nisbatiga ko'paytiradi — belgilanmagan dars = o'tilmagan
/// dars, uning haqi to'lanmaydi.
///
/// Rejadagi darslar dars jadvalidan emas, guruh hafta kunlaridan (<see cref="Group.Days"/>) chiqariladi
/// (jurnal ustunlari ham shundan — <see cref="JournalService.LessonDatesInMonth"/>).
/// Faqat MUHLATI o'tgan darslar hisoblanadi: kelajakdagi va oxirgi
/// <see cref="CenterMeta.SalaryGraceDays"/> kun ichidagi darslarni o'qituvchi hali belgilashi mumkin.
/// </summary>
public static class SalaryJournalStats
{
    /// <summary>Guruhning dars kunlarini hisoblash uchun kerakli minimal ma'lumot.</summary>
    public sealed record GroupInfo(string Id, string Name, List<int> Days, string? StartDate, string? EndDate);

    /// <summary>Bitta (oy, guruh) uchun jurnal holati. Planned=0 bo'lsa ushlanma yo'q (dars muddati kelmagan).</summary>
    public sealed record Stat(int Planned, int Conducted, List<string> MissedDates)
    {
        public int Missed => MissedDates.Count;
        /// <summary>To'langan ulush: 1 = hamma dars belgilangan, 0 = birortasi ham belgilanmagan.</summary>
        public decimal Ratio => Planned <= 0 ? 1m : (decimal)Conducted / Planned;
    }

    /// <summary>
    /// (oy, guruhId) → jurnal holati. <paramref name="notBefore"/> — o'qituvchi ishga kirgan sana
    /// ("yyyy-MM-dd"); undan oldingi darslar rejaga kirmaydi.
    /// </summary>
    /// <param name="excludeDates">
    /// (guruhId → sanalar) — REJADAN CHIQARILADIGAN kunlar. Hozircha yagona manba:
    /// <b>o'rinbosar o'qituvchi qamragan darslar</b>.
    /// <para>⚠️ NEGA KERAK: o'rinbosar o'tgan dars ham guruhning rejasida turadi. U jurnalda
    /// belgilanmasa, asosiy o'qituvchi uni "o'tkazib yuborgan" hisoblanib jarimaga tortilardi —
    /// USTIGA o'sha darsning haqi o'rinbosarlik ushlanmasi sifatida ham ayrilgan bo'lardi.
    /// Ya'ni BITTA dars uchun IKKI marta jarima. Endi u kun asosiy o'qituvchining
    /// <c>Planned</c>/<c>Missed</c> sanog'iga umuman kirmaydi (u kuni dars bergan boshqa odam).</para>
    /// </param>
    public static async Task<Dictionary<(string Month, string GroupId), Stat>> BuildAsync(
        IAppDbContext db, IReadOnlyList<GroupInfo> groups,
        string startMonth, string toMonth, int graceDays, string? notBefore,
        IReadOnlyDictionary<string, HashSet<string>>? excludeDates = null)
    {
        var result = new Dictionary<(string, string), Stat>();
        if (groups.Count == 0) return result;

        var groupIds = groups.Select(g => g.Id).ToList();
        var fromDate = $"{startMonth}-01";
        var toDate = $"{toMonth}-31";

        // Jurnalda "o'tildi" belgilangan darslar (guruh + sana). Fan bo'yicha filtrlamaymiz —
        // bir guruh bitta kursga tegishli, sana ustuni esa kurs bilan bir xil.
        var conducted = await db.LessonNotes
            .Where(n => groupIds.Contains(n.ClassId) && n.Conducted
                        && string.Compare(n.Date, fromDate) >= 0 && string.Compare(n.Date, toDate) <= 0)
            .Select(n => new { n.ClassId, n.Date })
            .Distinct()
            .ToListAsync();
        var conductedSet = conducted.Select(c => (c.ClassId, c.Date)).ToHashSet();

        // Bir martalik ko'chirishlar — rejadagi darslar ham shuni hisobga oladi (ko'chirilgan dars o'tkazib
        // yuborilgan hisoblanmaydi: asl kun rejadan chiqadi, yangi kun kiradi).
        var movesByGroup = (await db.LessonReschedules.Where(r => groupIds.Contains(r.ClassId)).ToListAsync())
            .GroupBy(r => r.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList());
        var noMoves = new List<JournalService.LessonMove>();

        // Muhlat chegarasi: shu sanagacha (shu sana ham) bo'lgan darslar allaqachon belgilangan bo'lishi kerak.
        var cutoff = AppClock.Today.AddDays(-Math.Max(0, graceDays)).ToString("yyyy-MM-dd");

        foreach (var month in TuitionService.MonthRange(startMonth, toMonth))
        {
            foreach (var g in groups)
            {
                var planned = 0;
                var done = 0;
                var missed = new List<string>();
                var skip = excludeDates?.GetValueOrDefault(g.Id);
                foreach (var date in JournalService.EffectiveLessonDatesInMonth(g.Days, month, movesByGroup.GetValueOrDefault(g.Id, noMoves)))
                {
                    if (skip is not null && skip.Contains(date)) continue;             // o'rinbosar o'tgan dars
                    if (string.CompareOrdinal(date, cutoff) > 0) continue;             // hali muhlati kelmagan
                    if (notBefore is { Length: >= 10 } && string.CompareOrdinal(date, notBefore) < 0) continue;
                    if (g.StartDate is { Length: >= 10 } && string.CompareOrdinal(date, g.StartDate[..10]) < 0) continue;
                    if (g.EndDate is { Length: >= 10 } && string.CompareOrdinal(date, g.EndDate[..10]) > 0) continue;

                    planned++;
                    if (conductedSet.Contains((g.Id, date))) done++;
                    else missed.Add(date);
                }
                if (planned > 0) result[(month, g.Id)] = new Stat(planned, done, missed);
            }
        }
        return result;
    }
}
