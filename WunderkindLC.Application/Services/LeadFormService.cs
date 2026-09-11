using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LID FORMALARI mantig'ining YAGONA joyi: forma tuzilishi, ommaviy ko'rinish, ariza qabul qilish
/// (lid yaratish/biriktirish) va kanal kesimidagi statistika.
///
/// <para><b>Modulning maqsadi:</b> har bir reklama kanali (Instagram, Facebook, Telegram, bannerdagi
/// QR ...) uchun ALOHIDA forma — o'z havolasi, o'z savollari va o'z MANBASI bilan. Shu sabab
/// "qaysi tarmoq nechta ariza va nechta haqiqiy o'quvchi keltirdi" degan savol hisobot darajasida
/// javob topadi (<see cref="BuildStatsAsync"/>).</para>
///
/// <para>Daraja testi (<see cref="LevelTestService"/>) bilan bir xil konvensiyalar: ommaviy slug,
/// telefon bo'yicha DUBLIKAT lid ochmaslik (<see cref="LeadIntake"/>), natijani lid izohiga yozish.</para>
/// </summary>
public static class LeadFormService
{
    // ==================== Maydon turlari — YAGONA katalog ====================

    public const string KindText = "text";
    public const string KindTextarea = "textarea";
    public const string KindNumber = "number";
    public const string KindSelect = "select";
    public const string KindRadio = "radio";
    public const string KindCheckbox = "checkbox";

    /// <summary>Qo'llab-quvvatlanadigan maydon turlari (frontend ro'yxati ham shundan quriladi).</summary>
    public static readonly IReadOnlyList<string> Kinds =
        new[] { KindText, KindTextarea, KindNumber, KindSelect, KindRadio, KindCheckbox };

    /// <summary>Bu tur uchun variantlar SHART (variantsiz maydon foydalanuvchiga bo'sh ko'rinardi).</summary>
    public static bool NeedsOptions(string? kind) =>
        kind is KindSelect or KindRadio or KindCheckbox;

    /// <summary>Bir nechta javob tanlash mumkin bo'lgan yagona tur.</summary>
    public static bool IsMultiple(string? kind) => kind == KindCheckbox;

    /// <summary>Noma'lum tur oddiy matnga tushadi (forma buzilib qolgandan ko'ra ishlagani yaxshi).</summary>
    public static string NormalizeKind(string? kind) =>
        kind is not null && Kinds.Contains(kind) ? kind : KindText;

    /// <summary>Bir formadagi qo'shimcha savollar chegarasi — ochiq forma spam maydonga aylanmasin.</summary>
    public const int MaxFields = 25;
    public const int MaxOptions = 30;
    /// <summary>Bitta javobning maksimal uzunligi (anonim endpoint — kirish cheklanadi).</summary>
    public const int MaxAnswerLength = 500;
    /// <summary>Statistikadagi kunlik grafik uzunligi.</summary>
    public const int DailyDays = 30;

    /// <summary>Bir formadagi kurs variantlari chegarasi.</summary>
    public const int MaxCourseOptions = 30;

    // ==================== Kurs variantlari va havolalar ====================

    /// <summary>
    /// Kurs variantlarini tozalaydi: bo'shlarni olib tashlaydi, takrorlarni (registr farqisiz)
    /// birlashtiradi, tartibni SAQLAYDI (admin yozgan tartib mijozga shundayligicha ko'rinadi).
    /// </summary>
    public static List<string> CleanCourseOptions(IEnumerable<string>? raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outList = new List<string>();
        foreach (var o in raw ?? Enumerable.Empty<string>())
        {
            var v = (o ?? "").Trim();
            if (v.Length == 0 || v.Length > 100) continue;
            if (!seen.Add(v)) continue;
            outList.Add(v);
            if (outList.Count >= MaxCourseOptions) break;
        }
        return outList;
    }

