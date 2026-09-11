using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

[ApiController]
public class LandingCmsController(AppDbContext db, ILogger<LandingCmsController> logger) : ControllerBase
{
    /// <summary>Landing CMS "Sozlamalar" bo'limining bir qismi — nav (`/admin/landing`) va marshrut
    /// darvozasi (`RequirePerm perm="settings"`) bilan AYNAN bir xil kalit.</summary>
    private const string SectionPerm = "settings.landing";

    // ==================== SUKUT QIYMATLAR (FAQAT O'QISHDA) ====================
    // ⚠️ Bu qiymatlar BAZAGA yozilmaydi — faqat OMMAVIY javobda bo'sh maydon o'rniga qo'yiladi.
    // Ilgari ular saqlashda ham qo'llanardi: admin YouTube havolasini O'CHIRSA, u
    // "https://youtube.com" bo'lib qaytib qolar va saytda baribir ko'rinardi — ya'ni maydonni
    // umuman o'chirib bo'lmasdi.

    // ⚠️ Zaxira qiymatlar BO'SH: ilgari bu yerda BOSHQA markazning haqiqiy telefoni,
    // manzili, xarita koordinatalari va ijtimoiy tarmoqlari turardi va CMS to'ldirilmagan
    // bo'lsa ommaviy sahifada AYNAN shular chiqardi. "Kiritilmagan" = bo'sh satr
    // (Program.cs dagi CenterMeta ustun default'lari bilan bir xil printsip).
    private const string DefaultMapUrl = "";
    private const string DefaultTelegramUrl = "";
    private const string DefaultInstagramUrl = "";
    private const string DefaultYoutubeUrl = "https://youtube.com";
    private const string DefaultFacebookUrl = "https://facebook.com";
    private const string DefaultEmail = "";
    private const string DefaultPhone = "";
    private const string DefaultAddress = "";
    private const string DefaultWorkingHours = "";

    // ==================== OMMAVIY ENDPOINT ====================

