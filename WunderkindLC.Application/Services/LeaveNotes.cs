using System.Globalization;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'quvchi KETGANDA yoziladigan erkin IZOH — «Guruhdan chiqarish» va «Arxivga ko'chirish» oynalaridagi
/// ixtiyoriy «Izoh» maydoni. U o'quvchi profilidagi izohlar tarixiga (<see cref="StudentNote"/>) tushadi:
/// muallifi va vaqti bilan, profilning «Izohlar» bo'limida va «Izohlarga javoblar» sahifasida ko'rinadi.
/// Batafsil: <c>.claude/rules/student-notes.md</c> §5.
///
/// <para>⚠️ <c>Membership</c> audit matniga QO'SHILMAYDI: KPI, AI tahlil va «Ketish sabablari» sababni
/// o'sha matnning OXIRIDAN («— sabab: X») o'qiydi (<see cref="MembershipReasons.ReasonFromAuditSummary"/>)
/// — izoh u yerda sababga yopishib qolardi.</para>
/// </summary>
public static class LeaveNotes
{
    public const int MaxLength = 500;
    public const string TooLongMessage = "Izoh 500 belgidan oshmasin";

    /// <summary>Kiritilgan izohni tozalaydi: bo'sh — <c>(null, null)</c> (izoh yozilmaydi),
    /// uzun — xato matni (jimgina qirqilmaydi: foydalanuvchi yozganining bir qismi yo'qolmasin).</summary>
    public static (string? Text, string? Error) Clean(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return (null, null);
        return text.Length > MaxLength ? (null, TooLongMessage) : (text, null);
    }

    /// <summary>Guruhdan chiqarishdagi izoh matni: «Guruhdan chiqarildi («A guruh», 15.10.2026): …».</summary>
    public static string GroupLeaveText(string groupName, string date, string note) =>
        GroupLeavePrefix(groupName, date) + note;

    /// <summary><see cref="GroupLeaveText"/> ning izohdan OLDINGI qismi — «Ketgan o'quvchilar» ro'yxati
    /// izohni shu prefiks bo'yicha topadi (<see cref="PickNote"/>).</summary>
    public static string GroupLeavePrefix(string groupName, string date) =>
        $"Guruhdan chiqarildi («{groupName}», {Day(date)}): ";

    /// <summary>Arxivga ko'chirishdagi izoh matni.</summary>
    public static string ArchiveText(string note) => ArchivePrefix + note;

    /// <summary><see cref="ArchiveText"/> ning izohdan OLDINGI qismi.</summary>
    public const string ArchivePrefix = "Arxivga ko'chirildi: ";

    /// <summary>Profil izohi — <see cref="PickNote"/> uchun (matn + yozilgan vaqt).</summary>
    public readonly record struct NoteRow(string Text, string CreatedAt);

    /// <summary>
    /// Bitta KETISHNING izohi (prefiksiz) — «Ketgan o'quvchilar» ro'yxati uchun. Avval AYNAN shu guruh va
    /// chiqish sanasi bilan yozilgan «Guruhdan chiqarildi» izohi; topilmasa — o'quvchi AYNAN chiqish kuni
    /// arxivlangan bo'lsa, o'sha kuni yozilgan «Arxivga ko'chirildi» izohi (arxivlash a'zolikni o'sha
    /// kuni yopadi). Bir nechta bo'lsa eng kechkisi. Topilmasa — bo'sh.
    /// <para>⚠️ Izoh profil izohlarida TAHRIRLANISHI mumkin — prefiks o'chirilsa izoh bu yerda ko'rinmaydi
    /// (qabul qilingan: alohida "chiqish izohi" ustuni ATAYIN ochilmadi, izoh bitta joyda tursin).</para>
    /// </summary>
    public static string PickNote(IEnumerable<NoteRow> notes, string groupName, string leftAt, string? archivedAt)
    {
        var list = notes as IReadOnlyCollection<NoteRow> ?? notes.ToList();
        var prefix = GroupLeavePrefix(groupName, leftAt);
        var leave = list.Where(n => n.Text.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(n => n.CreatedAt, StringComparer.Ordinal)
            .Select(n => n.Text[prefix.Length..].Trim())
            .FirstOrDefault(t => t.Length > 0);
        if (leave is not null) return leave;

        var left = leftAt.Length >= 10 ? leftAt[..10] : leftAt;
        if (archivedAt is not { Length: >= 10 } a || a[..10] != left) return "";
        return list.Where(n => n.Text.StartsWith(ArchivePrefix, StringComparison.Ordinal)
                               && n.CreatedAt.Length >= 10 && n.CreatedAt[..10] == left)
                   .OrderByDescending(n => n.CreatedAt, StringComparer.Ordinal)
                   .Select(n => n.Text[ArchivePrefix.Length..].Trim())
                   .FirstOrDefault(t => t.Length > 0) ?? "";
    }

    /// <summary>Izohni o'quvchi profiliga qo'shadi. <c>SaveChangesAsync</c> QILMAYDI — amal bilan BITTA
    /// tranzaksiyada saqlanadi (amal muvaffaqiyatsiz bo'lsa izoh ham yozilmaydi).</summary>
    public static void Add(IAppDbContext db, string studentId, string text, string authorId, string authorName) =>
        db.StudentNotes.Add(new StudentNote
        {
            StudentId = studentId,
            Text = text,
            AuthorId = authorId,
            AuthorName = authorName,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });

    /// <summary>"yyyy-MM-dd" → "dd.MM.yyyy" (UI'dagi sana ko'rinishi); buzuq qiymat o'zgarishsiz.</summary>
    private static string Day(string iso) =>
        DateOnly.TryParseExact(iso.Length >= 10 ? iso[..10] : iso, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : iso;
}
