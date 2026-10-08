using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// A'ZOLIK AMALIDAGI SABAB — yozish (<see cref="Record"/>) va «bu ketishning sababi nima?» degan
/// savolga javob (<see cref="Resolve"/>) — <b>YAGONA joy</b>. Batafsil: <c>.claude/rules/retention-loss.md</c>.
///
/// <para>Yozuvchilar (hammasi <c>ClassesController</c> da): guruhdan chiqarish, guruh almashtirish
/// (eski guruh tomoni), muzlatish (yakka va ommaviy), guruhni yopish. Audit yozuvi
/// (<c>audit.Record("Membership", …)</c>) avvalgidek yoziladi — bu jadval uning O'RNIGA emas,
/// YONIDA: audit — o'qiladigan tarix, bu — hisob-kitob uchun tuzilgan manba.</para>
/// </summary>
public static class MembershipReasons
{
    public const string ActionRemove = "remove";
    public const string ActionTransfer = "transfer";
    public const string ActionFreeze = "freeze";
    public const string ActionClose = "close";

    /// <summary>Sabab topilmagan ketishning yorlig'i (hisobot va diagrammada).</summary>
    public const string NoReasonLabel = "Sabab ko'rsatilmagan";

    /// <summary>«Tugatish (sertifikat bilan)» da KURSNI TUGATMAGAN (sinov/muzlatilgan) a'zoning
    /// chiqish sababi — tizim yozadi (<c>close</c> amali, <c>ReasonId</c> bo'sh). Usiz ular
    /// «Sabab ko'rsatilmagan» bo'lib, KPI'da noma'lum sabab jarimasiga tushardi.</summary>
    public const string CompletedNotFinishedLabel = "Guruh tugatildi — kursni tugatmagan (sinov/muzlatilgan)";

    /// <summary>
    /// Tanlangan sababni yozadi. Sabab tanlanmagan (yorliq bo'sh) bo'lsa — HECH NARSA yozilmaydi.
    /// <para><c>SaveChangesAsync</c> QILMAYDI — yozuv chaqiruvchining tranzaksiyasiga qo'shiladi
    /// (audit bilan bir xil naqsh), ya'ni amal muvaffaqiyatsiz bo'lsa sabab ham yozilmaydi.</para>
    /// </summary>
    public static void Record(
        IAppDbContext db, string studentId, string groupId, string action, string date,
        string? reasonId, string? reasonLabel)
    {
        var label = (reasonLabel ?? "").Trim();
        if (label.Length == 0 || string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(groupId)) return;
        db.MembershipReasonLogs.Add(new MembershipReasonLog
        {
            StudentId = studentId,
            GroupId = groupId,
            Action = action,
            Date = date.Length >= 10 ? date[..10] : date,
            ReasonId = string.IsNullOrWhiteSpace(reasonId) ? null : reasonId,
            ReasonLabel = label,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });
    }

    /* =========================================================================================
     *  SABABNI ANIQLASH — sof funksiya (testlangan: RetentionLossReportTests)
     * ====================================================================================== */

    /// <summary>Audit yozuvining kerakli qismi (<c>EntityType == "Membership"</c>).</summary>
    public readonly record struct AuditRow(string Timestamp, string Summary);

    /// <summary>O'quvchining arxiv holati — oxirgi zaxira manba.</summary>
    public readonly record struct ArchiveInfo(bool IsArchived, string? ArchivedAt, string? Reason);

    /// <summary>Topilgan sabab. <paramref name="Source"/>: <c>log</c> (tuzilgan yozuv) | <c>audit</c>
    /// (eski tarix — audit matnidan) | <c>archive</c> (o'quvchi arxiv sababi) | <c>""</c> (topilmadi).</summary>
    public readonly record struct Resolved(string? ReasonId, string Label, string Source);

