namespace IntellectCRM.Application.Services.Kpi;

/// <summary>Yashirin mijoz auditining bitta MEZONI.</summary>
public sealed record KpiCriterion(int No, string Label);

/// <summary>Tiket SABABI: kalit, yorliq, qaysi rolga tegishli va qaysi audit mezoniga bog'langan.</summary>
/// <param name="RoleCode">Rol kodi yoki <see cref="KpiTicketCatalog.AnyRole"/> — barcha rollarga.</param>
/// <param name="CriterionNo">Audit mezoni raqami; <c>null</c> — mezon bilan bog'lanmagan sabab.</param>
public sealed record KpiTicketReason(string Code, string Label, string RoleCode, int? CriterionNo);

/// <summary>
/// Tiket sabablari va yashirin mijoz auditining <b>13 mezoni</b> — KODDA turadigan QAT'IY katalog.
///
/// <para>⚠️ Bu ro'yxat <c>ActionReason</c> jadvaliga QO'SHILMAYDI. Sabab: u yerda operatsion
/// sabablar (nega bog'lanamiz, nega ketdi) turadi va ular admin tomonidan bemalol
/// tahrirlanadi/o'chiriladi. Audit mezonlari esa xodim bilan KELISHILGAN qat'iy ro'yxat va
/// har biri pulga (tiket = ushlanma) bog'langan: o'chirilgan sabab tarixdagi tiketni "sababsiz"
/// qilib qo'yardi, ya'ni oldingi oyning jarimasi tushuntirib bo'lmaydigan bo'lib qolardi.</para>
/// </summary>
public static class KpiTicketCatalog
{
    /// <summary>Barcha rollarga tegishli sabab.</summary>
    public const string AnyRole = "";

    /// <summary>Yashirin mijoz auditi mezonlari (1–13) — cheklist bandlari ham shularga bog'lanadi.</summary>
    public static IReadOnlyList<KpiCriterion> Criteria { get; } = new List<KpiCriterion>
    {
        new(1,  "Telefonga tez javob berdi"),
        new(2,  "Ismini va ehtiyojini so'radi"),
        new(3,  "DM ga tez javob berdi"),
        new(4,  "Narx va shartlarni tushuntirdi"),
        new(5,  "Natijani RAQAM bilan aytdi"),
        new(6,  "Sinov darsiga chaqirdi"),
        new(7,  "CRM ga yozdi"),
        new(8,  "Qayta qo'ng'iroq qildi"),
        new(9,  "E'tirozga javob berdi"),
        new(10, "Bino va xona holati"),
        new(11, "Kutish zonasi tartibi"),
        new(12, "Devordagi natijalar taxtasi"),
        new(13, "Administrator xushmuomala"),
    };

    /// <summary>Tiket sabablari — UI'da SHU tartibda (rol bo'yicha guruhlangan).</summary>
    public static IReadOnlyList<KpiTicketReason> Reasons { get; } = new List<KpiTicketReason>
    {
        // --- Kiruvchi admin: suhbat SIFATI ---
        new("result_not_numeric",  "Natijani raqam bilan aytmadi",             KpiConst.Intake, 5),
        new("no_need_asked",       "Ehtiyojni so'ramadi",                      KpiConst.Intake, 2),
        new("no_trial_invite",     "Sinov darsiga chaqirmadi",                 KpiConst.Intake, 6),
        new("no_objection_answer", "E'tirozga javob bermadi",                  KpiConst.Intake, 9),
        new("no_callback",         "Qayta qo'ng'iroq qilmadi",                 KpiConst.Intake, 8),
        new("not_in_crm",          "CRM ga yozmadi",                           KpiConst.Intake, 7),
        new("slow_answer",         "Qo'ng'iroqqa kech javob berdi",            KpiConst.Intake, 1),
        new("slow_dm",             "DM ga 15 daqiqadan kech javob berdi",      KpiConst.Intake, 3),

        // --- Chiquvchi admin: bino, davomat, qarz, uzaytirish ---
        new("room_not_checked",    "Xona nazorati o'tkazilmadi",               KpiConst.Retention, 10),
        new("absent_no_message",   "Kelmagan o'quvchiga xabar yuborilmadi",    KpiConst.Retention, null),
        new("debtor_not_called",   "Qarzdorga aloqaga chiqilmadi",             KpiConst.Retention, null),
        new("leave_reason_missing","Ketish sababi yozilmadi",                  KpiConst.Retention, null),
        new("waiting_area_messy",  "Kutish zonasi tartibsiz",                  KpiConst.Retention, 11),
        new("board_outdated",      "Natijalar taxtasi yangilanmagan",          KpiConst.Retention, 12),
        new("teacher_request_late","O'qituvchi so'rovi 2 kundan ortiq javobsiz", KpiConst.Retention, null),

        // --- Umumiy ---
        // ⚠️ "Boshqa" ATAYIN bor: ro'yxatda yo'q holat uchun rahbar tiket QO'YA OLMASA,
        // u umuman yozilmasdi — ya'ni muammo hisobotdan butunlay yo'qolardi.
        new("other",               "Boshqa",                                   AnyRole, null),
    };

    /// <summary>Shu ROLGA ko'rsatiladigan sabablar: rolniki + umumiylari.</summary>
    /// <remarks>⚠️ Call operator kiruvchining sabablarini ko'radi — ish mazmuni bir xil
    /// (qo'ng'iroq sifati), faqat bonus birligi boshqa.</remarks>
    public static IReadOnlyList<KpiTicketReason> ForRole(string roleCode)
    {
        var role = roleCode == KpiConst.Operator ? KpiConst.Intake : roleCode;
        return Reasons.Where(x => x.RoleCode == role || x.RoleCode == AnyRole).ToList();
    }

    /// <summary>Sabab kalitining yorlig'i. Noma'lum kalit — kalitning O'ZI (yozuv yo'qolmasin).</summary>
    public static string ReasonLabel(string? code) =>
        Reasons.FirstOrDefault(x => x.Code == code)?.Label ?? code ?? "";

    /// <summary>Mezon raqamining yorlig'i; noma'lum raqam — <c>null</c>.</summary>
    public static string? CriterionLabel(int? no) =>
        no is null ? null : Criteria.FirstOrDefault(x => x.No == no)?.Label;
}
