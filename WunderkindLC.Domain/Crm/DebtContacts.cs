namespace WunderkindLC.Domain;

/* =====================================================================================
 *  «BOG'LANISH KERAK» (servis) AVTOMATIKASI — egasining talabi, 2026-10-27. Qoidalar: `.claude/rules/contacts.md` §9, §9.5.
 *  Sozlamalar — «Sozlamalar → Servis»: kelmaganlar (necha dars) va qarzdorlar (oyning qaysi sanasidan) avtomatik
 *  navbatga tushadi; to'lov qilinib qarz yopilganda talab O'ZI yopiladi.
 * ===================================================================================== */

public partial class CenterMeta
{
    /// <summary>«To'lov qarzdorligi» sababi (`ActionReason`, kategoriya `contact`) — startup seed'i qo'yadi; admin uni
    /// qayta nomlasa ham shu id bilan topiladi. Bo'sh — talab sababning matni bilan (katalogsiz) ochiladi.</summary>
    public string? DebtContactReasonId { get; set; }

    /// <summary>Ketma-ket sababsiz qoldirilgan darslar → avtomatik talab (yoqilgan/o'chiq).</summary>
    public bool ContactAbsenceAutoEnabled { get; set; } = true;

    /// <summary>Necha dars ketma-ket sababsiz qoldirilsa talab ochiladi (1..10; standart 3).</summary>
    public int ContactAbsenceLessons { get; set; } = 3;

    /// <summary>Qarzdorlar → avtomatik talab (yoqilgan/o'chiq). O'chiq bo'lsa ham to'langanlarning talabi yopilaveradi.</summary>
    public bool ContactDebtAutoEnabled { get; set; } = true;

    /// <summary>Oyning shu sanasidan (to'lov muddati) qarzdorlar navbatga tushadi (1..28; standart 20).</summary>
    public int ContactDebtDay { get; set; } = 20;
}

/// <summary>
/// Bir o'quvchiga bir oyda BITTA qarzdorlik hodisasi (unikal <c>(StudentId, Month)</c> — fon xizmati qayta yugursa ham,
/// restartda ham takror yo'q). <see cref="OpenedRequest"/> — talabni SHU hodisa ochganmi (avtomatik yopish faqat
/// shundaylarga: boshqa sabab bilan ochilgan talabga faqat izoh qo'shilgan bo'lsa, u yopilmaydi).
/// </summary>
public class DebtContactAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Oy ("yyyy-MM").</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Hodisa paytidagi jami qarz (so'm) — tarix uchun.</summary>
    public decimal Amount { get; set; }
    public string? ContactRequestId { get; set; }
    public bool OpenedRequest { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Qarz yopilib talab avtomatik yopilgan vaqt (null — hali yopilmagan yoki yopish kerak bo'lmagan).</summary>
    public DateTime? ResolvedAt { get; set; }
}
