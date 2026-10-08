using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUHNI BUTUNLAY O'CHIRISH — pul himoyasi va guruhning o'quv/yordamchi ma'lumotini tozalash
/// (<b>YAGONA joy</b>, <c>ClassesController.Delete</c> chaqiradi, bitta tranzaksiya ichida).
///
/// <para><b>PUL HIMOYASI:</b> guruhga teglangan (bekor qilinmagan) to'lov yoki vozvrat bo'lsa —
/// O'CHIRILMAYDI (400, "arxivlang"). Ilgari hisoblar TO'LANGAN-TO'LANMAGANIGA qaramay balansga
/// qaytarilar edi: 9 oy to'lagan o'quvchi bir zumda 2,7 mln "avans" olardi, to'lovlar esa guruh
/// tegisiz qolib boshqa guruhlar o'qituvchilariga taqsimlanib ketardi. Faqat TO'LANMAGAN hisoblar
/// qolgan guruh (xato ochilgan guruh) o'chiriladi — ularning qarzi balansdan olib tashlanadi
/// (<see cref="MembershipBilling.CreditAndDropGroupChargesAsync"/>).</para>
/// </summary>
public static class GroupDeletion
{
    /// <summary>To'lov bor guruh uchun sabab matni (400).</summary>
    public const string HasPaymentsMessage = "Bu guruhda to'lovlar bor — o'chirib bo'lmaydi. Guruhni arxivlang.";

    /// <summary>Taqsimotdan qoladigan tiyin-darajadagi farq "to'lov" deb sanalmasin.</summary>
    private const decimal Epsilon = 0.5m;

    /// <summary>
    /// Guruhni o'chirishga PUL to'sqinlik qiladimi. <c>null</c> — to'siq yo'q; aks holda sabab matni.
    /// <list type="number">
    ///   <item>guruhga TEGLANGAN, bekor qilinmagan o'qish to'lovi yoki vozvrat (global filtr
    ///     bekor qilinganlarni o'zi chiqaradi);</item>
    ///   <item>ESKI teglanmagan (<c>GroupId = null</c>) to'lovlarning shu guruhga TUSHADIGAN ulushi —
    ///     per-guruh balans qoidasi bilan (<see cref="GroupBalanceService"/>: to'langan − hisoblangan).
    ///     Ulush musbat bo'lsa hisoblarning bir qismi TO'LANGAN — ularni balansga qaytarish yana
    ///     soxta avans yasardi.</item>
    /// </list>
    /// </summary>
    public static async Task<string?> MoneyBlockReasonAsync(IAppDbContext db, string groupId, CancellationToken ct = default)
    {
        var tagged = await db.FinanceTransactions.AsNoTracking()
            .AnyAsync(t => t.GroupId == groupId
                           && ((t.Direction == "income" && t.Category == "tuition")
                               || (t.Direction == "expense" && t.Category == "refund")), ct);
        if (tagged) return HasPaymentsMessage;

        var charged = (await db.MonthlyCharges.AsNoTracking()
                .Where(c => c.GroupId == groupId)
                .Select(c => new { c.StudentId, c.Amount, c.Discount })
                .ToListAsync(ct))
            .GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(c => Math.Max(0m, c.Amount - c.Discount)));
        if (charged.Count == 0) return null;

