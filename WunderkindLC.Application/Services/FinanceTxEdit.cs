using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Moliya → «Amallar» dagi UMUMIY tahrir (<c>PUT api/admin/finance/transactions/{id}</c>) qoidalari — sof
/// funksiyalar (testlangan: <c>AuditFixT3Tests</c>). Controller faqat qulf, audit va saqlashni qiladi.
///
/// <para>⚠️ <b>Pulga tegadigan ikki xil qator umumiy tahrirdan O'TMAYDI</b> (2026-10-07):</para>
/// <list type="bullet">
///   <item><b>O'quvchi to'lovi</b> (kirim + <c>tuition</c> + o'quvchi) — FAQAT «To'lovlar» dagi tahrir
///     (<c>PUT payments/{id}</c>, superadmin): u balans, oy, guruh, avans hisobi va vozvrat chegarasini
///     birga moslaydi. Umumiy forma o'quvchini yubormas edi va server <c>StudentId</c> ni bo'shatib,
///     to'lovni balansdan olib tashlardi.</item>
///   <item><b>Vozvrat</b> (chiqim + <c>refund</c>) — faqat XAVFSIZ maydonlar: sana, izoh, kassir izohi.
///     Ilgari tahrir <c>StudentId</c> ni bo'shatib, qaytarilgan pulni o'quvchi balansiga QAYTARIB berardi
///     (kassadan esa pul chiqib ketgan edi), turi esa forma zaxirasi bilan ("Arenda") almashardi.
///     Summa/tur kerak bo'lsa — vozvrat bekor qilinib, to'lovdan qaytadan qaytariladi.</item>
/// </list>
/// <para>Qolgan qatorlarda (kirim/chiqim/maosh/jarima) forma yubormagan bog'lanishlar SAQLANADI:
/// toifa o'zgarmasa <c>StudentId</c>/<c>TeacherId</c>/<c>TypeId</c> jimgina o'chirilmaydi.</para>
/// </summary>
public static class FinanceTxEdit
{
    /// <summary>Tranzaksiyaning o'quvchi BALANSIGA ta'siri: o'quvchiga bog'langan tuition KIRIMI balansni
    /// shu summaga oshiradi; VOZVRAT (expense+refund) KAMAYTIRADI. Boshqa turdagilar — 0.</summary>
    public static decimal StudentBalanceEffect(FinanceTransaction tx)
    {
        if (string.IsNullOrEmpty(tx.StudentId)) return 0m;
        if (tx.Direction == "income" && tx.Category == "tuition") return tx.Amount;
        if (tx.Direction == "expense" && tx.Category == "refund") return -tx.Amount;
        return 0m;
    }

    /// <summary>O'quvchi to'lovi (umumiy tahrirga YOPIQ — «To'lovlar» dagi tahrir).</summary>
    public static bool IsStudentPayment(FinanceTransaction tx) =>
        tx.Direction == "income" && tx.Category == "tuition" && !string.IsNullOrEmpty(tx.StudentId);

    /// <summary>Vozvrat qatori (umumiy tahrirda faqat sana/izoh).</summary>
    public static bool IsRefund(FinanceTransaction tx) => tx.Direction == "expense" && tx.Category == "refund";

    public const string StudentPaymentMessage =
        "O'quvchi to'lovi «To'lovlar» bo'limidagi tahrir orqali o'zgartiriladi — u yerda balans, oy va guruh birga moslanadi";

    public const string RefundLockedMessage =
        "Vozvratning summasi va turi o'zgartirilmaydi — bu yerda faqat sana va izoh tuzatiladi. "
        + "Summa noto'g'ri bo'lsa vozvratni bekor qilib, to'lovdan qaytadan qaytaring";

