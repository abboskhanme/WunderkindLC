using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "IZOHLARGA JAVOBLAR" — o'quvchi profillariga yozilgan ERKIN IZOHLAR (<see cref="Domain.StudentNote"/>)
/// bir joyda: kimga izoh yozilgan, nechta, oxirgisi qachon va nima deb yozilgan.
///
/// <para>Profilda izoh BITTA o'quvchi ichida ko'rinadi — ya'ni "kimda izoh bor" degan savolga
/// javob berish uchun barcha profillarni ochib chiqish kerak edi. Bu servis AYNAN shu savolga
/// javob beradi va O'quvchilar bo'limidagi alohida sahifani (`/admin/students/izohlar`) to'ldiradi.</para>
///
/// <para><b>YAGONA MANBA:</b> izohning O'ZI (yozish/tahrir/o'chirish, "faqat muallifi" qoidasi)
/// avvalgidek <c>StudentsController</c> dagi <c>{id}/notes</c> endpointlarida qoladi — bu yerda
/// faqat RO'YXAT yig'iladi. Sahifadagi "qo'shimcha izoh yozish" ham o'sha endpointga boradi,
/// ya'ni qoida ikki joyda ayri ketmaydi.</para>
/// </summary>
public static class StudentNoteService
{
    /// <summary>Bir so'rovda qaytadigan eng ko'p O'QUVCHI (izoh emas) soni.</summary>
    public const int MaxLimit = 500;

    /// <summary>Standart chegara.</summary>
    public const int DefaultLimit = 200;

    /// <summary>
    /// Sana filtri uchun kun oxiri. <c>StudentNote.CreatedAt</c> — "yyyy-MM-ddTHH:mm:ss", ya'ni
    /// yalang "yyyy-MM-dd" bilan solishtirilsa o'sha kunning O'ZI tushib qolardi
    /// (audit modulidagi bilan bir xil muammo).
    /// </summary>
    public const string DayEnd = "T23:59:59";

    /// <summary>
    /// OYLIK KALENDAR uchun: shu oyning har kunida nechta izoh yozilgan.
    ///
    /// <para>ALOHIDA (yengil) so'rov — ataylab: sahifada bitta KUN tanlanganda ham kalendar
    /// butun oyni ko'rsatib tursin (aks holda tanlangan kundan boshqa kataklar bo'shab qolardi —
    /// "Bog'lanish kerak" dagi bilan bir xil sabab).</para>
    /// </summary>
    /// <param name="month">"yyyy-MM" (noto'g'ri bo'lsa — joriy oy).</param>
    public static async Task<List<StudentNoteDayDto>> DaysAsync(
        IAppDbContext db, string? month, CancellationToken ct = default)
    {
        var m = (month ?? "").Trim();
        if (m.Length != 7 || !int.TryParse(m[..4], out _) || !int.TryParse(m[5..7], out _))
            m = AppClock.Today.ToString("yyyy-MM");

        var start = $"{m}-01";
        var end = $"{m}-31{DayEnd}";   // satr solishtiruvi uchun yetarli (kun 31 dan oshmaydi)

        var rows = await db.StudentNotes.AsNoTracking()
            .Where(n => string.Compare(n.CreatedAt, start) >= 0 && string.Compare(n.CreatedAt, end) <= 0)
            .Select(n => n.CreatedAt)
            .ToListAsync(ct);

        return rows
            .Where(c => c.Length >= 10)
            .GroupBy(c => c[..10])
            .Select(g => new StudentNoteDayDto(g.Key, g.Count()))
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Izoh yozilgan o'quvchilar ro'yxati — oxirgi izoh vaqti bo'yicha, eng yangisi tepada.
    /// </summary>
    /// <param name="q">Qidiruv: o'quvchi ISMI yoki izoh MATNI ichidan (ikkalasi ham — operator
    /// "kim haqida" va "nima deb yozilgan" ni bir maydondan qidiradi).</param>
    /// <param name="from">Izoh yozilgan sana "yyyy-MM-dd" dan (bo'sh — chegarasiz).</param>
    /// <param name="to">Izoh yozilgan sana "yyyy-MM-dd" gacha (KUN sifatida, kun oxirigacha).</param>
    public static async Task<List<StudentNoteOverviewDto>> OverviewAsync(
        IAppDbContext db, string? q = null, string? from = null, string? to = null,
        int limit = DefaultLimit, CancellationToken ct = default)
    {
        var notes = db.StudentNotes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(from))
            notes = notes.Where(n => string.Compare(n.CreatedAt, from) >= 0);
        if (!string.IsNullOrWhiteSpace(to))
        {
            var end = to.Trim() + DayEnd;
            notes = notes.Where(n => string.Compare(n.CreatedAt, end) <= 0);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            // Ism bo'yicha moslik — o'quvchilar jadvalidan (izohda ism saqlanmaydi).
            // `ToLower().Contains` — provayderga bog'liq emas (Npgsql `ILike` SQLite testlarida
            // ishlamasdi; audit modulidagi bilan bir xil sabab).
            var matchIds = await db.Students.AsNoTracking()
                .Where(s => s.FullName.ToLower().Contains(needle))
                .Select(s => s.Id)
                .ToListAsync(ct);
            notes = notes.Where(n => matchIds.Contains(n.StudentId) || n.Text.ToLower().Contains(needle));
        }

        // Jamlanma SQLda: butun izohlar jadvalini xotiraga yig'ib olmaymiz.
        var groups = await notes
            .GroupBy(n => n.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Count = g.Count(),
                First = g.Min(x => x.CreatedAt),
                Last = g.Max(x => x.CreatedAt),
            })
            .OrderByDescending(g => g.Last)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .ToListAsync(ct);

