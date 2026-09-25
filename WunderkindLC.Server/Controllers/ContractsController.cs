using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
using System.Globalization;
using System.Text.Json;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Shartnomalar bo'limi (admin): Word/matnli andoza, oluvchini (o'quvchi yoki xodim) tanlab
/// shartnomani <c>@</c>-o'rinbosarlar bilan to'ldirish. Ikki yo'l: (1) to'ldirilgan .docx faylni
/// YUKLAB OLISH (<c>build</c> — Telegram shart emas), (2) Telegram bot orqali yuborish (<c>send</c> —
/// faqat ro'yxatdan o'tganlarga). O'rinbosarlar bazadagi haqiqiy ma'lumot bilan almashtiriladi.
///
/// Hosil bo'lgan .docx <c>uploads</c> papkasiga saqlanadi va <c>Contract</c> yozuviga bog'lanadi.
/// So'ng superadmin hujjatni yakunlab, TAYYOR PDF nusxasini <c>{id}/pdf</c> orqali yuklaydi —
/// faqat shundan keyin shartnoma oluvchining (o'qituvchi/o'quvchi) ilovasida ko'rinadi.
/// </summary>
[ApiController]
[Authorize]
// O'QISH ham darvozalanadi (ReadRequiresPerm): `GET /` javobida shartnomaning `PdfUrl` va `DocxUrl`
// — ya'ni `/uploads/...` HUJJAT manzillari qaytadi. `/uploads` autentifikatsiyasiz berilgani uchun
// manzilni bir marta olgan xodim shartnomani abadiy o'qiy oladi. Shu sabab GET uchun ham
// `contracts` bo'limi ruxsati (biror amali) talab qilinadi.
[AdminPerm("contracts", ReadRequiresPerm = true)]
[Route("api/admin/contracts")]
public class ContractsController(
    AppDbContext db, ContractService contracts, TelegramService telegram, AuditService audit)
    : ControllerBase
{
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // ---------- Andozalar ----------

    [HttpGet("templates")]
    public async Task<ActionResult<IEnumerable<ContractTemplateDto>>> Templates([FromQuery] string? target)
    {
        var q = db.ContractTemplates.AsQueryable();
        if (!string.IsNullOrEmpty(target)) q = q.Where(t => t.Target == target);
        return (await q.OrderByDescending(t => t.UploadedAt).ToListAsync()).Select(ToDto).ToList();
    }

    [HttpPost("templates")]
    public async Task<ActionResult<ContractTemplateDto>> CreateTemplate(CreateContractTemplateRequest req)
    {
        // Word fayl YOKI custom matn — kamida bittasi bo'lishi shart.
        if (string.IsNullOrWhiteSpace(req.FileUrl) && string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Word fayl yuklang yoki matnli andoza yozing" });
        var tpl = new ContractTemplate
        {
            Target = req.Target == "staff" ? "staff" : "parent",
            Name = (req.Name ?? "").Trim(),
            FileUrl = req.FileUrl ?? "",
            FileName = req.FileName ?? "",
            Body = (req.Body ?? "").Trim(),
            FieldsJson = SerializeFields(req.Fields),
        };
        db.ContractTemplates.Add(tpl);
        audit.Record("Contract", tpl.Id, "create", $"Shartnoma andozasi qo'shildi: {tpl.Name}");
        await db.SaveChangesAsync();
        return ToDto(tpl);
    }

    /// <summary>Custom (matnli) andozani tahrirlash — nom va matn.</summary>
    [HttpPut("templates/{id}")]
    public async Task<ActionResult<ContractTemplateDto>> UpdateTemplate(string id, CreateContractTemplateRequest req)
    {
        var tpl = await db.ContractTemplates.FindAsync(id);
        if (tpl is null) return NotFound();
        if (string.IsNullOrWhiteSpace(tpl.Body))
            return BadRequest(new { message = "Faqat matnli andozani tahrirlash mumkin" });
        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Matnli andoza bo'sh bo'lishi mumkin emas" });
        tpl.Name = (req.Name ?? "").Trim();
        tpl.Body = req.Body.Trim();
        tpl.FieldsJson = SerializeFields(req.Fields);
        audit.Record("Contract", tpl.Id, "update", $"Shartnoma andozasi tahrirlandi: {tpl.Name}");
        await db.SaveChangesAsync();
        return ToDto(tpl);
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        var tpl = await db.ContractTemplates.FindAsync(id);
        if (tpl is null) return NotFound();
        audit.Record("Contract", tpl.Id, "delete", $"Shartnoma andozasi o'chirildi: {tpl.Name}");
        db.ContractTemplates.Remove(tpl);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Oluvchilar ----------

    /// <summary>O'quvchi oluvchilar — har o'quvchi alohida qator (shartnoma o'quvchi bo'yicha tuziladi).</summary>
    [HttpGet("recipients/students")]
    public async Task<ActionResult<IEnumerable<StudentRecipientDto>>> StudentRecipients()
    {
        var students = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived).OrderBy(s => s.FullName).ToListAsync();
        var regStudentIds = (await db.TelegramRegistrations
            .Where(r => r.StudentId != "").Select(r => r.StudentId).ToListAsync())
            .ToHashSet();
        var lastByKey = await LastNumbersAsync("parent");

        var groupNames = await db.Classes.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name);
        var memberGroups = (await db.StudentGroups.AsNoTracking().Where(sg => sg.IsActive)
                .Select(sg => new { sg.StudentId, sg.GroupId }).ToListAsync())
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => string.Join(", ",
                g.Select(m => groupNames.GetValueOrDefault(m.GroupId, "")).Where(n => n.Length > 0).Distinct()));

        return students.Select(s =>
        {
            // Eski yozuvlar ota-ona kaliti ("p:{telefon}") bilan saqlangan — oxirgi raqamda ularni ham hisobga olamiz.
            int? last = lastByKey.TryGetValue(s.Id, out var byId) ? byId : null;
            if (lastByKey.TryGetValue(LegacyParentKey(s), out var byLegacy))
                last = last is null ? byLegacy : Math.Max(last.Value, byLegacy);
            var groups = memberGroups.GetValueOrDefault(s.Id, "");
            return new StudentRecipientDto(
                s.Id, s.FullName, s.ParentFullName, s.ParentPhone,
                string.IsNullOrEmpty(groups) ? s.ClassName : groups,
                regStudentIds.Contains(s.Id), last);
        }).ToList();
    }

    [HttpGet("recipients/staff")]
    public async Task<ActionResult<IEnumerable<StaffRecipientDto>>> StaffRecipients()
    {
        var teachers = await db.Teachers.Where(t => !t.IsArchived).OrderBy(t => t.FullName).ToListAsync();
        var regTeacherIds = (await db.TelegramRegistrations
            .Where(r => r.TeacherId != null).Select(r => r.TeacherId!).ToListAsync())
            .ToHashSet();
        var lastByKey = await LastNumbersAsync("staff");
        return teachers.Select(t => new StaffRecipientDto(
            t.Id, t.FullName, t.Phone, regTeacherIds.Contains(t.Id),
            lastByKey.TryGetValue(t.Id, out var n) ? n : null)).ToList();
    }

    // ---------- Shartnoma tuzish (.docx yuklab olish) ----------

    /// <summary>
    /// Bitta oluvchi (o'quvchi yoki xodim) uchun shartnomani to'ldirib .docx sifatida qaytaradi —
    /// admin faylni YUKLAB OLADI (Telegram ro'yxati shart emas). Shartnoma raqami beriladi va tarixga yoziladi.
    /// </summary>
    [HttpPost("build")]
    public async Task<IActionResult> Build(BuildContractRequest req)
    {
        var tpl = await db.ContractTemplates.FindAsync(req.TemplateId);
        if (tpl is null) return BadRequest(new { message = "Andoza topilmadi" });

        var isCustom = !string.IsNullOrWhiteSpace(tpl.Body);
        byte[]? bytes = null;
        if (!isCustom)
        {
            bytes = contracts.ReadTemplate(tpl.FileUrl);
            if (bytes is null) return BadRequest(new { message = "Andoza fayli topilmadi" });
        }

        var ctx = await LoadCtxAsync();
        var number = (await db.Contracts.AnyAsync() ? await db.Contracts.MaxAsync(c => c.Number) : 0) + 1;
        var today = AppClock.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        string recipientName;
        Dictionary<string, string> tokens;
        var target = req.Target == "staff" ? "staff" : "parent";
        if (target == "staff")
        {
            var t = await db.Teachers.FindAsync(req.RecipientKey);
            if (t is null) return NotFound(new { message = "Xodim topilmadi" });
            recipientName = t.FullName;
            tokens = StaffTokens(t, ctx, number, today);
        }
        else
        {
            var s = await db.Students.FindAsync(req.RecipientKey);
            if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
            recipientName = s.FullName;
            tokens = StudentTokens(s, ctx, number, today);
        }

        var merged = MergeTokens(ParseFields(tpl.FieldsJson), tokens);
        var filled = isCustom
            ? contracts.BuildDocxFromText(tpl.Body, merged)
            : contracts.FillTemplate(bytes!, merged);

        // Word nusxa saqlanadi — admin uni keyin qayta yuklab olib, yakunlab, PDF qilib qaytadan yuklaydi.
        var docxUrl = await contracts.SaveUploadAsync(filled, ".docx");

        db.Contracts.Add(new Contract
        {
            Target = target,
            RecipientKey = req.RecipientKey,
            RecipientName = recipientName,
            Number = number,
            TemplateId = tpl.Id,
            SentAt = AppClock.Now,
            Delivered = false,
            Status = "downloaded",
            DocxUrl = docxUrl,
            FileName = $"Shartnoma № {number}",
            TemplateName = tpl.Name,
        });
        await db.SaveChangesAsync();

        return File(filled, DocxMime, $"shartnoma-{number}.docx");
    }

    // ---------- Telegram orqali yuborish ----------

    [HttpPost("send")]
    public async Task<ActionResult<IEnumerable<SendResultDto>>> Send(SendContractsRequest req)
    {
        var tpl = await db.ContractTemplates.FindAsync(req.TemplateId);
        if (tpl is null) return BadRequest(new { message = "Andoza topilmadi" });

        // Custom (matnli) andoza — matndan hosil qilamiz; aks holda yuklangan .docx faylni to'ldiramiz.
        var isCustom = !string.IsNullOrWhiteSpace(tpl.Body);
        byte[]? bytes = null;
        if (!isCustom)
        {
            bytes = contracts.ReadTemplate(tpl.FileUrl);
            if (bytes is null) return BadRequest(new { message = "Andoza fayli topilmadi" });
        }
        var customFields = ParseFields(tpl.FieldsJson);
        byte[] Render(IDictionary<string, string> tokens)
        {
            var merged = MergeTokens(customFields, tokens);
            return isCustom ? contracts.BuildDocxFromText(tpl.Body, merged) : contracts.FillTemplate(bytes!, merged);
        }

        var ctx = await LoadCtxAsync();
        var number = await db.Contracts.AnyAsync() ? await db.Contracts.MaxAsync(c => c.Number) : 0;
        var today = AppClock.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var results = new List<SendResultDto>();
        var keys = req.RecipientKeys.Distinct().ToList();

        if (req.Target == "staff")
        {
            var teachers = await db.Teachers.ToListAsync();
            foreach (var key in keys)
            {
                var t = teachers.FirstOrDefault(x => x.Id == key);
                if (t is null) { results.Add(new(key, false, null, "Topilmadi")); continue; }
                var chatIds = await db.TelegramRegistrations
                    .Where(r => r.TeacherId == t.Id).Select(r => r.ChatId).Distinct().ToListAsync();
                if (chatIds.Count == 0) { results.Add(new(key, false, null, "Telegramda ro'yxatdan o'tmagan")); continue; }

                number++;
                var filled = Render(StaffTokens(t, ctx, number, today));
                var ok = await DeliverAsync(chatIds, filled, number);
                var docxUrl = await contracts.SaveUploadAsync(filled, ".docx");
                db.Contracts.Add(new Contract
                {
                    Target = "staff",
                    RecipientKey = t.Id,
                    RecipientName = t.FullName,
                    Number = number,
                    TemplateId = tpl.Id,
                    SentAt = AppClock.Now,
                    Delivered = ok,
                    DocxUrl = docxUrl,
                    FileName = $"Shartnoma № {number}",
                    TemplateName = tpl.Name,
                });
                results.Add(new(key, ok, number, ok ? "Yuborildi" : "Yuborilmadi"));
            }
        }
        else
        {
            // O'quvchi bo'yicha yuborish — kalit = Student.Id (Telegram ro'yxati ham StudentId bilan bog'langan).
            var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();
            foreach (var key in keys)
            {
                var s = students.FirstOrDefault(x => x.Id == key);
                if (s is null) { results.Add(new(key, false, null, "Topilmadi")); continue; }
                var chatIds = await db.TelegramRegistrations
                    .Where(r => r.StudentId == s.Id).Select(r => r.ChatId).Distinct().ToListAsync();
                if (chatIds.Count == 0) { results.Add(new(key, false, null, "Telegramda ro'yxatdan o'tmagan")); continue; }

                number++;
                var filled = Render(StudentTokens(s, ctx, number, today));
                var ok = await DeliverAsync(chatIds, filled, number);
                var docxUrl = await contracts.SaveUploadAsync(filled, ".docx");
                db.Contracts.Add(new Contract
                {
                    Target = "parent",
                    RecipientKey = s.Id,
                    RecipientName = s.FullName,
                    Number = number,
                    TemplateId = tpl.Id,
                    SentAt = AppClock.Now,
                    Delivered = ok,
                    DocxUrl = docxUrl,
                    FileName = $"Shartnoma № {number}",
                    TemplateName = tpl.Name,
                });
                results.Add(new(key, ok, number, ok ? "Yuborildi" : "Yuborilmadi"));
            }
        }

        await db.SaveChangesAsync();
        return results;
    }

    // ---------- Tuzilgan shartnomalar (tarix) ----------

    /// <summary>Tuzilgan shartnomalar ro'yxati (eng yangisi birinchi). Ixtiyoriy filtrlar:
    /// <c>target</c> (parent|staff), <c>recipientKey</c> (o'quvchi/xodim id) va <c>q</c> (oluvchi/andoza nomi).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContractDocDto>>> Docs(
        [FromQuery] string? target, [FromQuery] string? recipientKey, [FromQuery] string? q)
    {
        var query = db.Contracts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(target))
            query = query.Where(c => c.Target == (target == "staff" ? "staff" : "parent"));
        if (!string.IsNullOrWhiteSpace(recipientKey))
            query = query.Where(c => c.RecipientKey == recipientKey);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(c => c.RecipientName.ToLower().Contains(s)
                                     || c.TemplateName.ToLower().Contains(s));
        }
        var items = await query.OrderByDescending(c => c.Number).Take(500).ToListAsync();
        return items.Select(ContractService.ToDoc).ToList();
    }

    /// <summary>Tayyor PDF nusxani biriktiradi — superadmin .docx ni yakunlab, PDF qilib yuklaydi
    /// (fayl avval /api/admin/uploads orqali yuklanadi). Shundan keyin shartnoma oluvchining
    /// ilovasida ko'rinadi. Qayta yuklansa — eskisi almashtiriladi.</summary>
    [HttpPost("{id}/pdf")]
    public async Task<ActionResult<ContractDocDto>> UploadPdf(string id, ContractPdfRequest req)
    {
        var c = await db.Contracts.FindAsync(id);
        if (c is null) return NotFound();
        var url = (req.FileUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { message = "PDF faylni yuklang" });
        if (!url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat PDF fayl yuklash mumkin" });
        // Eski nusxa almashtirilsa — diskda axlat qolmasin.
        if (!string.IsNullOrEmpty(c.PdfUrl) && c.PdfUrl != url) contracts.DeleteUpload(c.PdfUrl);
        c.PdfUrl = url;
        // MANZIL yozilmaydi — tarixni ko'rgan har kim `/uploads/...` havolasini olib qo'ymasin
        // (qarang: .claude/rules/uploads-security.md).
        audit.Record("Contract", c.Id, "update", $"Shartnomaga PDF nusxa biriktirildi: #{c.Number}");
        await db.SaveChangesAsync();
        return ContractService.ToDoc(c);
    }

    /// <summary>Shartnomani oluvchi ilovasida ko'rsatish/yashirish.</summary>
    [HttpPut("{id}/visibility")]
    public async Task<ActionResult<ContractDocDto>> SetVisibility(string id, ContractVisibilityRequest req)
    {
        var c = await db.Contracts.FindAsync(id);
        if (c is null) return NotFound();
        c.Visible = req.Visible;
        audit.Record("Contract", c.Id, "update",
            $"Shartnoma #{c.Number} oluvchi ilovasida " + (req.Visible ? "KO'RSATILDI" : "YASHIRILDI"));
        await db.SaveChangesAsync();
        return ContractService.ToDoc(c);
    }

    /// <summary>Shartnoma yozuvini va u bilan saqlangan fayllarni (PDF va DOCX) o'chiradi.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDoc(string id)
    {
        var c = await db.Contracts.FindAsync(id);
        if (c is null) return NotFound();
        contracts.DeleteUpload(c.PdfUrl);
        contracts.DeleteUpload(c.DocxUrl);
        audit.Record("Contract", c.Id, "delete", $"Shartnoma o'chirildi: #{c.Number}");
        db.Contracts.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Yordamchilar ----------

    private async Task<bool> DeliverAsync(List<long> chatIds, byte[] filled, int number)
    {
        var ok = false;
        var fileName = $"shartnoma-{number}.docx";
        foreach (var cid in chatIds)
            ok |= await telegram.SendDocumentAsync(cid, filled, fileName, $"Shartnoma № {number}");
        return ok;
    }

    private async Task<Dictionary<string, int>> LastNumbersAsync(string target) =>
        (await db.Contracts.Where(c => c.Target == target)
            .GroupBy(c => c.RecipientKey)
            .Select(g => new { Key = g.Key, Max = g.Max(x => x.Number) }).ToListAsync())
        .ToDictionary(x => x.Key, x => x.Max);

    /// <summary>Eski (parent-guruh) yozuvlardagi oluvchi kaliti — oxirgi raqamni topish uchun (orqaga moslik).</summary>
    private static string LegacyParentKey(Student s)
    {
        var phoneKey = PhoneUtil.Key(s.ParentPhone);
        return phoneKey.Length >= 7 ? "p:" + phoneKey
            : !string.IsNullOrWhiteSpace(s.ParentFullName) ? "n:" + s.ParentFullName.Trim().ToLowerInvariant()
            : "s:" + s.Id;
    }

    // ---------- O'rinbosarlar (tokenlar) ----------

    /// <summary>Token qiymatlari uchun umumiy kontekst — bir marta yuklanadi (yuborish/yuklab olishda bir xil).</summary>
    private sealed class TokenCtx
    {
        public CenterMeta? Meta;
        public Dictionary<string, string> CourseNames = new();
        public Dictionary<string, string> TeacherNames = new();
        public Dictionary<string, Group> Groups = new();
        /// <summary>StudentId → faol a'zolik guruh id'lari (IsActive).</summary>
        public Dictionary<string, List<string>> ActiveGroupsByStudent = new();
        /// <summary>StudentId → to'lovli ("active" status) a'zolik guruh id'lari.</summary>
        public Dictionary<string, List<string>> BillableGroupsByStudent = new();
        /// <summary>Chegirma REGISTRI — shartnomadagi oylik to'lov AYNAN shundan hisoblanadi
        /// (bir marta yuklanadi; ommaviy yuborishda o'quvchi boshiga so'rov qilinmasin).</summary>
        public DiscountBook Discounts = DiscountBook.Empty;
    }

    private async Task<TokenCtx> LoadCtxAsync()
    {
        var ctx = new TokenCtx
        {
            Meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(),
            CourseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name),
            TeacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName),
            Groups = await db.Classes.AsNoTracking().ToDictionaryAsync(g => g.Id),
            Discounts = await DiscountBook.LoadAllAsync(db),
        };
        var mems = await db.StudentGroups.AsNoTracking().Where(sg => sg.IsActive)
            .Select(sg => new { sg.StudentId, sg.GroupId, sg.Status }).ToListAsync();
        foreach (var m in mems)
        {
            if (!ctx.ActiveGroupsByStudent.TryGetValue(m.StudentId, out var all))
                ctx.ActiveGroupsByStudent[m.StudentId] = all = new List<string>();
            all.Add(m.GroupId);
            if (m.Status == "active")
            {
                if (!ctx.BillableGroupsByStudent.TryGetValue(m.StudentId, out var billable))
                    ctx.BillableGroupsByStudent[m.StudentId] = billable = new List<string>();
                billable.Add(m.GroupId);
            }
        }
        return ctx;
    }

    /// <summary>O'quvchi shartnomasi o'rinbosarlari — o'quvchi, ota-ona, guruh/kurs, to'lov va markaz ma'lumotlari.</summary>
    private static Dictionary<string, string> StudentTokens(Student s, TokenCtx ctx, int number, string today)
    {
        var groups = (ctx.ActiveGroupsByStudent.GetValueOrDefault(s.Id) ?? new List<string>())
            .Select(id => ctx.Groups.GetValueOrDefault(id))
            .Where(g => g is not null).Select(g => g!).ToList();
        var groupNames = groups.Select(g => g.Name).Distinct().ToList();
        if (groupNames.Count == 0 && !string.IsNullOrWhiteSpace(s.ClassName)) groupNames.Add(s.ClassName);
        var courses = groups.Select(g => ctx.CourseNames.GetValueOrDefault(g.CourseId, ""))
            .Where(n => n.Length > 0).Distinct().ToList();
        var teachers = groups.Select(g => ctx.TeacherNames.GetValueOrDefault(g.TeacherId, ""))
            .Where(n => n.Length > 0).Distinct().ToList();

        // OYLIK TO'LOV — to'lovli ("active") a'zoliklar bo'yicha, ⚠️ HAR GURUH UCHUN ALOHIDA:
        //     net = Σ (guruh narxi − O'SHA guruhga tegishli chegirma).
        //
        // ⚠️ Ilgari narxlar avval QO'SHILAR, keyin ustiga BITTA chegirma qo'llanardi. Chegirma
        // endi har fan uchun alohida bo'lishi mumkin, ya'ni eski usul matematika chegirmasini
        // ingliz tili narxidan ham ayirib yuborardi (shartnomada YOLG'ON summa).
        var month = TuitionService.CurrentMonth();
        var rows = ctx.Discounts.For(s.Id);
        var billable = (ctx.BillableGroupsByStudent.GetValueOrDefault(s.Id) ?? new List<string>())
            .Select(id => ctx.Groups.GetValueOrDefault(id)).Where(g => g is not null).Select(g => g!)
            .ToList();
        var net = 0m;
        foreach (var g in billable)
        {
            var fee = g.MonthlyFee;
            var line = fee - TuitionService.DiscountForMonth(rows, fee, month, g.Id);
            if (line > 0) net += line;
        }

        // @chegirma — bitta chegirma bo'lsa avvalgidek ("20%" / summa), bir nechta bo'lsa
        // FAN nomlari bilan qisqa yorliq («Matematika 20%, Ingliz tili 15%»).
        var inForce = rows.Where(d => DiscountRules.InForce(d, month)).ToList();
        var chegirma = inForce.Count switch
        {
            0 => "",
            1 => DiscountRules.ValueLabel(inForce[0].Pct, inForce[0].Amount),
            _ => DiscountRules.ActiveLabel(inForce),
        };

        var tokens = new Dictionary<string, string>
        {
            ["@oquvchi"] = s.FullName,
            ["@oquvchi_telefon"] = s.Phone,
            ["@tugilgan_kun"] = FormatDate(s.BirthDate),
            ["@manzil"] = s.Address,
            ["@guruh"] = !string.IsNullOrWhiteSpace(s.ClassName) ? s.ClassName : string.Join(", ", groupNames),
            ["@guruhlar"] = string.Join(", ", groupNames),
            ["@kurs"] = string.Join(", ", courses),
            ["@oqituvchi"] = string.Join(", ", teachers),
            ["@oylik_tolov"] = net > 0 ? AuditService.Money(net) : "",
            ["@chegirma"] = chegirma,
            ["@qabul_sana"] = FormatDate(s.EnrollmentDate),
            ["@ota_ona"] = s.ParentFullName,
            ["@telefon"] = s.ParentPhone,
            ["@otasi"] = s.FatherFullName,
            ["@otasi_telefon"] = s.FatherPhone,
            ["@onasi"] = s.MotherFullName,
            ["@onasi_telefon"] = s.MotherPhone,
            // Eski andozalar bilan moslik: @farzandlar endi bitta o'quvchi nomi.
            ["@farzandlar"] = s.FullName,
            ["@sana"] = today,
            ["@raqam"] = number.ToString(),
        };
        AddCenterTokens(tokens, ctx.Meta);
        return tokens;
    }

    /// <summary>Xodim shartnomasi o'rinbosarlari — xodim, guruh/kurs, maosh va markaz ma'lumotlari.</summary>
    private static Dictionary<string, string> StaffTokens(Teacher t, TokenCtx ctx, int number, string today)
    {
        var groups = ctx.Groups.Values.Where(g => !g.IsArchived && g.TeacherId == t.Id)
            .OrderBy(g => g.Name).ToList();
        var courses = groups.Select(g => ctx.CourseNames.GetValueOrDefault(g.CourseId, ""))
            .Concat(t.SubjectIds.Select(id => ctx.CourseNames.GetValueOrDefault(id, "")))
            .Where(n => n.Length > 0).Distinct().ToList();
        // Maosh: fixed — qat'iy summa; percent — guruh o'quvchilariga HISOBLANGAN oylikning foizi
        // (2026-09 dan — `billing.md`; shartnomaga matn ko'rinishida).
        var oylik = t.SalaryMode == "percent" && t.SalaryPercent > 0
            ? $"guruh o'quvchilariga hisoblangan oylik to'lovning {t.SalaryPercent.ToString("0.##", CultureInfo.InvariantCulture)}%i"
            : t.Salary > 0 ? AuditService.Money(t.Salary) : "";

        var tokens = new Dictionary<string, string>
        {
            ["@fish"] = t.FullName,
            ["@telefon"] = t.Phone,
            ["@lavozim"] = "O'qituvchi",
            ["@fanlar"] = string.Join(", ", courses),
            ["@guruhlar"] = string.Join(", ", groups.Select(g => g.Name)),
            ["@tugilgan_kun"] = FormatDate(t.BirthDate),
            ["@manzil"] = t.Address,
            ["@oylik"] = oylik,
            ["@oylik_foiz"] = t.SalaryPercent > 0
                ? t.SalaryPercent.ToString("0.##", CultureInfo.InvariantCulture) : "",
            ["@ish_boshlagan"] = FormatDate(TeacherSalaryCalc.StartDateOf(t) ?? ""),
            ["@sana"] = today,
            ["@raqam"] = number.ToString(),
        };
        AddCenterTokens(tokens, ctx.Meta);
        return tokens;
    }

    /// <summary>Markaz (CenterMeta) o'rinbosarlari — ikkala target uchun ham umumiy.</summary>
    private static void AddCenterTokens(Dictionary<string, string> tokens, CenterMeta? meta)
    {
        tokens["@markaz"] = meta?.Name ?? "";
        tokens["@direktor"] = meta?.Director ?? "";
        tokens["@markaz_telefon"] = meta?.Phone ?? "";
        tokens["@markaz_manzil"] = meta?.Address ?? "";
    }

    /// <summary>Avval custom qiymatlar, so'ng built-in oluvchi tokenlari (nom to'qnashsa built-in ustun).</summary>
    private static Dictionary<string, string> MergeTokens(
        List<ContractFieldDto> customFields, IDictionary<string, string> tokens)
    {
        var merged = new Dictionary<string, string>();
        foreach (var f in customFields) merged[f.Key] = f.Value;
        foreach (var kv in tokens) merged[kv.Key] = kv.Value;
        return merged;
    }

    private static string FormatDate(string iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : iso;

    private static ContractTemplateDto ToDto(ContractTemplate t) =>
        new(t.Id, t.Target, t.Name, t.FileUrl, t.FileName, t.Body, ParseFields(t.FieldsJson), t.UploadedAt.ToString("o"));

    /// <summary>FieldsJson → tozalangan o'rinbosarlar ro'yxati (yaroqsiz/bo'sh kalitlar chiqarib tashlanadi).</summary>
    private static List<ContractFieldDto> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var list = JsonSerializer.Deserialize<List<ContractFieldDto>>(json) ?? new();
            return list.Select(f => new ContractFieldDto(CleanTokenKey(f.Key), f.Value ?? ""))
                       .Where(f => f.Key.Length > 1).ToList();
        }
        catch { return new(); }
    }

    private static string SerializeFields(IEnumerable<ContractFieldDto>? fields)
    {
        var clean = (fields ?? Enumerable.Empty<ContractFieldDto>())
            .Select(f => new ContractFieldDto(CleanTokenKey(f.Key), (f.Value ?? "").Trim()))
            .Where(f => f.Key.Length > 1)
            .ToList();
        return clean.Count == 0 ? "" : JsonSerializer.Serialize(clean);
    }

    /// <summary>Token kalitini normallashtiradi: bitta "@" prefiks + faqat harf/pastki chiziq (regex bilan mos).</summary>
    private static string CleanTokenKey(string? key)
    {
        var cleaned = new string((key ?? "").TrimStart('@').Where(c => char.IsLetter(c) || c == '_').ToArray());
        return cleaned.Length == 0 ? "" : "@" + cleaned;
    }
}