    /// <summary>Umumiy tahrir RAD etiladimi — sabab matni yoki <c>null</c>.</summary>
    public static string? EditError(FinanceTransaction tx, FinanceTransactionPayload p, string today)
    {
        // Yo'nalish FAQAT ikki qiymat (2026-10-17, F3 revyu): "Expense" kabi qiymat tasdiq chegarasidan o'tib ketar,
        // kassa uni chiqim, P&L esa umuman ko'rmasdi.
        if (p.Direction is not ("income" or "expense")) return DirectionMessage;
        if (IsStudentPayment(tx)) return StudentPaymentMessage;
        if (IsRefund(tx))
        {
            if (p.Amount != tx.Amount || p.Direction != tx.Direction || p.Category != tx.Category)
                return RefundLockedMessage;
            if (!DateOnly.TryParse(p.Date, out var d)) return "Sana noto'g'ri";
            if (string.CompareOrdinal(d.ToString("yyyy-MM-dd"), today) > 0) return "Kelajak sanaga vozvrat kiritib bo'lmaydi";
            return null;
        }
        // Oddiy qatorni pulga tegadigan turga AYLANTIRIB bo'lmaydi — ular o'z yo'llari orqali yoziladi.
        if (p.Direction == "expense" && p.Category == "refund")
            return "Vozvrat faqat to'lov yonidagi «Pul qaytarish» tugmasi orqali yoziladi";
        if (p.Direction == "income" && p.Category == "tuition" && !string.IsNullOrWhiteSpace(p.StudentId))
            return "O'quvchi to'lovi «Yangi moliyaviy amal» → o'quvchi to'lovi (yoki o'quvchi profili) orqali kiritiladi";
        if (!string.IsNullOrWhiteSpace(p.Month) && !PaymentFields.IsMonth(p.Month.Trim()))
            return "Oy noto'g'ri (kutilgan ko'rinish: yyyy-MM)";
        // XODIM maoshi qatori o'qituvchiga aylantirilmaydi — ikkalasi birga bo'lsa to'lov o'qituvchi varaqasida HAM,
        // reestrdagi xodim qatorida HAM sanalardi.
        if (tx.StaffUserId is not null && !string.IsNullOrWhiteSpace(p.TeacherId))
            return "Bu xodim maoshi to'lovi — o'qituvchi tanlanmaydi";
        return null;
    }

    public const string DirectionMessage = "Yo'nalish noto'g'ri — «income» (kirim) yoki «expense» (chiqim)";

    /// <summary>
    /// TAHRIR CHIQIM TASDIG'INI CHETLAB O'TMASIN (2026-10-17, F3 revyu): superadmin bo'lmagan xodim qatorni chegaradan
    /// KATTA chiqimga aylantira olmaydi — summani oshirib ham (900 ming → 50 mln), kirimni chiqimga o'girib ham. Allaqachon
    /// shunday (yoki kattaroq) chiqim bo'lgan qatorni kamaytirish yoki boshqa maydonlarini tuzatish — mumkin.
    /// </summary>
    public static string? ApprovalEditError(string oldDirection, decimal oldAmount, FinanceTransactionPayload p,
                                            decimal threshold, bool isSuperAdmin)
    {
        if (!ExpenseApproval.NeedsApproval(threshold, p.Amount, p.Direction, isSuperAdmin)) return null;
        if (oldDirection == "expense" && p.Amount <= oldAmount) return null;
        return $"Chegaradan ({AuditService.Money(threshold)} so'm) katta chiqim faqat tasdiq orqali o'tadi — tahrirda "
               + "summani oshirib (yoki kirimni chiqimga aylantirib) bo'lmaydi. Yangi chiqim kiriting (u tasdiqqa yuboriladi) "
               + "yoki superadmin tahrirlaydi";
    }

    /// <summary>
    /// VOZVRAT qatoriga faqat xavfsiz maydonlarni qo'llaydi (sana, izoh, kassir izohi) — o'quvchi, summa,
    /// toifa, oy, guruh, tur va kassa TEGILMAYDI, ya'ni balansga ta'siri o'zgarmaydi. Qaytaradi: o'zgarishlar
    /// ro'yxati (audit matni uchun).
    /// </summary>
    public static List<string> ApplyRefundSafe(FinanceTransaction tx, FinanceTransactionPayload p)
    {
        var changes = new List<string>();
        var date = DateOnly.TryParse(p.Date, out var d) ? d.ToString("yyyy-MM-dd") : tx.Date;
        if (tx.Date != date) changes.Add($"sana {tx.Date} → {date}");
        tx.Date = date;
        if (tx.Note != p.Note) changes.Add("izoh o'zgartirildi");
        tx.Note = p.Note;
        if (p.Comment is not null && tx.Comment != p.Comment)
        {
            changes.Add("sabab izohi o'zgartirildi");
            tx.Comment = p.Comment;
        }
        return changes;
    }

