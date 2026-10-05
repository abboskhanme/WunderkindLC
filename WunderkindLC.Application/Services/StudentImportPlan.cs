using System.Globalization;
using System.Text.RegularExpressions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// EXCEL'DAN O'QUVCHI IMPORTI — har qator uchun QAROR (sof, <c>StudentImportPlanTests</c>).
/// Controller (`StudentsController.Import`) faqat shu rejani bajaradi.
///
/// <para><b>Nega kerak:</b> ilgari har qator YANGI o'quvchi yaratardi. Bitta o'quvchi ikki guruh
/// ro'yxatida bo'lsa (yoki fayl ikki marta yuklansa) ikki marta yaratilardi — 2026-09-30 importida
/// aynan shunday 22 ta dublikat paydo bo'lgan va ular qo'lda birlashtirilgan.</para>
///
/// <para><b>Dublikat qoidasi (qat'iy):</b> ISM bir xil (registr, apostrof va bo'shliqlardan qat'i
/// nazar) VA kamida bitta TELEFON mos (o'quvchi/ota/ona, oxirgi 9 raqam) → o'sha o'quvchi: yangisi
/// YARATILMAYDI, faqat guruhga qo'shiladi. Faqat ismi mos (telefon yo'q yoki boshqa) → yaratiladi,
/// lekin OGOHLANTIRISH bilan — ismdosh bolalarni jimgina birlashtirib yubormaslik uchun.</para>
///
/// <para>Faylning o'zidagi takror ham xuddi shunday: oldingi qatorda yaratilgan o'quvchi
/// keyingi qatorda "mavjud" hisoblanadi (bir o'quvchi — bir nechta guruh).</para>
/// </summary>
public static class StudentImportPlan
{
    /// <summary>Exceldan o'qilgan qator (ustunlar shablon tartibida).</summary>
    public sealed record Row(
        int ExcelRow, string FullName, string Phone, string FatherName, string FatherPhone,
        string MotherName, string MotherPhone, string Group, string Status, string BirthDate,
        string Gender, string Grade);

    /// <summary>Bazadagi o'quvchi (solishtirish uchun).</summary>
    public sealed record Existing(string Id, string FullName, bool IsArchived, int? SchoolGradeNow, params string?[] Phones);

    public enum Kind { Create, Join, Skip, Error }

    /// <summary>
    /// Bitta qator qarori. <c>Create</c> — yangi o'quvchi (<see cref="NewIndex"/> — shu importdagi tartib raqami);
    /// <c>Join</c> — MAVJUD o'quvchi (<see cref="ExistingId"/>) yoki shu importda yaratilgan
    /// (<see cref="NewIndex"/>) guruhga qo'shiladi; <c>Skip</c> — hech narsa (sababi <see cref="Message"/>);
    /// <c>Error</c> — qator xato. <see cref="FillGrade"/> — mavjud o'quvchida sinf yo'q edi, qatordagisi yoziladi.
    /// </summary>
    public sealed record Step(
        int ExcelRow, Kind Kind, Row Source, int? Grade, string? ExistingId = null, int? NewIndex = null,
        bool FillGrade = false, string? Message = null, string? Warning = null);

    // ============================ Sinf ============================

    // FAQAT raqam (ixtiyoriy "-sinf"/"sinf"/"класс" qo'shimchasi bilan). Harfli sinf ("7a", "8-B")
    // markazda ishlatilmaydi (foydalanuvchi, 2026-10-05) — u xato yozuv sifatida rad etiladi.
    private static readonly Regex GradeNumber = new(
        @"^(\d{1,2})(?:[.,]0+)?\s*(?:-?\s*(?:sinf|sinfi|класс|кл\.?))?$", RegexOptions.Compiled);

