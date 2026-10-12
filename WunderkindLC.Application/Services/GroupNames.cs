using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH NOMI — takrorlanmaslik qoidasi va nom o'zgarganda NOMGA bog'langan ma'lumotni ko'chirish
/// (<b>YAGONA joy</b>).
///
/// <para><b>NEGA NOM NOYOB BO'LISHI SHART:</b> o'quvchining «asosiy guruh» yorlig'i
/// (<see cref="Student.ClassName"/>), eski o'qituvchi rahbarligi (<see cref="Teacher.HomeroomClass"/>)
/// va Telegram e'lonining "Guruh" auditoriyasi (<c>MessagingAudience.GroupAudienceByNameAsync</c>)
/// guruhga Id bilan emas, NOMI bilan bog'langan. Ikki guruh bir xil nomda bo'lsa e'lon begona guruh
/// ota-onalariga ham ketardi, yorliq esa noto'g'ri guruhni ko'rsatardi. (Guruh chati ham nom bilan
/// bog'langan edi — u 2026-10 da olib tashlandi, eski yozishmalar ko'chirilmaydi.)</para>
///
/// <para>Qoida: nom bo'shliqlari tozalangan va REGISTRSIZ holda BARCHA guruhlar (arxivdagilar ham)
/// orasida takrorlanmaydi. ⚠️ Bazada UNIKAL INDEKS YO'Q — ATAYIN: prod'da allaqachon dublikatlar
/// bo'lishi mumkin va migratsiya yiqilardi. Shuning uchun tekshiruv faqat NOM O'ZGARGANDA
/// (yaratish, qayta nomlash, «Tugatish»dagi yangi guruh) qilinadi — eski dublikatli guruhni
/// boshqa maydonini tahrirlash bloklanmaydi.</para>
/// </summary>
public static class GroupNames
{
    /// <summary>Band nom uchun foydalanuvchiga ko'rsatiladigan sabab (400).</summary>
    public const string TakenMessage = "Bu nomli guruh allaqachon bor";

    /// <summary>Solishtirish kaliti: chetdagi bo'shliqlar olib tashlanadi, ichkaridagi bo'shliqlar
    /// bittaga qisqaradi, registr hisobga olinmaydi.</summary>
    public static string Normalize(string? name)
    {
        var parts = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    /// <summary>Saqlanadigan ko'rinish — chetdagi bo'shliqlarsiz.</summary>
    public static string Clean(string? name) => (name ?? string.Empty).Trim();

    /// <summary>Ikki nom bir xil hisoblanadimi (<see cref="Normalize"/> bo'yicha).</summary>
    public static bool SameName(string? a, string? b) => Normalize(a) == Normalize(b);

    /// <summary>
    /// <paramref name="name"/> boshqa guruhda band-mi (sof funksiya). <paramref name="excludeId"/> —
    /// tahrirlanayotgan guruhning o'zi (o'z nomi bilan to'qnashmasin).
    /// </summary>
    public static bool IsTaken(IEnumerable<(string Id, string Name)> groups, string? name, string? excludeId)
    {
        var key = Normalize(name);
        if (key.Length == 0) return false;
        return groups.Any(g => !string.Equals(g.Id, excludeId, StringComparison.Ordinal) && Normalize(g.Name) == key);
    }

    /// <summary>
    /// Bazadagi BARCHA guruhlar (arxivdagilar ham) orasida nom band-mi.
    /// <para>Taqqoslash XOTIRADA: guruhlar soni kichik (yuzlab), SQL'dagi <c>lower()</c> esa
    /// provayderga qarab kirill/lotin harflarini turlicha qisqartiradi (SQLite faqat ASCII).</para>
    /// </summary>
    public static async Task<bool> IsTakenAsync(IAppDbContext db, string? name, string? excludeId, CancellationToken ct = default)
    {
        var rows = await db.Classes.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        return IsTaken(rows.Select(r => (r.Id, r.Name)), name, excludeId);
    }

    /// <summary>Qayta nomlashda ko'chirilgan qatorlar soni.</summary>
    public readonly record struct RenameResult(int Students, int Teachers);

    /// <summary>
    /// Guruh NOMI o'zgarganda nomga bog'langan ma'lumotni yangi nomga ko'chiradi:
    /// o'quvchilarning «asosiy guruh» yorlig'i (<see cref="Student.ClassName"/>) va eski rahbarlik
    /// (<see cref="Teacher.HomeroomClass"/>).
    ///
    /// <para>⚠️ ESKI nom bilan BOSHQA guruh ham bo'lsa (prod'dagi eski dublikat) — ma'lumotni nom
    /// bo'yicha ajratib bo'lmaydi: yorliq faqat SHU guruh a'zolarida (tarixdagilar ham) va SHU guruh
    /// o'qituvchisida almashtiriladi.</para>
    ///
    /// <para>Yozuvlar KUZATILADI (tracked) — <c>SaveChanges</c> chaqiruvchida, ya'ni qayta nomlash
    /// bilan BITTA saqlashda (kesh ham avtomatik yangilanadi). Audit chaqiruvchida.</para>
    /// </summary>
    public static async Task<RenameResult> MigrateRenameAsync(
        IAppDbContext db, Group group, string oldName, string newName, CancellationToken ct = default)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal) || string.IsNullOrEmpty(oldName))
            return new RenameResult(0, 0);

        var shared = await db.Classes.AsNoTracking()
            .AnyAsync(c => c.Id != group.Id && c.Name == oldName, ct);

        var studentsQ = db.Students.Where(s => s.ClassName == oldName);
        if (shared)
        {
            var memberIds = db.StudentGroups.Where(sg => sg.GroupId == group.Id).Select(sg => sg.StudentId);
            studentsQ = studentsQ.Where(s => memberIds.Contains(s.Id));
        }
        var students = await studentsQ.ToListAsync(ct);
        foreach (var s in students) s.ClassName = newName;

        var teachersQ = db.Teachers.Where(t => t.HomeroomClass == oldName);
        if (shared) teachersQ = teachersQ.Where(t => t.Id == group.TeacherId);
        var teachers = await teachersQ.ToListAsync(ct);
        foreach (var t in teachers) t.HomeroomClass = newName;

        return new RenameResult(students.Count, teachers.Count);
    }
}
