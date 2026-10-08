using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINI ARXIVLASH = O'QUVCHI KETDI — a'zoliklar tomoni (<b>yagona joy</b>).
/// Batafsil: <c>.claude/rules/membership-periods.md</c> §9.5 va <c>retention-loss.md</c> §1.
///
/// <para>Ilgari arxivlash faqat <c>Student.IsArchived = true</c> qo'yardi — a'zoliklar TIRIK qolardi.
/// Oqibati: arxivdagi o'quvchi jurnalda, guruh SMS'larida, o'rin sanog'ida turar, "darsga kelmadi"
/// xabarlari ota-onaga ketaverar, eng yomoni — arxivdan qaytarilganda <c>AccrueDue</c> (startup +
/// har 12 soat butun tarixni skanerlaydi) arxivda o'tgan HAR oyni birdaniga hisoblab, har oy uchun
/// alohida "oylik hisoblandi" SMS yuborardi.</para>
///
/// <para>Endi arxivlashda HAR tirik a'zolik «Guruhdan chiqarish» (<c>ClassesController.RemoveMember</c>)
/// bilan AYNAN bir xil yopiladi: <see cref="MembershipBilling.LeaveGroupAsync"/> (aktivda chiqish
/// oyi qisman, keyingi oylar bekor), sabab yozuvi (<see cref="MembershipReasons"/>, arxiv sababi bilan),
/// a'zolik audit qatori va «asosiy guruh» yorlig'ini bo'shatish. Arxivdan qaytarish a'zoliklarni
/// TIKLAMAYDI — o'quvchi guruhsiz qaytadi, admin kerakli guruhga qo'shadi.</para>
///
/// <para><c>SaveChanges</c> QILMAYDI. Pul o'zgaradi — chaqiruvchi o'quvchi qulfini
/// (<see cref="PaymentIdempotency.LockAsync"/>, <c>"student:{id}"</c>) olib, balansni qulf ICHIDA qayta
/// o'qigan bo'lishi shart (RemoveMember bilan bir xil).</para>
/// </summary>
public static class StudentArchiving
{
    /// <summary>Arxivdan qaytarishdan keyin foydalanuvchiga aytiladigan matn (a'zoliklar tiklanmaydi).</summary>
    public const string RestoredWithoutGroupsMessage = "O'quvchi guruhsiz tiklandi — kerakli guruhga qo'shing";

    /// <summary>Yopilgan a'zoliklar natijasi.</summary>
    /// <param name="Closed">Nechta tirik a'zolik yopildi.</param>
    /// <param name="Restored">Keyingi oylar hisobi bekor qilinib balansga qaytgan jami summa.</param>
    /// <param name="GroupNames">Yopilgan a'zoliklar guruhlari (audit/izoh uchun).</param>
    public sealed record LeaveResult(int Closed, decimal Restored, IReadOnlyList<string> GroupNames);

    /// <summary>
    /// A'zolikning chiqish sanasi: <paramref name="leftAt"/>, lekin a'zolik QO'SHILGAN kundan oldin
    /// emas (eski arxivni orqaga yopishda arxivdan KEYIN qo'shilgan a'zolik teskari oraliq —
    /// <c>LeftAt &lt; JoinedAt</c> — bo'lib qolmasin). Qo'shilish sanasi KELAJAKDA bo'lsa (hali
    /// boshlanmagan a'zolik) chegara qo'yilmaydi — chiqish sanasi kelajakka surilmaydi (RemoveMember
    /// ham kelajak sanani qabul qilmaydi). Sof funksiya.
    /// </summary>
    public static string LeaveDateFor(StudentGroup m, string leftAt, string? today = null)
    {
        var t = today ?? AppClock.Today.ToString("yyyy-MM-dd");
        var joined = m.JoinedAt is { Length: >= 10 } j && DateOnly.TryParse(j[..10], out _) ? j[..10] : "";
        return joined.Length == 10
               && string.CompareOrdinal(joined, leftAt) > 0
               && string.CompareOrdinal(joined, t) <= 0
            ? joined : leftAt;
    }

    /// <summary>
    /// Arxiv sanasi: <see cref="Student.ArchivedAt"/> haqiqiy sana bo'lsa o'sha, aks holda
    /// <paramref name="fallback"/> (bugun). Sof funksiya.
    /// </summary>
    public static string ArchiveDateOr(Student s, string fallback) =>
        s.ArchivedAt is { Length: >= 10 } a && DateOnly.TryParse(a[..10], out _) ? a[..10] : fallback;

