using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// OMMAVIY / AVTOMATIK xabar auditoriyasi filtri.
/// <para>Guruhi YOPILGAN (arxivlangan) yoki TUGATILGAN o'quvchiga tizim o'zi xabar yubormasligi kerak:
/// ular endi o'qimaydi, lekin bazada qoladi (tarix, qarzdorlik, sertifikat). Aks holda qarzdorlik
/// eslatmasi, tug'ilgan kun tabrigi va ommaviy e'lonlar allaqachon o'qishni tugatgan oilalarga
/// yuborilaverardi.</para>
/// <para>Qoida: o'quvchi a'zoligi BOR-u, biror ham <b>TIRIK</b> a'zoligi qolmagan bo'lsa — chetlab
/// o'tiladi. Tirik a'zolik = <c>StudentGroup.IsActive</c> VA guruh ARXIVLANMAGAN. Ya'ni:
/// muzlatilgan bo'lsa ham guruhi faol bo'lsa — xabar boradi (ta'tildagi o'quvchi tizimdan uzilmaydi);
/// guruh yopilsa (barchasi muzlatilib arxivga) yoki tugatilsa (a'zolik yopiladi) — bormaydi.
/// A'zoligi UMUMAN yo'q (eski, faqat <c>ClassName</c> bilan yozilgan) o'quvchilar tegilmaydi —
/// ular uchun eski xulq saqlanadi.</para>
/// <para>DIQQAT: bu filtr FAQAT tizim o'zi boshlaydigan yuborishlarga (eslatma/tabrik/ommaviy e'lon)
/// tegishli. Hodisaga javob bo'lgan xabarlar — masalan "to'lov qabul qilindi" — YUBORILAVERADI:
/// yopilgan guruh qarzini to'lagan ota-ona tasdiqni olishi kerak.</para>
/// </summary>
public static class MessagingAudience
{
    /// <summary>
    /// Avtomatik xabar YUBORILMAYDIGAN o'quvchilar id'lari (guruhi yopilgan/tugatilgan yoki hamma
    /// guruhidan chiqarilgan). Chaqiruvchi ro'yxatini shu to'plam bo'yicha filtrlaydi.
    /// </summary>
    public static async Task<HashSet<string>> ClosedGroupStudentIdsAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await (from sg in db.StudentGroups
                          join c in db.Classes on sg.GroupId equals c.Id
                          select new { sg.StudentId, Live = sg.IsActive && !c.IsArchived })
                         .ToListAsync(ct);
        return rows
            .GroupBy(r => r.StudentId)
            .Where(g => !g.Any(r => r.Live))
            .Select(g => g.Key)
            .ToHashSet();
    }

    /// <summary>
    /// GURUH AUDITORIYASI — guruh nomi → shu guruhga xabar oladigan o'quvchilar id'lari.
    ///
    /// <para>⚠️ <b>A'ZOLIK bo'yicha, <c>Student.ClassName</c> bo'yicha EMAS.</b> <c>ClassName</c> faqat
    /// BIRINCHI guruhda yoziladi va keyin yangilanmaydi: ikkinchi guruhdagi o'quvchi e'lon olmasdi,
    /// guruh nomi o'zgarsa esa guruhga umuman hech narsa ketmasdi — holbuki ro'yxatdagi SANOQ
    /// a'zolikdan olinardi (ko'rsatilgan son ≠ oluvchilar).</para>
    ///
    /// <para>Qoida (sanoq ham, yuborish ham AYNAN shu funksiyadan — son = oluvchilar):</para>
    /// <list type="bullet">
    ///   <item>shu guruhda TIRIK a'zoligi bor (<c>StudentGroup.IsActive</c> + guruh ARXIVLANMAGAN —
    ///     <see cref="ClosedGroupStudentIdsAsync"/> bilan bir xil ta'rif; muzlatilgan ham kiradi);</item>
    ///   <item>a'zoligi UMUMAN yo'q eski o'quvchi — <c>ClassName</c> zaxirasi (eski xulq);</item>
    ///   <item>ARXIVDAGI o'quvchi HECH QACHON kirmaydi.</item>
    /// </list>
    /// </summary>
    /// <param name="onlyName">Berilsa — faqat shu guruh(lar) hisoblanadi (yuborish yo'li).</param>
    public static async Task<Dictionary<string, HashSet<string>>> GroupAudienceByNameAsync(
        IAppDbContext db, string? onlyName = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, HashSet<string>>();
        void Add(string name, string studentId)
        {
            if (!result.TryGetValue(name, out var set)) result[name] = set = new HashSet<string>();
            set.Add(studentId);
        }

        var live = await (from m in db.StudentGroups.AsNoTracking()
                          join st in db.Students.AsNoTracking() on m.StudentId equals st.Id
                          join c in db.Classes.AsNoTracking() on m.GroupId equals c.Id
                          where m.IsActive && !st.IsArchived && !c.IsArchived
                                && (onlyName == null || c.Name == onlyName)
                          select new { m.StudentId, c.Name }).ToListAsync(ct);
        foreach (var r in live) Add(r.Name, r.StudentId);

        // ZAXIRA: a'zoligi UMUMAN yo'q (eski, faqat ClassName bilan yozilgan) o'quvchilar.
        var legacyQ = db.Students.AsNoTracking()
            .Where(st => !st.IsArchived && st.ClassName != ""
                         && !db.StudentGroups.Any(m => m.StudentId == st.Id));
        if (onlyName is not null) legacyQ = legacyQ.Where(st => st.ClassName == onlyName);
        foreach (var st in await legacyQ.Select(st => new { st.Id, st.ClassName }).ToListAsync(ct))
            Add(st.ClassName, st.Id);

        return result;
    }

    /// <summary>Bitta guruh nomi bo'yicha auditoriya (<see cref="GroupAudienceByNameAsync"/> qoidasi).</summary>
    public static async Task<HashSet<string>> GroupAudienceAsync(
        IAppDbContext db, string groupName, CancellationToken ct = default) =>
        (await GroupAudienceByNameAsync(db, groupName, ct)).GetValueOrDefault(groupName) ?? new HashSet<string>();

    /// <summary>
    /// Ommaviy PUSH — "o'quvchi/ota-ona ilovasi" auditoriyasining akkaunt id'lari (takrorsiz).
    /// <paramref name="groupName"/> berilsa — <see cref="GroupAudienceAsync"/> (a'zolik bo'yicha);
    /// <paramref name="onlyDebtors"/> — faqat umumiy balansi manfiylar (SMS/Telegram bilan bir xil qoida;
    /// ilgari push uchun bu bayroq yo'q edi va qarz matni HAMMA ota-onaga "0 so'm" bilan ketardi).
    /// Arxivdagi va guruhi yopilgan/tugatilgan o'quvchilar chiqariladi.
    /// </summary>
    public static async Task<List<string>> ParentPushUserIdsAsync(
        IAppDbContext db, string? groupName, bool onlyDebtors, CancellationToken ct = default)
    {
        var q = db.Students.AsNoTracking().Where(s => !s.IsArchived && s.UserId != null);
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var memberIds = await GroupAudienceAsync(db, groupName.Trim(), ct);
            q = q.Where(s => memberIds.Contains(s.Id));
        }
        if (onlyDebtors) q = q.Where(s => s.Balance < 0);
        var closed = await ClosedGroupStudentIdsAsync(db, ct);
        return (await q.Select(s => new { s.Id, UserId = s.UserId! }).ToListAsync(ct))
            .Where(x => !closed.Contains(x.Id))
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Avto-xabar AUDITORIYASIGA qarab o'quvchi kartasidagi telefon: <c>students</c> — o'quvchining
    /// O'Z raqami birinchi; qolganlari (<c>parents</c>) — ota-ona raqami birinchi
    /// (ParentPhone → FatherPhone → MotherPhone → Phone). <see cref="AutoMessageService"/> bilan
    /// bir xil tartib — eslatma xizmatlari ham shuni ishlatadi.
    /// </summary>
    public static string? StudentPhone(Student s, string? audience) =>
        audience == "students"
            ? FirstNonEmpty(s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone)
            : FirstNonEmpty(s.ParentPhone, s.FatherPhone, s.MotherPhone, s.Phone);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
