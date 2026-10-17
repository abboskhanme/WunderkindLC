using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CHIQIM TASDIG'I (SPEC-backlog-2026-10-17 F3 K2; egasining qarori: faqat katta summalar). Sof qoidalar.
///
/// <para><b>Qachon tasdiq kerak:</b> qo'lda kiritilgan CHIQIM (maosh to'lovi ham), summa sozlamadagi chegaradan
/// KATTA, kirituvchi superadmin emas (uning ustida tasdiqlaydigan odam yo'q). Chegara 0 — o'chiq.
/// Vozvrat (superadmin) va kitob qaytarish (avtomatik) bu yo'ldan o'tmaydi.</para>
///
/// <para><b>Kim tasdiqlaydi:</b> superadmin yoki <see cref="PermKey"/> ruxsati ANIQ berilgan xodim
/// (<c>AdminPermAttribute.IsSuperAdminOrGranted</c> — oddiy admin ham, yalang <c>finance</c> ham yetmaydi:
/// «belgilangan mas'ul»). ⚠️ Kiritgan odam O'ZI tasdiqlay olmaydi.</para>
/// </summary>
public static class ExpenseApproval
{
    /// <summary>Alohida (sahifasiz) ruxsat kaliti — <c>retentionBonus</c> naqshi: rol matritsasida bo'limga yig'ilib
    /// (ixchamlanib) yo'qolib qolmasin.</summary>
    public const string PermKey = "expenseApproval";

    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static bool NeedsApproval(decimal threshold, decimal amount, string direction, bool isSuperAdmin) =>
        direction == "expense" && threshold > 0 && amount > threshold && !isSuperAdmin;

    /// <summary>Tasdiqlash/rad etishni to'sadigan sabab (null — mumkin).</summary>
    public static string? DecisionBlock(ExpenseRequest r, string? userId) =>
        r.Status != Pending ? "Bu so'rov allaqachon ko'rib chiqilgan"
        : !string.IsNullOrEmpty(r.CreatedById) && r.CreatedById == userId
            ? "O'zingiz kiritgan chiqimni o'zingiz tasdiqlay olmaysiz — boshqa mas'ul tasdiqlaydi"
        // O'z maoshini o'zi tasdiqlamaydi (2026-10-17, F3 revyu).
        : !string.IsNullOrEmpty(r.StaffUserId) && r.StaffUserId == userId
            ? "O'zingizga beriladigan maoshni o'zingiz tasdiqlay olmaysiz — boshqa mas'ul tasdiqlaydi"
            : null;

    /// <summary>Maosh to'lovi turi: <c>advance</c> | <c>final</c> | bo'sh. Boshqa qiymat — null bilan rad.</summary>
    public static bool TryPayKind(string? raw, out string? kind)
    {
        kind = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
        return kind is null or "advance" or "final";
    }

    public static string PayKindLabel(string? kind) => kind switch
    {
        "advance" => "avans",
        "final" => "yakuniy",
        _ => "",
    };

    /// <summary>Tasdiqlangan so'rovdan tranzaksiya — maydonlar so'rovdagidek; kiritgan odam — so'rov egasi.
    /// <para>⚠️ SANA — TASDIQ kuni (<paramref name="approvalDate"/>), so'rov sanasi EMAS (2026-10-17, F3 revyu): pul
    /// kassadan aynan shu kuni chiqadi. So'rov sanasi qo'yilsa o'tgan kunning kunlik hisoboti orqaga o'zgarar, oradagi
    /// kassa boshlang'ich qoldig'i sanasidan oldin qolsa esa chiqim qoldiqdan jimgina tushib qolardi. So'rov sanasi
    /// izohda qoladi; maosh OYI (<c>Month</c>) o'zgarmaydi.</para></summary>
    public static FinanceTransaction ToTransaction(ExpenseRequest r, string? cashboxId, string approvalDate) => new()
    {
        Date = approvalDate,
        Direction = "expense",
        Category = r.Category,
        TypeId = r.TypeId,
        Amount = r.Amount,
        Note = r.Date == approvalDate || string.IsNullOrWhiteSpace(r.Date)
            ? r.Note
            : $"{r.Note}{(string.IsNullOrWhiteSpace(r.Note) ? "" : " ")}[so'rov sanasi: {r.Date}]",
        Comment = r.Comment,
        TeacherId = r.TeacherId,
        StaffUserId = r.StaffUserId,
        Month = r.Month,
        Method = r.Method,
        SalaryPayKind = r.SalaryPayKind,
        CashboxId = cashboxId,
        CreatedBy = r.CreatedBy,
        CreatedById = r.CreatedById,
        ExpenseRequestId = r.Id,
    };
}
