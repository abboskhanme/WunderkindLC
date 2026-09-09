using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// O'QUVCHINING GURUH KESIMI — KO'RSATISH uchun YAGONA manba.
///
/// <para><b>INVARIANT (`MembershipLifecycle.MemberState`):</b> o'quvchining umumiy holati BARCHA
/// tirik a'zoliklar ustidan JAMLAMA (<c>active &gt; trial &gt; yearFrozen &gt; frozen &gt; ""</c>),
/// va "qaysi guruhda o'qiydi" savoliga ham a'zoliklar javob beradi — <b>hech qachon</b> bitta
/// (birinchi/oxirgi/tasodifiy) a'zolik va <b>hech qachon</b> <see cref="Student.ClassName"/>.</para>
///
/// <para>⚠️ <b>NEGA KERAK BO'LDI:</b> jamlama faqat o'quvchilar RO'YXATIDA (<c>GetAll</c>)
/// hisoblanardi. Bitta o'quvchi qaytaradigan endpointlar (<c>GET students/{id}</c>, arxiv ro'yxati,
/// yangi yaratilgan o'quvchi) uni to'ldirmasdi, ya'ni javobda <c>Active=false</c>,
/// <c>MemberState=""</c>, <c>Groups=[]</c> kelardi. Klient esa bunday holatda "Aktiv emas" deb
/// yozib, guruh sifatida zaxira maydonni — <see cref="Student.ClassName"/> ni — chizardi. U esa
/// FAQAT BIRINCHI qo'shilgan guruhda to'ldiriladi va keyin YANGILANMAYDI
/// (<c>ClassesController.AddMember</c>: <c>if (string.IsNullOrWhiteSpace(s.ClassName))</c>), ya'ni
/// eski guruhida muzlatilib yangisida o'qiyotgan o'quvchi "eski guruh + Aktiv emas" bo'lib
/// ko'rinardi.</para>
///
/// <para>⚠️ <b>Denormalizatsiyani "to'g'rilash" bilan tuzatilmaydi.</b> <c>ClassName</c> bir necha
/// joyda yoziladi (o'quvchi formasi, guruh almashtirish, guruhni yakunlash) va uni har doim
/// haqiqatga mos ushlab turish imkonsiz. Shuning uchun tuzatish O'QISH tomonida: avval TIRIK
/// a'zoliklar, <c>ClassName</c> esa faqat a'zolik UMUMAN bo'lmaganda (juda eski yozuvlar).</para>
/// </summary>
public static class StudentMembershipView
{
    /// <summary>Bitta o'quvchining TIRIK a'zoliklari (<c>IsActive</c>) — holati muhim emas.</summary>
    public static Task<List<StudentGroup>> LiveMembershipsAsync(
        IAppDbContext db, string studentId, CancellationToken ct = default) =>
        db.StudentGroups.AsNoTracking().Where(sg => sg.StudentId == studentId && sg.IsActive).ToListAsync(ct);

