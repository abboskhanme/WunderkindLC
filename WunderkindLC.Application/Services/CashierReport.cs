using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KASSIR HISOBOTI — kim qancha pul qabul qilgan. Ikki joyda ishlatiladi:
/// <list type="bullet">
///   <item>Kassaning o'zida — kassir FAQAT o'zi kiritgan to'lovlarni va jamini ko'radi
///     (<c>GET /api/admin/kassa/my-payments</c>);</item>
///   <item>Moliya → "Kassirlar" — admin/superadmin barcha kassirlar kesimini va har birining
///     to'lovlarini ko'radi (<c>GET /api/admin/finance/cashiers</c>).</item>
/// </list>
///
/// <para>KIM KIRITGANI: yangi yozuvlarda <c>FinanceTransaction.CreatedById</c> (akkaunt id'si) bor;
/// ESKI yozuvlarda faqat <c>CreatedBy</c> (F.I.Sh) bo'lgani uchun kalit
/// <c>CreatedById ?? "name:"+CreatedBy</c> — ya'ni eski to'lovlar ham ism bo'yicha guruhlanadi va
/// hisobotdan tushib qolmaydi.</para>
///
/// <para>Nima hisoblanadi: FAQAT KIRIM (<c>Direction=="income"</c>) — pul kassaga tushgani.
/// Vozvrat (expense/refund) bu hisobotga kirmaydi.</para>
/// </summary>
public static class CashierReport
{
    /// <summary>Yozuv kimga tegishli ekanini bildiruvchi kalit (id bo'lsa id, aks holda ism).</summary>
    public static string KeyOf(string? createdById, string? createdBy) =>
        !string.IsNullOrEmpty(createdById) ? createdById : "name:" + (createdBy ?? "");