    /// <summary>
    /// "Sinf" ustuni: bo'sh → <c>null</c> (kiritilmagan, xato emas); "7", "7-sinf", "7 sinf",
    /// "7 класс", "7.0" (Excel raqami) → 7; "bitirgan", "maktabni bitirgan", "tugatgan", "выпускник", "12"
    /// → 12 («Maktabni bitirgan»). Boshqa qiymat — <c>false</c> (qator xatosi).
    /// </summary>
    public static bool TryParseGrade(string? raw, out int? grade)
    {
        grade = null;
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return true;
        if (v.Contains("bitir") || v.Contains("tugat") || v.Contains("выпуск"))
        {
            grade = SchoolGrades.Graduated;
            return true;
        }
        var m = GradeNumber.Match(v);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var g)
            || !SchoolGrades.IsValid(g))
            return false;
        grade = g;
        return true;
    }

    // ============================ Kalitlar ============================

    /// <summary>Ko'rsatish va saqlash uchun ism: boshi/oxiri kesiladi, ortiqcha bo'shliq bitta.</summary>
    public static string CleanName(string? fullName) => Regex.Replace((fullName ?? "").Trim(), @"\s+", " ");

    /// <summary>Ism kaliti: kichik harf, apostrof turlari bir xil, ortiqcha bo'shliq bitta.</summary>
    public static string NameKey(string? fullName)
    {
        var s = (fullName ?? "").Trim().ToLowerInvariant();
        foreach (var a in new[] { 'ʻ', 'ʼ', '’', '‘', '`', '´' }) s = s.Replace(a, '\'');
        return Regex.Replace(s, @"\s+", " ");
    }

    /// <summary>Telefon kalitlari (oxirgi 9 raqam, <see cref="PhoneUtil.Key"/>); 7 raqamdan qisqasi tashlanadi.</summary>
    public static HashSet<string> PhoneKeys(IEnumerable<string?> phones) =>
        phones.Select(PhoneUtil.Key).Where(k => k.Length >= 7).ToHashSet(StringComparer.Ordinal);

    // ============================ Reja ============================

    private sealed record Person(string NameKey, HashSet<string> Phones, string? ExistingId, int? NewIndex, bool IsArchived);

    /// <param name="rows">Ma'lumot qatorlari (sarlavhasiz, bo'shlari ham — ular <c>Skip</c>).</param>
    /// <param name="existing">Bazadagi o'quvchilar (arxivlanganlari ham — ular uchun alohida xato).</param>
    /// <param name="memberships">Mavjud a'zoliklar: (o'quvchi id, guruh nomi, faolmi).</param>
    /// <param name="groupNames">Mavjud guruh nomlari (registrsiz solishtiriladi).</param>
    public static List<Step> Build(
        IReadOnlyList<Row> rows,
        IEnumerable<Existing> existing,
        IEnumerable<(string StudentId, string GroupName, bool IsActive)> memberships,
        IEnumerable<string> groupNames)
    {
        var groups = groupNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var people = new List<Person>();
        var gradeById = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var e in existing)
        {
            people.Add(new Person(NameKey(e.FullName), PhoneKeys(e.Phones), e.Id, null, e.IsArchived));
            gradeById[e.Id] = e.SchoolGradeNow;
        }
        // (kim, guruh) → faolmi. Kim: "id:{ExistingId}" yoki "new:{NewIndex}".
        var member = new Dictionary<(string Who, string Group), bool>();
        foreach (var (sid, g, active) in memberships)
            member[("id:" + sid, g.ToLowerInvariant())] = member.GetValueOrDefault(("id:" + sid, g.ToLowerInvariant())) || active;

        var steps = new List<Step>();
        var newCount = 0;
        foreach (var r in rows)
        {
            var name = CleanName(r.FullName);
            if (new[] { r.FullName, r.Phone, r.FatherName, r.FatherPhone, r.MotherName, r.MotherPhone, r.Group,
                        r.Status, r.BirthDate, r.Gender, r.Grade }.All(string.IsNullOrWhiteSpace))
            {
                steps.Add(new Step(r.ExcelRow, Kind.Skip, r, null, Message: "bo'sh qator"));
                continue;
            }
            if (name.Length == 0) { steps.Add(new Step(r.ExcelRow, Kind.Error, r, null, Message: "F.I.SH bo'sh")); continue; }
            if (!TryParseGrade(r.Grade, out var grade))
            {
                steps.Add(new Step(r.ExcelRow, Kind.Error, r, null,
                    Message: $"Sinf noto'g'ri: \"{r.Grade.Trim()}\" (1–11 yoki «bitirgan»)"));
                continue;
            }
            var group = r.Group.Trim();
            if (group.Length > 0 && !groups.Contains(group))
            {
                steps.Add(new Step(r.ExcelRow, Kind.Error, r, grade, Message: $"Guruh topilmadi: \"{group}\""));
                continue;
            }

            var key = NameKey(name);
            var phones = PhoneKeys([r.Phone, r.FatherPhone, r.MotherPhone]);
            var sameName = people.Where(p => p.NameKey == key).ToList();
            var same = sameName.FirstOrDefault(p => p.Phones.Overlaps(phones));

            if (same is { IsArchived: true })
            {
                steps.Add(new Step(r.ExcelRow, Kind.Error, r, grade,
                    Message: $"\"{name}\" arxivda turibdi — avval arxivdan tiklang, keyin import qiling"));
                continue;
            }

            if (same is not null)
            {
                // MAVJUD o'quvchi — yangisi yaratilmaydi.
                var who = same.ExistingId is not null ? "id:" + same.ExistingId : "new:" + same.NewIndex;
                var fillGrade = grade is not null && same.ExistingId is not null
                                && gradeById.GetValueOrDefault(same.ExistingId) is null;
                if (fillGrade) gradeById[same.ExistingId!] = grade;

                if (group.Length == 0)
                {
                    steps.Add(new Step(r.ExcelRow, Kind.Skip, r, grade, same.ExistingId, same.NewIndex, fillGrade,
                        Message: $"\"{name}\" allaqachon bor — yangi o'quvchi yaratilmadi"));
                    continue;
                }
                var gk = (who, group.ToLowerInvariant());
                if (member.TryGetValue(gk, out var active))
                {
                    steps.Add(new Step(r.ExcelRow, Kind.Skip, r, grade, same.ExistingId, same.NewIndex, fillGrade,
                        Message: active
                            ? $"\"{name}\" allaqachon «{group}» guruhida"
                            : $"\"{name}\" avval «{group}» guruhidan chiqqan — kerak bo'lsa guruh sahifasidan qayta qo'shing"));
                    continue;
                }
                member[gk] = true;
                steps.Add(new Step(r.ExcelRow, Kind.Join, r, grade, same.ExistingId, same.NewIndex, fillGrade,
                    Message: $"\"{name}\" allaqachon bor — «{group}» guruhiga qo'shildi"));
                continue;
            }

            // YANGI o'quvchi. Ismdosh bor, lekin telefon mos emas — yaratiladi, ammo ogohlantiriladi.
            var index = newCount++;
            people.Add(new Person(key, phones, null, index, false));
            if (group.Length > 0) member[("new:" + index, group.ToLowerInvariant())] = true;
            var warning = sameName.Count > 0
                ? $"\"{name}\" ismli o'quvchi allaqachon bor (telefon mos kelmadi) — dublikat emasligini tekshiring"
                : null;
            steps.Add(new Step(r.ExcelRow, Kind.Create, r, grade, NewIndex: index, Warning: warning));
        }
        return steps;
    }
}
