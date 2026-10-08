using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DAVOMAT AVTO-XABARLARI — "darsga kelmadi" (<c>attendance_absent</c>) va ommaviy "keldi"
/// (<c>attendance_present</c>) qachon va KIMGA yuborilishining yagona qoidasi. Admin jurnali
/// (<c>JournalController</c>) ham, o'qituvchi ilovasi (<c>TeacherPortalController</c>) ham shu
/// yerdan foydalanadi — ikki controllerda ikki xil qoida bo'lmasin.
///
/// <para><b>"Kelmadi" nima</b> (<see cref="IsNewAbsence"/>): yangi sabab qo'yildi VA u "kech"
/// (<see cref="Domain.AbsenceReason.IsLate"/>) EMAS VA oldingi holat "kelmadi" EMAS edi (sabab
/// yo'q yoki "kech"). Ya'ni:</para>
/// <list type="bullet">
/// <item>"Kech" — o'quvchi DARSDA BO'LGAN: ota-onaga "darsga kelmadi" HECH QACHON ketmaydi;</item>
/// <item>sababni almashtirish (Sababsiz → Kasal) — yangi hodisa EMAS, ikkinchi SMS ketmaydi;</item>
/// <item>"kech" → "sababsiz" — haqiqiy o'zgarish (endi kelmadi), xabar ketadi.</item>
/// </list>
///
/// <para><b>Ommaviy yuborish FONDA va BAZADAGI NAVBAT orqali</b> (<see cref="EnqueueBulkAsync"/> →
/// <see cref="AttendanceNotifyWorker"/>): 25 o'quvchiga SMS bittalab ketadi (Local SMS'da har biri
/// orasida kutish), so'rov Cloudflare'ning 100 soniyasidan oshib ketar, o'qituvchi "xato" ko'rib qayta
/// bosar va hamma ota-ona xabarni IKKI marta olardi. Shuning uchun so'rov darhol qaytadi.
/// ⚠️ Ilgari fon ishi xotirada edi (<c>Task.Run</c>) — yuborish paytida deploy bo'lsa qolgan ota-onalar
/// xabarni JIMGINA olmasdi. Endi oluvchilar (<see cref="AttendanceNotifyItem"/>) jurnal yozuvi bilan
/// BITTA SaveChanges da yoziladi va ishchi ilova qayta ishga tushganda ham qolganini yuboradi.</para>
/// </summary>
public static class AttendanceNotify
{
    /// <summary>
    /// Shu yozuv YANGI "kelmadi" hodisasimi (sof funksiya, testlangan).
    /// <paramref name="lateIds"/> — "kech" turidagi sabablar id'lari.
    /// </summary>
    public static bool IsNewAbsence(string? oldReasonId, string? newReasonId, IReadOnlySet<string> lateIds)
    {
        if (string.IsNullOrEmpty(newReasonId) || lateIds.Contains(newReasonId)) return false;
        return string.IsNullOrEmpty(oldReasonId) || lateIds.Contains(oldReasonId);
    }