    /// <summary>
    /// Bitta KETISHNING sababi. <paramref name="leftAt"/> — oraliqni yopgan a'zolikning chiqish sanasi,
    /// <paramref name="frozenAt"/> — o'sha a'zolikning muzlatish sanasi (bo'sh — muzlatilmagan).
    ///
    /// <para><b>Tartib</b> (birinchi topilgani olinadi):</para>
    /// <list type="number">
    /// <item>tuzilgan yozuv: chiqarish/almashtirish/yopish, sanasi AYNAN <paramref name="leftAt"/>.
    ///   ⚠️ U TIZIM sababi (<see cref="CompletedNotFinishedLabel"/>) bo'lsa va a'zolik chiqishda
    ///   muzlatilgan bo'lsa — avval MUZLATISH sababi (yozuv, keyin audit), tizim yorlig'i zaxira;</item>
    /// <item>a'zolik chiqish paytida MUZLATILGAN bo'lsa — muzlatish/yopish yozuvi, sanasi AYNAN
    ///   <paramref name="frozenAt"/> (muzlatilgan o'quvchi keyinroq sababsiz chiqarilgan holat:
    ///   haqiqiy sabab muzlatishda aytilgan);</item>
    /// <item>audit matni — shu jadvaldan OLDINGI tarix uchun: chiqishdan keyin yozilgan ENG OXIRGI
    ///   chiqarish/almashtirish yozuvi (u sababsiz bo'lsa ESKIROQ yozuvlarga QARALMAYDI — ular
    ///   oldingi a'zolik davriga tegishli), keyin xuddi shunday muzlatish yozuvi;</item>
    /// <item>o'quvchi chiqqandan keyin ARXIVLANGAN bo'lsa — arxiv sababi.</item>
    /// </list>
    /// ⚠️ Sana bo'yicha AYNAN moslik ATAYIN: bitta a'zolik qayta qo'shilib yana chiqarilishi mumkin —
    /// "eng oxirgi sababli yozuv" olinsa, bugungi sababsiz chiqishga o'tgan yilgi sabab yopishib qolardi.
    /// </summary>
    public static Resolved Resolve(
        string leftAt, string? frozenAt,
        IEnumerable<MembershipReasonLog> logs,
        IEnumerable<AuditRow> audits,
        ArchiveInfo archive)
    {
        var left = Day(leftAt);
        var frozen = Day(frozenAt);
        var frozenAtLeave = frozen.Length == 10 && left.Length == 10 && string.CompareOrdinal(frozen, left) <= 0;
        var logList = logs as IList<MembershipReasonLog> ?? logs.ToList();

        var auditList = audits as IList<AuditRow> ?? audits.ToList();

        // 1) Tuzilgan yozuv — chiqish amalining o'zi.
        var exit = Latest(logList.Where(l =>
            (l.Action is ActionRemove or ActionTransfer or ActionClose) && Day(l.Date) == left));
        if (exit is not null && !IsSystemLabel(exit)) return new Resolved(exit.ReasonId, exit.ReasonLabel, "log");

        // ⚠️ TIZIM sababi («Tugatish»da kursni tugatmagan) — faqat ZAXIRA (audit R1-4): a'zolik chiqishda
        // MUZLATILGAN bo'lsa HAQIQIY sabab muzlatishda aytilgan («Ko'chib ketdi» — nazoratdan tashqari).
        // Tizim yorlig'i birinchi topilsa u KPI'da NAZORATDAGI ketish bo'lib sanalar va hisobotda
        // haqiqiy sabab o'rniga ko'rinardi.
        if (exit is not null)
            return FreezeReason(logList, auditList, frozen, frozenAtLeave)
                   ?? new Resolved(exit.ReasonId, exit.ReasonLabel, "log");

        // 2) Tuzilgan yozuv — chiqishdan oldingi muzlatish.
        if (frozenAtLeave)
        {
            var freeze = Latest(logList.Where(l =>
                (l.Action is ActionFreeze or ActionClose) && Day(l.Date) == frozen));
            if (freeze is not null) return new Resolved(freeze.ReasonId, freeze.ReasonLabel, "log");
        }

        // 3) Eski tarix — audit matni.
        var exitAudit = auditList
            .Where(a => IsExitSummary(a.Summary) && string.CompareOrdinal(Day(a.Timestamp), left) >= 0)
            .OrderByDescending(a => a.Timestamp, StringComparer.Ordinal)
            .Select(a => (AuditRow?)a)
            .FirstOrDefault();
        if (exitAudit is { } ea && ReasonFromAuditSummary(ea.Summary) is { } r1)
            return new Resolved(null, r1, "audit");
        if (frozenAtLeave)
        {
            var freezeAudit = auditList
                .Where(a => IsFreezeSummary(a.Summary) && string.CompareOrdinal(Day(a.Timestamp), frozen) >= 0)
                .OrderByDescending(a => a.Timestamp, StringComparer.Ordinal)
                .Select(a => (AuditRow?)a)
                .FirstOrDefault();
            if (freezeAudit is { } fa && ReasonFromAuditSummary(fa.Summary) is { } r2)
                return new Resolved(null, r2, "audit");
        }

        // 4) O'quvchi chiqqandan keyin arxivlangan — arxiv sababi.
        if (archive.IsArchived && !string.IsNullOrWhiteSpace(archive.Reason)
            && string.CompareOrdinal(Day(archive.ArchivedAt), left) >= 0)
            return new Resolved(null, archive.Reason!.Trim(), "archive");

        return new Resolved(null, "", "");
    }