        var balances = await GroupBalanceService.ForGroupAsync(db, groupId, charged.Keys);
        foreach (var (sid, owed) in charged)
            if (balances.GetValueOrDefault(sid, 0m) + owed > Epsilon) return HasPaymentsMessage;
        return null;
    }

    /// <summary>Tozalash natijasi — audit matni uchun sonlar va saqlashdan KEYIN diskdan o'chiriladigan
    /// fayllar (<c>/uploads/...</c>).</summary>
    public sealed record PurgeResult(
        int Journal, int Grades, int Tests, int Other, int DiscountsCancelled, List<string> FileUrls);

    /// <summary>
    /// Guruhning O'QUV va YORDAMCHI ma'lumotini olib tashlaydi (a'zoliklar, jurnal, dars eslatmalari
    /// va hisoblar chaqiruvchida). Har jadval bo'yicha qaror (<c>billing.md</c> «GURUHNI O'CHIRISH»):
    /// <list type="bullet">
    ///   <item><b>O'CHADI</b> (guruhsiz ma'nosiz, aks holda "yo'q guruhdan" ball/test qoladi):
    ///     mezonli baholar (<c>CriterionGrades</c>), ball tuzatishlari (<c>StudentBallAdjustments</c>),
    ///     guruh mezonlari, dars ko'chirishlari, o'quv dasturi o'tilishi, guruh testlari (ballari,
    ///     tashqi ishtirokchilar, test SERTIFIKATLARI va ularning fayllari) + bot sessiyalari,
    ///     o'qituvchi tarixi (<c>GroupTeacherAssignments</c>), AI tahlillari, «Chegirma (kelmagan
    ///     darslar)» qatorlari (ularning hisobi ham o'chadi), ketish sabablari jurnali (a'zoliklar
    ///     o'chgani uchun ularni hech kim o'qimaydi), guruh CHATI (nom bo'shaydi — yangi shu nomli
    ///     guruh eski yozishmani meros olmasin; nom boshqa guruhda ham bo'lsa tegilmaydi).</item>
    ///   <item><b>BEKOR QILINADI</b>: shu guruhga qaratilgan AMALDAGI chegirmalar (tarix snapshot bilan qoladi).</item>
    ///   <item><b>QOLADI</b> (tarix/boshqa bo'lim): o'qituvchi haqidagi fikrlar, lid sinov darslari,
    ///     uzaytirish hodisalari (snapshot nomlar bilan), o'quvchining mashq urinishlari, xabar
    ///     tarixi (<c>Broadcast</c>), audit.</item>
    /// </list>
    /// O'rinbosarlar — <see cref="SubstituteGroupLifecycle.RemoveAllAsync"/> (audit bilan).
    /// <para><c>SaveChanges</c> QILMAYDI — chaqiruvchi tranzaksiyasida saqlaydi.</para>
    /// </summary>
    public static async Task<PurgeResult> PurgeLearningDataAsync(
        IAppDbContext db, Group group, string actor, CancellationToken ct = default)
    {
        var id = group.Id;
        var files = new List<string>();

        var criterionGrades = await db.CriterionGrades.Where(x => x.GroupId == id).ToListAsync(ct);
        db.CriterionGrades.RemoveRange(criterionGrades);
        var adjustments = await db.StudentBallAdjustments.Where(x => x.GroupId == id).ToListAsync(ct);
        db.StudentBallAdjustments.RemoveRange(adjustments);
        var criteria = await db.GroupGradingCriteria.Where(x => x.GroupId == id).ToListAsync(ct);
        db.GroupGradingCriteria.RemoveRange(criteria);

        var reschedules = await db.LessonReschedules.Where(x => x.ClassId == id).ToListAsync(ct);
        db.LessonReschedules.RemoveRange(reschedules);
        var curriculum = await db.GroupCurriculumLogs.Where(x => x.GroupId == id).ToListAsync(ct);
        db.GroupCurriculumLogs.RemoveRange(curriculum);

        // TESTLAR: ballar / tashqi natijalar / sertifikat yozuvlari ANIQ o'chiriladi (bazadagi FK
        // kaskadiga tayanilmaydi — provayderga bog'liq bo'lmasin). Sertifikat va savollar fayllari esa
        // diskda qolardi — manzillar OLDINDAN yig'iladi va saqlashdan KEYIN o'chiriladi.
        var tests = await db.TestResults.Where(t => t.GroupId == id).ToListAsync(ct);
        var testIds = tests.Select(t => t.Id).ToList();
        if (testIds.Count > 0)
        {
            var certs = await db.TestCertificates.Where(c => testIds.Contains(c.TestResultId)).ToListAsync(ct);
            files.AddRange(certs.SelectMany(c => new[] { c.DocxUrl, c.PdfUrl }));
            db.TestCertificates.RemoveRange(certs);
            db.TestScores.RemoveRange(await db.TestScores.Where(x => testIds.Contains(x.TestResultId)).ToListAsync(ct));
            db.ExternalTestScores.RemoveRange(
                await db.ExternalTestScores.Where(x => testIds.Contains(x.TestResultId)).ToListAsync(ct));
            // Savollar PDF'i — faqat boshqa (o'chmaydigan) test uni ishlatmasa.
            var pdfs = tests.Select(t => t.PdfUrl).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            if (pdfs.Count > 0)
            {
                var shared = await db.TestResults.AsNoTracking()
                    .Where(t => !testIds.Contains(t.Id) && pdfs.Contains(t.PdfUrl))
                    .Select(t => t.PdfUrl).ToListAsync(ct);
                files.AddRange(pdfs.Except(shared));
            }
            db.TestBotSessions.RemoveRange(
                await db.TestBotSessions.Where(s => testIds.Contains(s.TestResultId)).ToListAsync(ct));
            db.TestResults.RemoveRange(tests);
        }

        var other = 0;
        var history = await db.GroupTeacherAssignments.Where(x => x.GroupId == id).ToListAsync(ct);
        db.GroupTeacherAssignments.RemoveRange(history);
        other += history.Count;
        var ai = await db.GroupAiAnalyses.Where(x => x.GroupId == id).ToListAsync(ct);
        db.GroupAiAnalyses.RemoveRange(ai);
        other += ai.Count;
        var lessonDiscounts = await db.LessonDiscounts.Where(x => x.GroupId == id).ToListAsync(ct);
        db.LessonDiscounts.RemoveRange(lessonDiscounts);
        other += lessonDiscounts.Count;
        var reasonLogs = await db.MembershipReasonLogs.Where(x => x.GroupId == id).ToListAsync(ct);
        db.MembershipReasonLogs.RemoveRange(reasonLogs);
        other += reasonLogs.Count;

        // GURUH CHATI nom bilan bog'langan (GroupNames) — nom bo'shagach yangi guruh uni meros
        // olmasin. Eski dublikat (shu nomli boshqa guruh) bo'lsa chat o'sha guruhniki, tegilmaydi.
        var nameShared = await db.Classes.AsNoTracking().AnyAsync(c => c.Id != id && c.Name == group.Name, ct);
        if (!nameShared && !string.IsNullOrEmpty(group.Name))
        {
            var chat = await db.ChatMessages.Where(m => m.ClassName == group.Name).ToListAsync(ct);
            db.ChatMessages.RemoveRange(chat);
            other += chat.Count;
        }

        // Shu guruhga qaratilgan AMALDAGI chegirmalar — guruh yo'q bo'lgach hech qachon qo'llanmaydi;
        // bekor qilinadi (tarix snapshot nomlari bilan qoladi), ko'zgu yangilanadi.
        var discounts = await db.StudentDiscounts
            .Where(d => d.GroupId == id && d.Status == StudentDiscount.StatusActive).ToListAsync(ct);
        var now = AppClock.Iso();
        foreach (var d in discounts)
        {
            d.Status = StudentDiscount.StatusCancelled;
            d.CancelReason = "Guruh o'chirildi";
            d.EndedAt = now;
            d.EndedBy = actor;
        }
        foreach (var sid in discounts.Select(d => d.StudentId).Distinct())
            if (await db.Students.FirstOrDefaultAsync(s => s.Id == sid, ct) is { } st)
                await StudentDiscountService.RefreshMirrorAsync(db, st);

        return new PurgeResult(
            Journal: reschedules.Count + curriculum.Count,
            Grades: criterionGrades.Count + adjustments.Count + criteria.Count,
            Tests: tests.Count,
            Other: other,
            DiscountsCancelled: discounts.Count,
            FileUrls: files.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList());
    }
}
