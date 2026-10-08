using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUHNI TUGATISH (sertifikat bilan) — KIM YANGI GURUHGA KO'CHADI degan qoidaning
/// <b>YAGONA MANBASI</b> (sof funksiya, testlangan: <c>GroupClosingTests</c>).
///
/// <para>Qoida: yangi guruhga FAQAT eski guruhda <b>Status=="active"</b> bo'lgan a'zolar ko'chadi
/// va FAQAT ular sertifikat oladi (<see cref="Completes"/>). SINOVDAGI (<c>trial</c>) va
/// MUZLATILGAN (<c>frozen</c>) a'zolar ko'chirilmaydi — ular eski guruhdan ODDIY CHIQISH bilan
/// chiqadi (tarix saqlanadi), kerak bo'lsa admin yangi guruhga QO'LDA qo'shadi.</para>
///
/// <para>NEGA: sinovga kelib tashlab ketgan yoki muzlatilgan (ta'tildagi/to'xtatgan) o'quvchi kursni
/// tugatmagan. Ilgari BARCHA a'zolar avtomatik ko'chirilardi va yangi guruh birinchi kunidanoq
/// hech qachon kelmaydigan odamlar bilan to'lib turardi: guruh to'ldirilishi (`fill`), jurnal
/// ro'yxati va o'qituvchi ilovasi soxta ko'rinardi, `Student.ClassName` esa o'quvchini o'zi a'zo
/// bo'lmagan guruhga tegib qo'yardi.</para>
///
/// <para>Pul: faol a'zolik hisobi <see cref="MembershipBilling.SettleFreezeAsync"/> orqali yopish
/// sanasigacha yopiladi; sinov/muzlatilgan a'zolikda hisob yo'q yoki allaqachon yopilgan.</para>
/// </summary>
public static class GroupCompletionRules
{
    /// <summary>
    /// Yangi guruhga ko'chiriladigan o'quvchilar (takrorsiz, kelgan tartibda saqlanadi).
    /// </summary>
    /// <param name="members">Eski guruhning a'zoliklari — <b>statuslar "completed"ga
    /// o'zgartirilishidan OLDIN</b> uzatilishi shart, aks holda hech kim topilmaydi.</param>
    public static List<string> TransferableStudentIds(IEnumerable<StudentGroup> members) =>
        TransferableStudentIds(members, null);

    /// <inheritdoc cref="TransferableStudentIds(IEnumerable{StudentGroup})"/>
    /// <param name="archivedStudentIds">ARXIVDAGI o'quvchilar — ular ham ko'chirilmaydi va
    /// sertifikat olmaydi (eski ma'lumotda arxivlangan o'quvchining a'zoligi tirik qolgan bo'lishi
    /// mumkin: u kursni tugatmagan, shunchaki tizimda "qolib ketgan").</param>
    public static List<string> TransferableStudentIds(
        IEnumerable<StudentGroup> members, IReadOnlySet<string>? archivedStudentIds) =>
        members
            .Where(m => Completes(m, archivedStudentIds))
            .Select(m => m.StudentId)
            .Distinct()
            .ToList();

    /// <summary>
    /// A'zolik KURSNI TUGATDIMI — sertifikat oladi va <c>Status="completed"</c> bo'ladi.
    /// <para>FAQAT faol (<c>active</c>) a'zolik va arxivda bo'lmagan o'quvchi. Sinovdagi va
    /// muzlatilgan a'zolar kursni tugatmagan — ular ODDIY CHIQISH yo'li bilan yopiladi
    /// (<see cref="MembershipBilling.LeaveGroupAsync"/>: <c>IsActive=false</c>, <c>LeftAt</c>,
    /// holat o'zgarmaydi) va «Ketish sabablari»da ketish bo'lib sanaladi.</para>
    /// <para>⚠️ Ilgari ular ham sertifikat olib <c>completed</c> bo'lardi — ya'ni hisobotlarda
    /// "bitiruvchi" (churn emas) bo'lib ko'rinar, sinovga kelib tashlab ketgan odam qo'lida
    /// "kursni tugatdi" sertifikati qolardi.</para>
    /// </summary>
    public static bool Completes(StudentGroup m, IReadOnlySet<string>? archivedStudentIds = null) =>
        m.IsActive && m.Status == "active"
        && (archivedStudentIds is null || !archivedStudentIds.Contains(m.StudentId));
}