    /// <summary>
    /// O'quvchining BARCHA tirik a'zoliklarini <paramref name="leftAt"/> sanasida "guruhdan chiqdi"
    /// qiladi — <c>RemoveMember</c> bilan bir xil natija (har a'zolik uchun hisob, sabab, audit).
    /// </summary>
    /// <param name="reasonId">Arxiv sababi (<see cref="ActionReason"/>) id'si — bo'lsa.</param>
    /// <param name="reasonLabel">Arxiv sababi matni (katalogdan yoki erkin) — bo'sh bo'lsa sabab yozuvi
    /// QO'SHILMAYDI (<see cref="MembershipReasons.Record"/> qoidasi).</param>
    /// <param name="note">Audit izohidagi kontekst ("o'quvchi arxivlandi" / "eski arxiv — tiklashda").</param>
    /// <param name="purgeInactiveAfter"><c>true</c> — ESKI arxiv (bu tuzatishdan oldin arxivlangan)
    /// yopilyapti: muzlatilgan/sinovdagi a'zolikda ham arxiv oyidan KEYINGI hisoblar bekor qilinadi.
    /// Sabab: o'quvchi arxivda turganda guruh yopilsa (yoki arxivlansa) a'zolik o'sha sanada
    /// muzlatilib, arxivdagi o'quvchiga qisman oylik yozilgan bo'lishi mumkin — u hech qachon
    /// o'qimagan oy. Yangi arxivlashda (bugungi sana) bunday hisob bo'lmaydi va bayroq kerak emas.</param>
    public static async Task<LeaveResult> LeaveAllGroupsAsync(
        IAppDbContext db, AuditService audit, Student s, string leftAt,
        string? reasonId, string? reasonLabel, string note, bool purgeInactiveAfter = false,
        CancellationToken ct = default)
    {
        var live = await db.StudentGroups
            .Where(sg => sg.StudentId == s.Id && sg.IsActive)
            .ToListAsync(ct);
        if (live.Count == 0)
        {
            // A'zoligi UMUMAN yo'q (eski, guruhsiz) o'quvchida guruh = faqat yorliq: u ham bo'shatiladi.
            // ⚠️ Aks holda arxivdan qaytarilganda `AccrueMonth` ning guruhsiz shoxobchasi yorliq
            // narxi bilan arxivda o'tgan HAR oyni yozardi (u faqat EnrollmentDate'ga qaraydi).
            s.ClassName = "";
            return new LeaveResult(0, 0m, Array.Empty<string>());
        }

        var groupIds = live.Select(m => m.GroupId).Distinct().ToList();
        var groups = await db.Classes.Where(c => groupIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var label = (reasonLabel ?? "").Trim();

        var restoredTotal = 0m;
        var names = new List<string>();
        foreach (var m in live.OrderBy(x => x.JoinedAt, StringComparer.Ordinal))
        {
            var date = LeaveDateFor(m, leftAt);
            var status = m.Status;
            groups.TryGetValue(m.GroupId, out var g);
            var restored = 0m;
            if (g is not null)
            {
                // YAGONA yo'l — RemoveMember/TransferMember bilan bir xil (aktivda hisob yopiladi,
                // sinov/muzlatilganda pulga tegilmaydi, a'zolik IsActive=false + LeftAt).
                restored = (await MembershipBilling.LeaveGroupAsync(db, s, g, m, date)).Restored;
                if (purgeInactiveAfter && status != "active")
                    restored += (await TuitionService.PurgeChargesAfterMonthAsync(db, s, g.Id, date)).Restored;
            }
            else
            {
                // Guruhning o'zi o'chirilgan (yetim a'zolik) — hisob yo'q, faqat chiqish.
                m.IsActive = false;
                m.LeftAt = date;
            }
            restoredTotal += restored;
            var groupName = g?.Name ?? m.GroupId;
            names.Add(groupName);

            // Tuzilgan sabab — «Ketish sabablari» hisoboti (arxiv sababi = ketish sababi).
            MembershipReasons.Record(db, s.Id, m.GroupId, MembershipReasons.ActionRemove, date, reasonId, label);
            var statusLabel = status == "active" ? "aktiv" : status == "frozen" ? "muzlatilgan" : "sinovdagi";
            // ⚠️ "Guruhdan chiqarildi" PREFIKSI va "— sabab: X" OXIRIDA — KPI va AI tahlil quruvchilari
            // sababni audit matnidan shu naqsh bilan o'qiydi (`MembershipReasons.ReasonFromAuditSummary`).
            audit.Record("Membership", $"{m.GroupId}:{s.Id}", "delete",
                $"Guruhdan chiqarildi ({statusLabel}, {date}, guruh: {groupName}) — {note}"
                    + (restored > 0 ? $" — keyingi oylar hisobi bekor qilindi: {AuditService.Money(restored)} so'm" : "")
                    + (label.Length > 0 ? $" — sabab: {label}" : ""),
                studentId: s.Id);
        }

        // «Asosiy guruh» yorlig'i — tirik guruhi qolmadi, demak bo'sh (ReleasePrimaryGroupNameAsync
        // naqshi). ⚠️ Yorliq qolsa guruh chatida/portalda "qolib ketardi" va (a'zoligi umuman yo'q
        // o'quvchida) eski guruhsiz billing yorliqqa qarab qayta ishga tushardi.
        s.ClassName = "";
        return new LeaveResult(live.Count, restoredTotal, names);
    }

    /// <summary>
    /// Mavjud QATORLARDAN chiqadigan balans: tirik o'quv to'lovlari (kirim/tuition) − vozvratlar
    /// (chiqim/refund) − qolgan oylik hisoblarning effektiv summasi. Bekor qilingan pul amallari
    /// global filtr bilan chiqib ketadi. <c>FinanceController.StudentBalanceEffect</c> bilan bir xil
    /// qoida (balansga faqat shu ikki tur ta'sir qiladi).
    /// </summary>
    public static async Task<decimal> BalanceFromRowsAsync(IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var paid = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId && t.Direction == "income" && t.Category == "tuition")
            .Select(t => t.Amount).ToListAsync(ct);
        var refunded = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId && t.Direction == "expense" && t.Category == "refund")
            .Select(t => t.Amount).ToListAsync(ct);
        var charged = await db.MonthlyCharges.AsNoTracking()
            .Where(c => c.StudentId == studentId)
            .Select(c => new { c.Amount, c.Discount }).ToListAsync(ct);
        return paid.Sum() - refunded.Sum() - charged.Sum(c => Math.Max(0m, c.Amount - c.Discount));
    }

    /// <summary>
    /// BUTUNLAY O'CHIRILGAN o'quvchini «Arxiv (barcha turdagi)» dan tiklashdagi balans (MEM-5).
    /// O'chirishda (<c>StudentsController.Delete</c>) oylik hisoblar va a'zoliklar HAM o'chadi, surat esa
    /// eski balansni saqlaydi — ya'ni u endi mavjud bo'lmagan hisoblarni o'z ichiga oladi.
    /// <list type="bullet">
    ///   <item>QARZ faqat qolgan qatorlar uni TUSHUNTIRSA qoladi: suratdagi qarz o'chgan hisoblardan —
    ///     usiz "soxta qarz" (va unga to'lov eslatmasi SMS'i) chiqardi;</item>
    ///   <item>AVANS suratdagidan OSHMAYDI: to'lovlar qoladi, ular "yegan" hisoblar esa o'chgan — qatorlardan
    ///     hisoblansa butun to'langan pul avansga aylanib, keyingi oylarni "tekin" qilardi.</item>
    /// </list>
    /// Formula: <c>min(max(surat, 0), qatorlar)</c>. Sof funksiya.
    /// </summary>
    public static decimal RestoredBalance(decimal snapshot, decimal fromRows) =>
        Math.Min(Math.Max(snapshot, 0m), fromRows);

    /// <summary>
    /// ESKI ARXIV — bu tuzatishdan OLDIN arxivlangan o'quvchida a'zoliklar hamon tirik. Arxivdan
    /// qaytarishdan OLDIN ular ARXIV SANASIDA yopiladi (orqaga sanalgan chiqish): aks holda arxivda
    /// o'tgan oylar qaytarilishi bilanoq hisoblanib, har oy uchun SMS ketardi. Tirik a'zolik bo'lmasa
    /// hech narsa qilmaydi. <c>SaveChanges</c> QILMAYDI.
    /// </summary>
    public static Task<LeaveResult> CloseLegacyMembershipsAsync(
        IAppDbContext db, AuditService audit, Student s, CancellationToken ct = default) =>
        LeaveAllGroupsAsync(db, audit, s,
            ArchiveDateOr(s, AppClock.Today.ToString("yyyy-MM-dd")),
            reasonId: null, reasonLabel: s.ArchiveReason,
            note: "eski arxiv (a'zolik arxiv sanasida yopildi)", purgeInactiveAfter: true, ct: ct);
}