        if (groups.Count == 0) return new List<StudentNoteOverviewDto>();

        var ids = groups.Select(g => g.StudentId).ToList();

        // Tanlangan o'quvchilarning izohlari — oxirgi matn va mualliflar ro'yxati uchun.
        // ⚠️ Filtr QAYTA qo'llanadi: davr tanlanganda "oxirgi izoh" o'sha davrdagisi bo'lishi kerak,
        // aks holda ro'yxatdagi son (davr) va matn (umuman oxirgi) bir-biriga mos kelmasdi.
        var rows = await notes.Where(n => ids.Contains(n.StudentId))
            .Select(n => new { n.StudentId, n.Text, n.AuthorName, n.CreatedAt })
            .ToListAsync(ct);
        var byStudent = rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        var students = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone, s.IsArchived })
            .ToListAsync(ct);
        var studentById = students.ToDictionary(s => s.Id);

        // Guruh nomlari — o'quvchilar ro'yxatidagi qoida bilan bir xil: FAOL a'zoliklar,
        // muzlatilganlarsiz (aks holda eski guruhi ham "hozir o'qiyapti" bo'lib ko'rinardi).
        var memberships = await (from sg in db.StudentGroups
                                 join c in db.Classes on sg.GroupId equals c.Id
                                 where sg.IsActive && ids.Contains(sg.StudentId) && sg.Status != "frozen"
                                 select new { sg.StudentId, c.Name })
            .ToListAsync(ct);
        var groupsByStudent = memberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<StudentNoteOverviewDto>(groups.Count);
        foreach (var g in groups)
        {
            var st = studentById.GetValueOrDefault(g.StudentId);
            var mine = byStudent.GetValueOrDefault(g.StudentId) ?? new();
            var last = mine.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            result.Add(new StudentNoteOverviewDto(
                g.StudentId,
                // O'quvchi o'chirilgan bo'lsa izohlari ham o'chadi (StudentsController.Delete),
                // ya'ni bu yerga tushmaydi. Baribir himoya: ism topilmasa qator YO'QOLMASIN.
                st?.FullName ?? "— o'chirilgan o'quvchi —",
                groupsByStudent.GetValueOrDefault(g.StudentId) ?? new List<string>(),
                st?.Phone ?? "", st?.ParentPhone ?? "", st?.IsArchived ?? false,
                g.Count, g.First ?? "", g.Last ?? "",
                last?.Text ?? "", last?.AuthorName ?? "",
                mine.Select(x => x.AuthorName).Where(a => a.Length > 0).Distinct()
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList()));
        }
        return result;
    }
}