    /// <summary>
    /// ODDIY qatorga (o'quvchi to'lovi va vozvrat EMAS) tahrirni qo'llaydi. Forma yubormagan bog'lanishlar
    /// (o'quvchi, o'qituvchi, tur) toifa O'ZGARMASA saqlanadi — ilgari har tahrir ularni bo'shatardi
    /// (maosh qatori turini, jarima qatori o'qituvchisini yo'qotardi). Kassa va kvitansiya maydonlari —
    /// chaqiruvchida. Qaytaradi: o'zgarishlar ro'yxati (audit matni uchun).
    /// </summary>
    public static List<string> ApplyGeneric(FinanceTransaction tx, FinanceTransactionPayload p)
    {
        var changes = new List<string>();
        if (tx.Amount != p.Amount)
            changes.Add($"summa {AuditService.Money(tx.Amount)} → {AuditService.Money(p.Amount)} so'm");
        if (tx.Date != p.Date) changes.Add($"sana {tx.Date} → {p.Date}");
        if (tx.Direction != p.Direction) changes.Add($"yo'nalish {tx.Direction} → {p.Direction}");
        if (tx.Category != p.Category) changes.Add($"toifa {tx.Category} → {p.Category}");
        if (tx.Note != p.Note) changes.Add("izoh o'zgartirildi");

        var sameKind = tx.Direction == p.Direction && tx.Category == p.Category;
        var typeId = string.IsNullOrWhiteSpace(p.TypeId) ? (sameKind ? tx.TypeId : null) : p.TypeId;
        if (tx.TypeId != typeId) changes.Add("turi o'zgartirildi");

        tx.Date = p.Date;
        tx.Direction = p.Direction;
        tx.Category = p.Category;
        tx.TypeId = typeId;
        tx.Amount = p.Amount;
        tx.Note = p.Note;
        tx.StudentId = string.IsNullOrWhiteSpace(p.StudentId) ? (sameKind ? tx.StudentId : null) : p.StudentId;
        tx.TeacherId = string.IsNullOrWhiteSpace(p.TeacherId) ? (sameKind ? tx.TeacherId : null) : p.TeacherId;
        // Xodim maoshi bog'lanishi faqat maosh CHIQIMIDA ma'noli (FinanceController.Create qoidasi).
        if (tx.StaffUserId is not null && !(tx.Direction == "expense" && tx.Category == "salary")) tx.StaffUserId = null;
        // Oy/guruh teglarini SAQLAB QOLAMIZ: forma ularni yubormasa eski qiymat qoladi.
        if (!string.IsNullOrWhiteSpace(p.Month)) tx.Month = p.Month.Trim();
        if (!string.IsNullOrWhiteSpace(p.GroupId)) tx.GroupId = p.GroupId;
        if (p.Comment is not null) tx.Comment = p.Comment;
        if (!string.IsNullOrWhiteSpace(p.Method)) tx.Method = p.Method.Trim().ToLowerInvariant();
        return changes;
    }
}

/// <summary>
/// BO'LINGAN TO'LOV OILASI (<see cref="FinanceTransaction.SplitFromId"/>): guruh almashtirishda ortiqcha qism
/// yangi guruhga ko'chirilib to'lov ikkiga bo'linadi (<see cref="TuitionService.CarryGroupAdvanceAsync"/>).
/// Jismonan bu BITTA to'lov (bitta kvitansiya, bitta kassir, bitta kassa).
///
/// <para><b>Qaror:</b> BEKOR QILISH — butun oila birga bekor qilinadi (kaskad): to'lov xato kiritilgan
/// bo'lsa uning hamma qismi "kelmagan" pul. Biror qismidan vozvrat qilingan bo'lsa — rad etiladi (avval
/// vozvrat). VOZVRAT esa har qatordan ALOHIDA (rad etish + ko'rsatma): har qism o'z guruhi/oyiga tegishli
/// (o'qituvchi foizi, guruh balansi), shuning uchun ko'chirilgan qism o'z qatoridan qaytariladi.</para>
/// </summary>
public static class PaymentSplits
{
    /// <summary>Oila: ildiz + undan bo'lingan barcha qatorlar (kuzatiladi — bekor qilish shu ro'yxatda).
    /// Bo'linmagan to'lovda — faqat o'zi.</summary>
    public static async Task<List<FinanceTransaction>> FamilyAsync(IAppDbContext db, FinanceTransaction tx)
    {
        var root = tx.SplitFromId ?? tx.Id;
        var family = await db.FinanceTransactions
            .Where(t => t.Id == root || t.SplitFromId == root)
            .ToListAsync();
        if (!family.Any(t => t.Id == tx.Id)) family.Add(tx);
        return family.OrderBy(t => t.SplitFromId is null ? 0 : 1).ThenBy(t => t.CreatedAt).ToList();
    }

    /// <summary>Shu to'lovdan bo'lingan (boshqa guruhga ko'chirilgan) qatorlar — vozvrat xabari uchun.</summary>
    public static async Task<List<FinanceTransaction>> ChildrenAsync(IAppDbContext db, string txId) =>
        await db.FinanceTransactions.AsNoTracking().Where(t => t.SplitFromId == txId).ToListAsync();
}