    /// <summary>Davr bo'yicha KASSIRLAR kesimi — har biri uchun soni, jami va usul bo'yicha yoyilma.
    ///
    /// <para>ESKI va YANGI yozuvlar BIR QATORGA birlashadi: id'siz (eski) yozuvning
    /// <c>CreatedBy</c> ismi bo'yicha akkaunt BIR DONA topilsa — o'sha akkaunt id'si kalit qilib
    /// olinadi. Aks holda bitta odam ro'yxatda ikki marta ("Ali" va id'li "Ali") chiqib qolardi.</para>
    /// </summary>
    public static async Task<List<CashierSummaryDto>> SummaryAsync(IAppDbContext db, string from, string to)
    {
        var rows = await IncomeQuery(db, from, to)
            .Select(t => new { t.CreatedById, t.CreatedBy, t.Amount, t.Method, t.CreatedAt })
            .ToListAsync();

        // Akkauntlar: id → F.I.Sh va (NOYOB bo'lsa) F.I.Sh → id.
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToListAsync();
        var nameById = users.ToDictionary(u => u.Id, u => u.FullName);
        var idByName = users.GroupBy(u => u.FullName).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Eski yozuvning ismi noyob akkauntga to'g'ri kelsa — o'sha akkaunt id'siga bog'laymiz.
        string Resolve(string? id, string? name) =>
            !string.IsNullOrEmpty(id) ? id
            : (!string.IsNullOrWhiteSpace(name) && idByName.TryGetValue(name, out var uid) ? uid : KeyOf(null, name));

        return rows
            .GroupBy(t => Resolve(t.CreatedById, t.CreatedBy))
            .Select(g =>
            {
                var isId = !g.Key.StartsWith("name:", StringComparison.Ordinal);
                var display = isId && nameById.TryGetValue(g.Key, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n
                    : (string.IsNullOrWhiteSpace(g.First().CreatedBy) ? "(noma'lum)" : g.First().CreatedBy!);
                return Summarize(
                    g.Key,
                    isId ? g.Key : null,
                    display,
                    g.Select(x => (x.Amount, x.Method, x.CreatedAt)));
            })
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    /// <summary>
    /// Bitta kassir kiritgan to'lovlar ro'yxati (davr bo'yicha, eng yangisi tepada) + o'sha kassirning
    /// jami ko'rsatkichlari. <paramref name="cashierId"/> null bo'lsa faqat ism bo'yicha (eski yozuvlar).
    /// </summary>
    public static async Task<CashierPaymentsDto> PaymentsAsync(
        IAppDbContext db, string from, string to, string? cashierId, string? cashierName)
    {
        var id = string.IsNullOrWhiteSpace(cashierId) ? null : cashierId;
        var name = cashierName ?? "";

        var txs = await IncomeQuery(db, from, to)
            // Id bo'yicha (yangi yozuvlar) YOKI id'siz eski yozuvlarda ism bo'yicha.
            .Where(t => (id != null && t.CreatedById == id)
                        || (t.CreatedById == null && t.CreatedBy == name))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Nomlar (o'quvchi / guruh → kurs, o'qituvchi) — N+1 bo'lmasin: bir martada lug'atga.
        var studentIds = txs.Select(t => t.StudentId).Where(x => x != null).Distinct().ToList();
        var groupIds = txs.Select(t => t.GroupId).Where(x => x != null).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);
        var groups = await db.Classes.AsNoTracking().Where(c => groupIds.Contains(c.Id)).ToListAsync();
        var courseIds = groups.Select(g => g.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courses = await db.Subjects.AsNoTracking().Where(s => courseIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName);

        var payments = txs.Select(t =>
        {
            var g = t.GroupId is null ? null : groups.FirstOrDefault(x => x.Id == t.GroupId);
            return new CashierPaymentDto(
                Id: t.Id,
                Date: t.Date,
                Amount: t.Amount,
                Method: t.Method,
                StudentName: t.StudentId is null ? "" : students.GetValueOrDefault(t.StudentId, ""),
                GroupName: g?.Name ?? "",
                CourseName: string.IsNullOrEmpty(g?.CourseId) ? "" : courses.GetValueOrDefault(g!.CourseId, ""),
                TeacherName: string.IsNullOrEmpty(g?.TeacherId) ? "" : teachers.GetValueOrDefault(g!.TeacherId, ""),
                Month: t.Month,
                ReceiptNo: t.ReceiptNo,
                CardLast4: t.CardLast4,
                PaidTime: t.PaidTime,
                // UTC saqlanadi — markaz mintaqasiga (UTC+5) o'tkazib beramiz (jadvaldagi soat to'g'ri chiqsin).
                CreatedAt: AppClock.ToLocal(t.CreatedAt).ToString("s"));
        }).ToList();

        var summary = Summarize(
            KeyOf(id, name), id,
            string.IsNullOrWhiteSpace(name) ? "(noma'lum)" : name,
            txs.Select(t => (t.Amount, t.Method, t.CreatedAt)));

        return new CashierPaymentsDto(summary, payments);
    }

    /// <summary>
    /// TO'LOV KIRITA OLADIGANLAR ro'yxati — Moliya → "To'lovlar" dagi <b>"Kiritgan"</b> filtri uchun.
    ///
    /// <para>Ro'yxatga KIM kiradi: (1) to'lov kirita oladigan HAR BIR akkaunt — admin/superadmin va
    /// <c>kassa</c> / <c>students</c> / <c>finance</c> bo'limiga yozish ruxsati berilgan xodim (davr
    /// ichida hali bitta ham to'lov kiritmagan bo'lsa ham ro'yxatda turadi, tanlansa bo'sh chiqadi);
    /// (2) davr ichida to'lov kiritgan, lekin endi bunday ruxsati yo'q yoki akkaunti o'chirilgan
    /// kishilar — aks holda ularning to'lovlarini filtrlab bo'lmasdi.</para>
    ///
    /// <para>Kalit — <see cref="KeyOf"/> bilan bir xil konvensiya, ya'ni "Kassirlar" tabidagi kalit
    /// bilan mos tushadi.</para>
    /// </summary>
    public static async Task<List<PaymentAuthorDto>> AuthorsAsync(IAppDbContext db, string from, string to)
    {
        // Faqat panelga kiradigan rollar (o'quvchi/ota-ona akkauntlari ko'p — ular kerak emas).
        var panelRoles = new[] { Roles.Admin, Roles.SuperAdmin, Roles.PlatformOwner, Roles.Staff };
        var accounts = await db.Users.AsNoTracking()
            .Where(u => panelRoles.Contains(u.Role))
            .Select(u => new { u.Id, u.FullName, u.Role, u.Position, u.Permissions })
            .ToListAsync();

        // To'lov yozish yo'llari: kassa portali ("kassa"), o'quvchi kartochkasi ("students") va
        // Moliya ("finance"). Yozish = POST → yalang bo'lim yoki "bo'lim:create" ruxsati.
        static bool CanEnter(string role, IEnumerable<string> perms) =>
            role is Roles.Admin or Roles.SuperAdmin or Roles.PlatformOwner ||
            perms.Any(p => p is "kassa" or "students" or "finance"
                        or "kassa:create" or "students:create" or "finance:create");

        var result = accounts
            .Where(a => CanEnter(a.Role, a.Permissions))
            .Select(a => new PaymentAuthorDto(a.Id, a.Id, a.FullName, a.Position ?? "", true))
            .ToList();
        var seen = result.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        // Davr ichida haqiqatan to'lov kiritganlar — ruxsati olib tashlangan/o'chirilganlar ham
        // ro'yxatda qolsin (eski, id'siz yozuvlar ism bo'yicha).
        var entered = await IncomeQuery(db, from, to)
            .Select(t => new { t.CreatedById, t.CreatedBy })
            .Distinct().ToListAsync();
        var nameById = accounts.ToDictionary(a => a.Id, a => a.FullName);
        // Eski (id'siz) yozuvning ismi NOYOB akkauntga to'g'ri kelsa — o'sha akkaunt bilan
        // birlashadi (aks holda bitta odam ro'yxatda ikki marta chiqardi — "Kassirlar" tabidagi
        // birlashtirish bilan bir xil qoida).
        var idByName = accounts.GroupBy(a => a.FullName).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        foreach (var e in entered)
        {
            var key = !string.IsNullOrEmpty(e.CreatedById) ? e.CreatedById!
                : (!string.IsNullOrWhiteSpace(e.CreatedBy) && idByName.TryGetValue(e.CreatedBy!, out var uid)
                    ? uid
                    : KeyOf(null, e.CreatedBy));
            if (key == "name:" || !seen.Add(key)) continue;
            var isId = !key.StartsWith("name:", StringComparison.Ordinal);
            var name = isId && nameById.TryGetValue(key, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : (string.IsNullOrWhiteSpace(e.CreatedBy) ? "(noma'lum)" : e.CreatedBy!);
            result.Add(new PaymentAuthorDto(key, isId ? key : null, name, "", false));
        }

        return result.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Davr filtri: sana "yyyy-MM-dd" matn bo'lgani uchun oddiy taqqoslash ishlaydi.</summary>
    private static IQueryable<FinanceTransaction> IncomeQuery(IAppDbContext db, string from, string to)
    {
        var q = db.FinanceTransactions.AsNoTracking().Where(t => t.Direction == "income");
        if (!string.IsNullOrWhiteSpace(from)) q = q.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) q = q.Where(t => string.Compare(t.Date, to) <= 0);
        return q;
    }

    /// <summary>Yig'indi qatorini yasaydi (usul bo'yicha yoyilma bilan).</summary>
    private static CashierSummaryDto Summarize(
        string key, string? cashierId, string cashierName,
        IEnumerable<(decimal Amount, string? Method, DateTime CreatedAt)> items)
    {
        var list = items.ToList();
        decimal ByMethod(string m) => list.Where(x => x.Method == m).Sum(x => x.Amount);
        var total = list.Sum(x => x.Amount);
        var cash = ByMethod("cash");
        var card = ByMethod("card");
        var bank = ByMethod("bank");
        return new CashierSummaryDto(
            Key: key,
            CashierId: cashierId,
            CashierName: cashierName,
            Count: list.Count,
            Total: total,
            Cash: cash,
            Card: card,
            Bank: bank,
            Other: total - cash - card - bank,
            LastAt: list.Count == 0 ? null : AppClock.ToLocal(list.Max(x => x.CreatedAt)).ToString("s"));
    }
}
