using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// HISOBNI TUZATISH vositalari (Moliya). Ikkita: «Qo'shilgan oy hisobini tuzatish» — aktiv qilib qo'shilganda
/// hisoblanmay qolgan aktivlashtirish oyi (<see cref="ActivationChargeRepair"/>); «To'lov oyini tuzatish» —
/// noto'g'ri (eski, kam qoldiqli) oyga yozilgan to'lovlar ortig'ini keyingi oylarga (<see cref="PaymentMonthRepair"/>).
///
/// <para><b>Ruxsat — FAQAT superadmin</b> (HARD rol, <c>BranchesController</c> naqshi): ommaviy pul
/// tuzatish. <c>[AdminPerm]</c> oddiy admin'ni ham o'tkazib yuborardi (`year-freeze.md` §3).</para>
///
/// <para>Ikki qadam, ATAYIN: <c>GET</c> — ro'yxat (hech narsa yozmaydi), <c>POST</c> — faqat TANLANGAN
/// kalitlar. Server ro'yxatni qayta quradi va klientdan kelgan summaga ISHONMAYDI.</para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.SuperAdmin)]
[Route("api/admin/billing-repair")]
public class BillingRepairController(AppDbContext db, AuditService audit, ILogger<BillingRepairController> logger)
    : ControllerBase
{
    /// <summary>Ko'pi bilan shuncha a'zolik bir so'rovda tuzatiladi.</summary>
    public const int MaxApply = 1000;

    public sealed record ActivationChargeRowDto(
        string Key, string StudentId, string StudentName, string GroupId, string GroupName,
        string ActivatedAt, string Month, decimal Amount, decimal Balance);

    public sealed record ApplyRequest(List<string>? Keys);

    public sealed record ApplyResultDto(int Applied, decimal Total, int Skipped, List<string> Errors);

    /// <summary>Hisobsiz qolgan aktivlashtirish oylari (joriy va o'tgan oy) — HECH NARSA YOZMAYDI.</summary>
    [HttpGet("activation-charges")]
    public async Task<ActionResult<List<ActivationChargeRowDto>>> ActivationCharges(CancellationToken ct)
    {
        var list = await ActivationChargeRepair.FindAsync(db, ct);
        return list.Select(c => new ActivationChargeRowDto(
            ActivationChargeRepair.Key(c.StudentId, c.GroupId, c.Month), c.StudentId, c.StudentName,
            c.GroupId, c.GroupName, c.ActivatedAt, c.Month, c.Amount, c.Balance)).ToList();
    }

    /// <summary>
    /// TANLANGAN qatorlarga qisman hisob yozadi. Har o'quvchi o'z qulfi ostida va ALOHIDA saqlanadi —
    /// bittasining xatosi qolganlarini to'xtatmaydi (ommaviy aktivlashtirish naqshi).
    /// </summary>
    [HttpPost("activation-charges")]
    public async Task<ActionResult<ApplyResultDto>> ApplyActivationCharges(ApplyRequest req, CancellationToken ct)
    {
        var keys = (req.Keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0) return BadRequest(new { message = "Hech narsa tanlanmagan" });
        if (keys.Count > MaxApply) return BadRequest(new { message = $"Bir martada ko'pi bilan {MaxApply} ta" });

        // Ro'yxat SERVERDA qayta quriladi — klient yuborgan kalit nomzod bo'lmasa (allaqachon tuzatilgan,
        // bu orada to'lov hisob ochgan va h.k.) shunchaki o'tkazib yuboriladi.
        var candidates = (await ActivationChargeRepair.FindAsync(db, ct))
            .Where(c => keys.Contains(ActivationChargeRepair.Key(c.StudentId, c.GroupId, c.Month)))
            .ToList();

        int applied = 0, skipped = keys.Count - candidates.Count;
        var total = 0m;
        var errors = new List<string>();
        foreach (var c in candidates)
        {
            using var gate = await PaymentIdempotency.LockAsync("student:" + c.StudentId);
            try
            {
                var charged = await ActivationChargeRepair.ApplyOneAsync(db, audit, c.StudentId, c.GroupId, ct);
                if (charged <= 0) { skipped++; continue; }
                await db.SaveChangesAsync(ct);
                applied++;
                total += charged;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Qo'shilgan oy hisobini tuzatib bo'lmadi: {Student} / {Group}", c.StudentId, c.GroupId);
                errors.Add($"{c.StudentName} ({c.GroupName}): saqlab bo'lmadi");
            }
            finally
            {
                // Saqlanmagan (yarim) o'zgarishlar keyingi o'quvchining saqlashi bilan bazaga tushib ketmasin.
                db.ChangeTracker.Clear();
            }
        }
        return new ApplyResultDto(applied, total, skipped, errors);
    }

    /// <summary>
    /// NOTO'G'RI OYGA yozilgan to'lovlar (<see cref="PaymentMonthRepair"/>) — HECH NARSA YOZMAYDI. Har qator —
    /// (o'quvchi, guruh) va undagi ko'chirishlar (qaysi oydan qaysi oyga qancha).
    /// </summary>
    [HttpGet("payment-months")]
    public async Task<ActionResult<List<PaymentMonthRepair.Candidate>>> PaymentMonths(CancellationToken ct) =>
        await PaymentMonthRepair.FindAsync(db, ct);

    /// <summary>
    /// TANLANGAN (o'quvchi, guruh) juftliklarining to'lov oylarini tuzatadi. Reja har o'quvchi uchun uning QULFI
    /// ichida bazadan QAYTA quriladi (klient summasiga ishonilmaydi), har biri ALOHIDA saqlanadi — bittasining
    /// xatosi qolganlarini to'xtatmaydi. Pul/balans/kassa o'zgarmaydi — faqat to'lov oyi tegi.
    /// </summary>
    [HttpPost("payment-months")]
    public async Task<ActionResult<ApplyResultDto>> ApplyPaymentMonths(ApplyRequest req, CancellationToken ct)
    {
        var keys = (req.Keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0) return BadRequest(new { message = "Hech narsa tanlanmagan" });
        if (keys.Count > MaxApply) return BadRequest(new { message = $"Bir martada ko'pi bilan {MaxApply} ta" });

        int applied = 0, skipped = 0;
        var total = 0m;
        var errors = new List<string>();
        foreach (var key in keys)
        {
            var parts = key.Split('|');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) { skipped++; continue; }
            var (studentId, groupId) = (parts[0], parts[1]);
            using var gate = await PaymentIdempotency.LockAsync("student:" + studentId);
            try
            {
                var moved = await PaymentMonthRepair.ApplyOneAsync(db, audit, studentId, groupId, ct);
                if (moved <= 0) { skipped++; continue; }
                await db.SaveChangesAsync(ct);
                applied++;
                total += moved;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "To'lov oyini tuzatib bo'lmadi: {Student} / {Group}", studentId, groupId);
                db.ChangeTracker.Clear();
                var name = await db.Students.AsNoTracking().Where(s => s.Id == studentId)
                    .Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? studentId;
                errors.Add($"{name}: saqlab bo'lmadi");
            }
            finally
            {
                // Saqlanmagan (yarim) o'zgarishlar keyingi o'quvchining saqlashi bilan bazaga tushib ketmasin.
                db.ChangeTracker.Clear();
            }
        }
        return new ApplyResultDto(applied, total, skipped, errors);
    }
}
