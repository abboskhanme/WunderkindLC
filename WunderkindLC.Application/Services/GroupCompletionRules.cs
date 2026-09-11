using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUHNI TUGATISH (sertifikat bilan) — KIM YANGI GURUHGA KO'CHADI degan qoidaning
/// <b>YAGONA MANBASI</b> (sof funksiya, testlangan: <c>GroupClosingTests</c>).
///
/// <para>Qoida: yangi guruhga FAQAT eski guruhda <b>Status=="active"</b> bo'lgan a'zolar ko'chadi.
/// SINOVDAGI (<c>trial</c>) va MUZLATILGAN (<c>frozen</c>) a'zolar ko'chirilmaydi — ular eski
/// guruhda "completed" bo'lib qoladi (tarix saqlanadi), kerak bo'lsa admin yangi guruhga QO'LDA
/// qo'shadi.</para>
///
/// <para>NEGA: sinovga kelib tashlab ketgan yoki muzlatilgan (ta'tildagi/to'xtatgan) o'quvchi kursni
/// tugatmagan. Ilgari BARCHA a'zolar avtomatik ko'chirilardi va yangi guruh birinchi kunidanoq
/// hech qachon kelmaydigan odamlar bilan to'lib turardi: guruh to'ldirilishi (`fill`), jurnal
/// ro'yxati va o'qituvchi ilovasi soxta ko'rinardi, `Student.ClassName` esa o'quvchini o'zi a'zo
/// bo'lmagan guruhga tegib qo'yardi.</para>
///
/// <para>Bu qoida PUL bilan bog'liq EMAS — eski guruh hisobi (muzlatish/qisman oylik) baribir
/// BARCHA a'zolar uchun <see cref="MembershipBilling.SettleFreezeAsync"/> orqali yopiladi.</para>
/// </summary>
public static class GroupCompletionRules
{
    /// <summary>
    /// Yangi guruhga ko'chiriladigan o'quvchilar (takrorsiz, kelgan tartibda saqlanadi).
    /// </summary>
    /// <param name="members">Eski guruhning a'zoliklari — <b>statuslar "completed"ga
    /// o'zgartirilishidan OLDIN</b> uzatilishi shart, aks holda hech kim topilmaydi.</param>
    public static List<string> TransferableStudentIds(IEnumerable<StudentGroup> members) =>
        members
            .Where(m => m.IsActive && m.Status == "active")
            .Select(m => m.StudentId)
            .Distinct()
            .ToList();
}