    /// <summary>Landing sahifasi uchun barcha faol ma'lumotlar (O'qituvchilar, Sertifikatlar, Fikrlar, FAQ).</summary>
    [HttpGet("api/public/landing-data")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicLandingData()
    {
        var teachers = await db.LandingTeachers
            .Where(t => t.IsActive)
            .OrderBy(t => t.Order)
            .ToListAsync();

        var certificates = await db.LandingCertificates
            .Where(c => c.IsActive)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var testimonials = await db.LandingTestimonials
            .Where(t => t.IsActive)
            .OrderBy(t => t.Order)
            .ToListAsync();

        var faqs = await db.LandingFaqs
            .Where(f => f.IsActive)
            .OrderBy(f => f.Order)
            .ToListAsync();

        // ⚠️ O'QITUVCHI va SERTIFIKAT uchun ZAXIRA (namuna) MA'LUMOT YO'Q — ATAYIN.
        // Ular bazada bo'lmasa javob BO'SH massiv qaytaradi. Sabab: ommaviy saytda soxta
        // o'qituvchi ismi yoki boshqa odamning "IELTS 8.5" sertifikati — to'qib chiqarilgan
        // ma'lumot (markaz nomidan yolg'on da'vo). Bo'lim ko'rinishini KLIENT hal qiladi:
        // ro'yxat bo'sh bo'lsa u bo'limni umuman chizmaydi.
        //
        // FAQ esa qoladi: u markaz haqidagi umumiy javoblar (narx/jadval emas), va landing
        // sahifasida u uchun statik markup zaxira sifatida turibdi.
        if (faqs.Count == 0)
        {
            faqs = GetDefaultFaqs();
        }

        var meta = await db.CenterMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
        var mapUrl = string.IsNullOrWhiteSpace(meta?.MapIframeUrl) ? DefaultMapUrl : meta.MapIframeUrl.Trim();

        // Sukut qiymatlar FAQAT shu yerda (ommaviy o'qishda) qo'llanadi — bazada bo'sh qiymat
        // bo'sh bo'lib qoladi (admin maydonni haqiqatan o'chira olsin).
        var socials = new
        {
            telegramUrl = NormalizeUrl(meta?.TelegramUrl, DefaultTelegramUrl),
            instagramUrl = NormalizeUrl(meta?.InstagramUrl, DefaultInstagramUrl),
            youtubeUrl = NormalizeUrl(meta?.YoutubeUrl, DefaultYoutubeUrl),
            facebookUrl = NormalizeUrl(meta?.FacebookUrl, DefaultFacebookUrl),
            centerEmail = string.IsNullOrWhiteSpace(meta?.CenterEmail) ? DefaultEmail : meta.CenterEmail,
            appStoreUrl = NormalizeUrl(meta?.AppStoreUrl, string.Empty),
            playMarketUrl = NormalizeUrl(meta?.PlayMarketUrl, string.Empty),
            contactPhone = string.IsNullOrWhiteSpace(meta?.ContactPhone) ? DefaultPhone : meta.ContactPhone,
            centerAddress = string.IsNullOrWhiteSpace(meta?.CenterAddress) ? DefaultAddress : meta.CenterAddress,
            workingHours = string.IsNullOrWhiteSpace(meta?.WorkingHours) ? DefaultWorkingHours : meta.WorkingHours
        };

        return Ok(new
        {
            teachers,
            certificates,
            testimonials,
            faqs,
            mapUrl,
            socials
        });
    }

    // ==================== ADMIN MAP & SOCIALS ENDPOINTS ====================

    [HttpGet("api/admin/landing/socials")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetSocials()
    {
        await EnsureColumnsAsync();
        var meta = await db.CenterMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
        // ⚠️ ADMIN formasi XOM qiymatni ko'radi (sukut qiymat QO'YILMAYDI): aks holda admin
        // maydonni tozalab saqlaganidan keyin forma qayta ochilganda u yana to'lgan bo'lib
        // ko'rinar va keyingi saqlashda default qiymat bazaga qaytib yozilardi.
        return Ok(new
        {
            telegramUrl = meta?.TelegramUrl ?? "",
            instagramUrl = meta?.InstagramUrl ?? "",
            youtubeUrl = meta?.YoutubeUrl ?? "",
            facebookUrl = meta?.FacebookUrl ?? "",
            centerEmail = meta?.CenterEmail ?? "",
            appStoreUrl = meta?.AppStoreUrl ?? "",
            playMarketUrl = meta?.PlayMarketUrl ?? "",
            contactPhone = meta?.ContactPhone ?? "",
            centerAddress = meta?.CenterAddress ?? "",
            workingHours = meta?.WorkingHours ?? ""
        });
    }

    public class SocialsInput
    {
        public string? TelegramUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? YoutubeUrl { get; set; }
        public string? FacebookUrl { get; set; }
        public string? CenterEmail { get; set; }
        public string? AppStoreUrl { get; set; }
        public string? PlayMarketUrl { get; set; }
        public string? ContactPhone { get; set; }
        public string? CenterAddress { get; set; }
        public string? WorkingHours { get; set; }
    }

    private static string NormalizeUrl(string? url, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(url)) return fallback;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return "https://" + trimmed;
    }

    /// <summary>Yuborilmagan (<c>null</c>) maydon — ESKI qiymatida qoladi; yuborilgan bo'sh satr
    /// esa ATAYIN tozalash deb qabul qilinadi. Qisman payload butun blokni o'chirib yubormasin.</summary>
    private static string MergeText(string? incoming, string current) =>
        incoming is null ? current : incoming.Trim();

    /// <summary><see cref="MergeText"/> ning havola varianti — yuborilgan qiymat saqlashdan oldin
    /// <see cref="NormalizeUrl"/> dan o'tadi (sxemasiz yozilgan havola ham ishlasin).</summary>
    private static string MergeUrl(string? incoming, string current) =>
        incoming is null ? current : NormalizeUrl(incoming);

    [HttpPost("api/admin/landing/socials")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateSocials([FromBody] SocialsInput? input)
    {
        await EnsureColumnsAsync();
        var allMetas = await db.CenterMeta.ToListAsync();
        if (allMetas.Count == 0)
        {
            var newMeta = new CenterMeta();
            db.CenterMeta.Add(newMeta);
            allMetas.Add(newMeta);
        }

        foreach (var meta in allMetas)
        {
            // ⚠️ YUBORILMAGAN (null) va ATAYIN TOZALANGAN ("") — IKKI XIL NARSA.
            //
            // Bu endpoint BARCHA maydonni kelgan payload'dan yozadi. Ilgari yuborilmagan maydon ham
            // `?? ""` orqali BO'SHGA aylanardi: klient (yoki mobil ilova, yoki qo'lda curl) faqat
            // telefonni yuborsa, Telegram/Instagram/manzil/ish vaqti JIMGINA o'chib ketardi va
            // saytdan yo'qolardi. Ilgari bu xavf qattiq kodlangan default'lar bilan "yashiringan"
            // edi — endi default'lar faqat OMMAVIY o'qishda qo'yilgani uchun yo'qolish KO'RINADI.
            //
            // Shuning uchun: `null` = "tegilmadi" (eski qiymat qoladi), `""` = "admin ataylab
            // tozaladi" (bo'sh saqlanadi — maydonni o'chirish imkoni SAQLANADI).
            meta.TelegramUrl = MergeUrl(input?.TelegramUrl, meta.TelegramUrl);
            meta.InstagramUrl = MergeUrl(input?.InstagramUrl, meta.InstagramUrl);
            meta.YoutubeUrl = MergeUrl(input?.YoutubeUrl, meta.YoutubeUrl);
            meta.FacebookUrl = MergeUrl(input?.FacebookUrl, meta.FacebookUrl);
            meta.CenterEmail = MergeText(input?.CenterEmail, meta.CenterEmail);
            meta.AppStoreUrl = MergeUrl(input?.AppStoreUrl, meta.AppStoreUrl);
            meta.PlayMarketUrl = MergeUrl(input?.PlayMarketUrl, meta.PlayMarketUrl);
            meta.ContactPhone = MergeText(input?.ContactPhone, meta.ContactPhone);
            meta.CenterAddress = MergeText(input?.CenterAddress, meta.CenterAddress);
            meta.WorkingHours = MergeText(input?.WorkingHours, meta.WorkingHours);
        }

        await db.SaveChangesAsync();
        var updated = allMetas.First();
        return Ok(new { ok = true, socials = new {
            telegramUrl = updated.TelegramUrl,
            instagramUrl = updated.InstagramUrl,
            youtubeUrl = updated.YoutubeUrl,
            facebookUrl = updated.FacebookUrl,
            centerEmail = updated.CenterEmail,
            appStoreUrl = updated.AppStoreUrl,
            playMarketUrl = updated.PlayMarketUrl,
            contactPhone = updated.ContactPhone,
            centerAddress = updated.CenterAddress,
            workingHours = updated.WorkingHours
        } });
    }

    [HttpGet("api/admin/landing/map-url")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetMapUrl()
    {
        await EnsureColumnsAsync();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        // Admin XOM qiymatni ko'radi (GetSocials bilan bir xil sabab).
        return Ok(new { mapUrl = meta?.MapIframeUrl ?? "" });
    }

    public class MapUrlInput
    {
        /// <summary>⚠️ NULLABLE ATAYIN: <c>null</c> = maydon umuman yuborilmadi (eski qiymat
        /// qoladi), <c>""</c> = admin ataylab tozaladi. Ilgari bu <c>string = ""</c> edi va bo'sh
        /// tanali (<c>{}</c>) so'rov xaritani JIMGINA o'chirib yuborardi.</summary>
        public string? MapUrl { get; set; }
    }

    // ==================== USTUNLARNI TA'MINLASH (bir martalik) ====================

    /// <summary>Jarayon davomida BIR MARTA bajarilganini bildiradi (muvaffaqiyatli bo'lsa).</summary>
    private static bool _columnsEnsured;

    /// <summary>Bir vaqtda kelgan so'rovlar DDL'ni takrorlamasin.</summary>
    private static readonly SemaphoreSlim EnsureGate = new(1, 1);

    /// <summary>
    /// <c>CenterMeta</c> jadvalidagi landing ustunlarini ta'minlaydi (eski bazalar uchun).
    ///
    /// <para>⚠️ Bu MIGRATSIYANING ishi va bu yerda faqat VAQTINCHALIK yechim sifatida qolgan.
    /// Shuning uchun ikki narsa qat'iy:</para>
    /// <list type="bullet">
    ///   <item><b>OMMAVIY endpointdan chaqirilmaydi</b> — <c>GET /api/public/landing-data</c> landing
    ///     har ochilganda kelib turadi, ya'ni 11 ta <c>ALTER TABLE</c> tashqaridan boshqariladigan
    ///     yuk va jadval qulfi bo'lardi. Endi u faqat ADMIN marshrutlarida.</item>
    ///   <item><b>Jarayon davomida BIR MARTA</b> (<see cref="_columnsEnsured"/> + qulf) — har
    ///     so'rovda emas.</item>
    ///   <item><b>Xato JIM YUTILMAYDI</b> — logga yoziladi (ilgari istisno BO'SH bloqda jimgina yutilardi: prodda
    ///     ustun qo'shilmasa ham hech kim bilmasdi). So'rovning o'zi baribir buzilmaydi.</item>
    /// </list>
    /// </summary>
    private async Task EnsureColumnsAsync()
    {
        if (_columnsEnsured) return;
        await EnsureGate.WaitAsync();
        try
        {
            if (_columnsEnsured) return;
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""MapIframeUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""TelegramUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""InstagramUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""YoutubeUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""FacebookUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterEmail"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""AppStoreUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""PlayMarketUrl"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""ContactPhone"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterAddress"" text DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""WorkingHours"" text DEFAULT '';
            ");
            _columnsEnsured = true;
        }
        catch (Exception ex)
        {
            // Ataylab qayta urinishga qoldiriladi (_columnsEnsured yoqilmaydi), lekin xato
            // KO'RINADI — bo'shliq jimgina yo'qolmasin.
            logger.LogWarning(ex, "Landing CMS: CenterMeta ustunlarini ta'minlab bo'lmadi");
        }
        finally
        {
            EnsureGate.Release();
        }
    }

    [HttpPost("api/admin/landing/map-url")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateMapUrl([FromBody] MapUrlInput? input)
    {
        await EnsureColumnsAsync();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta == null)
        {
            meta = new CenterMeta();
            db.CenterMeta.Add(meta);
        }
        // Yuborilmagan (null) — tegilmaydi; bo'sh satr ATAYIN tozalash (ommaviy javobda sukut
        // xarita ko'rsatiladi). Farqi UpdateSocials dagi bilan bir xil.
        meta.MapIframeUrl = MergeText(input?.MapUrl, meta.MapIframeUrl ?? "");
        await db.SaveChangesAsync();
        return Ok(new { ok = true, mapUrl = meta.MapIframeUrl });
    }

    // ==================== ADMIN ENDPOINTS ====================

    // --- O'QITUVCHILAR (TEACHERS) ---

    [HttpGet("api/admin/landing/teachers")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetTeachers()
    {
        var list = await db.LandingTeachers.OrderBy(t => t.Order).ToListAsync();
        return Ok(list);
    }

    /// <summary>⚠️ POSITIONAL RECORD: JSON'da maydon YO'Q bo'lsa u <c>null</c> bo'lib keladi
    /// (klientda tur <c>Partial&lt;LandingTeacher&gt;</c>, ya'ni bu HAQIQIY holat). Shuning uchun
    /// maydonlar NULLABLE deb e'lon qilingan va foydalanishda <c>?? ""</c> bilan o'qiladi —
    /// aks holda <c>dto.FullName.Trim()</c> NullReferenceException berib, klientga xato SHAKLI
    /// mutlaqo boshqacha bo'lgan 500 qaytardi (`CertificateDto` da bu allaqachon shunday edi).</summary>
    public record TeacherDto(
        string? FullName,
        string? Subject,
        string? PhotoUrl,
        string? Badge,
        string? ShortBio,
        string? FullBio,
        int Order,
        bool IsActive
    );

    [HttpPost("api/admin/landing/teachers")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> CreateTeacher([FromBody] TeacherDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var teacher = new LandingTeacher
        {
            FullName = (dto.FullName ?? "").Trim(),
            Subject = (dto.Subject ?? "").Trim(),
            PhotoUrl = (dto.PhotoUrl ?? "").Trim(),
            Badge = (dto.Badge ?? "").Trim(),
            ShortBio = (dto.ShortBio ?? "").Trim(),
            FullBio = (dto.FullBio ?? "").Trim(),
            Order = dto.Order,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.LandingTeachers.Add(teacher);
        await db.SaveChangesAsync();
        return Ok(teacher);
    }

    [HttpPut("api/admin/landing/teachers/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateTeacher(string id, [FromBody] TeacherDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var teacher = await db.LandingTeachers.FindAsync(id);
        if (teacher == null) return NotFound();

        teacher.FullName = (dto.FullName ?? "").Trim();
        teacher.Subject = (dto.Subject ?? "").Trim();
        teacher.PhotoUrl = (dto.PhotoUrl ?? "").Trim();
        teacher.Badge = (dto.Badge ?? "").Trim();
        teacher.ShortBio = (dto.ShortBio ?? "").Trim();
        teacher.FullBio = (dto.FullBio ?? "").Trim();
        teacher.Order = dto.Order;
        teacher.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return Ok(teacher);
    }

    [HttpDelete("api/admin/landing/teachers/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> DeleteTeacher(string id)
    {
        var teacher = await db.LandingTeachers.FindAsync(id);
        if (teacher == null) return NotFound();
        db.LandingTeachers.Remove(teacher);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- SERTIFIKATLAR (CERTIFICATES) ---

    [HttpGet("api/admin/landing/certificates")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetCertificates()
    {
        var list = await db.LandingCertificates.OrderBy(c => c.Order).ToListAsync();
        return Ok(list);
    }

    /// <summary>Maydonlar NULLABLE — sabab <see cref="TeacherDto"/> dagi bilan bir xil.</summary>
    public record CertificateDto(
        string? Title,
        string? StudentName,
        string? ImageUrl,
        string? Category,
        string? CertType,
        string? OverallScore,
        string? Listening,
        string? Reading,
        string? Writing,
        string? Speaking,
        string? ResultNote,
        int Order,
        bool IsActive
    );

    [HttpPost("api/admin/landing/certificates")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> CreateCertificate([FromBody] CertificateDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = new LandingCertificate
        {
            Title = (dto.Title ?? "").Trim(),
            StudentName = (dto.StudentName ?? "").Trim(),
            ImageUrl = (dto.ImageUrl ?? "").Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "Xalqaro" : dto.Category.Trim(),
            CertType = string.IsNullOrWhiteSpace(dto.CertType) ? "IELTS" : dto.CertType.Trim(),
            OverallScore = (dto.OverallScore ?? "").Trim(),
            Listening = (dto.Listening ?? "").Trim(),
            Reading = (dto.Reading ?? "").Trim(),
            Writing = (dto.Writing ?? "").Trim(),
            Speaking = (dto.Speaking ?? "").Trim(),
            ResultNote = (dto.ResultNote ?? "").Trim(),
            Order = dto.Order,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.LandingCertificates.Add(item);
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("api/admin/landing/certificates/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateCertificate(string id, [FromBody] CertificateDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = await db.LandingCertificates.FindAsync(id);
        if (item == null) return NotFound();

        item.Title = (dto.Title ?? "").Trim();
        item.StudentName = (dto.StudentName ?? "").Trim();
        item.ImageUrl = (dto.ImageUrl ?? "").Trim();
        item.Category = string.IsNullOrWhiteSpace(dto.Category) ? "Xalqaro" : dto.Category.Trim();
        item.CertType = string.IsNullOrWhiteSpace(dto.CertType) ? "IELTS" : dto.CertType.Trim();
        item.OverallScore = (dto.OverallScore ?? "").Trim();
        item.Listening = (dto.Listening ?? "").Trim();
        item.Reading = (dto.Reading ?? "").Trim();
        item.Writing = (dto.Writing ?? "").Trim();
        item.Speaking = (dto.Speaking ?? "").Trim();
        item.ResultNote = (dto.ResultNote ?? "").Trim();
        item.Order = dto.Order;
        item.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("api/admin/landing/certificates/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> DeleteCertificate(string id)
    {
        var item = await db.LandingCertificates.FindAsync(id);
        if (item == null) return NotFound();
        db.LandingCertificates.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- FIKRLAR (TESTIMONIALS) ---

    [HttpGet("api/admin/landing/testimonials")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetTestimonials()
    {
        var list = await db.LandingTestimonials.OrderBy(t => t.Order).ToListAsync();
        return Ok(list);
    }

    /// <summary>Maydonlar NULLABLE — sabab <see cref="TeacherDto"/> dagi bilan bir xil.</summary>
    public record TestimonialDto(
        string? AuthorName,
        string? AuthorRole,
        string? AvatarUrl,
        int Rating,
        string? Comment,
        int Order,
        bool IsActive
    );

    [HttpPost("api/admin/landing/testimonials")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> CreateTestimonial([FromBody] TestimonialDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = new LandingTestimonial
        {
            AuthorName = (dto.AuthorName ?? "").Trim(),
            AuthorRole = (dto.AuthorRole ?? "").Trim(),
            AvatarUrl = (dto.AvatarUrl ?? "").Trim(),
            Rating = dto.Rating,
            Comment = (dto.Comment ?? "").Trim(),
            Order = dto.Order,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.LandingTestimonials.Add(item);
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("api/admin/landing/testimonials/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateTestimonial(string id, [FromBody] TestimonialDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = await db.LandingTestimonials.FindAsync(id);
        if (item == null) return NotFound();

        item.AuthorName = (dto.AuthorName ?? "").Trim();
        item.AuthorRole = (dto.AuthorRole ?? "").Trim();
        item.AvatarUrl = (dto.AvatarUrl ?? "").Trim();
        item.Rating = dto.Rating;
        item.Comment = (dto.Comment ?? "").Trim();
        item.Order = dto.Order;
        item.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("api/admin/landing/testimonials/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> DeleteTestimonial(string id)
    {
        var item = await db.LandingTestimonials.FindAsync(id);
        if (item == null) return NotFound();
        db.LandingTestimonials.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // --- FAQ (KO'P BERILADIGAN SAVOLLAR) ---

    [HttpGet("api/admin/landing/faqs")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> GetFaqs()
    {
        var list = await db.LandingFaqs.OrderBy(f => f.Order).ToListAsync();
        return Ok(list);
    }

    /// <summary>Maydonlar NULLABLE — sabab <see cref="TeacherDto"/> dagi bilan bir xil.</summary>
    public record FaqDto(
        string? Question,
        string? Answer,
        int Order,
        bool IsActive
    );

    [HttpPost("api/admin/landing/faqs")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> CreateFaq([FromBody] FaqDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = new LandingFaq
        {
            Question = (dto.Question ?? "").Trim(),
            Answer = (dto.Answer ?? "").Trim(),
            Order = dto.Order,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        db.LandingFaqs.Add(item);
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("api/admin/landing/faqs/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> UpdateFaq(string id, [FromBody] FaqDto? dto)
    {
        if (dto is null) return BadRequest(new { message = "Ma'lumot yuborilmadi" });
        var item = await db.LandingFaqs.FindAsync(id);
        if (item == null) return NotFound();

        item.Question = (dto.Question ?? "").Trim();
        item.Answer = (dto.Answer ?? "").Trim();
        item.Order = dto.Order;
        item.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("api/admin/landing/faqs/{id}")]
    [AdminPerm(SectionPerm)]
    public async Task<IActionResult> DeleteFaq(string id)
    {
        var item = await db.LandingFaqs.FindAsync(id);
        if (item == null) return NotFound();
        db.LandingFaqs.Remove(item);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ==================== DEFAULT FALLBACK DATA (FAQAT FAQ) ====================
    // ⚠️ Bu yerda FAQAT FAQ zaxirasi qoladi. O'qituvchilar va sertifikatlar zaxirasi ATAYIN
    // olib tashlangan: ommaviy saytda soxta o'qituvchi/sertifikat ko'rsatish — to'qib
    // chiqarilgan ma'lumot. Bazada yo'q bo'lsa javob bo'sh massiv qaytaradi, bo'limni
    // ko'rsatish-ko'rsatmaslikni klient hal qiladi. Yangi zaxira namuna QO'SHMANG.

    private static List<LandingFaq> GetDefaultFaqs()
    {
        return new List<LandingFaq>
        {
            new LandingFaq
            {
                Id = "f1",
                Question = "Sinov darsi bepulmi?",
                Answer = "Ha, barcha fanlar bo'yicha birinchi dars mutlaqo bepul. O'quvchi dars jarayoni va ustoz bilan tanishib ko'rgach qaror qabul qiladi.",
                Order = 1,
                IsActive = true
            },
            new LandingFaq
            {
                Id = "f2",
                Question = "Mobil ilova orqali nimalarni kuzatish mumkin?",
                Answer = "Ota-onalar va o'quvchilar kunlik davomat, jurnal baholari, oylik test natijalari, dars jadvali hamda oylik to'lovlar balansini real vaqt rejimida kuzatib boradilar.",
                Order = 2,
                IsActive = true
            },
            new LandingFaq
            {
                Id = "f3",
                Question = "Darslar necha kishi guruhda o'tiladi?",
                Answer = "Har bir guruhda maksimum 12-15 nafar o'quvchi ta'lim oladi. Bu har bir o'quvchiga individual e'tibor qaratish imkonini beradi.",
                Order = 3,
                IsActive = true
            }
        };
    }
}
