using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// BILIM BAZASI — <b>WORD HUJJATIDAN YUKLASH</b> (Marketing → Bilim bazasi → «Word yuklash»).
///
/// <para><b>Muammo.</b> O'quv markazining ma'lumotlari (kurslar, narxlar, jadval, shartlar)
/// odatda Word hujjatida tayyor turadi. Ilgari uni bilim bazasiga tushirishning yagona yo'li —
/// har bo'limni QO'LDA nusxalash edi: 40 betlik hujjat uchun bir necha soatlik ish, natijada
/// esa bilim bazasi yarim to'ldirilgan qolar va AI «bu haqda ma'lumotim yo'q» deb javob
/// berardi.</para>
///
/// <para><b>Yechim.</b> Hujjat yuklanadi, <see cref="DocxKnowledge"/> uni sarlavhalari bo'yicha
/// bo'laklarga ajratadi va ular bilim bazasiga <b>QO'SHILADI</b>.</para>
///
/// <para>🔴 <b>QO'SHILADI, ALMASHTIRMAYDI.</b> Bu markaziy qaror: markaz hujjatlarni bir marta
/// emas, vaqti-vaqti bilan yuklaydi (avval «Kurslar», keyin «Narxlar», keyin «Ichki tartib»).
/// Har yuklash bazani tozalab yuborsa ikkinchi hujjat birinchisini YO'Q QILARDI va buni hech
/// kim darhol sezmasdi — AI shunchaki eski savollarga javob bera olmay qolardi. Qo'lda
/// yozilgan bo'laklar ham HECH QACHON tegilmaydi.</para>
///
/// <para>Manba fayl nomi <see cref="IgKnowledge.SourceFile"/> da saqlanadi, ya'ni hujjatning
/// eski versiyasini bitta amal bilan olib tashlash mumkin (<see cref="DeleteKnowledgeSource"/>) —
/// aks holda eski narx bilim bazasida abadiy qolib ketardi.</para>
///
/// <para>Ruxsat: o'qish — sinf darajasida (<c>marketing</c>), yozish — <c>marketing.knowledge</c>.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Yuklanadigan hujjatning eng katta hajmi. Word hujjati matn bilan bir necha
    /// yuz KB bo'ladi; 10 MB — rasmga to'la hujjat uchun ham yetarli zaxira.</summary>
    private const int MaxKnowledgeFileBytes = 10 * 1024 * 1024;

    /// <summary>Fayl BOSHIDAGI tekshiriladigan baytlar (ZIP imzosi).</summary>
    private const int DocxSniffBytes = 4;

    /// <summary>Auditdagi o'zgarmas `EntityId` — bilim bazasi bitta obyekt sifatida ko'riladi.</summary>
    private const string KnowledgeAuditId = "knowledge";

    // =============================================================================================
    //  WORD HUJJATINI YUKLASH
    // =============================================================================================

    /// <summary>
    /// Word hujjatidan bilim bazasiga bo'lak qo'shish.
    ///
    /// <para><b>Uchta mustaqil tekshiruv</b> (media yuklashdagi naqsh, <c>uploads-security.md</c>):
    /// kengaytma · <c>Content-Type</c> · fayl boshidagi <b>ZIP imzosi</b> (<c>PK\x03\x04</c> —
    /// <c>.docx</c> aslida ZIP arxiv). Faqat kengaytmaga ishonish har qanday faylni
    /// <c>.docx</c> deb nomlab yuborish yo'lini ochib berardi; imzo esa eski <c>.doc</c> ni ham
    /// shu yerda ushlaydi va sabab foydalanuvchiga aniq aytiladi.</para>
    ///
    /// <para>⚠️ <b>Fayl DISKKA YOZILMAYDI.</b> Bizga matn kerak, faylning o'zi emas — u
    /// xotirada o'qilib, bo'laklar bazaga yoziladi va baytlar tashlanadi. Shu sababdan
    /// <c>/uploads</c> qoidalari (darvoza, zaxira, tozalash) bu yerga umuman tegishli emas.</para>
    ///
    /// <para>⚠️ <b>Bo'laklar FAOL holda qo'shiladi</b> (<c>IsActive = true</c>): admin hujjatni
    /// ataylab yuklayapti, ya'ni uning ma'lumoti AI'ga kerak. Kerak bo'lmagan bo'lakni
    /// ro'yxatdan o'chirish yoki o'chirib qo'yish bir bosishda.</para>
    ///
    /// <para>⚠️ <b>Vektorlar darhol hisoblanmaydi</b> — buni fon xizmati (<c>IgEmbeddingWorker</c>)
    /// bajaradi. Sabab: 200 ta bo'lak uchun 200 ta Gemini so'rovi so'rovning O'ZI ichida
    /// bajarilsa yuklash bir necha daqiqa osilib qolardi (va brauzer uzardi). Shu oraliqda
    /// modul ESKI YO'L bilan ishlaydi (butun bilim bazasi, <c>KnowledgeLimit</c> gacha) —
    /// javob beradi, faqat tanlov aniqligi pastroq. Holat
    /// <see cref="KnowledgeStatus"/> da ko'rinadi.</para>
    /// </summary>
    [HttpPost("knowledge/import")]
    [AdminPerm("marketing.knowledge")]
    [RequestSizeLimit(MaxKnowledgeFileBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxKnowledgeFileBytes)]
    public async Task<ActionResult<IgKnowledgeImportDto>> ImportKnowledge(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmadi." });
        if (file.Length > MaxKnowledgeFileBytes)
            return BadRequest(new { message = $"Fayl juda katta ({file.Length / 1024 / 1024} MB). Chegara — 10 MB." });

        var name = (file.FileName ?? "").Trim();
        if (!name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                message = name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
                    ? "Eski .doc formati qo'llab-quvvatlanmaydi. Hujjatni Word'da oching va "
                      + "«Save As → Word Document (.docx)» bilan saqlab, qaytadan yuklang."
                    : "Faqat Word hujjati (.docx) qabul qilinadi.",
            });

        // ZIP imzosi — baytlarni bazaga tegizishdan OLDIN.
        var head = new byte[DocxSniffBytes];
        await using (var probe = file.OpenReadStream())
            await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (!IsZip(head))
            return BadRequest(new
            {
                message = "Fayl Word hujjatiga o'xshamaydi (ichki tuzilishi mos kelmadi). "
                          + "Hujjatni Word'da qaytadan .docx qilib saqlab ko'ring.",
            });

        byte[] bytes;
        await using (var source = file.OpenReadStream())
        {
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var parsed = DocxKnowledge.Parse(bytes, name);
        if (parsed.Error.Length > 0) return BadRequest(new { message = parsed.Error });

        // ⚠️ Yangi bo'laklar ro'yxatning OXIRIGA qo'shiladi: `Order` — promptdagi tartib va
        // mavjud (qo'lda yozilgan, ehtimol eng muhim) bo'laklar tepada qolishi kerak.
        var now = AppClock.Iso();
        var sourceName = DocxKnowledge.TitleFromFileName(name);
        var nextOrder = await db.IgKnowledges.AnyAsync(ct)
            ? await db.IgKnowledges.MaxAsync(k => k.Order, ct) + 1
            : 0;

        foreach (var chunk in parsed.Chunks)
            db.IgKnowledges.Add(new IgKnowledge
            {
                Title = chunk.Title,
                Content = chunk.Content,
                Order = nextOrder++,
                IsActive = true,
                SourceFile = sourceName,
                UpdatedAt = now,
                UpdatedBy = Actor,
            });

        // ⚠️ Auditga hujjat MATNI yozilmaydi — faqat nomi va soni ("nima bo'ldi" yetarli,
        // matnning o'zi bilim bazasi ro'yxatida ko'rinadi).
        audit.Record(AuditEntity, KnowledgeAuditId, "update",
            $"Bilim bazasiga Word hujjati yuklandi: «{sourceName}» — {parsed.Chunks.Count} ta bo'lak qo'shildi"
            + (parsed.Skipped > 0 ? $" ({parsed.Skipped} tasi chegaradan oshgani uchun olinmadi)" : ""));
        await db.SaveChangesAsync(ct);

        return new IgKnowledgeImportDto(
            FileName: sourceName,
            Added: parsed.Chunks.Count,
            Skipped: parsed.Skipped,
            Items: await KnowledgeRowsAsync(ct));
    }

    // =============================================================================================
    //  FAYL BO'YICHA O'CHIRISH
    // =============================================================================================

    /// <summary>
    /// Bitta hujjatdan kelgan BARCHA bo'laklarni o'chiradi («narxlar hujjatining eski
    /// versiyasini olib tashlash»).
    ///
    /// <para>⚠️ Qo'lda yozilgan bo'laklar (<c>SourceFile</c> BO'SH) hech qachon tushmaydi:
    /// bo'sh nom bilan chaqirilsa amal RAD ETILADI. Aks holda bitta bosish butun qo'lda
    /// yig'ilgan bilim bazasini o'chirib yuborardi.</para>
    ///
    /// <para>⚠️ Topilmagan nom uchun <b>404 EMAS, 200 + <c>removed: 0</c></b>: ro'yxat orada
    /// o'zgargan bo'lishi mumkin va «o'chirilmadi» xatosi foydalanuvchiga hech narsa
    /// bermasdi — natija sonining O'ZI holatni aytadi.</para>
    /// </summary>
    [HttpDelete("knowledge/source")]
    [AdminPerm("marketing.knowledge")]
    public async Task<ActionResult<IgKnowledgeImportDto>> DeleteKnowledgeSource(
        [FromQuery] string? file, CancellationToken ct)
    {
        var name = (file ?? "").Trim();
        if (name.Length == 0)
            return BadRequest(new { message = "Fayl nomi berilmadi." });

        var rows = await db.IgKnowledges.Where(k => k.SourceFile == name).ToListAsync(ct);
        if (rows.Count > 0)
        {
            db.IgKnowledges.RemoveRange(rows);
            audit.Record(AuditEntity, KnowledgeAuditId, "update",
                $"Bilim bazasidan «{name}» hujjatining bo'laklari o'chirildi — {rows.Count} ta");
            await db.SaveChangesAsync(ct);
        }

        return new IgKnowledgeImportDto(
            FileName: name, Added: 0, Skipped: rows.Count, Items: await KnowledgeRowsAsync(ct));
    }

    // =============================================================================================
    //  HOLAT — "AI qidiruvi tayyormi"
    // =============================================================================================

    /// <summary>
    /// Bilim bazasining holati: bo'laklar soni, vektorlar tayyorligi va yuklangan hujjatlar.
    ///
    /// <para><b>Nega alohida endpoint kerak:</b> bilim bazasi kattalashgach javob sifati
    /// RAG'ga bog'liq bo'lib qoladi, RAG esa <b>HAR BIR faol bo'lakning vektori</b> tayyor
    /// bo'lgandagina yoqiladi (<see cref="IgKnowledgeRag.CanUseRag"/>). Katta hujjat
    /// yuklangandan keyin fon xizmati vektorlarni bir necha daqiqada hisoblaydi — shu oraliqda
    /// admin «AI hujjatni ko'rmayapti» deb o'ylab, hujjatni qayta-qayta yuklashi mumkin edi.
    /// Endi jarayon ekranda ochiq ko'rinadi.</para>
    ///
    /// <para>⚠️ <c>RagReady</c> — hisoblangan qiymat, bazada ustun YO'Q: u AYNAN
    /// <c>CanUseRag</c> dan olinadi, ya'ni ekrandagi holat modulning haqiqiy qarori bilan
    /// bir xil bo'ladi (ikki joyda ayri hisoblansa ular vaqt o'tib bir-biridan uzoqlashardi).</para>
    /// </summary>
    [HttpGet("knowledge/status")]
    public async Task<ActionResult<IgKnowledgeStatusDto>> KnowledgeStatus(CancellationToken ct)
    {
        var rows = await db.IgKnowledges.AsNoTracking()
            .Select(k => new { k.Id, k.Title, k.Content, k.Order, k.IsActive, k.SourceFile, k.EmbeddingJson })
            .ToListAsync(ct);

        var active = rows.Where(r => r.IsActive).ToList();
        var chunks = active
            .Select(r => new IgRagChunk(r.Id, r.Title, r.Content, r.Order, IgKnowledgeRag.ParseVector(r.EmbeddingJson)))
            .ToList();

        var sources = rows
            .Where(r => r.SourceFile.Length > 0)
            .GroupBy(r => r.SourceFile)
            .Select(g => new IgKnowledgeSourceDto(g.Key, g.Count(), g.Sum(x => x.Content.Length)))
            .OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new IgKnowledgeStatusDto(
            Total: rows.Count,
            Active: active.Count,
            Embedded: chunks.Count(c => c.Vector.Length > 0),
            RagReady: IgKnowledgeRag.CanUseRag(chunks),
            GeminiConfigured: AppSecrets.GeminiConfigured,
            Sources: sources);
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    /// <summary>Bilim bazasi qatorlari — import/o'chirish javoblarida ekran darhol yangilansin
    /// (klient qo'shimcha <c>GET</c> qilmasin).</summary>
    private Task<List<IgKnowledgeDto>> KnowledgeRowsAsync(CancellationToken ct) =>
        db.IgKnowledges.AsNoTracking()
            .OrderBy(k => k.Order).ThenBy(k => k.Title)
            .Select(k => new IgKnowledgeDto(
                k.Id, k.Title, k.Content, k.Order, k.IsActive, k.UpdatedAt, k.UpdatedBy, k.SourceFile))
            .ToListAsync(ct);

    /// <summary>ZIP imzosi (<c>PK\x03\x04</c>) — <c>.docx</c> aslida ZIP arxiv.</summary>
    private static bool IsZip(byte[] head) =>
        head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;
}

/// <summary>Word yuklash / fayl bo'yicha o'chirish natijasi + YANGILANGAN ro'yxat.</summary>
/// <param name="Skipped">Importda — chegaradan oshib olinmagan bo'laklar; o'chirishda —
/// o'chirilgan bo'laklar soni.</param>
public record IgKnowledgeImportDto(
    string FileName, int Added, int Skipped, List<IgKnowledgeDto> Items);

/// <summary>Yuklangan bitta hujjat: nomi, bo'laklari soni va matn hajmi.</summary>
public record IgKnowledgeSourceDto(string FileName, int Chunks, int Chars);

/// <summary>Bilim bazasi holati — «AI qidiruvi tayyormi» savoliga javob.</summary>
public record IgKnowledgeStatusDto(
    int Total, int Active, int Embedded, bool RagReady, bool GeminiConfigured,
    List<IgKnowledgeSourceDto> Sources);
