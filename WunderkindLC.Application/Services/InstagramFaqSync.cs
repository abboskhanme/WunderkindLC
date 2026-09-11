using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// FAQ tugmalarini (Instagram <b>ice breakers</b>) Meta'ga SINXRONLASH — YAGONA manba.
///
/// <para><b>Nega alohida servis:</b> ilgari sinxron mantig'i faqat <c>InstagramController.Faq.cs</c>
/// ichida (private metod) edi. Uni endi UCH joy chaqiradi:
/// <list type="bullet">
///   <item>FAQ CRUD (yaratish/tahrir/o'chirish/qo'lda sinxron) — <c>InstagramController.Faq.cs</c>;</item>
///   <item>modul YOQILGANDA (o'chiqdan→yoqilganga) — <c>InstagramController.SaveSettings</c>;</item>
///   <item>akkaunt ULANGANDA — <c>connect-token</c> (`InstagramController`) va OAuth
///     <c>callback</c> (`InstagramWebhookController`, BOSHQA controller).</item>
/// </list>
/// Callback boshqa controllerda bo'lgani uchun mantiq umumiy servisga ko'chirildi — aks holda u
/// ikki joyda ayri ketardi (`.claude/rules/marketing-instagram.md` §22).</para>
///
/// <para><b>Muammo bu servis tuzatadigan:</b> FAQ tugmalari Meta'ga faqat CRUD paytida
/// yuborilardi, lekin sinxron ikki darvozadan o'tadi — modul o'chiq yoki akkaunt ulanmagan bo'lsa
/// so'rov ketmaydi. Modul KEYIN yoqilganda yoki akkaunt KEYIN ulanganda esa qayta sinxron
/// QILINMASdi — natijada tugma bazada bor, Meta'da yo'q bo'lib ko'rinmasdi.</para>
///
/// <para><b>BEST-EFFORT:</b> yiqilsa asosiy amal (CRUD / sozlama saqlash / akkaunt ulash)
/// BEKOR QILINMAYDI — xato <see cref="IgAccount.FaqSyncError"/> ga yoziladi va natijada qaytadi.
/// Modul o'chiq bo'lsa tashqariga HECH QANDAY so'rov ketmaydi (§3 darvozasi).</para>
/// </summary>
public class InstagramFaqSync(IAppDbContext db, InstagramApi api, ILogger<InstagramFaqSync> logger)
{
    /// <summary>Meta'ga sinxronlash — BEST-EFFORT (chaqiruvchi natijasiga ta'sir qilmaydi).
    /// <para>Faol tugmalar bo'lsa <c>SetIceBreakersAsync</c>, bo'lmasa <c>DeleteIceBreakersAsync</c>
    /// (Meta bo'sh ro'yxatni rad etadi — profil maydoni DELETE bilan tozalanadi). Natija
    /// <see cref="IgAccount.FaqSyncedAt"/> / <see cref="IgAccount.FaqSyncError"/> ga yoziladi.</para>
    /// <para>⚠️ Modul o'chiq bo'lsa tashqariga HECH QANDAY so'rov ketmaydi (qoidalar §3 —
    /// "kichkina bitta so'rov" ham shu darvozadan o'tadi). Token logga/javobga tushmaydi.</para></summary>
    public async Task<(bool Ok, string Message)> SyncAsync(CancellationToken ct)
    {
        var account = await db.IgAccounts.FirstOrDefaultAsync(a => a.IsActive, ct);
        if (account is null || string.IsNullOrWhiteSpace(account.AccessToken))
            return (false,
                "Instagram akkaunti ulanmagan — tugmalar saqlandi, akkaunt ulangach «Sinxronlash» bosing.");

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramEnabled)
            return (false,
                "Instagram moduli o'chiq — tugmalar saqlandi, modul yoqilgach «Sinxronlash» bosing.");

        var items = await db.IgIceBreakers.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Order).ThenBy(b => b.CreatedAt)
            .Take(IgConst.MaxFaqItems)
            .ToListAsync(ct);

        var (ok, err) = items.Count == 0
            ? await api.DeleteIceBreakersAsync(account.AccessToken, ct)
            : await api.SetIceBreakersAsync(
                account.AccessToken,
                items.Select(b => (b.Question, InstagramContract.FaqPayload(b.Id))).ToList(),
                ct);

        if (!ok)
        {
            // ⚠️ `FaqSyncedAt` ATAYIN o'chirilmaydi — oxirgi muvaffaqiyat vaqti diagnostika
            // uchun qimmatli ("qachongacha ishlagan edi" savoli).
            account.FaqSyncError = InstagramContract.Trim(err, 500);
            await db.SaveChangesAsync(ct);
            return (false, err);
        }

        // Obuna BEST-EFFORT yangilanadi: eski ulangan akkaunt `messaging_postbacks` ga obuna
        // emas va tugma bosilgani unga umuman kelmasdi. Yiqilsa sinxron natijasi buzilmaydi —
        // sabab logda qoladi (tokensiz).
        var sub = await api.SubscribeWebhookAsync(account.AccessToken, ct);
        if (!sub.Ok)
            logger.LogWarning("Instagram: FAQ sinxronida webhook obunasini yangilab bo'lmadi: {Err}", sub.Error);
        else
            account.WebhookSubscribed = true;

        account.FaqSyncedAt = AppClock.Iso();
        account.FaqSyncError = "";
        await db.SaveChangesAsync(ct);
        return (true, items.Count == 0
            ? "FAQ tugmalari Instagram'dan olib tashlandi."
            : $"FAQ tugmalari Instagram bilan sinxronlandi ({items.Count} ta).");
    }
}
