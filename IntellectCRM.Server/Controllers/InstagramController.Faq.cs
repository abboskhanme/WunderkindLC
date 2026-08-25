using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// FAQ TUGMALARI (Instagram <b>ice breakers</b>) — Marketing → Javob qoidalari sahifasining
/// «FAQ tugmalari» bo'limi.
///
/// <para><b>Nima bu:</b> Direct birinchi ochilganda mijozga ko'rinadigan 4 tagacha tayyor savol
/// tugmasi. Tugma bosilsa saqlangan javob AVTOMATIK yuboriladi — AI chaqirilmaydi (oqim:
/// <c>InstagramPipeline</c> §4.7, hodisa <c>messaging_postbacks</c> webhook'idan keladi).</para>
///
/// <para><b>Sinxronlash BEST-EFFORT:</b> har mutatsiyadan keyin ro'yxat Meta'ga yuboriladi
/// (<c>POST /me/messenger_profile</c>), lekin sinxron yiqilsa CRUD BEKOR QILINMAYDI — tugma
/// bazada qoladi, xato esa <see cref="IgAccount.FaqSyncError"/> da (va javobdagi <c>sync</c>
/// obyektida) ochiq ko'rinadi. Aks holda "akkaunt hali ulanmagan" markaz tugmalarni oldindan
/// tayyorlab qo'ya olmasdi.</para>
///
/// <para>Ruxsat: o'qish — sinf darajasida (<c>marketing</c>), yozish — <c>marketing.rules</c>
/// (tugmalar mazmunan avto-javob qoidalari bilan bitta sahifada boshqariladi, yangi kalit
/// yasalmaydi — §8 naqsh).</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>FAQ auditidagi o'zgarmas amallar matni uchun qisqartma.</summary>
    private const string FaqLabel = "Instagram FAQ tugmasi";

    /// <summary>FAQ tugmalari ro'yxati + oxirgi sinxron holati.</summary>
    [HttpGet("faq")]
    public async Task<ActionResult<IgFaqListDto>> Faq(CancellationToken ct)
    {
        var acc = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        return new IgFaqListDto(
            Items: await FaqRowsAsync(ct),
            MaxItems: IgConst.MaxFaqItems,
            SyncedAt: NullIfEmpty(acc?.FaqSyncedAt),
            SyncError: NullIfEmpty(acc?.FaqSyncError));
    }

    [HttpPost("faq")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgFaqMutationDto>> CreateFaq(IgFaqPayload payload, CancellationToken ct)
    {
        var err = ValidateFaq(payload);
        if (err is not null) return BadRequest(new { message = err });

        // ⚠️ 4 talik chegara — Meta cheklovi (bitta locale'da 4 tagacha ice breaker).
        // Tekshiruv SAQLASHDA: 5-chi tugma bazaga tushib, Meta'da esa hech qachon
        // ko'rinmasligi jimgina nosozlik bo'lardi.
        if (await db.IgIceBreakers.CountAsync(ct) >= IgConst.MaxFaqItems)
            return BadRequest(new { message = "Ko'pi bilan 4 ta FAQ tugmasi qo'shish mumkin." });

        var maxOrder = await db.IgIceBreakers.Select(b => (int?)b.Order).MaxAsync(ct) ?? -1;
        var now = AppClock.Iso();
        var item = new IgIceBreaker
        {
            Question = payload.Question!.Trim(),
            Answer = payload.Answer!.Trim(),
            IsActive = payload.IsActive,
            Order = payload.Order ?? maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.IgIceBreakers.Add(item);
        audit.Record(AuditEntity, item.Id, "create", $"{FaqLabel} yaratildi: «{item.Question}»");
        // 🔴 CRUD sinxrondan OLDIN saqlanadi: Meta yiqilsa ham tugma yo'qolmaydi.
        await db.SaveChangesAsync(ct);

        return new IgFaqMutationDto(ToFaqDto(item), await SyncFaqToMetaAsync(ct));
    }

    [HttpPut("faq/{id}")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgFaqMutationDto>> UpdateFaq(string id, IgFaqPayload payload, CancellationToken ct)
    {
        var err = ValidateFaq(payload);
        if (err is not null) return BadRequest(new { message = err });

        var item = await db.IgIceBreakers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (item is null) return NotFound(new { message = "FAQ tugmasi topilmadi." });

        var before = FaqSnapshot(item);
        item.Question = payload.Question!.Trim();
        item.Answer = payload.Answer!.Trim();
        item.IsActive = payload.IsActive;
        if (payload.Order is int order) item.Order = order;
        item.UpdatedAt = AppClock.Iso();

        audit.Record(AuditEntity, item.Id, "update",
            $"{FaqLabel} tahrirlandi: «{item.Question}»", before: before, after: FaqSnapshot(item));
        await db.SaveChangesAsync(ct);

        return new IgFaqMutationDto(ToFaqDto(item), await SyncFaqToMetaAsync(ct));
    }

    [HttpDelete("faq/{id}")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgFaqDeleteDto>> DeleteFaq(string id, CancellationToken ct)
    {
        var item = await db.IgIceBreakers.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (item is null) return NotFound(new { message = "FAQ tugmasi topilmadi." });

        db.IgIceBreakers.Remove(item);
        audit.Record(AuditEntity, item.Id, "delete", $"{FaqLabel} o'chirildi: «{item.Question}»");
        await db.SaveChangesAsync(ct);

        return new IgFaqDeleteDto(await SyncFaqToMetaAsync(ct));
    }

    /// <summary>Qo'lda sinxronlash — masalan akkaunt KEYIN ulanganda yoki oxirgi sinxron
    /// yiqilganda («Qayta yuborish» tugmasi).</summary>
    [HttpPost("faq/sync")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgFaqSyncDto>> SyncFaq(CancellationToken ct)
    {
        var sync = await SyncFaqToMetaAsync(ct);
        // ⚠️ Audit sinxrondan KEYIN (adsstats/sync bilan bir xil sabab): oldin yozilsa
        // "qilindi" degan qator yarim yo'lda saqlanib qolishi mumkin edi.
        audit.Record(AuditEntity, "faq", "sync",
            sync.Ok ? "Instagram FAQ tugmalari Meta bilan sinxronlandi" : $"Instagram FAQ sinxroni yiqildi: {sync.Message}");
        await db.SaveChangesAsync(ct);
        return sync;
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    /// <summary>Meta'ga sinxronlash — BEST-EFFORT (CRUD natijasiga ta'sir qilmaydi).
    /// <para>Mantiq YAGONA manbada — <see cref="InstagramFaqSync"/>: FAQ CRUD, modul yoqilishi va
    /// akkaunt ulash (`connect-token` + OAuth callback) AYNAN shuni chaqiradi (§22). Bu yerda
    /// faqat servis natijasi CRUD javobidagi <see cref="IgFaqSyncDto"/> ga o'raladi.</para></summary>
    private async Task<IgFaqSyncDto> SyncFaqToMetaAsync(CancellationToken ct)
    {
        var (ok, message) = await faqSync.SyncAsync(ct);
        return new IgFaqSyncDto(ok, message);
    }

    private static string? ValidateFaq(IgFaqPayload p)
    {
        var question = (p.Question ?? "").Trim();
        var answer = (p.Answer ?? "").Trim();
        if (question.Length is 0 or > IgConst.FaqQuestionMaxLength)
            return $"Savol matni 1–{IgConst.FaqQuestionMaxLength} belgi bo'lsin (Meta chegarasi).";
        if (answer.Length is 0 or > IgConst.FaqAnswerMaxLength)
            return $"Javob matni 1–{IgConst.FaqAnswerMaxLength} belgi bo'lsin.";
        return null;
    }

    private Task<List<IgFaqDto>> FaqRowsAsync(CancellationToken ct) =>
        db.IgIceBreakers.AsNoTracking()
            .OrderBy(b => b.Order).ThenBy(b => b.CreatedAt)
            .Select(b => new IgFaqDto(b.Id, b.Question, b.Answer, b.Order, b.IsActive, b.TapCount, b.CreatedAt))
            .ToListAsync(ct);

    private static IgFaqDto ToFaqDto(IgIceBreaker b) =>
        new(b.Id, b.Question, b.Answer, b.Order, b.IsActive, b.TapCount, b.CreatedAt);

    private static object FaqSnapshot(IgIceBreaker b) =>
        new { b.Question, b.Answer, b.IsActive, b.Order };

    /// <summary>Entity'dagi "" konvensiyasini API kontraktidagi <c>null</c> ga o'giradi
    /// (kontrakt: <c>syncedAt: string|null</c>).</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

/// <summary><paramref name="MaxItems"/> — Meta'ning 4 talik chegarasi; klient uni qo'lda
/// yozmasin (drift bo'lmasin).</summary>
public record IgFaqListDto(List<IgFaqDto> Items, int MaxItems, string? SyncedAt, string? SyncError);

public record IgFaqDto(
    string Id, string Question, string Answer, int Order, bool IsActive, int TapCount, string CreatedAt);

/// <param name="Order">Faqat PUT'da ma'noli; berilmasa mavjud tartib saqlanadi
/// (POST'da esa oxiriga qo'shiladi).</param>
public record IgFaqPayload(string? Question, string? Answer, bool IsActive, int? Order);

/// <summary>Meta sinxroni natijasi. <paramref name="Ok"/> false bo'lsa ham CRUD muvaffaqiyatli —
/// <paramref name="Message"/> da o'zbekcha sabab.</summary>
public record IgFaqSyncDto(bool Ok, string Message);

public record IgFaqMutationDto(IgFaqDto Item, IgFaqSyncDto Sync);

public record IgFaqDeleteDto(IgFaqSyncDto Sync);
