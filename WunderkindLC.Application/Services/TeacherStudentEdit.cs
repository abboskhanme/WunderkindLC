using System.Text.RegularExpressions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QITUVCHI o'z guruhidagi o'quvchini tahrirlashi (`PUT api/teacher/groups/{classId}/students/{id}`)
/// — sof qoidalar (<c>TeacherStudentEditTests</c>).
///
/// <para>⚠️ FAQAT UCH MAYDON: ism-familiya, maktab sinfi, o'quvchining telefoni. Ota-ona, manzil,
/// guruh, chegirma, balans, login — o'qituvchiga YOPIQ (admin formasi `StudentsController.Update`).
/// Guruhga qo'shish/chiqarish/muzlatish o'qituvchi portalida umuman YO'Q.</para>
///
/// <para>Sinf qoidasi admin bilan BIR XIL (<c>school-grade.md</c> §3): <c>null</c> — tegilmaydi,
/// <c>0</c> — tozalash, 1..12 — joriy o'quv yiliga bog'lab yoziladi (keyingi yillarda o'zi oshadi).</para>
/// </summary>
public static class TeacherStudentEdit
{
    public const int MaxNameLength = 150;

    public sealed record Input(string? FullName, int? SchoolGrade, string? Phone);

    /// <summary>Xato matni (o'zbekcha, foydalanuvchiga ko'rsatiladi) yoki <c>null</c> — hammasi joyida.</summary>
    /// <param name="currentPhone">Bazadagi joriy raqam. Telefon O'ZGARMAGAN bo'lsa tekshirilmaydi —
    /// aks holda eski/xorijiy formatdagi raqami bor o'quvchining faqat ismini tuzatib bo'lmasdi.</param>
    public static string? Validate(Input p, string? currentPhone = null)
    {
        var name = CleanName(p.FullName);
        if (name.Length < 2) return "Ism-familiyani kiriting";
        if (name.Length > MaxNameLength) return $"Ism-familiya {MaxNameLength} belgidan oshmasin";

        if (p.SchoolGrade is { } g && g != 0 && !SchoolGrades.IsValid(g))
            return "Sinf 1 dan 11 gacha yoki «Maktabni bitirgan» bo'lishi kerak";

        // Telefon IXTIYORIY (bo'sh = tozalash), lekin kiritilgan bo'lsa haqiqiy raqam bo'lsin —
        // aks holda ota-onaga SMS/qo'ng'iroq noto'g'ri raqamga ketardi.
        if (!string.IsNullOrWhiteSpace(p.Phone) && !PhoneUnchanged(p.Phone, currentPhone)
            && !PhoneUtil.Validate(p.Phone).Valid)
            return "Telefon raqami noto'g'ri";

        return null;
    }

    /// <summary>
    /// O'zgarishni yozadi (<see cref="Validate"/> dan KEYIN chaqiriladi). SaveChanges QILMAYDI.
    /// Ism QISMLARI (<c>LastName/FirstName/MiddleName</c>) ham yangi FullName'dan qayta bo'linadi
    /// (<see cref="SetName"/>) — aks holda keyingi admin tahriri o'qituvchining tuzatishini bekor qilardi.
    /// </summary>
    public static void Apply(Student s, Input p, DateOnly today)
    {
        SetName(s, p.FullName);
        // O'zgarmagan raqam AYNAN saqlangan ko'rinishida qoladi (eski formatni jimgina "tuzatmaymiz").
        if (!PhoneUnchanged(p.Phone, s.Phone)) s.Phone = PhoneUtil.Normalize(p.Phone ?? "");

        if (p.SchoolGrade is null) return;
        if (p.SchoolGrade == 0)
        {
            s.SchoolGrade = null;
            s.SchoolGradeYear = null;
            return;
        }
        s.SchoolGrade = p.SchoolGrade;
        s.SchoolGradeYear = SchoolGrades.AcademicYear(today);
    }

    /// <summary>Kiritilgan raqam bazadagisi bilan bir xilmi (normallashtirilgan holda).</summary>
    public static bool PhoneUnchanged(string? phone, string? currentPhone) =>
        !string.IsNullOrWhiteSpace(currentPhone)
        && string.Equals(PhoneUtil.Normalize(phone), PhoneUtil.Normalize(currentPhone), StringComparison.Ordinal);

    /// <summary>
    /// FullName'ni yozadi va uni ism QISMLARIGA admin bilan BIR XIL qoida bo'yicha bo'ladi:
    /// 1-so'z — familiya, 2-so'z — ism, qolgani — sharif (klientdagi <c>splitFullName</c>).
    ///
    /// <para>⚠️ Nega SHART: admin formasi (`payloadFromStudent`) saqlangan QISMLARNI qayta yuboradi,
    /// `StudentsController.Update` esa FullName'ni qismlardan YIG'ADI. Ilgari o'qituvchi faqat
    /// FullName'ni tuzatar, qismlar eski qolardi — admin o'quvchini (masalan telefonini) tahrirlashi
    /// bilan ism jimgina ESKI holiga qaytardi. Endi ikki tomon bir xil: qismlardan yig'ilgan nom =
    /// o'qituvchi yozgan nom.</para>
    /// </summary>
    public static void SetName(Student s, string? fullName)
    {
        s.FullName = CleanName(fullName);
        var (last, first, middle) = SplitName(s.FullName);
        s.LastName = last;
        s.FirstName = first;
        s.MiddleName = middle;
    }

    /// <summary>"Familiya Ism Sharif ..." → (familiya, ism, sharif). Bo'sh qism — "".</summary>
    public static (string Last, string First, string Middle) SplitName(string? fullName)
    {
        var parts = CleanName(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (
            parts.Length > 0 ? parts[0] : "",
            parts.Length > 1 ? parts[1] : "",
            parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : "");
    }

    /// <summary>Boshi/oxiri kesiladi, ichidagi ortiqcha bo'shliqlar bitta qilinadi.</summary>
    public static string CleanName(string? name) =>
        Regex.Replace((name ?? "").Trim(), @"\s+", " ");
}
