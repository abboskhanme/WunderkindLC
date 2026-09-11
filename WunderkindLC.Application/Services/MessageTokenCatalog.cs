namespace WunderkindLC.Application.Services;

/// <summary>
/// Xabar {token}lari katalogi — frontend "token tanlash" UI'si shu ro'yxatdan quriladi
/// (GET /api/admin/auto-messages/tokens). Har token <see cref="MessageTokenizer"/> haqiqatan
/// qo'llab-quvvatlaydigan nom (o'ylab topilmagan). Guruhlar:
///   "student" — o'quvchi/ota-ona xabarlari (MessageTokenizer.Student);
///   "lead"    — lid xabarlari (MessageTokenizer.Lead);
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

        // ---------- Lid ----------
        new("{fish}", "Lid F.I.Sh.", "lead"),
        new("{telefon}", "Lid telefoni", "lead"),
        new("{fan}", "Qiziqqan fan (kurs)", "lead"),
        new("{oqituvchi}", "Sinov darsi guruhining o'qituvchisi F.I.Sh.", "lead"),
        new("{ota}", "Otasining F.I.Sh.", "lead"),
        new("{ota_telefon}", "Otasining telefoni", "lead"),
        new("{ona}", "Onasining F.I.Sh.", "lead"),
        new("{ona_telefon}", "Onasining telefoni", "lead"),
        new("{oquvchi_telefon}", "Lidning o'z telefoni", "lead"),
        new("{tugilgan}", "Tug'ilgan sana", "lead"),

        // ---------- Umumiy ----------
        new("{markaz}", "Markaz nomi", "common"),
        new("{sana}", "Joriy sana (kk.oo.yyyy)", "common"),
        new("{oy}", "Joriy oy nomi", "common"),
        new("{yil}", "Joriy yil", "common"),

        // ---------- Hodisaga xos ----------
        new("{summa}", "Summa (to'lov / oylik hisob)", "event"),
        new("{link}", "Havola (daraja-test)", "event"),
        new("{natija}", "Test natijasi", "event"),
        new("{daraja}", "Test darajasi", "event"),
        new("{ball}", "Test bali", "event"),
        new("{foiz}", "Test foizi", "event"),
        new("{kurs}", "Kurs nomi", "event"),
        new("{sabab}", "Davomat sababi (kelmadi)", "event"),
        new("{dars_sana}", "Dars sanasi", "event"),
        new("{dars_vaqti}", "Dars vaqti", "event"),
        new("{dars_kunlari}", "Dars kunlari (Du, Chor...)", "event"),
        new("{baho}", "Qo'yilgan baho", "event"),
    };

    /// <summary>
    /// QO'LDA yuborishda ishlatib BO'LMAYDIGAN token — uni faqat o'z hodisasi to'ldira oladi.
    /// </summary>
    /// <remarks>
    /// <para>Hozircha bitta: <c>{link}</c>. U <b>bir martalik</b> daraja-test havolasi va faqat
    /// "Daraja testi yuborish" oqimida (<c>POST leads/{id}/send-test</c>) tug'iladi — boshqa
    /// hech qayerda uni to'ldirishning iloji yo'q.</para>
    ///
    /// <para>⚠️ Haqiqiy hodisa (2026-09-07): lid oynasining "tayyor matn" ro'yxatida
    /// <c>test_link</c> andozasi ham chiqardi. Operator uni tanlab, oddiy "SMS yuborish"
    /// tugmasini bosdi — va abonentga <i>"Assalomu alaykum sizga {link} testi yuborildi"</i>
    /// degan matn KETDI. Hech qanday xato ko'rinmadi: SMS muvaffaqiyatli yuborilgan edi.</para>
    ///
    /// <para>Qolgan "event" tokenlari bu ro'yxatga KIRMAYDI: masalan <c>{dars_vaqti}</c> ni
    /// qo'lda yuborishda <see cref="MessageTokenizer"/> guruh jadvalidan to'ldira oladi.</para>
    /// </remarks>
    public static readonly string[] ManualForbidden = ["{link}"];

    /// <summary>
    /// Matnda qo'lda yuborib bo'lmaydigan token bormi — bo'lsa O'SHA tokenni qaytaradi.
    /// </summary>
    public static string? ForbiddenInManual(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var t in ManualForbidden)
            if (text.Contains(t, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }
}
