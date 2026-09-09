namespace IntellectCRM.Application.Services.Kpi;

/// <summary>
/// KPI modulining HOLAT qiymatlari — <b>magic string YO'Q</b>.
///
/// <para>Bu qiymatlar bazaga MATN bo'lib yoziladi (loyihaning odatdagi uslubi) va uch joyda
/// bir xil bo'lishi shart: entity, controller va klient. Har biri o'z joyida qo'lda yozilsa,
/// bittasidagi xato ("cancelled" ↔ "canceled") jimgina qatorni ro'yxatdan yo'qotardi —
/// xato emas, "yozuv yo'q" bo'lib ko'rinardi.</para>
/// </summary>
public static class KpiConst
{
    /* ---------- Rollar (KpiProfile.RoleCode, KpiRuleSet.RoleCode) ---------- */

    /// <summary>Kiruvchi admin: lid → sinov darsi → shartnoma.</summary>
    public const string Intake = "intake_admin";
    /// <summary>Chiquvchi admin: ushlab qolish, uzaytirish, qarz yig'ish.</summary>
    public const string Retention = "retention_admin";
    /// <summary>Call operator — kelajakdagi bo'linish; qoidalari kiruvchi bilan bir xil,
    /// farqi faqat <c>ContractBase</c> (sinovga KELGAN har bir odam uchun to'lanadi).</summary>
    public const string Operator = "call_operator";

    /* ---------- Tiket holati ---------- */

    /// <summary>AI yoki xodim taklif qildi — hali JARIMA emas.</summary>
    /// <remarks>⚠️ Tiketni AI o'zi TASDIQLAMAYDI (spetsifikatsiya §10): faqat
    /// <c>proposed</c> qo'yadi, rahbar tasdiqlaydi. Aks holda model xatosi to'g'ridan-to'g'ri
    /// xodimning oyligidan pul yechib olardi.</remarks>
    public const string TicketProposed = "proposed";
    /// <summary>Rahbar tasdiqladi — AYNAN shu holat oylik hisobida sanaladi.</summary>
    public const string TicketConfirmed = "confirmed";
    /// <summary>Xodim e'tiroz bildirdi — qaror kutilmoqda.</summary>
    public const string TicketDisputed = "disputed";
    /// <summary>Bekor qilindi — hisobga kirmaydi (o'chirilmaydi: tarix qoladi).</summary>
    public const string TicketCancelled = "cancelled";

    /* ---------- Oylik natija holati ---------- */

    /// <summary>Qoralama — raqamlar hamon JONLI hisoblanadi va o'zgarishi mumkin.</summary>
    public const string MonthDraft = "draft";
    /// <summary>Tasdiqlangan — raqamlar MUZLATILDI (keyingi o'zgarishlar ta'sir qilmaydi).</summary>
    public const string MonthConfirmed = "confirmed";

    /* ---------- Cheklist belgisi ---------- */

    /// <summary>Bajarildi ("✓").</summary>
    public const string CheckDone = "done";
    /// <summary>Bajarilmadi ("✗") — samaradorlik foiziga tushadi.</summary>
    public const string CheckFailed = "failed";
    /// <summary>Tegishli emas ("–") — MAXRAJGA ham kirmaydi.</summary>
    /// <remarks>⚠️ "na" ni "failed" bilan aralashtirmang: dam olish kunidagi band
    /// bajarilmagani uchun jarima bo'lsa, samaradorlik foizi sun'iy pasayardi.</remarks>
    public const string CheckNa = "na";

    /// <summary>Xodim o'zi belgiladi.</summary>
    public const string SourceManual = "manual";
    /// <summary>Tizim o'zi belgiladi (<c>AutoCheckKey</c> bo'yicha).</summary>
    public const string SourceAuto = "auto";
}
