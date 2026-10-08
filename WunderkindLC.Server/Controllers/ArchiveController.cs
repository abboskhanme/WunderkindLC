using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Arxiv — o'chirilgan Lid/O'quvchi/O'qituvchi/Xodim/Guruh/Moliya yozuvlarining JSON
/// suratlari (<see cref="ArchivedRecord"/>). Ro'yxat, tiklash (asl entity'ni qaytarib
/// qo'shadi) va butunlay o'chirish. "Sozlamalar" ruxsati talab qilinadi.
///
/// <para>Tiklash ham, butunlay o'chirish ham «O'zgarishlar tarixi»ga yoziladi (tur — tiklanayotgan
/// entity'ning o'z turi, ya'ni yozuv o'sha bo'limda ko'rinadi). Yordamchi turlar
/// (<see cref="ArchiveService.HiddenTypes"/>, masalan lid tarixi) ro'yxatda KO'RINMAYDI — ular asosiy
/// yozuv bilan birga tiklanadi/o'chiriladi.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings.archive")]
[Route("api/admin/archive")]
public class ArchiveController(AppDbContext db, AuditService audit, ILogger<ArchiveController> logger) : ControllerBase
{
    private static readonly string[] Hidden = ArchiveService.HiddenTypes.ToArray();

    /// <summary>Arxiv ro'yxati (ixtiyoriy <paramref name="type"/> bo'yicha), DeletedAt kamayuvchi.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ArchivedRecordDto>>> List([FromQuery] string? type = null)
    {
        var q = db.ArchivedRecords.Where(r => !Hidden.Contains(r.Type));
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(r => r.Type == type);
        return await q
            .OrderByDescending(r => r.DeletedAt)
            .Select(r => new ArchivedRecordDto(
                r.Id, r.Type, r.EntityId, r.Title, r.Subtitle, r.Reason, r.DeletedAt, r.ActorName))
            .ToListAsync();
    }