    /// <summary>
    /// "ASOSIY GURUH" — bitta guruh ko'rsatilishi SHART bo'lgan joylar uchun
    /// (<see cref="MembershipLifecycle.PrimaryMembership"/>: faol → sinov → muzlatilgan, keyin eng
    /// yangi qo'shilgani). A'zolik umuman bo'lmasa — eski <see cref="Student.ClassName"/> yorlig'i
    /// bo'yicha guruh (orqaga moslik). Hech biri topilmasa <c>null</c>.
    /// </summary>
    public static async Task<Group?> PrimaryGroupAsync(IAppDbContext db, Student s, CancellationToken ct = default)
    {
        var memberships = await LiveMembershipsAsync(db, s.Id, ct);
        var primary = MembershipLifecycle.PrimaryMembership(memberships);
        if (primary is not null)
        {
            var g = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == primary.GroupId, ct);
            if (g is not null) return g;
        }
        if (string.IsNullOrWhiteSpace(s.ClassName)) return null;
        return await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Name == s.ClassName, ct);
    }

    /// <summary>
    /// KO'RSATILADIGAN guruh nomlari (ro'yxat ustuni bilan bir xil qoida): MUZLATILGANLAR
    /// chiqmaydi — o'quvchi eski guruhida muzlatilib yangisida o'qiyotgan bo'lsa faqat YANGISI
    /// ko'rinsin. Hamma a'zoligi muzlatilgan bo'lsa — o'shalar (aks holda "guruhsiz" bo'lib
    /// ko'rinardi). Nomlar alifbo bo'yicha, takrorsiz.
    /// </summary>
    public static List<string> DisplayGroupNames(
        IEnumerable<StudentGroup> memberships, IReadOnlyDictionary<string, string> groupNames)
    {
        var live = memberships.Where(m => m.IsActive && groupNames.ContainsKey(m.GroupId)).ToList();
        var open = live.Where(m => m.Status != "frozen").ToList();
        var use = open.Count > 0 ? open : live;
        return use.Select(m => groupNames[m.GroupId]).Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Bitta o'quvchi uchun ko'rsatiladigan guruh nomlari (<see cref="DisplayGroupNames"/>),
    /// a'zolik bo'lmasa — eski <see cref="Student.ClassName"/> (bo'sh bo'lsa bo'sh ro'yxat).</summary>
    public static async Task<List<string>> DisplayGroupNamesAsync(
        IAppDbContext db, Student s, CancellationToken ct = default)
    {
        var memberships = await LiveMembershipsAsync(db, s.Id, ct);
        var ids = memberships.Select(m => m.GroupId).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<string, string>()
            : await db.Classes.AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var list = DisplayGroupNames(memberships, names);
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(s.ClassName)) list.Add(s.ClassName);
        return list;
    }

    /// <summary>
    /// GURUH ARXIVLANGANDA — u bilan birga arxivlanadigan o'quvchilar id'si: shu guruhda TIRIK
    /// a'zoligi bor, lekin BOSHQA (arxivlanmagan) guruhda tirik a'zoligi YO'Q o'quvchilar.
    ///
    /// <para>⚠️ Ilgari tanlov <c>Student.ClassName == guruh nomi</c> edi. Bu yorliq BIRINCHI
    /// qo'shilgan guruhda qotib qoladi va guruh nomlari unikal ham emas — natijada allaqachon
    /// boshqa guruhga o'tgan o'quvchi ham arxivlanib ketardi (arxivlangan o'quvchiga oylik
    /// hisoblanmaydi — ya'ni u jimgina to'lovdan chiqib qolardi).</para>
    ///
    /// <para>A'zolik yozuvi UMUMAN bo'lmagan eski o'quvchilar bu ro'yxatga kirmaydi — ular
    /// chaqiruvchida avvalgidek <c>ClassName</c> bo'yicha qo'shiladi.</para>
    /// </summary>
    public static async Task<List<string>> ArchivableWithGroupAsync(
        IAppDbContext db, string groupId, CancellationToken ct = default)
    {
        var memberIds = await db.StudentGroups.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.IsActive)
            .Select(m => m.StudentId).Distinct().ToListAsync(ct);
        if (memberIds.Count == 0) return new List<string>();
        var studyElsewhere = (await (from m in db.StudentGroups.AsNoTracking()
                                     join g in db.Classes.AsNoTracking() on m.GroupId equals g.Id
                                     where m.IsActive && m.GroupId != groupId && !g.IsArchived
                                           && memberIds.Contains(m.StudentId)
                                     select m.StudentId).Distinct().ToListAsync(ct)).ToHashSet();
        return memberIds.Where(id => !studyElsewhere.Contains(id)).ToList();
    }

    /// <summary>
    /// O'quvchi obyektlarini KO'RSATISH maydonlari bilan to'ldiradi: <see cref="Student.Groups"/>,
    /// <see cref="Student.GroupStates"/>, <see cref="Student.Active"/>,
    /// <see cref="Student.MemberState"/>. Bazaga yozilmaydi (maydonlar <c>[NotMapped]</c>).
    ///
    /// <para>Ro'yxat, qidiruv, arxiv va BITTA o'quvchi qaytaradigan endpointlar AYNAN shuni
    /// chaqiradi — jamlama qoidasi nusxalanmasin (nusxa jimgina eskirardi).</para>
    /// </summary>
    public static async Task EnrichAsync(
        IAppDbContext db, IReadOnlyCollection<Student> students, CancellationToken ct = default)
    {
        if (students.Count == 0) return;
        var ids = students.Select(s => s.Id).ToList();
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.IsActive && ids.Contains(sg.StudentId)).ToListAsync(ct);
        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        // Guruhi o'chirilgan (yetim) a'zolik ro'yxatga kirmaydi — eski `join` xatti-harakati.
        var groups = groupIds.Count == 0
            ? new Dictionary<string, (string Name, string TeacherId)>()
            : (await db.Classes.AsNoTracking().Where(c => groupIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name, c.TeacherId }).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => (Name: c.Name, TeacherId: c.TeacherId ?? ""));

        var byStudent = memberships.Where(m => groups.ContainsKey(m.GroupId))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var s in students)
        {
            var mine = byStudent.GetValueOrDefault(s.Id) ?? new List<StudentGroup>();
            s.GroupStates = mine
                .OrderBy(m => groups[m.GroupId].Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new StudentGroupState
                {
                    GroupId = m.GroupId,
                    Name = groups[m.GroupId].Name,
                    TeacherId = groups[m.GroupId].TeacherId,
                    Status = m.Status ?? "",
                    YearFreeze = m.YearFreeze,
                }).ToList();
            // Ro'yxat ustunidagi NOMLAR — MUZLATILGANLARSIZ (qarang: `Student.Groups` izohi).
            // ⚠️ Bu yerda `DisplayGroupNames` ning "hammasi muzlatilgan bo'lsa o'shalarni ko'rsat"
            // tarmog'i ISHLATILMAYDI: ro'yxat ustuni muzlatilganlarni O'ZI xira qilib chizadi
            // (`StudentsPage`), ya'ni eski xatti-harakat saqlanadi.
            s.Groups = mine.Where(m => m.Status != "frozen")
                .Select(m => groups[m.GroupId].Name).Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            s.MemberState = MembershipLifecycle.MemberState(mine);
            s.Active = s.MemberState == "active";
        }
    }
}
