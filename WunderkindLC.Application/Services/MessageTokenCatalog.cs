namespace WunderkindLC.Application.Services;

/// <summary>
/// Xabar {token}lari katalogi — frontend "token tanlash" UI'si shu ro'yxatdan quriladi
/// (GET /api/admin/auto-messages/tokens). Har token <see cref="MessageTokenizer"/> haqiqatan
/// qo'llab-quvvatlaydigan nom (o'ylab topilmagan). Guruhlar:
///   "student" — o'quvchi/ota-ona xabarlari (MessageTokenizer.Student);
///   "common"  — barcha xabarlar (markaz/sana/oy/yil);
///   "event"   — hodisaga xos qo'shimcha tokenlar (dispatcher extraTokens orqali beradi).
/// </summary>
public static class MessageTokenCatalog
{
    public record TokenInfo(string Token, string Label, string Group);

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
        new("{summa}", "Summa (to'lov / oylik hisob)", "event"),
        new("{kurs}", "Kurs nomi", "event"),
        new("{sabab}", "Davomat sababi (kelmadi)", "event"),
        new("{dars_sana}", "Dars sanasi", "event"),
        new("{dars_vaqti}", "Dars vaqti", "event"),
        new("{dars_kunlari}", "Dars kunlari (Du, Chor...)", "event"),
        new("{baho}", "Qo'yilgan baho", "event"),
    };

    // (Lid tokenlari, daraja testi tokenlari ({link}, {natija}, {daraja}, {ball}, {foiz}) va
    //  ForbiddenInManual darvozasi 2026-10 da lidga SMS bilan birga olib tashlandi.)
}