    /// <summary>Tur bo'yicha arxiv yozuvlari soni (lead/student/teacher/staff/group/finance).</summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> Counts()
    {
        var counts = await db.ArchivedRecords
            .Where(r => !Hidden.Contains(r.Type))
            .GroupBy(r => r.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        var result = new Dictionary<string, int>
        {
            ["lead"] = 0, ["student"] = 0, ["teacher"] = 0,
            ["staff"] = 0, ["group"] = 0, ["finance"] = 0,
        };
        foreach (var c in counts) result[c.Type] = c.Count;
        return result;
    }

    /// <summary>
    /// Arxiv yozuvini TIKLASH — asl entity'ni qayta qo'shadi va arxiv yozuvini o'chiradi. Javobda
    /// ixtiyoriy <c>message</c> — tiklangandan keyin admin nima qilishi kerakligi (parol, guruh).
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var rec = await db.ArchivedRecords.FindAsync(id);
        if (rec is null) return NotFound();
        if (Hidden.Contains(rec.Type))
            return BadRequest(new { message = "Bu yordamchi yozuv — u asosiy yozuv bilan birga tiklanadi" });

        const string existsMessage = "Bu yozuv allaqachon mavjud — tiklab bo'lmaydi";
        string? message = null;
        IDisposable? gate = null;
        try
        {
            var json = rec.Json;
            switch (rec.Type)
            {
                case "lead":
                {
                    var lead = JsonSerializer.Deserialize<Lead>(json)!;
                    if (await db.Leads.AnyAsync(x => x.Id == lead.Id))
                        return BadRequest(new { message = existsMessage });
                    // DUBLIKAT: o'chirilgandan keyin shu raqam bilan YANGI lid ochilgan bo'lishi mumkin.
                    if (await ArchiveService.ConflictingLeadNameAsync(db, lead) is { } other)
                        return BadRequest(new { message = $"Bu raqam bilan boshqa lid bor: {other}" });
                    // Bosqich o'chirilgan bo'lsa — birinchi ustun (aks holda lid kanbanda ko'rinmasdi).
                    if (string.IsNullOrEmpty(lead.Stage) || !await db.LeadStages.AnyAsync(s => s.Id == lead.Stage))
                        lead.Stage = await LeadIntake.FirstStageIdAsync(db);
                    db.Leads.Add(lead);
                    var (events, trials) = await ArchiveService.RestoreLeadHistoryAsync(db, lead.Id);
                    audit.Record("Lead", lead.Id, "create",
                        $"Lid arxivdan tiklandi ({lead.FullName})"
                            + (events + trials > 0 ? $" — {events} ta hodisa, {trials} ta sinov darsi bilan" : ""));
                    break;
                }
                case "student":
                {
                    var student = JsonSerializer.Deserialize<Student>(json)!;
                    if (await db.Students.AnyAsync(x => x.Id == student.Id))
                        return BadRequest(new { message = existsMessage });
                    gate = await PaymentIdempotency.LockAsync("student:" + student.Id);
                    // MEM-5: o'chirishda oylik hisoblar va a'zoliklar HAM o'chgan — surat balansi ularni
                    // hali o'z ichiga oladi. Qolgan qatorlarga moslanadi (`StudentArchiving.RestoredBalance`).
                    var snapshotBalance = student.Balance;
                    student.Balance = StudentArchiving.RestoredBalance(
                        snapshotBalance, await StudentArchiving.BalanceFromRowsAsync(db, student.Id));
                    // Guruhi yo'q — yorliq qolsa `AccrueMonth` ning guruhsiz shoxobchasi yorliq narxi
                    // bilan o'tgan har oyni hisoblab, SMS yuborardi.
                    student.ClassName = "";
                    // Login akkaunti o'chirishda o'chgan — bog'lanish qolmasin (parol tiklashda yangisi yaratiladi).
                    if (student.UserId is not null && !await db.Users.AnyAsync(u => u.Id == student.UserId))
                        student.UserId = null;
                    db.Students.Add(student);
                    audit.Record(AuditService.EntityStudent, student.Id, "create",
                        $"O'quvchi «O'chirilganlar arxivi»dan tiklandi ({student.FullName}) — guruhsiz"
                            + (student.Balance != snapshotBalance
                                ? $"; balans qolgan yozuvlarga moslandi: {AuditService.Money(snapshotBalance)} → {AuditService.Money(student.Balance)} so'm"
                                : ""),
                        studentId: student.Id);
                    message = StudentArchiving.RestoredWithoutGroupsMessage
                              + (student.UserId is null ? ". Ilovaga kirish uchun parolni qayta yarating" : "");
                    break;
                }
                case "teacher":
                {
                    var teacher = JsonSerializer.Deserialize<Teacher>(json)!;
                    if (await db.Teachers.AnyAsync(x => x.Id == teacher.Id))
                        return BadRequest(new { message = existsMessage });
                    // Akkaunt o'qituvchi bilan birga o'chgan — yetim bog'lanish qolmasin.
                    if (teacher.UserId is not null && !await db.Users.AnyAsync(u => u.Id == teacher.UserId))
                        teacher.UserId = null;
                    db.Teachers.Add(teacher);
                    audit.Record(AuditService.EntityTeacherSalary, teacher.Id, "create",
                        $"O'qituvchi arxivdan tiklandi ({teacher.FullName})", teacherId: teacher.Id);
                    if (teacher.UserId is null)
                        message = "O'qituvchi tiklandi — ilovaga kirish uchun login/parolni qayta yarating";
                    break;
                }
                case "staff":
                {
                    var user = JsonSerializer.Deserialize<AppUser>(json)!;
                    if (await db.Users.AnyAsync(x => x.Id == user.Id))
                        return BadRequest(new { message = existsMessage });
                    // Login (Email) band bo'lsa — tiklab bo'lmaydi (boshqa user shu login bilan turibdi).
                    if (await db.Users.AnyAsync(u => u.Email == user.Email && u.Id != user.Id))
                        return BadRequest(new { message = "Bu login band — tiklab bo'lmaydi" });
                    // Xavfsizlik: eski ruxsatlarni jimgina tiklamaymiz — admin qayta beradi.
                    user.Permissions = new();
                    // ⚠️ ROL bog'lanishi ham uziladi: u qolsa rolning KEYINGI tahriri
                    // (`StaffRoles.SyncMembersAsync`) eski ruxsatlarni jimgina qaytarib yozardi.
                    user.RoleTemplateId = null;
                    // Parol suratda saqlanmaydi (`ArchiveService.StripSecrets`); eski suratda bo'lsa ham
                    // QAYTARILMAYDI — o'chirilgan odamning eski paroli bilan kirish ochilib qolmasin.
                    user.BlockLogin();
                    user.FailedLoginCount = 0;
                    user.LockoutUntil = null;
                    db.Users.Add(user);
                    audit.Record("Staff", user.Id, "create",
                        $"Xodim arxivdan tiklandi: {user.FullName} — rol/ruxsatlarsiz, parolsiz");
                    message = "Xodim tiklandi — parolni qayta o'rnating (Xodimlar → Parolni tiklash) va rolini bering";
                    break;
                }
                case "group":
                {
                    var group = JsonSerializer.Deserialize<Group>(json)!;
                    if (await db.Classes.AnyAsync(x => x.Id == group.Id))
                        return BadRequest(new { message = existsMessage });
                    db.Classes.Add(group);
                    audit.Record(AuditService.EntityGroup, group.Id, "create",
                        $"Guruh arxivdan tiklandi ({group.Name})");
                    break;
                }
                case "finance":
                {
                    var tx = JsonSerializer.Deserialize<FinanceTransaction>(json)!;
                    if (await db.FinanceTransactions.IgnoreQueryFilters().AnyAsync(x => x.Id == tx.Id))
                        return BadRequest(new { message = existsMessage });
                    if (!string.IsNullOrEmpty(tx.StudentId))
                        gate = await PaymentIdempotency.LockAsync("student:" + tx.StudentId);
                    var isRefund = tx.Direction == "expense" && tx.Category == "refund";
                    if (isRefund && tx.RefundOfId is not null)
                    {
                        // Vozvrat asl to'lovsiz yoki undan KO'P bo'lib qaytmasin (Refund endpointidagi cheklov).
                        var original = await db.FinanceTransactions.AsNoTracking()
                            .FirstOrDefaultAsync(t => t.Id == tx.RefundOfId);
                        if (original is null)
                            return BadRequest(new { message = "Asl to'lov topilmadi (o'chirilgan yoki bekor qilingan) — avval asl to'lovni tiklang" });
                        var already = (await db.FinanceTransactions.AsNoTracking()
                            .Where(r => r.Direction == "expense" && r.Category == "refund" && r.RefundOfId == tx.RefundOfId)
                            .Select(r => r.Amount).ToListAsync()).Sum();
                        if (already + tx.Amount > original.Amount)
                            return BadRequest(new { message = $"Bu to'lovdan allaqachon {AuditService.Money(already)} so'm qaytarilgan — vozvratni tiklasa to'lov summasidan oshadi" });
                    }
                    // Kassalardan oldingi arxiv yozuvi (kassasiz) yoki kassasi endi yo'q — standart kassaga.
                    if (tx.CashboxId is null || !await db.Cashboxes.AnyAsync(b => b.Id == tx.CashboxId))
                        tx.CashboxId = (await CashboxService.ResolveAsync(db, null, null)).CashboxId;
                    db.FinanceTransactions.Add(tx);
                    // O'chirishda balans QAYTARILGAN edi (FinanceController.Delete) — tiklashda QAYTA qo'llaymiz:
                    // o'quv to'lovi balansni oshiradi, VOZVRAT esa kamaytiradi (`StudentBalanceEffect`).
                    var effect = string.IsNullOrEmpty(tx.StudentId) ? 0m
                        : tx.Direction == "income" && tx.Category == "tuition" ? tx.Amount
                        : isRefund ? -tx.Amount
                        : 0m;
                    if (effect != 0m)
                    {
                        var student = await db.Students.FindAsync(tx.StudentId);
                        if (student is not null)
                        {
                            // Qulf ICHIDA qayta o'qiladi (parallel to'lov ta'siri ustidan yozilmasin).
                            await StudentBalanceSync.ReloadAsync(db, student);
                            student.Balance += effect;
                        }
                    }
                    audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
                        $"Pul amali arxivdan tiklandi: {(isRefund ? "vozvrat" : tx.Direction == "income" ? "kirim" : "chiqim")} "
                            + $"{AuditService.Money(tx.Amount)} so'm ({tx.Date})",
                        after: AuditService.Snapshot(tx), studentId: tx.StudentId);
                    break;
                }
                default:
                    return BadRequest(new { message = $"Noma'lum arxiv turi: {rec.Type}" });
            }

            db.ArchivedRecords.Remove(rec);
            await db.SaveChangesAsync();
        }
        // ⚠️ `ex.Message` klientga QAYTARILMAYDI: `try` endi `SaveChanges` ni ham o'raydi va EF/Npgsql
        // xatosi jadval/ustun nomlari, kalit qiymatlari va SQL bo'laklarini o'z ichiga oladi. Kutilgan
        // holatlar (mavjud yozuv, band login, vozvrat chegarasi) yuqorida o'z sababi bilan 400 qaytaradi;
        // bu yer — kutilmagan xato: to'liq matn LOGGA, foydalanuvchiga umumiy o'zbekcha sabab.
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Arxiv surati o'qilmadi: {Id} ({Type})", rec.Id, rec.Type);
            return BadRequest(new { message = "Tiklab bo'lmadi: arxiv surati buzilgan yoki eski formatda." });
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Arxivdan tiklashda saqlash xatosi: {Id} ({Type})", rec.Id, rec.Type);
            return BadRequest(new
            {
                message = "Tiklab bo'lmadi: bazaga saqlanmadi — bog'liq yozuv (kurs, o'qituvchi, guruh) o'chirilgan "
                          + "yoki shu ma'lumot allaqachon mavjud bo'lishi mumkin.",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Arxivdan tiklashda kutilmagan xato: {Id} ({Type})", rec.Id, rec.Type);
            return BadRequest(new { message = "Tiklab bo'lmadi: kutilmagan xato (tafsiloti server jurnalida)." });
        }
        finally
        {
            gate?.Dispose();
        }
        return Ok(new { ok = true, message });
    }

