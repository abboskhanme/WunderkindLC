using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «Davomat intizomi» va «Davomat xabarlari» sozlamalari (SPEC P3.2, <c>.claude/rules/journal.md</c>):
/// davomat muddati, o'qituvchiga eslatmalar, administratorga xabar, jarima (U3c) va «kelmadi» xabari
/// vaqti/tasdiqlash rejimi (U3b). Ruxsat — jurnal siyosati bilan bir xil darvoza: <c>classes</c>
/// (yozish — bo'limning tahrir amali).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes")]
[Route("api/admin/attendance/settings")]
public class AttendanceSettingsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttendanceSettingsDto>> Get() =>
        await WithWarningsAsync(AttendanceSettingsRules.FromMeta(await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync()));

    /// <summary>Saqlash — har qoida <see cref="AttendanceSettingsRules.Validate"/> da (400 + sabab).
    /// O'zgarish "O'zgarishlar tarixi"ga QIYMATLARI bilan yoziladi (jarima summasi — pul).</summary>
    [HttpPut]
    public async Task<ActionResult<AttendanceSettingsDto>> Save(AttendanceSettingsDto req)
    {
        var dto = req with
        {
            ReminderMinutes = req.ReminderMinutes ?? [],
            AbsentNoticeApproval = (req.AbsentNoticeApproval ?? "").Trim(),
            Warnings = null,
        };
        if (AttendanceSettingsRules.Validate(dto) is { } error)
            return BadRequest(new { message = error });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            meta = new CenterMeta();
            db.CenterMeta.Add(meta);
        }
        AttendanceSettingsRules.Apply(meta, dto);
        SettingsAudit.Record<CenterMeta>(audit, db.ChangeTracker, "Davomat intizomi",
            withValues: true, labels: AttendanceSettingsRules.AuditLabels);
        await db.SaveChangesAsync();
        return await WithWarningsAsync(AttendanceSettingsRules.FromMeta(meta));
    }

    /// <summary>Eski «Davomat eslatmasi» avto-xabar qoidasi (dars boshlangach) hali yoqilgan bo'lsa — ikki
    /// eslatma ogohlantirishi (qaror adminniki, saqlashni to'xtatmaydi).</summary>
    private async Task<AttendanceSettingsDto> WithWarningsAsync(AttendanceSettingsDto dto)
    {
        var warnings = new List<string>();
        if (dto.DisciplineEnabled && dto.ReminderMinutes.Count > 0
            && await db.AutoMessageRules.AsNoTracking().AnyAsync(r =>
                r.Enabled && r.Trigger == AutoMessageTriggers.LessonAttendance
                && r.SendScope == ReminderSendScopes.LessonStart))
            warnings.Add(AttendanceSettingsRules.DoubleReminderWarning);
        return dto with { Warnings = warnings };
    }
}
