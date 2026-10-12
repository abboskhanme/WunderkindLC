namespace WunderkindLC.Application.Services;

/// <summary>
/// Xabar {token}lari katalogi — frontend "token tanlash" UI'si shu ro'yxatdan quriladi
/// (GET /api/admin/auto-messages/tokens). Har token <see cref="MessageTokenizer"/> haqiqatan
/// qo'llab-quvvatlaydigan nom (o'ylab topilmagan). Guruhlar:
///   "student" — o'quvchi/ota-ona xabarlari (MessageTokenizer.Student);
///   "common"  — barcha xabarlar (markaz/sana/oy/yil);
///   "event"   — hodisaga xos qo'shimcha tokenlar (dispatcher extraTokens orqali beradi).
/// <para><see cref="TokenInfo.Example"/> — server PREVIEW'ida (CRM upgrade P2.5) hodisa tokenlari o'rniga
/// qo'yiladigan namuna qiymat ("" — qiymat namuna o'quvchidan to'ladi).</para>
/// </summary>
public static class MessageTokenCatalog
{
    public record TokenInfo(string Token, string Label, string Group, string Example = "");

    public static readonly TokenInfo[] All =
    {
        // ---------- O'quvchi (ota-ona) ----------
        new("{fish}", "O'quvchi F.I.Sh.", "student"),
        new("{ism}", "O'quvchi ismi", "student"),
        new("{familiya}", "O'quvchi familiyasi", "student"),
        new("{sharif}", "O'quvchi sharifi (otasining ismi)", "student"),
        new("{sinf}", "Guruh nomi (eski {sinf})", "student"),
        new("{guruh}", "Guruh nomi", "student"),
        new("{oqituvchi}", "Guruh o'qituvchisi F.I.Sh.", "student"),
        new("{qarzdorlik}", "Qarzdorlik summasi", "student"),
        new("{balans}", "Balans", "student"),
        new("{ota-ona}", "Ota-ona ismi", "student"),
        new("{telefon}", "Aloqa telefoni", "student"),
        new("{ota}", "Otasining F.I.Sh.", "student"),
        new("{ota_telefon}", "Otasining telefoni", "student"),
        new("{ona}", "Onasining F.I.Sh.", "student"),
        new("{ona_telefon}", "Onasining telefoni", "student"),
        new("{oquvchi_telefon}", "O'quvchining telefoni", "student"),
        new("{manzil}", "Manzil", "student"),
        new("{tugilgan}", "Tug'ilgan sana", "student"),

        // ---------- Umumiy ----------
        new("{markaz}", "Markaz nomi", "common"),
        new("{sana}", "Joriy sana (kk.oo.yyyy)", "common"),
        new("{oy}", "Joriy oy nomi", "common"),
        new("{yil}", "Joriy yil", "common"),

        // ---------- Hodisaga xos ----------
        new("{summa}", "Summa (to'lov / oylik hisob)", "event", "350 000"),
        new("{kurs}", "Kurs nomi", "event"),
        new("{sabab}", "Davomat sababi (kelmadi)", "event", "Sababsiz"),
        new("{dars_sana}", "Dars sanasi", "event"),
        new("{dars_vaqti}", "Dars vaqti", "event"),
        new("{dars_kunlari}", "Dars kunlari (Du, Chor...)", "event"),
        new("{baho}", "Qo'yilgan baho", "event", "5"),
        // CRM upgrade (Phase 2): relation, achievements, test results and the monthly rating.
        new("{qarindosh}", "Kim (otasi/onasi/o'zi)", "event", "Otasi"),
        new("{yutuq}", "Yutuq nomi", "event", "IELTS"),
        new("{yutuq_natija}", "Yutuq natijasi", "event", "7.5"),
        new("{test}", "Test nomi", "event", "Unit 5"),
        new("{turi}", "Test turi (Oylik/Oraliq)", "event", "Oylik"),
        new("{ball}", "Ball (masalan 25/30)", "event", "25/30"),
        new("{foiz}", "Foiz", "event", "83%"),
        new("{orin}", "O'rin (masalan 3/18)", "event", "3/18"),
        new("{guruh_orin}", "Guruhdagi oylik o'rin", "event", "2/12"),
        new("{reyting_orin}", "Markazdagi oylik o'rin", "event", "12/430"),
    };

    /// <summary>Tokens the tokenizer accepts but the picker does not list (spelling aliases).</summary>
    public static readonly string[] Aliases = { "{ota_ona}" };

    /// <summary>Is <paramref name="token"/> (with braces) a token the tokenizer knows (case-insensitive)?</summary>
    public static bool IsKnown(string token) =>
        All.Any(t => string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase))
        || Aliases.Any(a => string.Equals(a, token, StringComparison.OrdinalIgnoreCase));

    /// <summary>Preview sample value of an event token (<c>""</c> when the value comes from the sample student).</summary>
    public static string ExampleOf(string token) =>
        All.FirstOrDefault(t => string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase))?.Example ?? "";

    // (Lid tokenlari, daraja testi tokenlari ({link}, {natija}, {daraja}) va ForbiddenInManual darvozasi
    //  2026-10 da lidga SMS bilan birga olib tashlandi. {ball}/{foiz} 2026-10-12 da test natijasi va reyting
    //  xabarlari uchun QAYTDI — CRM upgrade Phase 2/4.)
}
