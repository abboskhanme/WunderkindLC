using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KASSALAR (Moliya → «Kassalar», `docs/SPEC-kassalar.md`). Qoida — <see cref="CashboxService"/>.
///
/// <para><b>Ruxsat:</b> tanlov ro'yxati (<c>GET options</c>) — to'lov oynasi uchun, admin panelidagi
/// har kimga (kassir ham, `kassa` ruxsati bilan). Qoldiqlar — `finance.main` (ReadRequiresPerm).
/// Yaratish/tahrirlash/arxivlash — <b>FAQAT superadmin</b>: HARD rol tekshiruvi, chunki
/// <see cref="AdminPermAttribute"/> admin'ni ham cheklovsiz o'tkazadi («Aktiv muzlatish» bilan bir xil
/// sabab, `year-freeze.md` §3). Yangi ruxsat kaliti YO'Q.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main")]
[Route("api/admin/cashboxes")]
public class CashboxesController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    /// <summary>Faqat superadmin — 403 SABAB bilan (jim rad etilmaydi).</summary>
    private ObjectResult? DenyUnlessSuperAdmin() =>
        User.IsInRole(Roles.SuperAdmin) ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Kassani faqat superadmin yaratadi va tahrirlaydi" });

    /// <summary>To'lov oynasidagi «Kassa» tanlovi: faol kassalar + shu foydalanuvchining standart kassasi.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<CashboxOptionsDto>> Options()
    {
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        var uid = UserId;
        return new CashboxOptionsDto(
            CashboxService.Active(boxes)
                .Select(b => new CashboxOptionDto(b.Id, b.Name, b.ResponsibleName, uid is not null && b.ResponsibleUserId == uid))
                .ToList(),
            CashboxService.PickDefault(boxes, uid)?.Id);
    }

    /// <summary>Kassalar kartalari: qoldiq (butun tarix) + davr ichidagi kirim/chiqim. Faollar avval.</summary>
    [HttpGet]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<CashboxDto>>> List([FromQuery] string? from, [FromQuery] string? to)
    {
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        var balances = await CashboxService.BalancesAsync(db, from, to);
        return CashboxService.Active(boxes)
            .Concat(boxes.Where(b => b.IsArchived).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            .Select(b => ToDto(b, balances.GetValueOrDefault(b.Id)))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CashboxDto>> Create(CashboxPayload p)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(p.Name, boxes, null, out var name) is { } nameError)
            return BadRequest(new { message = nameError });
        var (respId, respName, respError) = await ResponsibleAsync(p.ResponsibleUserId);
        if (respError is not null) return BadRequest(new { message = respError });
        if (OpeningError(p) is { } openingError) return BadRequest(new { message = openingError });

        var box = new Cashbox
        {
            Name = name, ResponsibleUserId = respId, ResponsibleName = respName,
            CreatedAt = AppClock.Iso(), CreatedByName = ActorName,
            Kind = CashboxService.NormalizeKind(p.Kind) ?? "cash",
            OpeningBalance = p.OpeningBalance ?? 0m,
            OpeningDate = string.IsNullOrWhiteSpace(p.OpeningDate) ? null : p.OpeningDate.Trim(),
        };
        db.Cashboxes.Add(box);
        audit.Record(AuditService.EntityCashbox, box.Id, "create",
            $"Kassa yaratildi: «{name}»" + (respName is null ? "" : $" — mas'ul: {respName}"));
        await db.SaveChangesAsync();
        return ToDto(box, null);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CashboxDto>> Update(string id, CashboxPayload p)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(p.Name, boxes, id, out var name) is { } nameError)
            return BadRequest(new { message = nameError });
        var (respId, respName, respError) = await ResponsibleAsync(p.ResponsibleUserId);
        if (respError is not null) return BadRequest(new { message = respError });

        if (OpeningError(p) is { } openingError) return BadRequest(new { message = openingError });

        var changes = new List<string>();
        if (box.Name != name) changes.Add($"nomi «{box.Name}» → «{name}»");
        if (box.ResponsibleUserId != respId) changes.Add($"mas'ul {box.ResponsibleName ?? "—"} → {respName ?? "—"}");
        box.Name = name;
        box.ResponsibleUserId = respId;
        box.ResponsibleName = respName;
        // Turi va boshlang'ich qoldiq — faqat yuborilganda (eski forma ularni yubormaydi).
        if (p.Kind is not null && CashboxService.NormalizeKind(p.Kind) is { } kind && kind != box.Kind)
        {
            changes.Add($"turi {KindLabel(box.Kind)} → {KindLabel(kind)}");
            box.Kind = kind;
        }
        if (p.OpeningBalance is { } ob && ob != box.OpeningBalance)
        {
            changes.Add($"boshlang'ich qoldiq {AuditService.Money(box.OpeningBalance)} → {AuditService.Money(ob)} so'm");
            box.OpeningBalance = ob;
        }
        if (p.OpeningDate is not null)
        {
            var od = string.IsNullOrWhiteSpace(p.OpeningDate) ? null : p.OpeningDate.Trim();
            if (od != box.OpeningDate)
            {
                changes.Add($"boshlang'ich sana {box.OpeningDate ?? "—"} → {od ?? "—"}");
                box.OpeningDate = od;
            }
        }
        if (changes.Count > 0)
            audit.Record(AuditService.EntityCashbox, box.Id, "update", $"Kassa tahrirlandi: «{name}» — " + string.Join(", ", changes));
        await db.SaveChangesAsync();
        return ToDto(box, (await CashboxService.BalancesAsync(db, null, null)).GetValueOrDefault(box.Id));
    }

    /// <summary>Arxivlash: yangi pul qabul qilmaydi, qoldig'i va tarixi saqlanadi. «Asosiy kassa» — yo'q.</summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        if (box.Id == CashboxService.MainId)
            return BadRequest(new { message = "«Asosiy kassa» arxivlanmaydi — mas'uli yo'q to'lovlar shu kassaga tushadi" });
        if (box.IsArchived) return Ok(new { ok = true });

        var balance = (await CashboxService.BalancesAsync(db, null, null)).GetValueOrDefault(box.Id)?.Total ?? 0m;
        box.IsArchived = true;
        audit.Record(AuditService.EntityCashbox, box.Id, "update",
            $"Kassa arxivlandi: «{box.Name}»" + (balance != 0 ? $" (qoldig'i {AuditService.Money(balance)} so'm)" : ""));
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        if (!box.IsArchived) return Ok(new { ok = true });
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(box.Name, boxes, box.Id, out _) is { } nameError)
            return BadRequest(new { message = nameError + " — avval birini qayta nomlang" });

        box.IsArchived = false;
        audit.Record(AuditService.EntityCashbox, box.Id, "update", $"Kassa arxivdan tiklandi: «{box.Name}»");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>KASSA KO'CHIRMASI (SPEC-backlog F3 K1): davr boshidagi qoldiq, kirim, chiqim, ko'chirishlar, yakuniy qoldiq
    /// va davr harakatlari. `finance.main` (O'QISH ham — summalar).</summary>
    [HttpGet("{id}/statement")]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<CashboxStatementDto>> Statement(string id, [FromQuery] string? from, [FromQuery] string? to)
    {
        var today = AppClock.Today;
        var t = DateOnly.TryParseExact(to ?? "", "yyyy-MM-dd", out var tt) ? tt : today;
        var f = DateOnly.TryParseExact(from ?? "", "yyyy-MM-dd", out var ff) ? ff : new DateOnly(t.Year, t.Month, 1);
        if (f > t) return BadRequest(new { message = "Davr boshi oxiridan keyin bo'lishi mumkin emas" });
        if (t.DayNumber - f.DayNumber > 366) return BadRequest(new { message = "Davr bir yildan uzun bo'lmasin" });
        var box = await db.Cashboxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        var st = await CashboxService.StatementAsync(db, id, f.ToString("yyyy-MM-dd"), t.ToString("yyyy-MM-dd"));
        if (st is null) return NotFound(new { message = "Kassa topilmadi" });

        var sIds = st.Rows.Select(r => r.StudentId).OfType<string>().Distinct().ToList();
        var tIds = st.Rows.Select(r => r.TeacherId).OfType<string>().Distinct().ToList();
        var uIds = st.Rows.Select(r => r.StaffUserId).OfType<string>().Distinct().ToList();
        var typeIds = st.Rows.Select(r => r.TypeId).OfType<string>().Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => sIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName);
        var teachers = await db.Teachers.AsNoTracking().Where(x => tIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName);
        var users = await db.Users.AsNoTracking().Where(u => uIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var types = await db.TransactionTypes.AsNoTracking().Where(x => typeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var boxes = await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);
        string? Party(CashboxService.StatementRow r) =>
            r.StudentId is not null ? students.GetValueOrDefault(r.StudentId)
            : r.TeacherId is not null ? teachers.GetValueOrDefault(r.TeacherId)
            : r.StaffUserId is not null ? users.GetValueOrDefault(r.StaffUserId)
            : r.OtherCashboxId is not null ? boxes.GetValueOrDefault(r.OtherCashboxId)
            : null;
        return new CashboxStatementDto(
            box.Id, box.Name, box.Kind, st.From, st.To, st.Opening, st.Income, st.Expense, st.TransferIn, st.TransferOut, st.Closing,
            st.Rows.Select(r => new CashboxStatementRowDto(
                r.Id, r.Date, r.Kind, r.Category, r.TypeId is null ? null : types.GetValueOrDefault(r.TypeId),
                r.Amount, r.Note, Party(r), r.CreatedBy)).ToList());
    }

    private static string KindLabel(string kind) => kind == "bank" ? "bank hisob raqami" : "naqd";

    /// <summary>Tur va boshlang'ich sana tekshiruvi (null — xato yo'q).</summary>
    private static string? OpeningError(CashboxPayload p)
    {
        if (p.Kind is not null && CashboxService.NormalizeKind(p.Kind) is null) return "Kassa turi «naqd» yoki «bank» bo'lsin";
        if (!string.IsNullOrWhiteSpace(p.OpeningDate))
        {
            if (!DateOnly.TryParseExact(p.OpeningDate.Trim(), "yyyy-MM-dd", out var od))
                return "Boshlang'ich qoldiq sanasi noto'g'ri (yyyy-MM-dd)";
            // Kelajak sana barcha joriy harakatlarni qoldiqdan chiqarib yuborardi (ko'chirishning minus tekshiruvi ham).
            if (od > AppClock.Today) return "Boshlang'ich qoldiq sanasi kelajakda bo'lmasin";
        }
        return null;
    }

    /// <summary>Mas'ul — admin paneli akkaunti (xodim/admin/superadmin); bo'sh = mas'ulsiz.</summary>
    private async Task<(string? Id, string? Name, string? Error)> ResponsibleAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return (null, null, null);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Trim()
                        && (u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
            .Select(u => new { u.Id, u.FullName })
            .FirstOrDefaultAsync();
        return user is null ? (null, null, "Mas'ul xodim topilmadi") : (user.Id, user.FullName, null);
    }

    private static CashboxDto ToDto(Cashbox b, CashboxService.Balance? balance) =>
        new(b.Id, b.Name, b.ResponsibleUserId, b.ResponsibleName, b.Id == CashboxService.MainId, b.IsArchived,
            balance?.Total ?? b.OpeningBalance, balance?.PeriodIncome ?? 0m, balance?.PeriodExpense ?? 0m, b.CreatedAt,
            balance?.PeriodTransferIn ?? 0m, balance?.PeriodTransferOut ?? 0m,
            b.Kind, b.OpeningBalance, b.OpeningDate);
}