    /// <summary>Tizim yozgan sabab (admin tanlamagan): <see cref="CompletedNotFinishedLabel"/>, <c>ReasonId</c> bo'sh.</summary>
    private static bool IsSystemLabel(MembershipReasonLog l) =>
        string.IsNullOrEmpty(l.ReasonId) && l.ReasonLabel == CompletedNotFinishedLabel;

    /// <summary>Chiqishdan oldingi MUZLATISH sababi — tuzilgan yozuv, bo'lmasa (eski tarix) audit matni.
    /// Tizim yorlig'i hisobga olinmaydi. A'zolik chiqishda muzlatilmagan bo'lsa <c>null</c>.</summary>
    private static Resolved? FreezeReason(
        IList<MembershipReasonLog> logs, IList<AuditRow> audits, string frozen, bool frozenAtLeave)
    {
        if (!frozenAtLeave) return null;
        var freeze = Latest(logs.Where(l =>
            (l.Action is ActionFreeze or ActionClose) && Day(l.Date) == frozen && !IsSystemLabel(l)));
        if (freeze is not null) return new Resolved(freeze.ReasonId, freeze.ReasonLabel, "log");
        var freezeAudit = audits
            .Where(a => IsFreezeSummary(a.Summary) && string.CompareOrdinal(Day(a.Timestamp), frozen) >= 0)
            .OrderByDescending(a => a.Timestamp, StringComparer.Ordinal)
            .Select(a => (AuditRow?)a)
            .FirstOrDefault();
        return freezeAudit is { } fa && ReasonFromAuditSummary(fa.Summary) is { } r
            ? new Resolved(null, r, "audit")
            : null;
    }

    /// <summary>Audit izohidan «… — sabab: X» qismini ajratadi (sabab doim izohning OXIRIDA yoziladi).</summary>
    public static string? ReasonFromAuditSummary(string? summary)
    {
        if (string.IsNullOrEmpty(summary)) return null;
        const string marker = "sabab: ";
        var i = summary.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var value = summary[(i + marker.Length)..].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Chiqish amalining audit izohi (<c>RemoveMember</c> / <c>TransferMember</c>).</summary>
    private static bool IsExitSummary(string? s) =>
        s is not null && (s.StartsWith("Guruhdan chiqarildi", StringComparison.Ordinal)
                          || s.StartsWith("Guruh almashtirildi", StringComparison.Ordinal));

    /// <summary>Muzlatish amalining audit izohi (<c>FreezeCoreAsync</c>: oddiy va «aktiv muzlatish»).</summary>
    private static bool IsFreezeSummary(string? s) =>
        s is not null && (s.StartsWith("Muzlatildi", StringComparison.Ordinal)
                          || s.StartsWith("Aktiv muzlatildi", StringComparison.Ordinal));

    private static MembershipReasonLog? Latest(IEnumerable<MembershipReasonLog> rows) =>
        rows.OrderByDescending(l => l.CreatedAt, StringComparer.Ordinal).FirstOrDefault();

    private static string Day(string? iso) => iso is { Length: >= 10 } ? iso[..10] : iso ?? "";
}