    /// <summary>Arxiv yozuvini BUTUNLAY o'chirish (faqat surat o'chadi, boshqa narsa emas). Lid
    /// suratining yordamchi tarixi (<see cref="ArchiveService.TypeLeadHistory"/>) ham birga o'chadi.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Purge(string id)
    {
        var rec = await db.ArchivedRecords.FindAsync(id);
        if (rec is null) return NotFound();
        db.ArchivedRecords.Remove(rec);
        if (rec.Type == "lead")
            db.ArchivedRecords.RemoveRange(await ArchiveService.LeadHistoryRecordsAsync(db, rec.EntityId));
        audit.Record(AuditEntityOf(rec.Type), rec.EntityId, "delete",
            $"Arxivdagi surat butunlay o'chirildi ({TypeLabel(rec.Type)}): {rec.Title}"
                + (string.IsNullOrWhiteSpace(rec.Subtitle) ? "" : $" — {rec.Subtitle}"));
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Arxiv turi → audit turi (yozuv o'sha bo'limning tarixida ko'rinsin, `AuditSections`).</summary>
    private static string AuditEntityOf(string type) => type switch
    {
        "lead" or ArchiveService.TypeLeadHistory => "Lead",
        "student" => AuditService.EntityStudent,
        "teacher" => AuditService.EntityTeacherSalary,
        "staff" => "Staff",
        "group" => AuditService.EntityGroup,
        "finance" => AuditService.EntityFinanceTransaction,
        _ => "ArchivedRecord",
    };

    private static string TypeLabel(string type) => type switch
    {
        "lead" => "lid",
        ArchiveService.TypeLeadHistory => "lid tarixi",
        "student" => "o'quvchi",
        "teacher" => "o'qituvchi",
        "staff" => "xodim",
        "group" => "guruh",
        "finance" => "pul amali",
        _ => type,
    };
}