    /// <summary>"Kech" turidagi davomat sabablari id'lari.</summary>
    public static async Task<HashSet<string>> LateReasonIdsAsync(IAppDbContext db, CancellationToken ct = default) =>
        (await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "Darsga kelmadi" xabarini berilgan o'quvchilarga yuboradi (guruh + sabab bilan).
    /// Arxivlangan o'quvchi CHETLAB o'tiladi (eski arxivdagilarda a'zolik hali tirik bo'lishi mumkin).
    /// Chaqiruvchi ro'yxatni ALLAQACHON "yangi kelmadi"larga toraytirgan bo'lishi kerak.
    /// </summary>
    public static async Task DispatchAbsencesAsync(
        IAppDbContext db, AutoMessageService autoMsg, string classId, string date, string? reasonId,
        IEnumerable<string> studentIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonId)) return;
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var cls = await db.Classes.FindAsync(new object[] { classId }, ct);
        var groupName = cls?.Name ?? "";
        var reasonName = (await db.AbsenceReasons.FindAsync(new object[] { reasonId }, ct))?.Name ?? "Sababsiz";
        foreach (var sid in ids)
        {
            var s = await db.Students.FindAsync(new object[] { sid }, ct);
            if (s is null || s.IsArchived) continue;
            await autoMsg.DispatchAttendanceAbsentAsync(db, s, groupName, reasonName, date, cls, ct);
        }
    }

    /// <summary>Berilganlardan ARXIVLANMAGAN o'quvchilar (avto-xabar arxivdagiga ketmasin).</summary>
    public static async Task<List<string>> NotArchivedAsync(IAppDbContext db, IReadOnlyCollection<string> studentIds)
    {
        if (studentIds.Count == 0) return [];
        var ids = studentIds.ToList();
        var archived = (await db.Students.AsNoTracking()
                .Where(s => s.IsArchived && ids.Contains(s.Id)).Select(s => s.Id).ToListAsync())
            .ToHashSet();
        return ids.Where(id => !archived.Contains(id)).ToList();
    }

    public const string KindAbsent = "absent";
    public const string KindPresent = "present";

    /// <summary>
    /// OMMAVIY davomat xabarlarini NAVBATGA yozadi (<see cref="AttendanceNotifyItem"/>, har oluvchi —
    /// bitta qator). <see cref="JournalService.BulkAttendanceAsync"/> ning <c>beforeSave</c> chaqiruvidan
    /// ishlatiladi — ya'ni jurnal yozuvi bilan BITTA SaveChanges da saqlanadi. SaveChanges QILMAYDI.
    /// <list type="bullet">
    /// <item>"kelmadi" — faqat shu so'rovda YANGI kelmaganlarga (<c>NewlyAbsentIds</c>);</item>
    /// <item>"keldi" — yozilganlardan saqlashdan OLDIN allaqachon «keldi» bo'lmaganlarga
    /// (<paramref name="wasPresent"/>).</item>
    /// </list>
    /// Shu hodisaga yoqilgan qoida bo'lmasa qator YOZILMAYDI (dispatcher baribir hech narsa yubormasdi —
    /// jadval bekorga o'smasin). Arxiv/joriy «keldi» holati YUBORISH paytida qayta tekshiriladi.
    /// </summary>
    public static async Task<int> EnqueueBulkAsync(
        IAppDbContext db, BulkAttendanceRequest req, JournalService.BulkAttendanceResult result,
        IReadOnlySet<string> wasPresent, CancellationToken ct = default)
    {
        var now = AppClock.Now;
        var items = new List<AttendanceNotifyItem>();
        if (result.AbsentReasonId is not null && result.NewlyAbsentIds.Count > 0
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendanceAbsent, ct))
            items.AddRange(result.NewlyAbsentIds.Distinct().Select(id => Item(KindAbsent, id, result.AbsentReasonId)));
        if (!req.Absent && result.WrittenIds.Count > 0
            && await RuleEnabledAsync(db, AutoMessageTriggers.AttendancePresent, ct))
            items.AddRange(result.WrittenIds.Where(id => !wasPresent.Contains(id)).Distinct()
                .Select(id => Item(KindPresent, id, null)));
        if (items.Count > 0) db.AttendanceNotifyItems.AddRange(items);
        return items.Count;

        AttendanceNotifyItem Item(string kind, string studentId, string? reasonId) => new()
        {
            Kind = kind, ClassId = req.ClassId, SubjectId = req.SubjectId, Date = req.Date, Period = req.Period,
            ReasonId = reasonId, StudentId = studentId, Status = "pending", CreatedAt = now, UpdatedAt = now,
        };
    }

    private static Task<bool> RuleEnabledAsync(IAppDbContext db, string trigger, CancellationToken ct) =>
        db.AutoMessageRules.AnyAsync(r => r.Enabled && r.Trigger == trigger, ct);

    /// <summary>
    /// Navbatdagi BITTA oluvchiga xabarni yuboradi (<see cref="AttendanceNotifyWorker"/> chaqiradi).
    /// Yuborish paytidagi holat qayta tekshiriladi: arxivlangan o'quvchiga hech narsa ketmaydi,
    /// «keldi» esa faqat hozir ham «keldi» bo'lsa.
    /// </summary>
    public static async Task DispatchItemAsync(
        IAppDbContext db, AutoMessageService autoMsg, AttendanceNotifyItem item, CancellationToken ct = default)
    {
        var one = new[] { item.StudentId };
        if (item.Kind == KindAbsent)
            await DispatchAbsencesAsync(db, autoMsg, item.ClassId, item.Date, item.ReasonId, one, ct);
        else if (item.Kind == KindPresent)
            await autoMsg.DispatchNewlyPresentAsync(db, item.ClassId, item.SubjectId, item.Date, item.Period,
                await NotArchivedAsync(db, one), new HashSet<string>(), ct);
    }
}
