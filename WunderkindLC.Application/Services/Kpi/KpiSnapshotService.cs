using System.Globalization;
using System.Text.Json;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// OY BOSHIDAGI SURAT (<see cref="KpiMonthSnapshot"/>) — fon xizmati.
///
/// <para>Ba'zi raqamlarni ORQAGA TIKLAB BO'LMAYDI: "oy boshida nechta FAOL o'quvchi bor edi",
/// "qarz qoldig'i qancha edi". O'quvchi ketadi, qarz to'lanadi, a'zolik muzlaydi — jonli hisob
/// har kuni boshqa son beradi. Chiquvchi adminning ketish foizi esa AYNAN shu songa bo'linadi,
/// ya'ni oy oxirida hisoblangan "oy boshi" soxta bo'lardi.</para>
///
/// <para>Shuning uchun har oyning 1-kuni, yarim tundan keyin surat olinadi (server o'chgan
/// bo'lsa — startupda ham). Kechikkan surat baribir taxminiy bo'lgani uchun catch-up oyning
/// faqat DASTLABKI kunlarida ishlaydi (<see cref="MaxCatchUpDay"/>); undan keyin surat
/// YOZILMAYDI va hisob "snapshot yo'q → taxminiy" bayrog'i bilan jonli hisobga tushadi
/// (soxta aniqlikdan ko'ra ochiq taxmin yaxshi).</para>
///
/// <para>⚠️ «Faol o'quvchi» ta'rifi bu yerda QAYTA YOZILMAYDI — <see cref="KpiActiveStudentRule"/>
/// chaqiriladi. Oy boshi va oy oxiri bir xil qoida bilan sanalishi SHART.</para>
/// </summary>
public class KpiSnapshotService(IServiceScopeFactory scopes, ILogger<KpiSnapshotService> logger)
    : BackgroundService
{
    /// <summary>Oyning shu kunigacha kechikkan surat olinishi mumkin (keyin — taxmin, yozilmaydi).</summary>
    public const int MaxCatchUpDay = 5;

    /// <summary>Tekshiruv oralig'i: 1-kuni yarim tundan keyin surat yarim soat ichida olinadi.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = AppClock.Today;
                if (today.Day <= MaxCatchUpDay)
                {
                    var month = today.ToString("yyyy-MM");
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

                    // Surat allaqachon olingan bo'lsa TEGILMAYDI — "oy boshi" surati oyning
                    // birinchi soatlaridagi holat bo'lib qolishi kerak, kun davomida qayta
                    // hisoblanib surilib ketmasin. Qayta olish faqat QO'LDA (admin endpointi).
                    var already = await db.KpiMonthSnapshots.AnyAsync(s => s.Month == month, stoppingToken);
                    if (!already)
                    {
                        var rows = await TakeAsync(db, month, stoppingToken);
                        if (rows > 0)
                            logger.LogInformation("[kpi] {Month} oyi uchun {Rows} ta surat olindi", month, rows);
                    }
                }
            }
            catch (Exception ex)
            {
                // ⚠️ Bitta xato siklni O'LDIRMAYDI: keyingi urinishda (30 daqiqadan keyin)
                // qayta uriniladi — migratsiya hali qo'llanmagan bo'lsa ham normal.
                logger.LogError(ex, "[kpi] Oy boshi suratini olishda xatolik");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /* =========================================================================================
     *  SURAT OLISH (admin "hozir surat ol" endpointi ham SHUNI chaqiradi)
     * ========================================================================================= */

    /// <summary>
    /// <paramref name="month"/> ("yyyy-MM") uchun suratlarni yozadi: bitta MARKAZ qatori
    /// (<c>UserId = ""</c>) va har FAOL <see cref="KpiProfile"/> uchun bittadan.
    /// </summary>
    /// <returns>Nechta qator YOZILDI (qo'shildi yoki yangilandi).</returns>
    /// <remarks>
    /// ⚠️ <b>IDEMPOTENT.</b> Unikal indeks <c>(Month, RoleCode, UserId)</c> — shuning uchun
    /// avval qidiriladi, keyin yangilanadi. Ko'r-ko'rona <c>Add</c> qilinsa, 1-kuni server
    /// qayta ishga tushishi bilan <c>23505</c> bilan yiqilardi.
    ///
    /// ⚠️ <b>O'TGAN OY SURATI HECH QACHON QAYTA YOZILMAYDI</b> — modulning butun ma'nosi
    /// o'sha paytdagi holatni MUZLATIB qo'yishda. Joriy oyniki esa faqat AYNAN BUGUN olingan
    /// bo'lsa yangilanadi (ya'ni "bugun ikki marta bosildi" holati), kechagisi emas.
    ///
    /// ⚠️ Hisob bir marta ishlaydigan OYLIK ish bo'lgani uchun ma'lumot xotiraga to'plab
    /// o'qiladi (N+1 yo'q, lekin so'rovlar keng) — bu ATAYIN: har o'quvchi uchun alohida
    /// so'rov minglab so'rovga aylanardi.
    /// </remarks>
    public static async Task<int> TakeAsync(IAppDbContext db, string month, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(month) || month.Length < 7) return 0;
        month = month[..7];
        if (!DateOnly.TryParse($"{month}-01", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
            return 0;

        var currentMonth = AppClock.Today.ToString("yyyy-MM");
        var todayIso = AppClock.Today.ToString("yyyy-MM-dd");

        var (activeCount, debtTotal) = await CentreValuesAsync(db, asOf, ct);

        var existing = await db.KpiMonthSnapshots.Where(s => s.Month == month).ToListAsync(ct);
        var profiles = await db.KpiProfiles.Where(p => p.IsActive).ToListAsync(ct);

        var written = 0;

        // (1) MARKAZ qatori. Roli — chiquvchi admin: raqamlar butun bazaga tegishli va aynan
        // uning bonusi ostida turadi (spetsifikatsiya §17.1: bo'sh UserId = markaz darajasi).
        written += Upsert(db, existing, month, KpiConst.Retention, "", currentMonth, todayIso,
            new Dictionary<string, double>
            {
                ["activeCount"] = activeCount,
                ["debtTotal"] = (double)debtTotal,
            });

        // (2) Har faol profil uchun. Kiruvchi admin va operatorda oy BOSHIDA tiklab bo'lmaydigan
        // raqam YO'Q (lid/sinov/shartnoma sanasi bo'yicha keyin ham sanaladi) — ularning qatori
        // bo'sh qiymatlar bilan yoziladi va faqat "shu oyda surat olingan" dalili bo'lib xizmat
        // qiladi. Chiquvchi adminga esa markaz raqamlari nusxalanadi, chunki hisob (Month sahifasi)
        // suratni AYNAN (oy, rol, xodim) bo'yicha qidiradi.
        foreach (var p in profiles)
        {
            if (string.IsNullOrEmpty(p.UserId)) continue;
            var values = p.RoleCode == KpiConst.Retention
                ? new Dictionary<string, double>
                {
                    ["activeCount"] = activeCount,
                    ["debtTotal"] = (double)debtTotal,
                }
                : new Dictionary<string, double>();

            written += Upsert(db, existing, month, p.RoleCode, p.UserId, currentMonth, todayIso, values);
        }

        if (written > 0) await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>Bitta qatorni qo'shadi yoki (ruxsat etilgan holatda) yangilaydi.</summary>
    private static int Upsert(IAppDbContext db, List<KpiMonthSnapshot> existing, string month,
        string roleCode, string userId, string currentMonth, string todayIso,
        Dictionary<string, double> values)
    {
        var json = JsonSerializer.Serialize(values);
        var row = existing.FirstOrDefault(s => s.RoleCode == roleCode && s.UserId == userId);

        if (row is null)
        {
            var created = new KpiMonthSnapshot
            {
                Month = month,
                RoleCode = roleCode,
                UserId = userId,
                Json = json,
            };
            db.KpiMonthSnapshots.Add(created);
            existing.Add(created);   // shu chaqiruv ichida ikkinchi marta qo'shilmasin
            return 1;
        }

        // O'tgan (yoki kelajakdagi) oy — MUZLATILGAN.
        if (!string.Equals(month, currentMonth, StringComparison.Ordinal)) return 0;
        // Joriy oy, lekin surat BOSHQA kuni olingan — u "oy boshi" holati, tegilmaydi.
        if (!row.TakenAt.StartsWith(todayIso, StringComparison.Ordinal)) return 0;

        row.Json = json;
        row.TakenAt = AppClock.Iso();
        return 1;
    }

    /* =========================================================================================
     *  MARKAZ RAQAMLARI
     * ========================================================================================= */

    /// <summary>
    /// <paramref name="asOf"/> sanasidagi FAOL o'quvchilar soni va umumiy QARZ.
    /// </summary>
    /// <remarks>
    /// ⚠️ A'zolik holati (<c>Status == "active" &amp;&amp; IsActive</c>) JONLI o'qiladi — u tarixi
    /// bilan saqlanmaydi. AYNAN shuning uchun surat oyning 1-kuni olinadi: kechikkan surat
    /// o'sha kungi a'zoliklarni "oy boshi" deb yozib qo'yardi.
    ///
    /// ⚠️ Arxivlangan o'quvchi na sanoqqa, na qarzga kiradi: uning qarzini chiquvchi admin
    /// yig'a olmaydi, ya'ni uni "yig'ilishi kerak bo'lgan qarz"ga qo'shish bonus C ning
    /// maxrajini soxta kattalashtirardi.
    /// </remarks>
    private static async Task<(int ActiveCount, decimal DebtTotal)> CentreValuesAsync(
        IAppDbContext db, DateOnly asOf, CancellationToken ct)
    {
        var asOfIso = asOf.ToString("yyyy-MM-dd");
        var asOfMonth = asOf.ToString("yyyy-MM");

        var students = await db.Students
            .Select(s => new { s.Id, s.IsArchived })
            .ToListAsync(ct);

        var activeMembers = (await db.StudentGroups
                .Where(sg => sg.Status == "active" && sg.IsActive)
                .Select(sg => sg.StudentId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // Oxirgi "kelgan" jurnal yozuvi (sanaga qadar).
        var lastPresent = (await db.JournalEntries
                .Where(j => j.Present && j.Date != "" && string.Compare(j.Date, asOfIso) <= 0)
                .GroupBy(j => j.StudentId)
                .Select(g => new { StudentId = g.Key, Last = g.Max(x => x.Date) })
                .ToListAsync(ct))
            .ToDictionary(x => x.StudentId, x => x.Last, StringComparer.Ordinal);

        // Hisoblar va to'lovlar — qarz va "eng eski to'lanmagan oy" uchun.
        var charges = (await db.MonthlyCharges
                .Select(c => new { c.StudentId, c.Month, c.Amount, c.Discount, c.Date })
                .ToListAsync(ct))
            .Where(c => c.Month.Length >= 7 && string.CompareOrdinal(c.Month[..7], asOfMonth) <= 0)
            .GroupBy(c => c.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Month, StringComparer.Ordinal).ToList(),
                          StringComparer.Ordinal);

        var paid = (await db.FinanceTransactions
                .Where(t => t.StudentId != null && t.Date != ""
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .Select(t => new { t.StudentId, t.Direction, t.Amount, t.Date })
                .ToListAsync(ct))
            .Where(t => string.CompareOrdinal(t.Date, asOfIso) <= 0)
            .GroupBy(t => t.StudentId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(t => t.Direction == "income" ? t.Amount : -t.Amount),
                StringComparer.Ordinal);

        var activeCount = 0;
        var debtTotal = 0m;

        foreach (var s in students)
        {
            if (s.IsArchived) continue;

            var owed = 0m;
            string? oldestUnpaidDue = null;
            if (charges.TryGetValue(s.Id, out var rows))
            {
                var settled = paid.GetValueOrDefault(s.Id, 0m);
                var running = 0m;
                foreach (var c in rows)
                {
                    var due = Math.Max(0m, c.Amount - c.Discount);
                    if (due <= 0m) continue;
                    running += due;
                    // FIFO: to'lovlar eski oylardan boshlab yopiladi — birinchi YOPILMAGAN
                    // oyning muddati "eng eski qarz" bo'ladi.
                    if (oldestUnpaidDue is null && running > settled)
                        oldestUnpaidDue = string.IsNullOrEmpty(c.Date) ? $"{c.Month[..7]}-01" : c.Date;
                }
                owed = running;
                debtTotal += Math.Max(0m, owed - settled);
            }

            if (KpiActiveStudentRule.IsActive(
                    hasActiveMembership: activeMembers.Contains(s.Id),
                    lastPresentDate: lastPresent.GetValueOrDefault(s.Id),
                    oldestUnpaidDueDate: oldestUnpaidDue,
                    isArchived: s.IsArchived,
                    asOf: asOf))
                activeCount++;
        }

        return (activeCount, debtTotal);
    }
}
