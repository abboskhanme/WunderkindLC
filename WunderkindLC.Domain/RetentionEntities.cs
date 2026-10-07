namespace WunderkindLC.Domain;

/// <summary>
/// A'ZOLIK AMALIDAGI SABAB — tuzilgan (strukturaviy) yozuv: o'quvchi guruhdan CHIQARILGANDA,
/// GURUHI ALMASHTIRILGANDA, MUZLATILGANDA yoki guruh YOPILGANDA tanlangan sabab.
///
/// <para><b>NEGA KERAK:</b> ilgari sabab faqat audit MATNIGA («… — sabab: X») tushardi va uchta
/// hisobot (KPI ketish foizi, guruh/o'qituvchi AI tahlili) uni matndan qirqib olardi. Matn
/// o'zgarsa yoki sabab nomi tahrirlansa (ActionReason.Label) bog'lanish jimgina uzilardi va
/// "nazoratdan tashqari" bayrog'ini topib bo'lmasdi. Endi sabab <see cref="ReasonId"/> bilan
/// saqlanadi — «Ketish sabablari» hisoboti (<c>RetentionLossReport</c>) avval shu jadvalga
/// qaraydi, audit matni esa faqat shu jadvaldan OLDINGI tarix uchun zaxira.</para>
///
/// <para><b>Faqat sabab TANLANGAN amal yoziladi</b> (sababsiz amal — qator yo'q). Yagona yozuvchi —
/// <c>MembershipReasons.Record</c>. Qator O'ZGARMAYDI va o'chirilmaydi: bu tarix (a'zolik qayta
/// qo'shilsa ham eski chiqish sababi saqlanib qoladi).</para>
///
/// <para>A'zolikka (<see cref="StudentGroup"/>) ustun qo'shilmadi ATAYIN: bitta a'zolik muzlatilib,
/// keyin chiqarilishi (ikki sabab), qayta qo'shilib yana chiqarilishi mumkin — bitta ustun eski
/// sababni ustidan yozib yuborardi yoki qayta qo'shishda tozalashni unutgan joyda yolg'on qolardi.</para>
/// </summary>
public class MembershipReasonLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Amal: <c>remove</c> (guruhdan chiqarish) | <c>transfer</c> (guruh almashtirish — eski
    /// guruh tomoni) | <c>freeze</c> (muzlatish, «aktiv muzlatish» ham) | <c>close</c> (guruhni yopish).</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Amalning KUCHGA KIRGAN sanasi (ISO "YYYY-MM-DD"): chiqish (<c>LeftAt</c>) yoki
    /// muzlatish (<c>FrozenAt</c>) sanasi — orqaga sanalgan bo'lishi mumkin.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Tanlangan sabab (<see cref="ActionReason.Id"/>). Sabab keyin o'chirilsa ham id qoladi.</summary>
    public string? ReasonId { get; set; }
    /// <summary>Sabab nomining SURATI — sabab katalogda tahrirlansa/o'chirilsa ham tarix buzilmasin.</summary>
    public string ReasonLabel { get; set; } = string.Empty;
    /// <summary>Yozilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss", markaz vaqti) — bir kunda bir nechta
    /// yozuv bo'lsa eng kechkisi olinadi.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