    /// <summary>
    /// Havolani xavfsiz ko'rinishga keltiradi: sxemasiz yozilgan bo'lsa `https://` qo'shiladi.
    ///
    /// <para>⚠️ FAQAT `http`/`https` qabul qilinadi — havola mijozning brauzerida ochiladi va
    /// `javascript:` kabi sxema o'sha sahifada kod ishga tushirardi. Noto'g'ri/uzun qiymat jimgina
    /// BO'SH ga aylanadi (ikonka umuman chizilmaydi), chunki bu admin sozlamasi, mijoz kiritmaydi.</para>
    /// </summary>
    public static string NormalizeUrl(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0 || s.Length > 300) return "";
        // Sxema yozilganmi? (`instagram.com/...` — yo'q, `https://...` — ha)
        if (System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-zA-Z][a-zA-Z0-9+.-]*:"))
        {
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "";
        }
        else s = "https://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
        return uri.ToString();
    }

    /// <summary>Ommaviy ikonkalar ro'yxati — faqat to'ldirilganlari, TAYIN tartibda.</summary>
    public static List<PublicSocialLinkDto> SocialsOf(LeadForm f)
    {
        var items = new (string Kind, string Url)[]
        {
            ("instagram", f.InstagramUrl),
            ("telegram", f.TelegramUrl),
            ("facebook", f.FacebookUrl),
            ("youtube", f.YoutubeUrl),
            ("website", f.WebsiteUrl),
        };
        return items.Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .Select(x => new PublicSocialLinkDto(x.Kind, x.Url))
            .ToList();
    }

    /// <summary>Payload'dagi havolalarni formaga yozadi (har biri <see cref="NormalizeUrl"/> dan o'tadi).</summary>
    public static void WriteSocials(LeadForm f, LeadFormSocialsDto? s)
    {
        f.InstagramUrl = NormalizeUrl(s?.Instagram);
        f.TelegramUrl = NormalizeUrl(s?.Telegram);
        f.FacebookUrl = NormalizeUrl(s?.Facebook);
        f.YoutubeUrl = NormalizeUrl(s?.Youtube);
        f.WebsiteUrl = NormalizeUrl(s?.Website);
    }

    // ==================== Tuzilish (admin) ====================

    /// <summary>Forma nomidan o'qiladigan, NOYOB slug (`instagram-sinov-3f2a`).</summary>
    public static Task<string> GenerateSlugAsync(IAppDbContext db, string? title) =>
        SlugUtil.UniqueAsync(title, slug => db.LeadForms.AnyAsync(f => f.Slug == slug), "forma");

    /// <summary>Admin muharriri uchun bitta formaning to'liq tafsiloti (maydonlari bilan).</summary>
    public static async Task<LeadFormDetailDto> BuildDetailAsync(IAppDbContext db, LeadForm f)
    {
        var fields = await db.LeadFormFields.Where(x => x.FormId == f.Id)
            .OrderBy(x => x.Order).ToListAsync();
        return new LeadFormDetailDto(
            f.Id, f.Title, f.Slug, f.Source, f.CourseName, f.CourseOptions,
            f.Intro, f.SuccessText, f.ButtonText,
            f.AskAge, f.AskCourse, f.AskParentPhone, f.IsActive,
            f.Views, f.CreatedAt, f.CreatedBy,
            fields.Select(x => new LeadFormFieldDto(
                x.Id, x.Label, x.Kind, x.Options, x.Placeholder, x.Required, x.Order)).ToList(),
            new LeadFormSocialsDto(f.InstagramUrl, f.TelegramUrl, f.FacebookUrl, f.YoutubeUrl, f.WebsiteUrl));
    }

    /// <summary>
    /// Payload'dagi maydonlarni entity'ga yozadi (mavjudlari OLDIN o'chirilgan bo'lishi kerak —
    /// to'liq almashtirish, daraja testidagi savollar bilan bir xil, sodda va ishonchli usul).
    /// </summary>
    public static void WriteFields(IAppDbContext db, string formId, List<LeadFormFieldInput>? fields)
    {
        if (fields is null) return;
        var order = 0;
        foreach (var f in fields.Take(MaxFields))
        {
            var label = (f.Label ?? "").Trim();
            if (label.Length == 0) continue; // yorliqsiz maydon — foydalanuvchiga ma'nosiz
            var kind = NormalizeKind(f.Kind);
            var opts = (f.Options ?? new()).Select(o => (o ?? "").Trim())
                .Where(o => o.Length > 0).Distinct().Take(MaxOptions).ToList();
            // Variantli tur variantsiz qolsa — foydalanuvchi hech narsa tanlay olmasdi, shu sabab
            // maydon oddiy matnga tushiriladi (jimgina yo'qotib yuborilmaydi).
            if (NeedsOptions(kind) && opts.Count == 0) kind = KindText;
            db.LeadFormFields.Add(new LeadFormField
            {
                FormId = formId,
                Label = label,
                Kind = kind,
                Options = NeedsOptions(kind) ? opts : new(),
                Placeholder = (f.Placeholder ?? "").Trim(),
                Required = f.Required,
                Order = order++,
            });
        }
    }

    // ==================== Ommaviy ko'rinish ====================

    /// <summary>Ommaviy forma (anonim). Forma yo'q yoki faol emas — null.</summary>
    public static async Task<PublicLeadFormDto?> GetPublicAsync(IAppDbContext db, string slug)
    {
        var form = await db.LeadForms.FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive);
        if (form is null) return null;
        var fields = await db.LeadFormFields.Where(x => x.FormId == form.Id)
            .OrderBy(x => x.Order).ToListAsync();
        // Kurs variantlari — MARKAZ kurslaridan EMAS, formaning O'ZIDA yozilganidan.
        var courses = form.AskCourse ? form.CourseOptions.ToList() : new List<string>();
        // Variant yozilmagan bo'lsa savol ko'rsatilmaydi (bo'sh select ma'nosiz).
        var askCourse = form.AskCourse && courses.Count > 0;
        return new PublicLeadFormDto(
            form.Title, form.Intro,
            string.IsNullOrWhiteSpace(form.ButtonText) ? "Yuborish" : form.ButtonText.Trim(),
            form.CourseName,
            form.AskAge, askCourse, form.AskParentPhone,
            courses,
            fields.Select(x => new PublicLeadFormFieldDto(
                x.Id, x.Label, x.Kind, x.Options, x.Placeholder, x.Required)).ToList(),
            SocialsOf(form));
    }

    /// <summary>
    /// Sub-kanal belgisini tozalaydi (`?ref=story`): faqat harf/raqam/`-`/`_`, ko'pi bilan 40 belgi.
    /// Ochiq havoladan kelgani uchun XOM saqlanmaydi.
    /// </summary>
    public static string NormalizeRef(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return "";
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(40).ToArray();
        return new string(chars);
    }

    /// <summary>Ariza natijasi: <see cref="Result"/> — muvaffaqiyat, <see cref="Error"/> — 400 xabari,
    /// ikkalasi ham null — forma topilmadi (404).</summary>
    public sealed record SubmitOutcome(LeadFormSubmitResultDto? Result, string? Error);

    /// <summary>
    /// ARIZANI QABUL QILADI: tekshiradi, javoblarni yig'adi, LID yaratadi (yoki telefon bo'yicha
    /// mavjudiga biriktiradi) va topshiruvni saqlaydi. SaveChanges shu yerda bajariladi.
    ///
    /// <para>⚠️ TAKRORIY ariza mavjud lidning MANBASINI o'zgartirmaydi — birinchi teginish
    /// (first-touch) saqlanadi, aks holda "bu odamni qaysi kanal olib keldi" savoli javobsiz
    /// qolardi. Forma kesimidagi hisobot esa baribir to'g'ri: topshiruv o'z <c>FormId</c> si bilan
    /// yoziladi.</para>
    /// </summary>
    public static async Task<SubmitOutcome> SubmitAsync(
        IAppDbContext db, string slug, LeadFormSubmitRequest req,
        TelegramService? telegram = null, AutoMessageService? autoMsg = null)
    {
        var form = await db.LeadForms.FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive);
        if (form is null) return new SubmitOutcome(null, null);

        static SubmitOutcome Err(string m) => new(null, m);

        var fullName = (req.FullName ?? "").Trim();
        if (fullName.Length == 0) return Err("Ism-familiyani kiriting");
        if (fullName.Length > 100) return Err("Ism-familiya juda uzun");

        var (phoneValid, phoneNorm, phoneError) = PhoneUtil.Validate(req.Phone);
        if (!phoneValid) return Err(phoneError ?? "Telefon raqami noto'g'ri");

        var parentPhone = "";
        if (form.AskParentPhone && !string.IsNullOrWhiteSpace(req.ParentPhone))
        {
            var (ok, norm, err) = PhoneUtil.Validate(req.ParentPhone);
            if (!ok) return Err($"Ota-onaning telefoni: {err ?? "noto'g'ri"}");
            parentPhone = norm;
        }
        var age = form.AskAge ? Math.Clamp(req.Age, 0, 120) : 0;

        // ---- Qo'shimcha savollar ----
        var fields = await db.LeadFormFields.Where(x => x.FormId == form.Id)
            .OrderBy(x => x.Order).ToListAsync();
        var answers = new List<SurveyAnswerDto>();
        foreach (var f in fields)
        {
            var raw = req.Answers is not null && req.Answers.TryGetValue(f.Id, out var v) && v is not null
                ? v : new List<string>();
            var vals = raw.Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Select(x => x.Length > MaxAnswerLength ? x[..MaxAnswerLength] : x)
                .ToList();

            if (NeedsOptions(f.Kind))
                // Variantli maydonda FAQAT mavjud variantlar qabul qilinadi — ochiq endpointga
                // qo'lda yuborilgan begona matn lidga tushib qolmasin.
                vals = vals.Where(x => f.Options.Contains(x)).Distinct().ToList();
            if (!IsMultiple(f.Kind) && vals.Count > 1) vals = vals.Take(1).ToList();

            if (f.Required && vals.Count == 0)
                return Err($"«{f.Label}» — bu maydon to'ldirilishi shart");
            answers.Add(new SurveyAnswerDto(f.Label, vals));
        }

        // ---- Kurs (qiziqish) ----
        var courseName = "";
        if (form.AskCourse && !string.IsNullOrWhiteSpace(req.Course))
        {
            var wanted = req.Course.Trim();
            // Faqat FORMANING O'Z variantlari qabul qilinadi (registr farqisiz). Ro'yxatda yo'q
            // qiymat jimgina rad etiladi va formaning kursiga qaytiladi — ochiq endpointga qo'lda
            // yuborilgan axlat matn lidning "qiziqqan kursi" bo'lib qolmasin.
            courseName = form.CourseOptions
                .FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) ?? "";
        }
        if (courseName.Length == 0) courseName = form.CourseName;

        var refTag = NormalizeRef(req.Ref);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        // ---- Lid izohi (menejer AYNAN shuni o'qiydi) ----
        var lines = new List<string> { $"Forma: {form.Title}" };
        if (refTag.Length > 0) lines.Add($"Havola belgisi: {refTag}");
        if (age > 0) lines.Add($"Yoshi: {age}");
        if (courseName.Length > 0) lines.Add($"Kurs: {courseName}");
        if (parentPhone.Length > 0) lines.Add($"Ota-ona telefoni: {parentPhone}");
        foreach (var a in answers)
            lines.Add($"• {a.Question}: {(a.Answers.Count > 0 ? string.Join(", ", a.Answers) : "—")}");
        var noteLine = string.Join("\n", lines);

        // ---- LID: mavjudiga biriktirish yoki yangisini ochish ----
        var existing = await LeadIntake.FindByPhoneAsync(db, req.Phone);
        var isNewLead = existing is null;
        Lead lead;
        if (existing is not null)
        {
            lead = existing;
            lead.Note = ((lead.Note ?? "").TrimEnd() + "\n" + noteLine).Trim();
            if (string.IsNullOrWhiteSpace(lead.InterestSubject) && courseName.Length > 0)
                lead.InterestSubject = courseName;
            if (!string.IsNullOrWhiteSpace(fullName)
                && (string.IsNullOrWhiteSpace(lead.FullName) || lead.FullName.StartsWith("Noma'lum")))
                lead.FullName = fullName;
            if (string.IsNullOrWhiteSpace(lead.FatherPhone) && parentPhone.Length > 0)
                lead.FatherPhone = parentPhone;
            // TAKRORIY MUROJAAT belgisi — lidning BOSQICHI ataylab o'zgartirilmaydi (first-touch),
            // shuning uchun "yo'qotilgan" ustunidagi odam qayta murojaat qilgani kanbanda AYNAN
            // shu belgi orqali ko'rinadi (aks holda faqat izoh va Telegram xabarida qolardi).
            lead.RepeatCount += 1;
            lead.LastRepeatAt = now;
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "note", ActorName = "Forma", CreatedAt = now,
                Text = $"«{form.Title}» formasini yana to'ldirdi",
            });
        }
        else
        {
            var firstStage = await LeadIntake.FirstStageIdAsync(db);
            lead = new Lead
            {
                FullName = fullName,
                Phone = phoneNorm,
                FatherPhone = parentPhone,
                Source = form.Source,
                InterestSubject = courseName,
                Note = noteLine,
                Stage = firstStage,
                CreatedAt = now,
            };
            db.Leads.Add(lead);
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "created", ActorName = "Forma", CreatedAt = now,
                Text = $"«{form.Title}» formasi orqali keldi",
                // Voronka analitikasi uchun: lid birinchi bosqichga tushdi (ActorUserId yo'q — o'zi to'ldirdi).
                ToStage = firstStage,
            });
        }

        db.LeadFormSubmissions.Add(new LeadFormSubmission
        {
            FormId = form.Id, LeadId = lead.Id, IsNewLead = isNewLead,
            // Arizada AYNAN kiritilgan ma'lumot saqlanadi (lidnikini emas): takroriy arizada
            // odam ismini boshqacha yozgan bo'lishi mumkin — bu forma tarixi uchun muhim.
            FullName = fullName, Phone = phoneNorm, ParentPhone = parentPhone,
            Age = age, CourseName = courseName, Ref = refTag,
            AnswersJson = answers.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(answers) : "",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        // Botdagi adminlarga xabarnoma (xato jim yutiladi — arizani buzmasin).
        if (telegram is not null)
            await LeadNotifier.NotifyNewLeadAsync(db, telegram, lead, isNewLead: isNewLead,
                createdBy: $"Forma: {form.Title}");

        // Avto-xabar FAQAT yangi lidga — takroriy ariza uchun tanishuv SMS'i qayta ketmasin.
        if (autoMsg is not null && isNewLead)
            await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.LeadNew, lead);

        var msg = string.IsNullOrWhiteSpace(form.SuccessText)
            ? "Rahmat! Arizangiz qabul qilindi — tez orada siz bilan bog'lanamiz."
            : form.SuccessText.Trim();
        return new SubmitOutcome(new LeadFormSubmitResultDto(msg), null);
    }

    // ==================== Arizalar ro'yxati (admin) ====================

    /// <summary>JSON javoblarni DTO ro'yxatiga (buzilgan bo'lsa bo'sh).</summary>
    public static List<SurveyAnswerDto> ParseAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<SurveyAnswerDto>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Arizalar ro'yxati — lidning HOZIRGI holati bilan (o'quvchi bo'ldimi, faolmi).</summary>
    public static async Task<List<LeadFormSubmissionDto>> BuildSubmissionsAsync(
        IAppDbContext db, List<LeadFormSubmission> subs, IReadOnlyDictionary<string, string> formTitles)
    {
        var outcome = await LeadOutcome.BuildAsync(db, subs.Select(s => s.LeadId));
        return subs.Select(s =>
        {
            var stage = outcome.StageOf(s.LeadId);
            return new LeadFormSubmissionDto(
                s.Id, s.FormId, formTitles.GetValueOrDefault(s.FormId, ""),
                s.FullName, s.Phone, s.ParentPhone, s.Age, s.CourseName, s.Ref, s.CreatedAt,
                s.LeadId, s.IsNewLead,
                outcome.StudentOf(s.LeadId), outcome.IsActive(s.LeadId), outcome.IsDeletedLead(s.LeadId),
                stage.Title, stage.Color,
                outcome.HasPaid(s.LeadId), outcome.PaidTotal(s.LeadId), outcome.FirstPaidAt(s.LeadId),
                ParseAnswers(s.AnswersJson));
        }).ToList();
    }

    // ==================== Statistika (kanal kesimi) ====================

    /// <summary>
    /// VORONKA: ochildi → ariza → lid → o'quvchi. Modulning asosiy savoli shu — "qaysi ijtimoiy
    /// tarmoq haqiqiy o'quvchi keltiryapti".
    ///
    /// <para>⚠️ Konversiya foizi <b>takrorsiz LIDLAR</b> bo'yicha hisoblanadi (arizalar bo'yicha
    /// emas): bir odam formani ikki marta to'ldirsa ham u bitta mijoz — aks holda ko'p to'ldirilgan
    /// forma sun'iy ravishda yomon ko'rinardi.</para>
    /// </summary>
    /// <summary>
    /// Statistika uchun arizaning YENGIL kesimi. Voronka faqat shu beshta ustunga tayanadi —
    /// ariza entity'sining o'zi (ism, telefon, javoblar JSON'i) hisobga umuman kirmaydi, shuning
    /// uchun butun jadval o'rniga faqat shular o'qiladi.
    /// </summary>
    private sealed record SubRow(string FormId, string LeadId, bool IsNewLead, string Ref, string CreatedAt);

    /// <summary>
    /// TAKRORSIZ lidlar soni: jami + forma kesimida.
    ///
    /// <para>⚠️ Bu son <see cref="LeadFormStatsDto"/> da ALOHIDA maydon sifatida YO'Q va javobdan
    /// chiqarib olib ham bo'lmaydi: <c>ByForm</c> — FORMALAR kesimi (bir odam ikki formani
    /// to'ldirsa ikki qatorda sanaladi, ya'ni yig'indi TAKRORLI chiqadi), <c>ByStage</c> esa
    /// bosqichsiz (ustuni o'chirilgan) lidni umuman qoldiradi. Foizlarning MAXRAJI aynan shu
    /// takrorsiz son bo'lgani uchun u alohida, lekin <see cref="BuildStatsAsync"/> ichidagi
    /// <c>Funnel</c> bilan AYNAN bir xil qoidada hisoblanadi: <c>LeadId</c> bo'sh qatorlar
    /// sanoqqa umuman kirmaydi.</para>
    ///
    /// <para>Bitta so'rov bilan olinadi: <c>(forma, lid)</c> juftliklari DB tomonda takrorsizlanadi,
    /// keyin xotirada ikki kesimga bo'linadi.</para>
    /// </summary>
    public static async Task<(int Total, Dictionary<string, int> ByForm)> DistinctLeadCountsAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var pairs = await db.LeadFormSubmissions.AsNoTracking()
            .Where(s => s.LeadId != "")
            .Select(s => new { s.FormId, s.LeadId })
            .Distinct()
            .ToListAsync(ct);
        return (
            pairs.Select(p => p.LeadId).Distinct().Count(),
            pairs.GroupBy(p => p.FormId).ToDictionary(g => g.Key, g => g.Count()));
    }

    public static async Task<LeadFormStatsDto> BuildStatsAsync(IAppDbContext db)
    {
        var forms = await db.LeadForms.AsNoTracking()
            .Select(f => new { f.Id, f.Title, f.Source, f.IsActive, f.Views }).ToListAsync();
        var subs = await db.LeadFormSubmissions.AsNoTracking()
            .Select(s => new SubRow(s.FormId, s.LeadId, s.IsNewLead, s.Ref, s.CreatedAt))
            .ToListAsync();

        var outcome = await LeadOutcome.BuildAsync(db, subs.Select(s => s.LeadId));

        // BUTUN CRM manzarasi ham shu sahifada kerak: bu yerdagi raqamlar faqat FORMALARDAN
        // kelganlarni sanaydi, markazda esa qo'lda kiritilgan, daraja testidan va Instagramdan
        // kelgan lidlar ham bor. Ularsiz sahifa "markazning hamma lidi" deb o'qilardi.
        var overview = await LeadCrmOverview.BuildAsync(db);

        // Bir guruh (forma / manba) uchun voronka sanoqlari — takrorsiz lidlar bo'yicha.
        //
        // ⚠️ `Paid` va `Revenue` ham TAKRORSIZ lid bo'yicha: bir odam ikkita forma to'ldirgan
        // bo'lsa uning to'lovi har bir formada ko'rinadi (savol "shu kanal pul keltirdimi"),
        // lekin BITTA forma ichida ikki marta sanalmaydi.
        (int Subs, int NewLeads, int Converted, int Active, int Paid, decimal Revenue,
            double ConvertRate, double PayRate) Funnel(IEnumerable<SubRow> rows)
        {
            var list = rows.ToList();
            var leadIds = list.Select(s => s.LeadId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            var converted = leadIds.Count(id => outcome.StudentOf(id) != null);
            var active = leadIds.Count(outcome.IsActive);
            var paid = leadIds.Count(outcome.HasPaid);
            // Tushum — faqat MUSBAT sof summalar: to'liq qaytarilgan pul kanalning "daromadi" emas.
            var revenue = leadIds.Sum(id => Math.Max(0m, outcome.PaidTotal(id)));
            var rate = leadIds.Count > 0 ? Math.Round(converted * 100.0 / leadIds.Count, 1) : 0;
            var payRate = leadIds.Count > 0 ? Math.Round(paid * 100.0 / leadIds.Count, 1) : 0;
            return (list.Count, list.Count(s => s.IsNewLead), converted, active, paid, revenue, rate, payRate);
        }

        var byForm = forms
            .Select(f =>
            {
                var fn = Funnel(subs.Where(s => s.FormId == f.Id));
                return new LeadFormStatRowDto(
                    f.Id, f.Title, f.Source, f.IsActive, f.Views, fn.Subs, fn.NewLeads,
                    fn.Converted, fn.Active, fn.Paid, fn.Revenue,
                    f.Views > 0 ? Math.Round(fn.Subs * 100.0 / f.Views, 1) : 0,
                    fn.ConvertRate, fn.PayRate);
            })
            .OrderByDescending(r => r.Submissions).ThenBy(r => r.Title)
            .ToList();

        var formSource = forms.ToDictionary(f => f.Id, f => string.IsNullOrWhiteSpace(f.Source) ? "" : f.Source);
        var bySource = forms
            .GroupBy(f => formSource[f.Id])
            .Select(g =>
            {
                var ids = g.Select(f => f.Id).ToHashSet();
                var fn = Funnel(subs.Where(s => ids.Contains(s.FormId)));
                return new LeadFormSourceDto(
                    g.Key, g.Count(), fn.Subs, fn.Converted, fn.Active, fn.Paid, fn.Revenue);
            })
            .OrderByDescending(x => x.Submissions).ToList();

        var byRef = subs
            .GroupBy(s => s.Ref ?? "")
            .Select(g =>
            {
                var fn = Funnel(g);
                return new LeadFormRefDto(g.Key, fn.Subs, fn.Converted, fn.Paid);
            })
            .OrderByDescending(x => x.Submissions).ToList();

        // BOSQICHLAR kesimi — formalardan kelgan TAKRORSIZ lidlar hozir qaysi ustunda turibdi.
        // Bosqichi yo'q (yoki o'chirilgan) lid ro'yxatga kirmaydi — kanbanda ham ko'rinmaydi.
        var byStage = subs.Select(s => s.LeadId).Where(x => !string.IsNullOrEmpty(x)).Distinct()
            .Select(id => outcome.StageOf(id))
            .Where(st => st.Title.Length > 0)
            .GroupBy(st => (st.Title, st.Color))
            .Select(g => new LeadStageCountDto(g.Key.Title, g.Key.Color, g.Count()))
            .OrderByDescending(x => x.Leads).ThenBy(x => x.Stage)
            .ToList();

        // Kunlik oqim — oxirgi DailyDays kun, BO'SH kunlar ham (grafik uzilib qolmasin).
        var today = AppClock.Now.Date;
        var counts = subs.GroupBy(s => (s.CreatedAt ?? "") is { Length: >= 10 } c ? c[..10] : "")
            .ToDictionary(g => g.Key, g => g.Count());
        var daily = Enumerable.Range(0, DailyDays)
            .Select(i => today.AddDays(-(DailyDays - 1 - i)).ToString("yyyy-MM-dd"))
            .Select(d => new DayCountDto(d, counts.GetValueOrDefault(d, 0)))
            .ToList();

        var total = Funnel(subs);
        return new LeadFormStatsDto(
            forms.Count, forms.Count(f => f.IsActive), forms.Sum(f => f.Views),
            total.Subs, total.NewLeads, total.Converted, total.Active, total.Paid, total.Revenue,
            byForm, bySource, byRef, byStage, daily,
            // BUTUN CRM manzarasi — daraja testi statistikasi ham AYNAN shu funksiyadan oladi.
            Overview: overview);
    }
}
