using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "BOG'LANISH KERAK" NAVBATIGA QO'SHISH — <b>yagona manba</b>.
///
/// <para>Talab uch joydan ochiladi: o'quvchi profilidagi "⋮", o'quvchilar ro'yxatidagi tanlash
/// paneli va guruh jurnalidagi "Aloqa" tabi (o'qituvchi ham, admin ham). Ular ikki xil
/// controllerda (<c>ContactsController</c> — admin/xodim, <c>TeacherPortalController</c> —
/// o'qituvchi), shuning uchun qoida shu servisda: "bir o'quvchida bitta ochiq talab",
/// hodisa yozuvi va audit izi HAMMA joyda bir xil bo'lsin.</para>
/// </summary>
public class ContactQueueService(IAppDbContext db, AuditService audit)
{
    /// <summary>Audit tur nomi — <see cref="AuditSections"/> da "Bog'lanish kerak" bo'limiga tushadi.</summary>
    public const string AuditEntity = "ContactRequest";

    /// <summary>Bir amalda navbatga qo'shiladigan eng ko'p o'quvchi.</summary>
    public const int MaxBulk = 500;

    /// <summary>Ko'plab qo'shish natijasi.</summary>
    /// <param name="Skipped">Ochiq talabi borligi uchun CHETLAB O'TILGANLAR.</param>
    /// <param name="NotFound">Topilmagan (yoki ruxsat berilmagan) o'quvchilar.</param>
    public readonly record struct BulkResult(
        int Created, int Skipped, List<string> SkippedNames, int NotFound);

    /// <summary>
    /// Sabab id'sini tekshiradi va MATNINI (snapshot) qaytaradi. Topilmasa ikkalasi ham bo'sh —
    /// sabab ixtiyoriy, noto'g'ri id tufayli amal to'xtamasin.
    /// </summary>
    public async Task<(string Id, string Label)> ResolveReasonAsync(string? reasonId)
    {
        var id = (reasonId ?? "").Trim();
        if (id.Length == 0) return ("", "");
        var label = await db.ActionReasons.Where(r => r.Id == id).Select(r => r.Label)
            .FirstOrDefaultAsync() ?? "";
        return label.Length == 0 ? ("", "") : (id, label);
    }

    /// <summary>
    /// Talabni (va uning "ochildi" hodisasini) tranzaksiyaga qo'shadi.
    /// <b>SaveChanges QILMAYDI</b> — chaqiruvchi saqlaydi (ko'plab qo'shishda bitta SaveChanges).
    /// </summary>
    /// <param name="due">Qayta qo'ng'iroq sanasi ("yyyy-MM-dd"); bo'sh — darhol navbatga.</param>
    public ContactRequest Add(
        Student student, string reasonId, string reasonLabel, string note, string due,
        string actorId, string actorName)
    {
        var now = AppClock.Iso();
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var c = new ContactRequest
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            ReasonId = reasonId,
            ReasonLabel = reasonLabel,
            Note = note,
            // Sana berilgan bo'lsa darhol "qayta qo'ng'iroq" — masalan "ertaga bog'laning".
            Status = due.Length > 0 ? ContactStatuses.Callback : ContactStatuses.New,
            DueDate = due,
            CreatedAt = now,
            CreatedBy = actorName,
            LastActorName = actorName,
            LastActionAt = now,
        };
        db.ContactRequests.Add(c);

        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = c.Id,
            StudentId = c.StudentId,
            Type = ContactAttemptTypes.Created,
            NextStatus = c.Status,
            DueDate = due,
            Response = c.Note,
            ActorId = actorId,
            ActorName = actorName,
            CreatedAt = now,
            Date = today,
        });

        audit.Record(AuditEntity, c.Id, "create",
            $"Bog'lanish kerak: {c.StudentName}"
            + (reasonLabel.Length > 0 ? $" — sabab: {reasonLabel}" : "")
            + (due.Length > 0 ? $", qayta qo'ng'iroq: {due}" : ""),
            studentId: c.StudentId);

        return c;
    }

    /// <summary>
    /// Bir nechta o'quvchini navbatga qo'shadi va SAQLAYDI.
    ///
    /// <para>⚠️ Ochiq talabi bor o'quvchi CHETLAB O'TILADI, butun amal to'xtamaydi — aks holda
    /// 100 ta tanlangandan bittasi tufayli hech kim navbatga tushmasdi.</para>
    /// </summary>
    /// <param name="allowedStudentIds">Ruxsat etilgan o'quvchilar (masalan o'qituvchi guruhining
    /// a'zolari). <c>null</c> — cheklov yo'q (admin oqimi).</param>
    public async Task<BulkResult> AddManyAsync(
        IEnumerable<string> studentIds, string? reasonId, string? note, string? due,
        string actorId, string actorName, ISet<string>? allowedStudentIds = null)
    {
        var ids = studentIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct().Take(MaxBulk).ToList();
        if (ids.Count == 0) return new BulkResult(0, 0, new List<string>(), 0);

        var (rid, rlabel) = await ResolveReasonAsync(reasonId);
        var text = (note ?? "").Trim();
        var dueDate = (due ?? "").Trim();

        // Ikkita TO'PLAMLI so'rov — o'quvchi boshiga alohida so'rov ketmasin (N+1).
        var students = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var alreadyOpen = (await db.ContactRequests
                .Where(c => ids.Contains(c.StudentId)
                            && (c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback))
                .Select(c => c.StudentId).ToListAsync())
            .ToHashSet();

        var created = 0;
        var skipped = new List<string>();
        var notFound = 0;
        foreach (var id in ids)
        {
            // Ruxsat ro'yxati berilgan bo'lsa — undan tashqaridagi o'quvchi JIM chetlab o'tiladi
            // (topilmagan deb sanaladi): o'qituvchi begona o'quvchini navbatga qo'sha olmasin.
            if (allowedStudentIds is not null && !allowedStudentIds.Contains(id)) { notFound++; continue; }
            if (!students.TryGetValue(id, out var student)) { notFound++; continue; }
            if (alreadyOpen.Contains(id)) { skipped.Add(student.FullName); continue; }
            Add(student, rid, rlabel, text, dueDate, actorId, actorName);
            created++;
        }

        await db.SaveChangesAsync();
        // Ismlar ro'yxati CHEGARALANADI — 300 ta ism xabarga sig'maydi (soni baribir to'liq).
        return new BulkResult(created, skipped.Count, skipped.Take(10).ToList(), notFound);
    }

    /// <summary>O'quvchida OCHIQ talab bormi (bo'lsa — o'sha talab).</summary>
    public Task<ContactRequest?> OpenRequestAsync(string studentId) =>
        db.ContactRequests.FirstOrDefaultAsync(
            c => c.StudentId == studentId
                 && (c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback));
}
