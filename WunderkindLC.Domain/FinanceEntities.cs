namespace WunderkindLC.Domain;

// MOLIYA — kassalar orasida ko'chirish va bir martalik seed belgilari (`.claude/rules/billing.md`
// «KASSADAN KASSAGA KO'CHIRISH», «KIRIM/CHIQIM TURLARI»).

/// <summary>
/// KASSADAN KASSAGA KO'CHIRISH — pul bir kassadan boshqasiga o'tdi (masalan kassir kun oxirida
/// naqdni asosiy kassaga topshirdi). Ikkala kassaning QOLDIG'I o'zgaradi, lekin bu KIRIM ham,
/// CHIQIM ham EMAS.
///
/// <para>⚠️ ATAYIN <see cref="FinanceTransaction"/> ga yozilmaydi (alohida jadval): moliya
/// tranzaksiyalarini 25+ joy o'qiydi — P&amp;L, pul oqimi, «Kirim chiqim», maosh bazasi, kassir
/// hisoboti, AI tahlil, KPI. Ko'chirish o'sha yerda yozilsa HAR BIRIGA istisno qo'shish kerak
/// bo'lardi va bittasi unutilsa pul ikki marta "kirim" bo'lib sanalardi. Alohida jadvalni esa
/// faqat kassa qoldig'i (<c>CashboxService.BalancesAsync</c>) o'qiydi — oqib ketish yo'li yo'q.</para>
///
/// <para>O'chirilmaydi — <b>bekor qilinadi</b> (<see cref="IsVoided"/>, global filtr): qoldiqlar
/// tiklanadi, qator esa tarixda ustiga chizilgan holda qoladi (`FinanceTransaction` bilan bir xil).</para>
/// </summary>
public class CashboxTransfer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi kassadan chiqdi (<see cref="Cashbox"/>.Id).</summary>
    public string FromCashboxId { get; set; } = string.Empty;
    /// <summary>Qaysi kassaga tushdi (<see cref="Cashbox"/>.Id).</summary>
    public string ToCashboxId { get; set; } = string.Empty;
    /// <summary>Summa (har doim musbat).</summary>
    public decimal Amount { get; set; }
    /// <summary>Ko'chirish sanasi "yyyy-MM-dd" (kelajak sana — yo'q).</summary>
    public string Date { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>Yozilgan vaqt — markaz vaqti, "yyyy-MM-ddTHH:mm:ss".</summary>
    public string CreatedAt { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public string? CreatedByName { get; set; }

    /// <summary>BEKOR QILINGAN — qoldiqqa ta'sir qilmaydi (global filtr), tarixda ko'rinadi.</summary>
    public bool IsVoided { get; set; }
    public string? VoidedAt { get; set; }
    public string? VoidedById { get; set; }
    public string? VoidedByName { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// BIR MARTALIK seed belgisi: <see cref="Key"/> yozilgan bo'lsa o'sha seed QAYTA ishlamaydi.
///
/// <para>Nega kerak: "yo'q bo'lsa qo'shiladi" naqshi foydalanuvchi BOSHQARADIGAN katalogda
/// (tranzaksiya turlari) yaramaydi — admin ATAYIN o'chirgan tur har restartda qayta tug'ilardi.
/// "Faqat jadval bo'sh bo'lsa" esa mavjud bazaga yangi turlarni umuman olib kirmaydi. Belgi
/// ikkalasining o'rtasi: mavjud bazada BIR marta to'ldiradi, keyin admin qaroriga tegmaydi.</para>
/// </summary>
public class SeedMarker
{
    /// <summary>Seed kaliti, masalan <c>"finance-types-2026-10"</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Qachon bajarilgan — markaz vaqti, "yyyy-MM-ddTHH:mm:ss".</summary>
    public string AppliedAt { get; set; } = string.Empty;
}
