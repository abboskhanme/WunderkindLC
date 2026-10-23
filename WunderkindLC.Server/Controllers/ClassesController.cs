using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("classes.list")]
[Route("api/admin/classes")]
public partial class ClassesController(AppDbContext db, AuditService audit, ILogger<ClassesController> logger, CertificateService certSvc, RoomConflictService roomConflict, AutoMessageService autoMsg, IConfiguration config, DataCache dataCache) : ControllerBase
{
    private string Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";

    /// <summary>Joriy foydalanuvchi id'si (<see cref="AppUser"/>.Id) — UZAYTIRISH hodisasi KIMGA
    /// yozilishini belgilaydi (chiquvchi adminning "Bonus B" manbai). Claim bo'lmasa bo'sh satr:
    /// hodisa baribir yoziladi (fakt yo'qolmasin), lekin hech kimning hisobiga tushmaydi.</summary>
    private string ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>Aktivlashtirishdagi «Bonus hisoblansin» ptichkasi uchun ruxsat kaliti
    /// ("Xodimlar va rollar" da ko'rinadi). Klientdagi `adminPermissions` bilan bir xil bo'lishi shart.</summary>
    private const string RetentionBonusPerm = "retentionBonus";

    /// <summary>«AKTIV MUZLATISH» (yangi o'quv yiliga o'tish) — FAQAT superadmin.
    /// <para>⚠️ Bu yerda <c>[AdminPerm]</c> YARAMAYDI: u admin va superadmin IKKALASINI ham
    /// o'tkazib yuboradi (<see cref="AdminPermAttribute"/>), talab esa oddiy <c>admin</c> ham
    /// kirmasligi. Shuning uchun HARD rol tekshiruvi; yangi ruxsat kaliti kiritilmaydi.</para>
    /// <para>⚠️ Ruxsatsiz so'rov JIMGINA oddiy muzlatishga TUSHIRILMAYDI (retentionBonus dagidek
    /// emas): tugma "Aktiv muzlatish" deb bosilib, natijada oddiy muzlatish bo'lib qolsa —
    /// "yangi yilga nechta o'quvchi bilan o'tyapmiz" hisoboti YOLG'ON bo'lardi.</para></summary>
    private bool MaySetYearFreeze => User.IsInRole(Roles.SuperAdmin);

    /// <summary>
    /// GURUHNI YOPISH / ARXIVLASH / ARXIVDAN CHIQARISH — UI'dagi darvoza bilan AYNAN bir xil:
    /// <c>can('classes.list','delete')</c> (admin/superadmin yoki «O'chirish» ruxsati berilgan xodim).
    /// <para>⚠️ NEGA QO'LDA: bu amallar POST, <c>[AdminPerm]</c> esa POST'ni <c>create</c> deb
    /// tekshiradi — ya'ni faqat "qo'shish" ruxsati bor xodim API'ni to'g'ridan-to'g'ri chaqirib
    /// butun guruhni yopib, barcha a'zolarni muzlatib yubora olardi (tugma unga ko'rinmasa ham).
    /// Naqsh — <c>StudentPurgeController.MayPurge</c>.</para>
    /// </summary>
    private bool MayCloseGroups =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin) ||
        (User.IsInRole(Roles.Staff) && PermissionRules.CanWrite(
            User.Claims.Where(c => c.Type == AdminPermAttribute.ClaimType).Select(c => c.Value),
            "classes.list", "DELETE"));

    /// <summary>
    /// «Guruhdan chiqarish» oynasidagi «arxivga ham o'tkazish» — «Arxivga ko'chirish» bilan AYNAN bir xil
    /// darvoza: <c>StudentsController.Archive</c> sinf darajasidagi <c>[AdminPerm("students.list")]</c>, POST →
    /// <c>create</c> (klientda <c>can('students.list','create')</c>). Guruhlar ruxsatining o'zi YETMAYDI.
    /// </summary>
    private bool MayArchiveStudents =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin) ||
        (User.IsInRole(Roles.Staff) && PermissionRules.CanWrite(
            User.Claims.Where(c => c.Type == AdminPermAttribute.ClaimType).Select(c => c.Value),
            "students.list", "POST"));

    internal const string ArchiveForbiddenMessage =
        "O'quvchini arxivga o'tkazish uchun «O'quvchilar» bo'limida «Qo'shish» ruxsati kerak";

    private ObjectResult CloseForbidden() =>
        StatusCode(403, new { message = "Guruhni yopish/arxivlash uchun «Guruhlar» bo'limida «O'chirish» ruxsati kerak" });

    /// <summary>«TUGATISH (sertifikat bilan)» — FAQAT superadmin (UI ham faqat superadmin'ga
    /// ko'rsatadi). HARD rol: <c>[AdminPerm]</c> admin'ni ham o'tkazib yuborardi
    /// (<c>.claude/rules/year-freeze.md</c> §3 dagi sabab).</summary>
    private bool MayCompleteGroups => User.IsInRole(Roles.SuperAdmin);

    /// <summary>Guruh tugash sanasini normallashtiradi: bo'sh → <c>null</c>, aks holda "yyyy-MM-dd".
    /// <c>false</c> — sana buzuq (chaqiruvchi 400 qaytaradi).</summary>
    private static bool TryNormalizeDate(string? raw, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!DateOnly.TryParse(raw.Trim(), out var d)) return false;
        value = d.ToString("yyyy-MM-dd");
        return true;
    }

    /// <summary>
    /// Faol (arxivlanmagan) guruhlar. <paramref name="includeArchived"/>=true bo'lsa hammasi.
    /// <para><paramref name="teacherId"/> berilsa — FAQAT o'sha o'qituvchining guruhlari.
    /// ⚠️ ATAYIN qo'shildi: o'qituvchi sahifasi markazning BARCHA guruhlarini tortib, keyin
    /// brauzerda <c>teacherId</c> bo'yicha filtrlardi. Filtr SQL'ga tushirildi —
    /// <c>Group.TeacherId</c> allaqachon indekslangan (<c>AppDbContext</c>).</para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Group>>> GetAll(
        [FromQuery] bool includeArchived = false, [FromQuery] string? teacherId = null)
    {
        var q = db.Classes.AsNoTracking().AsQueryable();
        if (!includeArchived) q = q.Where(c => !c.IsArchived);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(c => c.TeacherId == teacherId);
        return await q.OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
    }

    /// <summary>Arxivlangan guruhlar ro'yxati.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<Group>>> GetArchived() =>
        await db.Classes.AsNoTracking().Where(c => c.IsArchived)
            .OrderByDescending(c => c.ArchivedAt).ThenBy(c => c.Name).ToListAsync();

    /// <summary>
    /// BITTA guruh — ro'yxatdagi element bilan AYNAN bir xil shakl (`Group`).
    /// <para>⚠️ Bu endpoint ATAYIN qo'shildi: guruh sahifasi bitta guruhni topish uchun BUTUN
    /// ro'yxatni (<c>GET /classes?includeArchived=true</c>) tortardi. Prod o'lchovi:
    /// <c>Classes</c> jadvali 38 000 martadan ko'p sequential scan qilingan, va har guruh
    /// sahifasi ochilishi markazning HAMMA guruhini tarmoqdan o'tkazardi.</para>
    /// <para>Arxivlangan guruh ham qaytadi — tugatilgan guruh sahifasi (o'qituvchi profilidan
    /// yoki eski havoladan kirilganda) ochilishi kerak.</para>
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Group>> GetOne(string id)
    {
        var g = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return g is null ? NotFound() : g;
    }

    [HttpPost]
    public async Task<ActionResult<Group>> Create(
        ClassPayload p, [FromQuery] bool force = false, [FromQuery] string? forceReason = null)
    {
        // Guruhga o'qituvchi biriktirish MAJBURIY (foizli maosh va jurnal shunga tayanadi).
        if (string.IsNullOrWhiteSpace(p.TeacherId))
            return BadRequest(new { message = "Guruhga o'qituvchi biriktirish majburiy" });
        var teacher = await db.Teachers.FindAsync(p.TeacherId);
        if (teacher is null)
            return BadRequest(new { message = "Tanlangan o'qituvchi topilmadi" });
        // ARXIVDAGI (ishdan ketgan) o'qituvchi — faol guruhga biriktirilmaydi: darslar maoshda hech kimga
        // yozilmasdi (arxiv davri `InactivePeriods`), jurnalni ham hech kim yuritmasdi (audit R1-10).
        if (teacher.IsArchived)
            return BadRequest(new { message = ArchivedTeacherMessage(teacher) });

        // NOM — bo'shliqsiz, BARCHA guruhlar (arxivdagilar ham) orasida takrorlanmaydi: «asosiy guruh»
        // yorlig'i va e'lonning "Guruh" auditoriyasi nom bilan bog'langan (`GroupNames` izohi).
        var name = GroupNames.Clean(p.Name);
        if (name.Length == 0) return BadRequest(new { message = "Guruh nomini kiriting" });
        if (await GroupNames.IsTakenAsync(db, name, null))
            return BadRequest(new { message = GroupNames.TakenMessage });
        // «Arxiv» holati faqat yorliq edi (guruh arxivga TUSHMASDI) — arxivlash alohida amal.
        if (p.Status == "archived")
            return BadRequest(new { message = "Guruhni arxivlash uchun «Arxivlash» amalidan foydalaning" });
        if (!TryNormalizeDate(p.EndDate, out var endDate))
            return BadRequest(new { message = "Tugash sanasi noto'g'ri" });
        if (endDate is not null && DateOnly.TryParse(p.StartDate, out var startD) && DateOnly.Parse(endDate) < startD)
            return BadRequest(new { message = "Tugash sanasi boshlanish sanasidan oldin bo'lmaydi" });

        // RoomId berilsa — xona nomini DB dan olish (Room string field uchun).
        string? resolvedRoomName = p.Room;
        if (!string.IsNullOrWhiteSpace(p.RoomId))
        {
            var roomEntity = await db.Rooms.FindAsync(p.RoomId);
            if (roomEntity is not null) resolvedRoomName = roomEntity.Name;
        }

        // Xona/o'qituvchi TO'QNASHUVI — QAT'IY taqiq (409); faqat superadmin sabab bilan "baribir saqlaydi"
        // (ScheduleConflicts, CRM Phase 5). Ilgari bu yerda 200 + ogohlantirish edi.
        var conflicts = await roomConflict.FindAsync(new ScheduleConflicts.Slot(
            "", name, p.RoomId?.Trim() ?? "", p.TeacherId?.Trim() ?? "", p.Days ?? [],
            p.StartTime ?? "", p.EndTime ?? "", p.StartDate, endDate, IsArchived: false));
        var verdict = ScheduleConflicts.Decide(conflicts.Count, force, forceReason, User.IsInRole(Roles.SuperAdmin));
        if (verdict.Verdict == ScheduleConflicts.Verdict.Blocked) return ScheduleConflict(verdict, conflicts);

        var cls = new Group
        {
            Name = name,
            Grade = p.Grade,
            Language = p.Language,
            MonthlyFee = p.MonthlyFee,
            Room = resolvedRoomName,
            RoomId = string.IsNullOrWhiteSpace(p.RoomId) ? null : p.RoomId,
            Status = string.IsNullOrWhiteSpace(p.Status) ? "active" : p.Status!,
            StartDate = p.StartDate,
            EndDate = endDate,
            Capacity = p.Capacity,
            CourseId = p.CourseId ?? "",
            TeacherId = p.TeacherId ?? "",
            Note = p.Note ?? "",
            Days = p.Days ?? new(),
            StartTime = p.StartTime ?? "",
            EndTime = p.EndTime ?? "",
        };
        // Guruh kursi belgilangan bo'lsa — guruh oyligi (MonthlyFee) shu kurs narxidan keladi.
        if (!string.IsNullOrEmpty(cls.CourseId))
        {
            var course = await db.Subjects.FindAsync(cls.CourseId);
            if (course is not null) cls.MonthlyFee = course.Price;
        }
        db.Classes.Add(cls);

        // O'qituvchi TARIXINI ochamiz — Group.TeacherId keyinchalik almashsa, kim qachon
        // o'qitgani shu yerda qoladi (retention bonusini oylar nisbatida bo'lish uchun).
        await GroupTeacherHistory.AssignAsync(db, cls.Id, cls.TeacherId, Actor);

        if (cls.MonthlyFee > 0)
            audit.Record(AuditService.EntityClassFee, cls.Id, "create",
                $"Oylik to'lov belgilandi: {AuditService.Money(cls.MonthlyFee)} so'm ({cls.Name})",
                after: new { cls.MonthlyFee, cls.Name });

        audit.Record(AuditService.EntityGroup, cls.Id, "create",
            $"Guruh yaratildi ({cls.Name})",
            after: AuditService.GroupSnapshot(cls));
        if (verdict.Verdict == ScheduleConflicts.Verdict.Forced)
            audit.Record(AuditService.EntityGroup, cls.Id, "create",
                ScheduleConflicts.ForcedAuditText(conflicts, verdict.Reason));

        await db.SaveChangesAsync();
        return cls;
    }

    /// <summary>
    /// Guruhni tahrirlash. Oylik to'lov o'zgarsa va <paramref name="applyFee"/> = true bo'lsa
    /// ("Ha"), yangi narx shu guruh o'quvchilarining JORIY oy to'loviga ham qo'llanadi (balans
    /// farqqa moslab to'g'rilanadi). false bo'lsa ("Yo'q") — joriy oy eski narxda qoladi, yangi
    /// narx keyingi oy hisoblashidan amal qiladi.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Group>> Update(string id, ClassPayload p, [FromQuery] bool applyFee = false,
        [FromQuery] bool force = false, [FromQuery] string? forceReason = null)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();

        var beforeGroup = AuditService.GroupSnapshot(cls);

        // O'qituvchi biriktirish majburiy (eski, o'qituvchisiz guruhlar ham tahrirlanganda biriktirilsin).
        if (string.IsNullOrWhiteSpace(p.TeacherId))
            return BadRequest(new { message = "Guruhga o'qituvchi biriktirish majburiy" });
        // Yangi o'qituvchini bir marta olamiz — nomi audit yozuvida kerak (GUID emas).
        var newTeacher = await db.Teachers.FindAsync(p.TeacherId);
        if (newTeacher is null)
            return BadRequest(new { message = "Tanlangan o'qituvchi topilmadi" });
        // Faol guruhga ARXIVDAGI o'qituvchi biriktirilmaydi (Create bilan bir xil, audit R1-10). Arxivdagi
        // (yopilgan) guruhni tahrirlash bloklanmaydi — u yerda dars yo'q.
        if (newTeacher.IsArchived && !cls.IsArchived)
            return BadRequest(new { message = ArchivedTeacherMessage(newTeacher) });

        // NOM — faqat O'ZGARGANDA tekshiriladi (prod'dagi eski dublikatli guruhning boshqa maydonini
        // tahrirlash bloklanmasin). Registrsiz, arxivdagi guruhlar bilan ham solishtiriladi.
        var name = GroupNames.Clean(p.Name);
        if (name.Length == 0) return BadRequest(new { message = "Guruh nomini kiriting" });
        var renamed = !string.Equals(name, cls.Name, StringComparison.Ordinal);
        if (!GroupNames.SameName(name, cls.Name) && await GroupNames.IsTakenAsync(db, name, cls.Id))
            return BadRequest(new { message = GroupNames.TakenMessage });
        // «Arxiv» holati faqat YORLIQ edi — guruh arxivga tushmas, a'zoliklar muzlamas, oylik
        // yozilaverardi. Arxivlash — alohida amal («Arxivlash» / «Guruhni yopish»). Eski
        // yozuvdagi shu yorliq saqlanaveradi (o'zgarish emas).
        if (p.Status == "archived" && cls.Status != "archived" && !cls.IsArchived)
            return BadRequest(new { message = "Guruhni arxivlash uchun «Arxivlash» amalidan foydalaning" });
        if (!TryNormalizeDate(p.EndDate, out var endDate))
            return BadRequest(new { message = "Tugash sanasi noto'g'ri" });
        if (endDate is not null && DateOnly.TryParse(p.StartDate, out var startD) && DateOnly.Parse(endDate) < startD)
            return BadRequest(new { message = "Tugash sanasi boshlanish sanasidan oldin bo'lmaydi" });

        // RoomId berilsa — xona nomini DB dan olish (Room string field uchun).
        string? resolvedRoomName = p.Room;
        if (!string.IsNullOrWhiteSpace(p.RoomId))
        {
            var roomEntity = await db.Rooms.FindAsync(p.RoomId);
            if (roomEntity is not null) resolvedRoomName = roomEntity.Name;
        }

        // Xona/o'qituvchi TO'QNASHUVI — QAT'IY taqiq (409), o'z id'si hisobga olinmaydi. ⚠️ FAQAT jadval
        // maydonlari (xona, o'qituvchi, kunlar, vaqt, boshlanish/tugash sanasi) O'ZGARGANDA tekshiriladi:
        // bazada allaqachon to'qnashib turgan guruhning nomi/izohini tuzatish bloklanmasin.
        var afterSlot = new ScheduleConflicts.Slot(
            cls.Id, name, p.RoomId?.Trim() ?? "", p.TeacherId?.Trim() ?? "", p.Days ?? [],
            p.StartTime ?? "", p.EndTime ?? "", p.StartDate, endDate, cls.IsArchived);
        var conflicts = ScheduleConflicts.ScheduleChanged(ScheduleConflicts.Of(cls), afterSlot)
            ? await roomConflict.FindAsync(afterSlot)
            : [];
        var verdict = ScheduleConflicts.Decide(conflicts.Count, force, forceReason, User.IsInRole(Roles.SuperAdmin));
        if (verdict.Verdict == ScheduleConflicts.Verdict.Blocked) return ScheduleConflict(verdict, conflicts);
        if (verdict.Verdict == ScheduleConflicts.Verdict.Forced)
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                ScheduleConflicts.ForcedAuditText(conflicts, verdict.Reason));

        var oldFee = cls.MonthlyFee;
        var oldName = cls.Name;   // o'quvchilar hozir shu nom bilan biriktirilgan
        var oldCourseId = cls.CourseId;  // kurs o'zgarishni kuzatamiz
        var oldTeacherId = cls.TeacherId;   // o'qituvchi almashuvi — tarixda NOM bilan yoziladi

        // O'QITUVCHI ALMASHDI — bugundan keyingi o'rinbosar sanalari YANGI egaga moslanadi (ushlanma
        // haqiqiy egadan; yangi ega o'zi o'rinbosar bo'lsa o'sha sanalar olib tashlanadi). Guruh
        // maydonlari o'zgarishidan OLDIN: eski (sanalarsiz) tayinlov ESKI jadval bo'yicha yoyiladi.
        if (!string.Equals(oldTeacherId, p.TeacherId, StringComparison.Ordinal))
            await SubstituteGroupLifecycle.OnTeacherChangedAsync(
                db, audit, cls, p.TeacherId!, AppClock.Today.ToString("yyyy-MM-dd"), Actor);

        cls.Name = name;
        cls.Grade = p.Grade;
        cls.Language = p.Language;
        cls.MonthlyFee = p.MonthlyFee;
        cls.Room = resolvedRoomName;
        cls.RoomId = string.IsNullOrWhiteSpace(p.RoomId) ? null : p.RoomId;
        if (!string.IsNullOrWhiteSpace(p.Status)) cls.Status = p.Status!;
        cls.StartDate = p.StartDate;
        // Bo'sh tugash sanasi — `null` ("" emas): jadval/maosh "tugash sanasi yo'q" deb o'qisin.
        cls.EndDate = endDate;
        cls.Capacity = p.Capacity;
        cls.CourseId = p.CourseId ?? "";
        cls.TeacherId = p.TeacherId ?? "";
        // O'qituvchi TARIXI: almashsa eski biriktirish yopiladi va yangisi ochiladi (retention
        // bonusi "qaysi oyda kim o'qitgan" savoliga aynan shu tarixdan javob oladi).
        // Almashmagan bo'lsa ham chaqiriladi — o'sha o'qituvchida ochiq qator bo'lsa AssignAsync
        // hech narsa qilmaydi, yo'q bo'lsa (tarixsiz eski guruh) yetishmayotganini to'ldiradi.
        // Audit alohida yozilmaydi — pastdagi GroupSnapshot before/after TeacherId'ni ham qamraydi.
        await GroupTeacherHistory.AssignAsync(db, cls.Id, cls.TeacherId, Actor);
        cls.Note = p.Note ?? "";
        cls.Days = p.Days ?? new();
        cls.StartTime = p.StartTime ?? "";
        cls.EndTime = p.EndTime ?? "";
        // Guruh kursi belgilangan bo'lsa — guruh oyligi (MonthlyFee) shu kurs narxidan keladi.
        if (!string.IsNullOrEmpty(cls.CourseId))
        {
            var course = await db.Subjects.FindAsync(cls.CourseId);
            if (course is not null) cls.MonthlyFee = course.Price;
        }

        // Kurs o'zgarsa — faqat ESKI kursga biriktirilgan-u YANGI kursda ENDI YO'Q dasturlar progressi
        // tozalanadi (bir dastur ikkala kursga ham biriktirilgan bo'lsa — progress saqlanib qoladi,
        // chunki u kontentga tegishli, kursga emas).
        if (!string.Equals(oldCourseId, cls.CourseId, StringComparison.Ordinal) && !string.IsNullOrEmpty(oldCourseId))
        {
            var memberIds = await db.StudentGroups
                .Where(sg => sg.GroupId == id && sg.IsActive)
                .Select(sg => sg.StudentId)
                .ToListAsync();

            if (memberIds.Count > 0)
            {
                var oldCurriculumIds = await db.SubjectCurricula
                    .Where(sc => sc.SubjectId == oldCourseId).Select(sc => sc.CurriculumId).ToListAsync();
                var newCurriculumIds = string.IsNullOrEmpty(cls.CourseId)
                    ? new List<string>()
                    : await db.SubjectCurricula.Where(sc => sc.SubjectId == cls.CourseId)
                        .Select(sc => sc.CurriculumId).ToListAsync();
                var curriculumIdsToReset = oldCurriculumIds.Except(newCurriculumIds).ToList();

                if (curriculumIdsToReset.Count > 0)
                {
                    var itemIdsToReset = await db.CourseItems
                        .Where(i => curriculumIdsToReset.Contains(i.CurriculumId)).Select(i => i.Id).ToListAsync();
                    await db.CourseProgresses
                        .Where(p => memberIds.Contains(p.StudentId) && itemIdsToReset.Contains(p.ItemId))
                        .ExecuteDeleteAsync();
                }
            }
        }

        // Guruh BOSHQA KURSGA o'tkazilsa uning SINF NARXLARI joriy oyga so'ralmasdan qo'llanadi
        // (kurs tahriridagi kabi — `SubjectsController.Update`); asosiy narx esa faqat «Ha» bo'lsa.
        // ⚠️ `oldCourseId ?? ""`: kurssiz guruh uchun `null` uzatilsa servis uni YANGI kurs deb oladi.
        var courseChanged = (oldCourseId ?? "") != (cls.CourseId ?? "");
        if (courseChanged && oldFee == cls.MonthlyFee)
        {
            var bandApplied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
                db, cls.Id, oldName, cls.MonthlyFee, await PricingBook.LoadAsync(db),
                courseId: cls.CourseId, oldFee: oldFee, oldCourseId: oldCourseId ?? "", audit: audit);
            if (bandApplied > 0)
                audit.Record(AuditService.EntityClassFee, cls.Id, "update",
                    $"Guruh kursi almashdi — sinf narxlari joriy oydan {bandApplied} o'quvchiga qo'llandi ({cls.Name})");
        }

        if (oldFee != cls.MonthlyFee)
        {
            var applied = 0;
            if (applyFee || courseChanged)
                // Kurs ham shu so'rovda almashgan bo'lishi mumkin — YANGI va ESKI kurs aniq uzatiladi
                // (bazadan o'qilsa eskisi chiqardi); qisman oy hisoblari tegilmaydi (`oldFee`).
                // «Yo'q» + kurs almashuvi: joriy oyda asosiy narx eskisi, sinf narxlari — yangi kursniki.
                applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
                    db, cls.Id, oldName, applyFee ? cls.MonthlyFee : oldFee, await PricingBook.LoadAsync(db),
                    courseId: cls.CourseId, oldFee: oldFee, oldCourseId: oldCourseId ?? "", audit: audit);

            var summary = $"Oylik to'lov o'zgartirildi: {AuditService.Money(oldFee)} → {AuditService.Money(cls.MonthlyFee)} so'm ({cls.Name})";
            summary += applyFee
                ? $" — joriy oydan {applied} o'quvchiga qo'llandi"
                : " — keyingi oydan amal qiladi";
            audit.Record(AuditService.EntityClassFee, cls.Id, "update", summary,
                before: new { MonthlyFee = oldFee, cls.Name }, after: new { cls.MonthlyFee, cls.Name });
        }

        // O'QITUVCHI va KURS almashuvi — alohida, O'QILADIGAN yozuv. Ilgari bular faqat
        // GroupSnapshot ichidagi GUID sifatida "yozilardi": ro'yxatda "Guruh tahrirlandi" dan
        // boshqa hech narsa ko'rinmas, kim kimga almashtirilgani bilinmasdi.
        if (oldTeacherId != cls.TeacherId)
        {
            var oldTeacherName = string.IsNullOrEmpty(oldTeacherId)
                ? "—"
                : await db.Teachers.Where(t => t.Id == oldTeacherId).Select(t => t.FullName)
                    .FirstOrDefaultAsync() ?? "—";
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                $"Guruh o'qituvchisi almashtirildi ({cls.Name}): {oldTeacherName} → {newTeacher.FullName}",
                teacherId: cls.TeacherId);
        }

        if (oldCourseId != cls.CourseId)
        {
            async Task<string> CourseNameAsync(string? cid) =>
                string.IsNullOrEmpty(cid)
                    ? "—"
                    : await db.Subjects.Where(x => x.Id == cid).Select(x => x.Name)
                        .FirstOrDefaultAsync() ?? "—";
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                $"Guruh kursi almashtirildi ({cls.Name}): " +
                $"{await CourseNameAsync(oldCourseId)} → {await CourseNameAsync(cls.CourseId)}");
        }

        // QAYTA NOMLASH — «asosiy guruh» yorlig'i va eski rahbarlik NOM bilan bog'langan:
        // yangi nomga ko'chirilmasa o'quvchilar nomi yo'q guruhda qolardi.
        // Yozuvlar kuzatiladi — guruh bilan BITTA SaveChanges'da saqlanadi.
        if (renamed)
        {
            var moved = await GroupNames.MigrateRenameAsync(db, cls, oldName, cls.Name);
            var parts = new List<string>();
            if (moved.Students > 0) parts.Add($"{moved.Students} ta o'quvchi yorlig'i");
            if (moved.Teachers > 0) parts.Add($"{moved.Teachers} ta o'qituvchi rahbarligi");
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                $"Guruh nomi o'zgardi: «{oldName}» → «{cls.Name}»"
                + (parts.Count > 0 ? $" — {string.Join(", ", parts)} yangi nomga ko'chirildi" : ""));
        }

        // Guruh maydonlari o'zgarganda — "kim o'zgartirdi" audit izi (oylik to'lov moliyaviy audit'dan alohida).
        var afterGroup = AuditService.GroupSnapshot(cls);
        if (System.Text.Json.JsonSerializer.Serialize(beforeGroup) != System.Text.Json.JsonSerializer.Serialize(afterGroup))
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                $"Guruh tahrirlandi ({cls.Name})",
                before: beforeGroup, after: afterGroup);

        await db.SaveChangesAsync();
        return cls;
    }

    /// <summary>
    /// Guruhni BUTUNLAY o'chirish (xato ochilgan guruh). Pul tarixi bor guruh O'CHIRILMAYDI — arxivlanadi.
    /// <list type="bullet">
    ///   <item>faol (arxivda bo'lmagan) o'quvchi bo'lsa — 400;</item>
    ///   <item>guruhga teglangan to'lov/vozvrat (yoki eski teglanmagan to'lovning shu guruhga tushadigan
    ///     ulushi) bo'lsa — 400 "arxivlang" (<see cref="GroupDeletion.MoneyBlockReasonAsync"/>). Ilgari
    ///     hisoblar TO'LANGANIGA qaramay balansga qaytarilib, o'quvchilarda soxta avans paydo bo'lardi;</item>
    ///   <item>faqat TO'LANMAGAN hisoblar bo'lsa — ular balansga qaytariladi (soxta qarz qolmasin);</item>
    ///   <item>guruhning o'quv/yordamchi ma'lumoti (baholar, ball, testlar va ularning fayllari,
    ///     dars ko'chirishlari, dastur, o'rinbosarlar, o'qituvchi tarixi, chat …) — o'sha tranzaksiyada
    ///     (<see cref="GroupDeletion.PurgeLearningDataAsync"/>).</item>
    /// </list>
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromServices] IWebHostEnvironment env, [FromQuery] string? reasonId = null)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        // Faol o'quvchi bo'lsa — ClassName bo'yicha YOKI faol a'zolik (M2M) bo'yicha — o'chirib bo'lmaydi.
        // ARXIVDAGI o'quvchining (eski ma'lumotda qolib ketgan) tirik a'zoligi "faol o'quvchi" emas.
        var byName = await db.Students.CountAsync(s => s.ClassName == cls.Name && !s.IsArchived);
        var activeMembers = await db.StudentGroups
            .CountAsync(sg => sg.GroupId == id && sg.IsActive
                              && db.Students.Any(s => s.Id == sg.StudentId && !s.IsArchived));
        if (byName > 0 || activeMembers > 0)
            return BadRequest(new
            {
                message = $"Bu guruhda {Math.Max(byName, activeMembers)} ta faol o'quvchi bor — guruhni o'chirib bo'lmaydi. " +
                          "Avval o'quvchilarni chiqaring yoki arxivlang.",
            });

        // PUL TARIXI BOR GURUH O'CHIRILMAYDI (to'lov/vozvrat) — arxivlanadi.
        if (await GroupDeletion.MoneyBlockReasonAsync(db, id) is { } moneyBlock)
            return BadRequest(new { message = moneyBlock });

        // ⚠️ HAMMASI BITTA `SaveChangesAsync` DA (u o'zi tranzaksiya). Ilgari ommaviy `Execute*`
        // o'chirish/yangilashlari o'z SQL'ini DARHOL yuborardi va qo'lda ochilgan tranzaksiya bilan
        // o'ralgan edi — lekin `EnableRetryOnFailure` (Program.cs) qo'lda ochilgan tranzaksiyani
        // execution strategy'siz RAD ETADI, ya'ni guruhni o'chirish har safar 500 bilan yiqilardi.
        // Endi barcha o'chirish/teg tozalash KUZATILADIGAN amal: atomiklik ham, qayta urinish ham saqlanadi.

        // Bog'liq qatorlar orphan qolmasin: a'zoliklar (o'tganlar ham), jurnal yozuvlari, dars eslatmalari,
        // shu guruhga tegishli per-guruh oylik hisoblar (aks holda ledger yo'q guruhni hisoblardi).
        //
        // ⚠️ HISOBLAR SHUNCHAKI O'CHIRILMAYDI — avval BALANSGA QAYTARILADI. Har hisob yaratilganda
        // `Student.Balance` effektiv miqdorda kamaygan (`TuitionService.AccrueOne`); qatorni
        // balansga tegmasdan o'chirish o'quvchida MANGU soxta qarz qoldirardi (sabab va batafsil
        // izoh: `MembershipBilling.CreditAndDropGroupChargesAsync`). Yuqoridagi pul himoyasi tufayli
        // bu yerga faqat TO'LANMAGAN hisoblar yetib keladi.
        var credit = await MembershipBilling.CreditAndDropGroupChargesAsync(db, id);

        // O'quv/yordamchi ma'lumot (baholar, ball, testlar, dastur, chat …) va o'rinbosarlar — kuzatiladi,
        // pastdagi SaveChanges bilan saqlanadi (kesh ham o'z-o'zidan yangilanadi).
        var purge = await GroupDeletion.PurgeLearningDataAsync(db, cls, Actor);
        var subsRemoved = await SubstituteGroupLifecycle.RemoveAllAsync(db, audit, cls);

        // Arxivdagi o'quvchining «asosiy guruh» yorlig'i yo'q guruhda qolmasin: u arxivdan qaytarilsa
        // eski nom bo'yicha guruhsiz hisob (va SMS) boshlanib ketardi. (Faol o'quvchi yuqorida rad etilgan.)
        if (!await db.Classes.AnyAsync(c => c.Id != id && c.Name == cls.Name))
            foreach (var st in await db.Students.Where(s => s.ClassName == cls.Name && s.IsArchived).ToListAsync())
                st.ClassName = "";

        var memberships = await db.StudentGroups.Where(sg => sg.GroupId == id).ToListAsync();
        db.StudentGroups.RemoveRange(memberships);

        var journal = await db.JournalEntries.Where(e => e.ClassId == id).ToListAsync();
        db.JournalEntries.RemoveRange(journal);
        var jeDeleted = journal.Count;

        var notes = await db.LessonNotes.Where(n => n.ClassId == id).ToListAsync();
        db.LessonNotes.RemoveRange(notes);
        var lnDeleted = notes.Count;

        // Moliya tarixi SAQLANADI, lekin yo'q guruhga ishora qilmasin — GroupId tozalanadi (yozuv qoladi).
        // Bu yerga faqat o'qish to'lovi/vozvrat BO'LMAGAN va BEKOR QILINGAN qatorlar yetadi (pul himoyasi).
        var tagged = await db.FinanceTransactions.IgnoreQueryFilters().Where(t => t.GroupId == id).ToListAsync();
        foreach (var t in tagged) t.GroupId = null;
        var untagged = tagged.Count;

        db.Classes.Remove(cls);

        var reason = await ReasonLabelAsync(reasonId);
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        ArchiveService.Snapshot(db, "group", cls.Id, cls.Name, "", cls,
            reason.Length > 0 ? reason : null, actor);
        var removed = new List<string>();
        if (jeDeleted + lnDeleted > 0) removed.Add($"jurnal: {jeDeleted + lnDeleted}");
        if (purge.Grades > 0) removed.Add($"baholar/ball: {purge.Grades}");
        if (purge.Tests > 0) removed.Add($"testlar: {purge.Tests}");
        if (purge.Journal > 0) removed.Add($"dars ko'chirish/dastur: {purge.Journal}");
        if (subsRemoved > 0) removed.Add($"o'rinbosar tayinlovi: {subsRemoved}");
        if (purge.DiscountsCancelled > 0) removed.Add($"bekor qilingan chegirma: {purge.DiscountsCancelled}");
        audit.Record("Group", id, "delete",
            $"Guruh o'chirildi ({cls.Name})"
            + (removed.Count > 0 ? $" — o'chirildi: {string.Join(", ", removed)}" : "")
            + (reason.Length > 0 ? $" — sabab: {reason}" : ""));
        // PUL HARAKATI TARIXDA QOLSIN: nechta hisob bekor qilindi, kimlarga qancha qaytarildi va
        // nechta to'lov guruh tegisiz qoldi. Usiz "balansim nega o'zgardi" savoli javobsiz qolardi.
        if (credit.Rows > 0 || untagged > 0)
            audit.Record(AuditService.EntityClassFee, id, "delete",
                $"Guruh o'chirildi ({cls.Name}) — {credit.Rows} ta to'lanmagan oylik hisob bekor qilindi, " +
                $"{credit.Students} ta o'quvchi qarzidan {AuditService.Money(credit.Credited)} so'm olib tashlandi" +
                (untagged > 0 ? $"; {untagged} ta moliya yozuvi guruh tegisiz qoldi" : ""));
        await db.SaveChangesAsync();

        // Fayllar — baza saqlangandan KEYIN (saqlash yiqilsa fayl yo'qolib qolmasin). `StudentPurge` naqshi.
        foreach (var url in purge.FileUrls)
        {
            var path = StudentPurge.ResolveUploadPath(env.ContentRootPath, url);
            if (path is null || !System.IO.File.Exists(path)) continue;
            try { System.IO.File.Delete(path); }
            catch (Exception ex) { logger.LogWarning(ex, "O'chirilgan guruh faylini o'chirib bo'lmadi: {File}", Path.GetFileName(path)); }
        }
        return NoContent();
    }

    /// <summary>
    /// Guruhni arxivlash — <c>IsArchived=true</c>. A'zoliklar muzlatiladi (qisman to'lov + keyingi
    /// oylar hisobini bekor qilish bilan), guruh faol ro'yxatlardan chiqadi.
    ///
    /// <para>⚠️ <b>O'QUVCHILAR ARXIVLANMAYDI.</b> Ilgari shu guruhdagi o'quvchilar
    /// <c>IsArchived=true</c> qilinardi — ular o'quvchilar ro'yxatidan butunlay yo'qolib, faqat
    /// «Arxiv» tabida qolardi. Foydalanuvchi talabi: o'quvchi arxivga TUSHMASIN, shunchaki
    /// <b>«Aktiv emas»</b> ro'yxatida qolsin. Buni a'zolikni muzlatish o'zi beradi:
    /// <c>MemberState</c> "frozen" bo'ladi (<c>Active=false</c>) va oylik ham hisoblanmaydi
    /// (<c>TuitionService.AccrueMonth</c> faqat <c>active</c> a'zolikni oladi).</para>
    ///
    /// <para>⚠️ <b>A'ZOLIKNI MUZLATISH SHART</b> — faqat "arxivlashni olib tashlash" YETMAYDI:
    /// <c>AccrueMonth</c> guruhning <c>IsArchived</c> bayrog'iga QARAMAYDI, ya'ni a'zolik
    /// <c>active</c> bo'lib qolsa o'quvchiga har oy YOLG'ON QARZ yozilib, ota-onalarga to'lov
    /// eslatmasi SMS'i ketaverardi (<c>.claude/rules/membership-periods.md</c> §6).</para>
    ///
    /// <para>Amal «Guruhni yopish» (<see cref="Close"/>) bilan AYNAN bir xil yadroda —
    /// <c>CloseMembersAsync</c>; farqi: yopishda sana va sabab so'raladi, arxivlashda bugungi
    /// sana olinadi.</para>
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        if (!MayCloseGroups) return CloseForbidden();
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (cls.IsArchived) return BadRequest(new { message = "Guruh allaqachon arxivda" });

        var today = AppClock.Today.ToString("yyyy-MM-dd");

        // O'rinbosar sanalari bugundan KEYIN qolmasin (guruh yopiq — dars yo'q, haq ham yo'q).
        // ⚠️ Guruh maydonlari o'zgarishidan OLDIN (eski tayinlov guruh umri bo'yicha yoyiladi).
        await SubstituteGroupLifecycle.TrimAfterAsync(db, audit, cls, today, "guruh arxivlandi");

        // ⚠️ TARTIB: avval a'zoliklar yopiladi, KEYIN guruh arxivga olinadi — «Guruhni yopish»
        // dagi bilan aynan bir xil (pul hisobi guruhning arxiv bayrog'iga bog'liq bo'lib
        // qolmasin).
        var (frozen, alreadyFrozen, trialClosed, restored) =
            await CloseMembersAsync(cls, today, "Guruh arxivlandi");

        cls.IsArchived = true;
        cls.ArchivedAt = today;

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Guruh arxivlandi ({cls.Name}) — {frozen} a'zolik muzlatildi"
            + (trialClosed > 0 ? $", {trialClosed} sinovdagi a'zolik yakunlandi" : "")
            + "; o'quvchilar arxivlanmadi");
        await db.SaveChangesAsync();

        // ⚠️ `archivedStudents` ATAYIN 0 bo'lib qoldi (eski mijozlar javob shaklini kutadi) —
        // haqiqiy natija `frozenMembers` da.
        return Ok(new
        {
            archivedStudents = 0,
            frozenMembers = frozen,
            alreadyFrozen,
            trialClosed,
            restoredCharges = restored,
        });
    }

    /// <summary>
    /// Guruhni arxivdan chiqarish — <c>IsArchived=false</c>. Faqat shu guruh bilan arxivlangan
    /// (<c>ArchivedWithClass=true</c>) o'quvchilar qaytariladi; alohida arxivlanganlar arxivda qoladi.
    /// </summary>
    [HttpPost("{id}/unarchive")]
    public async Task<IActionResult> Unarchive(string id)
    {
        // `?force=true&forceReason=...` — jadval to'qnashuvini baribir o'tkazish (faqat superadmin, pastda).
        // Imzo ATAYIN o'zgartirilmadi (manba matnini tekshiradigan testlar shu imzoga tayanadi).
        var force = bool.TryParse(Request.Query["force"], out var forceFlag) && forceFlag;
        string? forceReason = Request.Query["forceReason"];
        if (!MayCloseGroups) return CloseForbidden();
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (!cls.IsArchived) return BadRequest(new { message = "Guruh arxivda emas" });
        // Qayta ochilgan guruhning o'qituvchisi ARXIVDA bo'lsa — darslar hech kimga yozilmasdi (audit R1-10).
        if (!string.IsNullOrWhiteSpace(cls.TeacherId)
            && await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == cls.TeacherId) is { IsArchived: true } archivedTeacher)
            return BadRequest(new { message = ArchivedTeacherMessage(archivedTeacher) +
                " Avval o'qituvchini arxivdan qaytaring yoki guruhga boshqa o'qituvchi biriktiring." });

        // «Guruhni yopish»/«Tugatish» qo'ygan TUGASH SANASI tozalanadi — aks holda qayta ochilgan
        // guruhning darslari jadvaldan, o'qituvchi ilovasidan va «Davomat olinmagan» monitoridan
        // yo'qolar, o'quvchilarga esa oylik yozilaverardi. Kelajakdagi (rejalashtirilgan) tugash
        // sanasi tegilmaydi; kerak bo'lsa guruh formasidan tahrirlanadi.
        var newEnd = GroupLifecycleRules.EndDateOnUnarchive(cls.EndDate, cls.ArchivedAt);
        var endCleared = newEnd is null && !string.IsNullOrWhiteSpace(cls.EndDate);

        // Qayta ochilgan guruh jadvali boshqa FAOL guruh bilan to'qnashmasin (yopiq turgan paytda xona/
        // o'qituvchi band qilingan bo'lishi mumkin) — 409, faqat superadmin sabab bilan (CRM Phase 5).
        var conflicts = await roomConflict.FindAsync(ScheduleConflicts.Of(cls) with { EndDate = newEnd, IsArchived = false });
        var verdict = ScheduleConflicts.Decide(conflicts.Count, force, forceReason, User.IsInRole(Roles.SuperAdmin));
        if (verdict.Verdict == ScheduleConflicts.Verdict.Blocked) return ScheduleConflict(verdict, conflicts);
        if (verdict.Verdict == ScheduleConflicts.Verdict.Forced)
            audit.Record(AuditService.EntityGroup, cls.Id, "update",
                ScheduleConflicts.ForcedAuditText(conflicts, verdict.Reason));
        var groupArchivedAt = cls.ArchivedAt;
        cls.EndDate = newEnd;
        cls.IsArchived = false;
        cls.ArchivedAt = null;
        // "Guruhni yopish" Status'ni "archived" qilgan bo'lsa — qaytarganda yana faol holatga o'tkazamiz
        // (a'zoliklar muzlatilganicha qoladi — kerak bo'lsa qo'lda aktivlashtiriladi).
        if (cls.Status == "archived") cls.Status = "active";

        // Qaytariladiganlar — shu guruh bilan arxivlanganlar (`ArchivedWithClass`): a'zoligi shu
        // guruhda bo'lganlar YOKI (eski yozuvlar) yorlig'i shu guruh nomi bo'lganlar.
        var memberIds = await db.StudentGroups.Where(m => m.GroupId == cls.Id)
            .Select(m => m.StudentId).Distinct().ToListAsync();
        var students = await db.Students
            .Where(s => s.IsArchived && s.ArchivedWithClass
                        && (memberIds.Contains(s.Id) || s.ClassName == cls.Name)).ToListAsync();

        // ⚠️ ESKI yozuvlarda bu o'quvchilarning a'zoligi `active` qolgan: o'quvchi qaytgach 12 soatlik
        // AccrueDue arxivda turgan HAR OYGA orqaga qarz (va SMS) yozardi. A'zolik o'quvchi arxivlangan
        // sanadan MUZLATILGAN holatda tiklanadi — davom ettirish ongli «Aktivlashtirish» bilan
        // (GroupUnarchive). ⚠️ Arxiv maydonlari tozalanishidan OLDIN (sana `ArchivedAt` dan olinadi).
        var revivedFrozen = await GroupUnarchive.FreezeRevivedMembershipsAsync(db, audit, cls, groupArchivedAt, students);

        foreach (var s in students)
        {
            s.IsArchived = false;
            s.ArchivedAt = null;
            s.ArchiveReason = null;
            s.ArchivedWithClass = false;
        }

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Guruh arxivdan chiqarildi ({cls.Name}) — {students.Count} ta o'quvchi bilan"
            + (revivedFrozen > 0 ? $" ({revivedFrozen} ta a'zolik muzlatilgan holatda tiklandi — arxivdagi oylar hisoblanmaydi)" : "")
            + (endCleared ? "; tugash sanasi tozalandi" : ""));
        await db.SaveChangesAsync();
        return Ok(new { restoredStudents = students.Count });
    }

    /// <summary>
    /// GURUHNI VAQTINCHA BLOKLASH — guruh o'qituvchi ilovasida UMUMAN ko'rinmay qoladi
    /// (ro'yxat, jurnal, baholash, testlar, o'quv dasturi, guruh chati) va o'qituvchi unga
    /// yoza olmaydi. Qoida bitta joyda: <see cref="TeacherGroupAccess"/>.
    ///
    /// <para>ARXIVLASH EMAS: o'quvchilar, a'zoliklar, oylik hisobi, maosh va hisobotlar
    /// TEGILMAYDI — guruh admin panelida odatdagidek faol ro'yxatda qoladi (belgisi bilan).
    /// Blokdan chiqarish bir tugma (<c>unblock</c>).</para>
    ///
    /// <para>NEGA PUT (POST emas): <c>AdminPermAttribute</c> da POST → <c>classes:create</c>,
    /// PUT → <c>classes:edit</c>. Bloklash — mavjud guruhni TAHRIRLASH, shuning uchun UI'dagi
    /// <c>can('classes','edit')</c> darvozasi bilan aynan mos tushadi.</para>
    /// </summary>
    [HttpPut("{id}/block")]
    public async Task<IActionResult> Block(string id, BlockRequest? req)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (cls.IsArchived)
            return BadRequest(new { message = "Arxivdagi guruh — u allaqachon o'qituvchida ko'rinmaydi" });
        if (cls.IsBlocked) return BadRequest(new { message = "Guruh allaqachon bloklangan" });

        cls.IsBlocked = true;
        cls.BlockedAt = AppClock.Today.ToString("yyyy-MM-dd");
        cls.BlockNote = (req?.Note ?? "").Trim();

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Guruh vaqtincha bloklandi ({cls.Name}) — o'qituvchida ko'rinmaydi"
                + (cls.BlockNote.Length > 0 ? $": \"{cls.BlockNote}\"" : ""));
        await db.SaveChangesAsync();
        return Ok(new { ok = true, blockedAt = cls.BlockedAt });
    }

    /// <summary>Guruhni blokdan chiqarish — o'qituvchida yana odatdagidek ko'rinadi.</summary>
    [HttpPut("{id}/unblock")]
    public async Task<IActionResult> Unblock(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (!cls.IsBlocked) return BadRequest(new { message = "Guruh bloklanmagan" });

        cls.IsBlocked = false;
        cls.BlockedAt = null;
        cls.BlockNote = string.Empty;

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Guruh blokdan chiqarildi ({cls.Name}) — o'qituvchida yana ko'rinadi");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ---------- Guruh a'zoligi (M2M) ----------

    /// <summary>Guruh a'zolari (faol + o'tgan). Faol a'zolar yuqorida.</summary>
    [HttpGet("{id}/members")]
    public async Task<ActionResult<IEnumerable<GroupMemberDto>>> Members(string id)
    {
        // ⚠️ ARXIVDAGI o'quvchining tirik a'zoligi (eski ma'lumot — ilgari arxivlash a'zolikka
        // tegmasdi) "hozirgi a'zo" emas: ro'yxatga tushmaydi. Chiqqan (tarixdagi) a'zoligi esa ko'rinadi.
        var rows = await (from sg in db.StudentGroups
                          join s in db.Students on sg.StudentId equals s.Id
                          where sg.GroupId == id && !(sg.IsActive && s.IsArchived)
                          orderby sg.IsActive descending, s.FullName
                          select new GroupMemberDto(s.Id, s.FullName, sg.JoinedAt, sg.LeftAt, sg.IsActive,
                              sg.Status, sg.ActivatedAt, sg.FrozenAt, s.Balance, sg.YearFreeze, 0, null, false))
                         .ToListAsync();
        // Balans — SHU GURUH bo'yicha (umumiy Student.Balance emas): boshqa guruhdagi qarz bu ro'yxatni
        // qizil qilib qo'ymasin (jurnal ro'yxati bilan bir xil mantiq).
        var balances = await GroupBalanceService.ForGroupAsync(db, id, rows.Select(r => r.StudentId));
        // CRM upgrade U6b: «Telegram» column — usable chats + relations (counts only).
        var telegram = await WunderkindLC.Application.Services.Messaging.TelegramLinkSummary.ForStudentsAsync(
            db, rows.Select(r => r.StudentId).ToList());
        return rows
            .Select(r => r with
            {
                Balance = balances.GetValueOrDefault(r.StudentId, 0m),
                TelegramLinked = telegram.GetValueOrDefault(r.StudentId)?.Linked ?? 0,
                TelegramRelations = telegram.GetValueOrDefault(r.StudentId)?.Relations ?? new List<string>(),
                CabinetLoggedIn = telegram.GetValueOrDefault(r.StudentId)?.Cabinet ?? false,
            })
            .ToList();
    }

    /// <summary>
    /// Guruhning FAOL a'zolari SMS yuborish uchun: telefonlar + a'zolik holati + SHU GURUH balansi.
    /// <para>⚠️ Bu endpoint ATAYIN qo'shildi. Ilgari guruh sahifasidagi "SMS jo'natish" modali
    /// <c>GET /admin/students</c> bilan markazning BARCHA o'quvchisini (to'liq entity — passport,
    /// manzil, chegirma va h.k.) tortib olib, brauzerda ~15 a'zoni filtrlardi. Server Fransiyada,
    /// foydalanuvchi O'zbekistonda — bu bir necha yuz kilobayt ortiqcha trafik edi.</para>
    /// <para>Balans <see cref="GroupBalanceService"/> orqali SHU GURUH bo'yicha (umumiy
    /// <c>Student.Balance</c> emas) — "faqat qarzdorlar" filtri a'zolar ro'yxatidagi bilan
    /// bir xil raqamga tayanishi uchun.</para>
    /// </summary>
    [HttpGet("{id}/sms-recipients")]
    public async Task<ActionResult<IEnumerable<GroupSmsRecipientDto>>> SmsRecipients(string id)
    {
        // Arxivdagi o'quvchiga guruh SMS'i ketmaydi (tirik a'zoligi eski ma'lumotda qolgan bo'lsa ham).
        var rows = await (from sg in db.StudentGroups
                          join s in db.Students on sg.StudentId equals s.Id
                          where sg.GroupId == id && sg.IsActive && !s.IsArchived
                          orderby s.FullName
                          select new GroupSmsRecipientDto(s.Id, s.FullName, s.Phone, s.ParentPhone,
                              s.FatherPhone, s.MotherPhone, sg.Status, 0m))
                         .ToListAsync();
        var balances = await GroupBalanceService.ForGroupAsync(db, id, rows.Select(r => r.StudentId));
        return rows
            .Select(r => r with { Balance = balances.GetValueOrDefault(r.StudentId, 0m) })
            .ToList();
    }

    /// <summary>
    /// Guruh sahifasi → "O'quvchilar" tabi (edutizim jadvali): a'zolar ro'yxati (faol + o'tgan) +
    /// telefon, kurslar soni, shu oy narxi va oxirgi izoh.
    /// <para>Balans — <see cref="Members"/> dagi bilan AYNAN bir manbadan (SHU GURUH bo'yicha).</para>
    /// <para>⚠️ NOZIK maydonlar javobning O'ZIDA darvozalanadi (GET bu controllerda har xodimga
    /// ochiq): narx (chegirma summasini ochadi) — faqat moliya ruxsatiga, oxirgi izoh — faqat
    /// o'quvchilar ro'yxati/izohlar ruxsatiga (<c>StudentsController.RedactDocs</c> naqshi).</para>
    /// </summary>
    [HttpGet("{id}/roster")]
    public async Task<ActionResult<IEnumerable<GroupRosterRowDto>>> Roster(string id)
    {
        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (group is null) return NotFound();

        var rows = await (from sg in db.StudentGroups.AsNoTracking()
                          join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                          // Arxivdagi o'quvchining tirik a'zoligi — hozirgi a'zo emas (Members bilan bir xil).
                          where sg.GroupId == id && !(sg.IsActive && s.IsArchived)
                          orderby sg.IsActive descending, s.FullName
                          select new
                          {
                              s.Id, s.FullName, s.Phone, sg.JoinedAt, sg.LeftAt, sg.IsActive,
                              sg.Status, sg.ActivatedAt, sg.FrozenAt, sg.YearFreeze,
                              s.SchoolGrade, s.SchoolGradeYear,
                          })
                         .ToListAsync();
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var balances = await GroupBalanceService.ForGroupAsync(db, id, ids);

        // "N ta Kurs" — hozir o'qiyotgan (joriy, muzlatilmagan) a'zoliklardagi TURLI kurslar.
        // Kurssiz guruh o'z-o'zicha bitta "kurs" deb sanaladi (aks holda chip yo'qolib qolardi).
        var memberCourses = await (from sg in db.StudentGroups.AsNoTracking()
                                   join g in db.Classes.AsNoTracking() on sg.GroupId equals g.Id
                                   where ids.Contains(sg.StudentId) && sg.IsActive && sg.Status != "frozen"
                                   select new { sg.StudentId, sg.GroupId, g.CourseId })
                                  .ToListAsync();
        var courseCount = memberCourses
            .GroupBy(x => x.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => string.IsNullOrEmpty(x.CourseId) ? "group:" + x.GroupId : x.CourseId)
                    .Distinct().Count());

        var lastNotes = new Dictionary<string, (string Text, string At)>();
        if (AdminPermAttribute.HasSectionAccess(User, "students.list")
            || AdminPermAttribute.HasSectionAccess(User, "students.notes"))
        {
            var notes = await db.StudentNotes.AsNoTracking()
                .Where(n => ids.Contains(n.StudentId))
                .Select(n => new { n.StudentId, n.Text, n.CreatedAt })
                .ToListAsync();
            foreach (var g in notes.GroupBy(n => n.StudentId))
            {
                var last = g.OrderByDescending(n => n.CreatedAt, StringComparer.Ordinal).First();
                var text = last.Text.Length > 200 ? last.Text[..200] + "…" : last.Text;
                lastNotes[g.Key] = (text, last.CreatedAt);
            }
        }

        var book = CanSeeFinance() ? await DiscountBook.LoadAsync(db, ids) : null;
        var month = TuitionService.CurrentMonth();
        // Har o'quvchining oyligi — SINFIGA qarab (`TuitionPricing`), hisob bilan bir xil.
        var bands = (await PricingBook.LoadForCourseAsync(db, group.CourseId)).BandsOf(group.CourseId);

        return rows.Select(r =>
        {
            var fee = TuitionPricing.Resolve(bands, group.MonthlyFee, r.SchoolGrade, r.SchoolGradeYear, month).Monthly;
            decimal? price = book is null
                ? null
                : fee - book.DiscountFor(r.Id, fee, month, id);
            var note = lastNotes.TryGetValue(r.Id, out var n) ? n : ("", "");
            return new GroupRosterRowDto(
                r.Id, r.FullName, r.JoinedAt, r.LeftAt, r.IsActive,
                r.Status, r.ActivatedAt, r.FrozenAt, balances.GetValueOrDefault(r.Id, 0m), r.YearFreeze,
                r.Phone, courseCount.GetValueOrDefault(r.Id, 0), price, note.Item1, note.Item2);
        }).ToList();
    }

    /// <summary>
    /// "Guruh → Guruh o'quvchilari" (edutizim <c>/group/group-students</c>): BARCHA guruhlardagi JORIY
    /// a'zoliklar, filtr va SERVERDA sahifalash bilan (ro'yxat 2–3 ming qator bo'lishi mumkin).
    /// <para>Filtr qoidasi — <see cref="GroupStudentsList"/> (sof, testlangan); bu yerda faqat qatorlar
    /// yig'iladi. Ruxsat — sinf darajasidagi <c>classes.list</c> (guruhlar ro'yxati bilan bir xil).</para>
    /// <para>⚠️ Marshrut <c>{id}</c> dan OLDIN mos keladi (literal segment parametrdan ustun).</para>
    /// </summary>
    [HttpGet("memberships")]
    public async Task<ActionResult<GroupStudentsList.Result>> Memberships(
        [FromQuery] bool frozen = false,
        [FromQuery] string? teacherId = null,
        [FromQuery] string? groupState = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GroupStudentsList.DefaultPageSize)
    {
        var rows = await (from sg in db.StudentGroups.AsNoTracking()
                          where sg.IsActive
                          join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                          // Arxivdagi o'quvchi "hozir kim qayerda" ro'yxatiga (va sanoqlariga) kirmaydi.
                          where !s.IsArchived
                          join g in db.Classes.AsNoTracking() on sg.GroupId equals g.Id
                          join t in db.Teachers.AsNoTracking() on g.TeacherId equals t.Id into tj
                          from t in tj.DefaultIfEmpty()
                          select new GroupStudentsList.Row(
                              sg.Id, s.Id, s.FullName, g.Id, g.Name, g.IsArchived,
                              g.TeacherId, t != null ? t.FullName : "",
                              sg.Status, sg.YearFreeze, sg.JoinedAt, sg.IsActive))
                         .ToListAsync();
        return GroupStudentsList.Apply(rows,
            new GroupStudentsList.Query(frozen, teacherId, groupState, from, to, q, page, pageSize));
    }

    /// <summary>O'quvchini guruhga qo'shish (M2M). Sig'im to'lgan bo'lsa rad etadi. Avval guruhsiz
    /// o'quvchining asosiy ClassName'i shu guruh nomiga o'rnatiladi (eski ko'rinishlar uchun).
    /// ARXIVDAGI o'quvchi qo'shilsa — avtomatik ARXIVDAN CHIQARILADI (o'qishga qaytdi degani);
    /// javobda <c>restored=true</c> qaytadi. Login paroli bloklangicha qoladi (arxivlashda tozalangan) —
    /// kerak bo'lsa admin "Parolni tiklash" orqali beradi, bu arxivdan qaytarish endpointi bilan bir xil.</summary>
    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddMember(string id, AddStudentToGroupRequest req)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound(new { message = "Guruh topilmadi" });
        // ARXIVDAGI (yopilgan/tugatilgan) guruhga o'quvchi qo'shilmaydi — u yerda dars yo'q, a'zolik
        // esa keyin aktivlashtirilib yopiq guruhdan oylik yozilardi. Avval «Arxivdan chiqarish».
        if (cls.IsArchived)
            return BadRequest(new { message = "Guruh arxivda — o'quvchi qo'shib bo'lmaydi. Avval guruhni arxivdan chiqaring." });
        var s = await db.Students.FindAsync(req.StudentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });

        // ESKI ARXIV (audit C1, `StudentArchiving`): 2026-10-07 dan oldin arxivlangan o'quvchida a'zoliklar
        // hamon TIRIK — arxivdan chiqarishdan OLDIN ular ARXIV SANASIDA yopiladi, aks holda arxivda o'tgan
        // oylar birdaniga hisoblanib SMS ketardi. Yangi arxivda tirik a'zolik yo'q — hech narsa qilinmaydi.
        using var legacyGate = s.IsArchived ? await PaymentIdempotency.LockAsync("student:" + s.Id) : null;
        if (s.IsArchived)
        {
            await StudentBalanceSync.ReloadAsync(db, s);
            await StudentArchiving.CloseLegacyMembershipsAsync(db, audit, s);
        }

        var existing = await db.StudentGroups
            .FirstOrDefaultAsync(sg => sg.StudentId == req.StudentId && sg.GroupId == id);
        if (existing is { IsActive: true })
            return BadRequest(new { message = "O'quvchi allaqachon shu guruhda" });

        if (cls.Capacity > 0)
        {
            // Sig'imni FAQAT o'rin band qilganlar to'ldiradi (active + trial) — MUZLATILGAN
            // a'zolik va ARXIVDAGI o'quvchi o'rin egallamaydi (MembershipLifecycle.SeatHolders).
            var enrolled = await MembershipLifecycle
                .SeatHolders(db.StudentGroups.Where(sg => sg.GroupId == id), db.Students)
                .CountAsync();
            if (enrolled >= cls.Capacity)
                return BadRequest(new { message = $"Guruh to'lgan ({cls.Capacity} o'rin)" });
        }

        var joinedAt = string.IsNullOrWhiteSpace(req.JoinedAt)
            ? AppClock.Today.ToString("yyyy-MM-dd") : req.JoinedAt!;
        // RecordedAt — HAQIQIY bugungi sana (JoinedAt orqaga sanalgan bo'lishi mumkin). Jurnalda
        // "dars o'tildi + yozuv yo'q = keldi" konventsiyasi shu sanadan oldingi (orqaga sanalgan) darslarga
        // qo'llanmasin deb — JournalService.GroupMonthAsync shu maydonni PresentDefaultFrom sifatida ishlatadi.
        var recordedAt = AppClock.Today.ToString("yyyy-MM-dd");
        if (existing is not null)
        {
            // Joriy faol davr tarixga ko'chiriladi — pastda ActivatedAt/FrozenAt/LeftAt TOZALANADI
            // va usiz o'tmish "pullik emas" bo'lib qolardi (MembershipLifecycle.ClosePeriod izohi).
            MembershipLifecycle.ClosePeriod(existing, recordedAt);
            existing.IsActive = true;
            existing.LeftAt = null;
            existing.JoinedAt = joinedAt;
            // Qayta qo'shilganda — yana sinov holatiga qaytadi.
            existing.Status = "trial";
            existing.ActivatedAt = string.Empty;
            existing.FrozenAt = string.Empty;
            // «Aktiv muzlatish» belgisi muzlatish bilan BIRGA yashaydi: a'zolik sinovga qaytdi,
            // demak eski belgi ro'yxatda yolg'on ko'rinib qolmasin (belgi KO'RSATUV uchun xolos).
            existing.YearFreeze = false;
            existing.RecordedAt = recordedAt;
        }
        else
        {
            db.StudentGroups.Add(new StudentGroup
            {
                StudentId = req.StudentId, GroupId = id, JoinedAt = joinedAt, IsActive = true,
                Status = "trial", RecordedAt = recordedAt,
            });
        }
        // Eski (single-class) ko'rinishlar uchun asosiy guruh nomini to'ldiramiz.
        if (string.IsNullOrWhiteSpace(s.ClassName)) s.ClassName = cls.Name;

        // ARXIVDAN CHIQARISH: arxivdagi o'quvchi guruhga qo'shilyapti — demak o'qishga qaytdi.
        // Maydonlar `POST students/{id}/restore` bilan bir xil tozalanadi (parol bloki tegilmaydi).
        var restored = s.IsArchived;
        if (restored)
        {
            s.IsArchived = false;
            s.ArchivedAt = null;
            s.ArchiveReason = null;
            s.ArchivedWithClass = false;
            audit.Record(AuditService.EntityStudentDiscount, s.Id, "update",
                $"O'quvchi arxivdan chiqarildi — \"{cls.Name}\" guruhiga qo'shildi ({s.FullName})",
                studentId: s.Id);
        }

        audit.Record("Membership", $"{id}:{req.StudentId}", "create",
            $"Guruhga qo'shildi: {s.FullName} → {cls.Name} (sinov, {joinedAt})" +
            (existing is not null ? " — qayta qo'shildi" : "") +
            (restored ? " — o'quvchi arxivdan qaytarildi" : ""),
            studentId: s.Id);

        await db.SaveChangesAsync();
        // Qulf SAQLASHDAN KEYIN darhol bo'shatiladi (`PaymentIntake` naqshi) — avto-xabar (SMS/Telegram)
        // sekin bo'lishi mumkin, shu o'quvchining (va shu qulf bo'lagidagi boshqalarning) to'lovi uni
        // kutmasin (audit R1-11). `Dispose` ikki marta chaqirilsa zarar yo'q.
        legacyGate?.Dispose();

        // Avto xabar — o'quvchi guruhga qo'shilganda ota-onaga ("O'quvchi guruhga qo'shilganda" hodisasi).
        await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.StudentAdded, s,
            new Dictionary<string, string> { ["{guruh}"] = cls.Name }, group: cls);
        return Ok(new { ok = true, restored });
    }

    /// <summary>Arxivdagi o'qituvchini faol guruhga biriktirib bo'lmasligi sababi (400).</summary>
    private static string ArchivedTeacherMessage(Teacher t) =>
        $"O'qituvchi {t.FullName} arxivda (ishdan ketgan) — faol guruhga biriktirib bo'lmaydi.";

    /// <summary>Amal sababini (ActionReason) id bo'yicha matnga aylantiradi — yo'q/bo'sh bo'lsa "".</summary>
    private async Task<string> ReasonLabelAsync(string? reasonId)
    {
        if (string.IsNullOrWhiteSpace(reasonId)) return "";
        return await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "";
    }

    /// <summary>O'quvchini guruhdan chiqarish (LeftAt belgilanadi, IsActive=false). Tarix saqlanadi.
    /// Sabab (holatga qarab remove_active/remove_trial/remove_frozen) tanlansa auditga yoziladi.
    ///
    /// <para>⚠️ AKTIV a'zolikda HISOB ham yopiladi — muzlatish/guruh almashtirish bilan AYNAN bir xil
    /// (<see cref="MembershipBilling.SettleFreezeAsync"/>): chiqish oyi faqat <paramref name="date"/>
    /// gacha qatnashgan darslar uchun, keyingi oylarga (oldindan) yozilgan hisoblar bekor qilinadi.
    /// Ilgari faqat <c>IsActive=false</c> qo'yilardi — oy o'rtasida chiqqan o'quvchida TO'LIQ oylik va
    /// oldindan yozilgan keyingi oylar qarz bo'lib qolardi ("butunlay chiqib ketsin" — foydalanuvchi,
    /// 2026-10-06). Muzlatilgan a'zolik hisobi muzlatishda allaqachon yopilgan, sinovda hisob yo'q.</para>
    /// </summary>
    /// <param name="date">Chiqish sanasi ("yyyy-MM-dd"); bo'sh — bugun. Kelajak sana bo'lmaydi.</param>
    /// <param name="note">Ixtiyoriy erkin izoh — o'quvchi profilidagi izohlarga yoziladi (<see cref="LeaveNotes"/>).</param>
    /// <param name="archiveIfLast">«Boshqa guruhi qolmasa — arxivga ham o'tkazish» (2026-10-23, mijoz: "guruhdan
    /// chiqarilganlar arxivga tushmayapti"). O'quvchining BOSHQA tirik a'zoligi qolmasa u shu chiqarish bilan
    /// BIRGA arxivlanadi (<see cref="StudentArchiveFlow"/>, arxiv sababi = chiqarish sababi), qolsa — faqat
    /// chiqariladi (javobda <c>archived = false</c>). Ruxsat — «Arxivga ko'chirish» bilan bir xil.</param>
    [HttpDelete("{id}/members/{studentId}")]
    public async Task<IActionResult> RemoveMember(string id, string studentId, [FromQuery] string? reasonId = null,
                                                  [FromQuery] string? date = null, [FromQuery] string? note = null,
                                                  [FromQuery] bool archiveIfLast = false)
    {
        var (noteText, noteError) = LeaveNotes.Clean(note);
        if (noteError is not null) return BadRequest(new { message = noteError });
        // Darvoza HECH NARSA o'zgarmasdan OLDIN — aks holda o'quvchi chiqarilib, arxivlash 403 bilan yiqilardi.
        if (archiveIfLast && !MayArchiveStudents)
            return StatusCode(403, new { message = ArchiveForbiddenMessage });

        var sg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
        if (sg is null) return NotFound(new { message = "Faol a'zolik topilmadi" });

        var today = AppClock.Today;
        var leftAt = today.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParse(date.Trim(), out var d)) return BadRequest(new { message = "Chiqish sanasi noto'g'ri" });
            if (d > today) return BadRequest(new { message = "Chiqish sanasi kelajakda bo'lishi mumkin emas" });
            leftAt = d.ToString("yyyy-MM-dd");
        }

        var cls = await db.Classes.FindAsync(id);
        var student = await db.Students.FindAsync(studentId);
        var status = sg.Status;
        var restored = 0m;
        if (status == "active" && cls is not null && student is not null)
        {
            // Bir o'quvchining pul amallari bir navbatda (to'lov/vozvrat bilan bir qulf) — balans
            // qulf ICHIDA qayta o'qiladi (parallel to'lov ta'siri eski qiymat ustidan yozilmasin).
            using var gate = await PaymentIdempotency.LockAsync("student:" + studentId);
            await StudentBalanceSync.ReloadAsync(db, student);
            restored = (await MembershipBilling.LeaveGroupAsync(db, student, cls, sg, leftAt)).Restored;
            await ReleasePrimaryGroupNameAsync(student, cls);
            await RecordRemovalAsync();
            var archivedActive = await ArchiveIfLastAsync();
            await db.SaveChangesAsync();
            return Ok(new { archived = archivedActive });
        }

        // Sinov/muzlatilgan — hisob yo'q yoki allaqachon yopilgan: faqat chiqish.
        // Arxivlash so'ralsa — o'quvchi qulfi (arxivlash pul amallari bilan bir navbatda, `Archive` bilan bir xil).
        using var archiveGate = archiveIfLast && student is not null
            ? await PaymentIdempotency.LockAsync("student:" + studentId)
            : (IDisposable?)null;
        if (archiveGate is not null) await StudentBalanceSync.ReloadAsync(db, student!);
        sg.IsActive = false;
        sg.LeftAt = leftAt;
        if (student is not null && cls is not null) await ReleasePrimaryGroupNameAsync(student, cls);
        await RecordRemovalAsync();
        var archived = await ArchiveIfLastAsync();
        await db.SaveChangesAsync();
        return Ok(new { archived });

        // «Boshqa guruhi qolmasa — arxivga ham o'tkazish». Shu a'zolik (hali saqlanmagan) hisobga olinmaydi.
        async Task<bool> ArchiveIfLastAsync()
        {
            if (!archiveIfLast || student is null || student.IsArchived) return false;
            var hasOther = await db.StudentGroups.AnyAsync(x =>
                x.StudentId == studentId && x.IsActive && x.GroupId != id);
            if (hasOther) return false;
            await StudentArchiveFlow.ArchiveAsync(db, audit, student, AppClock.Today.ToString("yyyy-MM-dd"),
                reasonId, await ReasonLabelAsync(reasonId), "oxirgi guruhidan chiqarilganda");
            return true;
        }

        async Task RecordRemovalAsync()
        {
            var reason = await ReasonLabelAsync(reasonId);
            // Tuzilgan sabab — «Ketish sabablari» hisoboti uchun (audit matni faqat o'qish uchun).
            MembershipReasons.Record(db, studentId, id, MembershipReasons.ActionRemove, leftAt, reasonId, reason);
            var statusLabel = status == "active" ? "aktiv" : status == "frozen" ? "muzlatilgan" : "sinovdagi";
            audit.Record("Membership", $"{id}:{studentId}", "delete",
                $"Guruhdan chiqarildi ({statusLabel}, {leftAt}, guruh: {cls?.Name ?? id})"
                    + (restored > 0 ? $" — keyingi oylar hisobi bekor qilindi: {AuditService.Money(restored)} so'm" : "")
                    + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
                studentId: studentId);
            // Izoh audit matniga EMAS (sabab o'sha matn oxiridan o'qiladi) — profil izohlariga.
            if (noteText is not null)
                LeaveNotes.Add(db, studentId, LeaveNotes.GroupLeaveText(cls?.Name ?? id, leftAt, noteText), ActorId, Actor);
        }
    }

    /// <summary>
    /// O'quvchining «asosiy guruh» yorlig'i (<see cref="Student.ClassName"/>) chiqqan guruh nomida
    /// qolmasin: boshqa TIRIK guruhi bo'lsa o'sha, bo'lmasa bo'sh. ⚠️ Ilgari chiqarilgan o'quvchi yorliq
    /// orqali guruhda "qolib ketardi": portalda guruh darslari va chati, ro'yxatda guruh belgisi va
    /// filtri, guruhni o'chirishda "faol o'quvchi bor" xatosi. Billing bunga tayanmaydi (`AccrueMonth`
    /// yorliqni faqat UMUMAN a'zoligi yo'q o'quvchida ishlatadi).
    /// </summary>
    private async Task ReleasePrimaryGroupNameAsync(Student s, Group left)
    {
        if (!string.Equals(s.ClassName, left.Name, StringComparison.Ordinal)) return;
        s.ClassName = await (from sg in db.StudentGroups
                             join c in db.Classes on sg.GroupId equals c.Id
                             where sg.StudentId == s.Id && sg.IsActive && sg.GroupId != left.Id
                             orderby sg.JoinedAt
                             select c.Name).FirstOrDefaultAsync() ?? "";
    }

    /// <summary>A'zolikni SINOVGA qaytarish (active/frozen → trial). Oylik to'lov hisoblanmaydi (trial).</summary>
    [HttpPost("{id}/members/{studentId}/return-trial")]
    public async Task<IActionResult> ReturnToTrial(string id, string studentId, MembershipStatusRequest req)
    {
        var sg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
        if (sg is null) return NotFound(new { message = "Faol a'zolik topilmadi" });
        // ActivatedAt tozalanishidan OLDIN joriy faol davrni tarixga ko'chiramiz: o'quvchi sinovga
        // qaytdi degani "u hech qachon pullik bo'lmagan" degani EMAS.
        MembershipLifecycle.ClosePeriod(sg, AppClock.Today.ToString("yyyy-MM-dd"));
        sg.Status = "trial";
        sg.ActivatedAt = string.Empty;
        sg.FrozenAt = string.Empty;
        // Muzlatish bekor bo'ldi — «aktiv muzlatish» belgisi ham qolmaydi (faqat KO'RSATUV belgisi).
        sg.YearFreeze = false;

        var reason = await ReasonLabelAsync(req.ReasonId);
        var cls = await db.Classes.FindAsync(id);
        audit.Record("Membership", $"{id}:{studentId}", "update",
            $"Sinovga qaytarildi (guruh: {cls?.Name ?? id})" + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            studentId: studentId);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>A'zolikni AKTIVLASHTIRISH (sinov → faol). Birinchi (qisman) oy to'lovi avtomatik hisoblanadi:
    /// oyning birinchi darsidan / 12+ dars qolgan bo'lsa to'liq oylik, aks holda qolgan dars × kursning bir
    /// dars yaxlit narxi (LessonPrice); to'liq oylikdan oshmaydi; chegirma qo'llanadi. ORQAGA SANALGAN
    /// aktivlashtirishda oraliq oylar (aktiv oydan keyingi oydan joriy oygacha) DARHOL to'liq oylik bilan
    /// yoziladi — <see cref="TuitionService.AccrueCatchUpAsync"/>; javobdagi <c>catchUpMonths</c> — nechta oy.
    /// Keyingi (kelajak) to'liq oylar oddiy oylik hisob (AccrueMonth) orqali. TRANSACTION: race condition oldini olish uchun atomik read-modify-write.
    /// <para>Shu OYDA boshqa guruhda muzlatilgan a'zolik bo'lsa (qo'lda guruh almashtirish), o'sha guruhda
    /// ORTIB QOLGAN to'lov shu guruhga ko'chiriladi — <see cref="TuitionService.CarryGroupAdvanceAsync"/>.</para></summary>
    [HttpPost("{id}/members/{studentId}/activate")]
    public async Task<IActionResult> ActivateMember(string id, string studentId, MembershipStatusRequest req)
    {
        try
        {
            var cls = await db.Classes.FindAsync(id);
            if (cls is null) return NotFound(new { message = "Guruh topilmadi" });
            // Sana tekshiriladi (ommaviy amaldagi kabi): buzuq sana `ActivatedAt` ga tushib, keyin
            // oylar oralig'ini hisoblashda cheksiz siklga olib borardi (membership-periods.md §3).
            if (!TryNormalizeDate(req.Date, out var parsed))
                return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
            var date = parsed ?? AppClock.Today.ToString("yyyy-MM-dd");

            // Refresh'langan ma'lumot bilan oqiylik (dirty-read oldini olish).
            var sg = await db.StudentGroups
                .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
            if (sg is null)
                return NotFound(new { message = "Faol a'zolik topilmadi" });

            // ARXIVDAGI guruhda (yoki arxivdagi o'quvchida) aktivlashtirish yo'q — yopiq guruhdan oylik
            // yozila boshlardi. Sabab ochiq aytiladi (MembershipBulk.ActivationBlockReason — yagona qoida).
            if (sg.Status != "active")
            {
                var studentArchived = await db.Students.AsNoTracking()
                    .Where(x => x.Id == studentId).Select(x => x.IsArchived).FirstOrDefaultAsync();
                switch (MembershipBulk.ActivationBlockReason(cls.IsArchived, studentArchived))
                {
                    case MembershipBulk.GroupArchivedReason:
                        return BadRequest(new { message = "Guruh arxivda — aktivlashtirib bo'lmaydi. Avval guruhni arxivdan chiqaring yoki o'quvchini faol guruhga o'tkazing." });
                    case MembershipBulk.StudentArchivedReason:
                        return BadRequest(new { message = "O'quvchi arxivda — avval o'quvchini arxivdan qaytaring." });
                }
            }

            var r = await ActivateCoreAsync(cls, sg, date, req.RetentionBonus);
            // Allaqachon faol bo'lsa — qayta aktivlashtirish kerak emas (ikki marta hisoblamaslik uchun).
            if (r.Already) return Ok(new { ok = true, already = true });

            await db.SaveChangesAsync();

            return Ok(new { ok = true, movedAdvance = r.MovedAdvance, catchUpMonths = r.CatchUpMonths });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ActivateMember error for group={GroupId}, student={StudentId}: {Message}", id, studentId, ex.Message);
            return StatusCode(500, new { message = "Aktivlashtirish xatosi", error = ex.Message });
        }
    }

    /// <summary>
    /// AKTIV a'zolikning AKTIVLASHTIRISH SANASINI o'zgartirish — sanasi xato/kech kiritilgan o'quvchi uchun
    /// (masalan 8-sanadan qisman hisoblangan, aslida 1-sanadan o'qiyapti). Qoida va pul —
    /// <see cref="ActivationDateChange"/>. <c>preview=true</c> — natija ko'rsatiladi, HECH NARSA saqlanmaydi.
    /// Ruxsat — aktivlashtirish bilan bir xil (sinf darajasidagi <c>classes.list</c>, POST).
    /// </summary>
    [HttpPost("{id}/members/{studentId}/activation-date")]
    public async Task<ActionResult<ActivationDateResultDto>> ChangeActivationDate(
        string id, string studentId, ActivationDateRequest req)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound(new { message = "Guruh topilmadi" });
        // Arxivdagi (yopilgan) guruh — orqaga surilsa catch-up yopiq guruhdan to'liq oyliklar yozardi
        // (`MembershipBulk.ActivationBlockReason` bilan bir xil sabab).
        if (cls.IsArchived)
            return BadRequest(new { message = "Guruh arxivda — aktivlashtirish sanasini o'zgartirib bo'lmaydi" });
        // Bir o'quvchining pul amallari bir navbatda (to'lov/vozvrat bilan bir qulf).
        using var gate = await PaymentIdempotency.LockAsync("student:" + studentId);
        var sg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
        if (sg is null) return NotFound(new { message = "Faol a'zolik topilmadi" });
        var reason = ActivationDateChange.BlockReason(sg, req.Date, AppClock.Today);
        if (reason is not null) return BadRequest(new { message = reason });
        var s = await db.Students.FindAsync(studentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (s.IsArchived) return BadRequest(new { message = "O'quvchi arxivda — avval arxivdan qaytaring" });

        await StudentBalanceSync.ReloadAsync(db, s);
        var r = await ActivationDateChange.ApplyAsync(db, s, cls, sg, req.Date!);
        var dto = new ActivationDateResultDto(!req.Preview, r.OldDate, r.NewDate, r.BalanceBefore, r.BalanceAfter,
            r.Months.Select(m => new ActivationDateMonthDto(m.Month, m.Before, m.After)).ToList(), r.LockedMonths);
        if (req.Preview)
        {
            // Ko'rish rejimi — o'zgarishlar bazaga TUSHMAYDI.
            db.ChangeTracker.Clear();
            return dto;
        }

        var monthsText = r.Months.Count == 0 ? "hisob o'zgarmadi"
            : "hisob: " + string.Join("; ", r.Months.Select(m =>
                $"{m.Month} {AuditService.Money(m.Before ?? 0)} → {AuditService.Money(m.After ?? 0)} so'm"));
        audit.Record("Membership", $"{cls.Id}:{studentId}", "update",
            $"Aktivlashtirish sanasi o'zgartirildi: {r.OldDate} → {r.NewDate} — {s.FullName}, {cls.Name}; {monthsText}"
            + (r.LockedMonths.Count > 0 ? $" (qo'lda tahrirlangan oy tegilmadi: {string.Join(", ", r.LockedMonths)})" : ""),
            studentId: studentId);
        await db.SaveChangesAsync();
        return dto;
    }

    /// <summary>
    /// «Aktiv o'quvchi qilish» oynasi (lidni o'quvchiga aylantirish + darhol aktivlashtirish) — shu guruhga shu sanadan
    /// aktivlashtirilsa BIRINCHI oyga qancha yozilishi va qanday hisoblangani. Hech narsa yozmaydi; yangi o'quvchi
    /// (sinfsiz, chegirmasiz) — aktivlashtirish aynan shunday hisoblaydi (`TuitionService.ActivationQuoteAsync`).
    /// </summary>
    [HttpGet("{id}/activation-quote")]
    public async Task<ActionResult<ActivationQuoteDto>> ActivationQuote(string id, [FromQuery] string? date)
    {
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cls is null) return NotFound(new { message = "Guruh topilmadi" });
        if (!TryNormalizeDate(date, out var parsed))
            return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        var q = await TuitionService.ActivationQuoteAsync(db, cls, parsed ?? AppClock.Today.ToString("yyyy-MM-dd"));
        if (q is null) return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        return new ActivationQuoteDto(q.Month, q.Monthly, q.LessonsInMonth, q.LessonsLeft, q.LessonFee, q.Amount, q.FullMonth);
    }

    /// <summary>Bitta a'zolikni AKTIVLASHTIRISH — hisob-kitobning YAGONA manbai: yakka
    /// <see cref="ActivateMember"/> ham, OMMAVIY <see cref="BulkApplyAsync"/> ham AYNAN shuni chaqiradi
    /// (aks holda ikki oqim vaqt o'tib bir-biridan ayrilib ketardi).
    /// <para><c>SaveChangesAsync</c> QILMAYDI — chaqiruvchi saqlaydi (audit yozuvi ham shu tranzaksiyada).</para></summary>
    /// <returns><c>Already</c> — allaqachon faol edi (hech narsa o'zgarmadi).</returns>
    /// <param name="lessonFee">Kursning bir dars yaxlit narxi, OLDINDAN hisoblangan bo'lsa (OMMAVIY
    /// amalda — <see cref="TuitionService.LessonFeesForCoursesAsync"/>). ⚠️ FAQAT tezlik uchun:
    /// <c>null</c> bo'lsa AYNAN o'sha qiymat pastda yakka so'rov bilan olinadi. Yakka
    /// <see cref="ActivateMember"/> uni bermaydi va avvalgidek ishlaydi —
    /// <see cref="FreezeCoreAsync"/> dagi bilan bir xil naqsh.</param>
    private async Task<(bool Already, decimal MovedAdvance, int CatchUpMonths)> ActivateCoreAsync(
        Group cls, StudentGroup sg, string date, bool? retentionBonus, decimal? lessonFee = null,
        PricingBook? pricing = null)
    {
        // Allaqachon faol bo'lsa — qayta aktivlashtirish kerak emas (ikki marta hisoblamaslik uchun).
        if (sg.Status == "active") return (true, 0m, 0);

        var studentId = sg.StudentId;

        // SHU OYDA muzlatilgandan keyin qayta aktivlashtirilyaptimi? Bo'lsa, muzlatishgacha studied segment
        // saqlanib, yangi segment USTIGA QO'SHILADI (aks holda studied portion yo'qolardi).
        // Muzlatilgan a'zolikni qayta aktivlashtirishga RUXSAT beriladi (avval guard noto'g'ri bloklardi).
        var reactivateFromFreeze = sg.Status == "frozen"
            && sg.FrozenAt.Length >= 7 && date.Length >= 7 && sg.FrozenAt[..7] == date[..7];

        // ⚠️ ActivatedAt USTIDAN yozilmoqda — oldingi faol davr shu yerda tarixga ko'chirilmasa,
        // izsiz yo'qolardi (aynan shu sabab muzlashdan oldingi oylar "pullik emas" bo'lib qolar,
        // to'lov oynasida ko'rinmas, maosh/bonus/analitika esa noto'g'ri hisoblanardi).
        MembershipLifecycle.ClosePeriod(sg, date);
        // Shu OYDA chiqarilib qayta qo'shilgan (sinov) a'zolik ham oldingi segmentni saqlaydi —
        // ilgari faqat muzlatishdan qaytish hisobga olinardi, qayta qo'shishda esa oy boshidagi
        // qatnashilgan darslar yangi segment bilan almashib, hisobdan yo'qolardi.
        var addSegment = reactivateFromFreeze || MembershipLifecycle.ClosedPeriodEndsInMonth(sg, date);
        sg.Status = "active";
        sg.ActivatedAt = date;
        sg.FrozenAt = string.Empty;
        // «Aktiv muzlatish» belgisi FrozenAt bilan birga tozalanadi: o'quvchi yangi o'quv yiliga
        // qaytdi. Belgi hisob-kitobga tegmaydi — u faqat ro'yxatdagi ko'rsatuv uchun edi.
        sg.YearFreeze = false;
        // RecordedAt — HAQIQIY bugungi sana (date orqaga sanalgan bo'lishi mumkin, masalan o'tgan
        // oydan aktivlashtirilsa). Jurnalda MemberStart (=date) bilan RecordedAt orasidagi allaqachon
        // o'tilgan darslar avtomatik "keldi" bo'lib ko'rinmasin — o'qituvchi ularni qo'lda belgilaydi.
        sg.RecordedAt = AppClock.Today.ToString("yyyy-MM-dd");

        var s = await db.Students.FindAsync(studentId);
        // Oraliqlar — bir marta (yakka amalda shu yerda, ommaviyda chaqiruvchidan).
        pricing ??= await PricingBook.LoadForCourseAsync(db, cls.CourseId);
        // Tarixdagi "oylik" — o'quvchining SINFIGA qarab (`TuitionPricing`), haqiqiy hisob bilan bir xil.
        var monthlyForAudit = s is null || date.Length < 7 ? cls.MonthlyFee : pricing.MonthlyFee(cls, s, date[..7]);
        audit.Record("Membership", $"{cls.Id}:{studentId}", "update",
            $"Aktivlashtirildi: {s?.FullName ?? studentId} — {cls.Name} ({date} sanasidan, " +
            $"oylik {AuditService.Money(monthlyForAudit)} so'm)" +
            (reactivateFromFreeze ? " — muzlatishdan qaytarildi" : ""),
            studentId: studentId);

        var catchUpMonths = 0;
        if (s is not null)
        {
            await TuitionService.ChargeActivationProrateAsync(db, s, cls, date, addSegment: addSegment, lessonFee: lessonFee, pricing: pricing);
            // ORQAGA SANALGAN aktivlashtirish: aktivlashtirilgan oydan KEYINGI oylardan joriy oygacha
            // to'liq oylik hisoblar DARHOL yoziladi (aks holda fon xizmati 12 soatgacha kechikardi).
            // Idempotent — mavjud hisoblarga tegmaydi, kelajak oy yozmaydi.
            catchUpMonths = await TuitionService.AccrueCatchUpAsync(db, s, cls, date, sg);

            // USHLAB TURISH BONUSI: shu guruh FANI bo'yicha bonus hisoblansinmi (aktivlashtirish
            // oynasidagi ptichka). Sanoq AYNAN shu — aktivlashtirilgan — oydan boshlanadi:
            // o'quvchi guruhga bir oyda qo'shilib, keyingi oydan aktivlashtirilishi mumkin.
            // retentionBonus == null bo'lsa tegilmaydi (eski chaqiruvlar).
            //
            // RUXSAT: ptichkani FAQAT superadmin yoki "retentionBonus" ruxsati berilgan xodim
            // qo'ya oladi (oddiy `admin` roli ham kirmaydi — markaz egasining talabi).
            // Ruxsatsiz kelgan qiymat JIM e'tiborsiz qoldiriladi: UI ptichkani ko'rsatmaydi,
            // ya'ni bunday so'rov faqat qo'lda yasalgan bo'lishi mumkin va u aktivlashtirishning
            // o'zini bloklamasligi kerak.
            var maySetBonus = AdminPermAttribute.IsSuperAdminOrGranted(User, RetentionBonusPerm);
            await RetentionBonusService.ApplyOnActivateAsync(
                db, studentId, cls, date, maySetBonus ? retentionBonus : null, Actor);
        }

        // AVANSNI KO'CHIRISH (guruh almashtirish QO'LDA bajarilganda): o'quvchi SHU OYDA boshqa guruhda
        // muzlatilgan bo'lib, o'sha guruhga to'lagan puli muzlatish hisobidan ORTIB QOLGAN bo'lsa — u shu
        // yangi guruhga o'tadi. Aks holda to'lagan o'quvchi yangi guruhda "qarzdor" (qizil) ko'rinardi.
        // Faqat AYNAN SHU OYDA muzlatilgan a'zolik (ya'ni guruh almashtirish belgisi) hisobga olinadi —
        // ilgari muzlatilgan (masalan ta'tildagi) a'zolikning avansi tegilmaydi.
        var movedAdvance = 0m;
        if (s is not null && date.Length >= 7)
        {
            var month = date[..7];
            // Shu oyda MUZLATILGAN yoki guruhdan CHIQQAN a'zolik — chiqarish endi muzlatish emas, balki
            // `IsActive=false` + `LeftAt` (`MembershipBilling.LeaveGroupAsync`); ikkalasi ham "guruh
            // almashtirish belgisi" — aks holda «chiqarish + qo'shish» oqimida avans eski guruhda qolardi.
            var ended = await db.StudentGroups
                .Where(x => x.StudentId == studentId && x.GroupId != cls.Id && (x.Status == "frozen" || !x.IsActive))
                .ToListAsync();
            foreach (var fz in ended.Where(x =>
                         (x.Status == "frozen" && x.FrozenAt.Length >= 7 && x.FrozenAt[..7] == month)
                         || (!x.IsActive && (x.LeftAt ?? "").Length >= 7 && x.LeftAt![..7] == month)))
            {
                var fromGroup = await db.Classes.FindAsync(fz.GroupId);
                if (fromGroup is null) continue;
                var carried = await TuitionService.CarryGroupAdvanceAsync(db, s, fromGroup, cls, month);
                if (carried <= 0) continue;
                movedAdvance += carried;
                audit.Record("Membership", $"{cls.Id}:{studentId}", "update",
                    $"To'lov guruhi ko'chirildi: {fromGroup.Name} → {cls.Name} — {AuditService.Money(carried)} so'm ({month})",
                    studentId: studentId);
            }
        }

        return (false, movedAdvance, catchUpMonths);
    }

    /// <summary>A'zolikni MUZLATISH — kiritilgan sanadan (shu oydan) boshlab oylik to'lov hisoblanmaydi. TRANSACTION:
    /// race condition (shunas vaqtda activation + freeze) oldini olish uchun atomik read-modify-write.
    /// <para>ORQAGA SANALGAN muzlatishda muzlatish oyidan KEYINGI hisoblar BEKOR QILINADI
    /// (<see cref="TuitionService.PurgeChargesAfterMonthAsync"/>) va effektiv summa balansga qaytariladi —
    /// o'quvchi o'qimagan oylar uchun qarzdor bo'lib qolmasin. Bu "Guruhni yopish" (<c>close</c>) bilan
    /// AYNAN bir xil konvensiya; javobdagi <c>restored</c> — qaytarilgan summa.</para>
    /// <para>Muzlatish sanasi aktivlashtirish sanasidan OLDIN bo'lsa (ya'ni umuman o'qimagan) —
    /// qisman to'lov ham yozilmaydi va aktivlashtirish oyi hisobi ham butunlay bekor qilinadi.</para></summary>
    [HttpPost("{id}/members/{studentId}/freeze")]
    public async Task<IActionResult> FreezeMember(string id, string studentId, MembershipStatusRequest req)
    {
        if (req.YearFreeze == true && !MaySetYearFreeze)
            return StatusCode(403, new { message = "«Aktiv muzlatish» faqat superadmin uchun" });

        // Sana — ommaviy muzlatishdagi bilan AYNAN bir xil tekshiruv (buzuq sana `FrozenAt` ga tushib,
        // qisman oylik va keyingi oylarni bekor qilish jimgina noto'g'ri ishlardi).
        if (!TryNormalizeDate(req.Date, out var parsed))
            return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        var date = parsed ?? AppClock.Today.ToString("yyyy-MM-dd");

        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound(new { message = "Guruh topilmadi" });

        // Refresh'langan ma'lumot bilan oqiylik (dirty-read oldini olish).
        var sg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
        if (sg is null)
            return NotFound(new { message = "Faol a'zolik topilmadi" });

        var reason = await ReasonLabelAsync(req.ReasonId);
        var r = await FreezeCoreAsync(cls, sg, date, reason, req.YearFreeze == true, reasonId: req.ReasonId);
        // trial yoki active emas — kutilmagan holat (eski/buzilgan yozuv).
        if (r.Error is not null) return BadRequest(new { message = r.Error });
        // Allaqachon muzlatilgan bo'lsa — qayta muzlatish kerak emas (takroriy prorate'ni oldini olamiz).
        // Idempotent: 400 o'rniga jim qaytamiz (foydalanuvchi tugmani qayta bossa xato chiqmasin).
        if (r.Already) return Ok(new { ok = true, already = true });

        await db.SaveChangesAsync();

        return Ok(new { ok = true, restored = r.Restored });
    }

    /// <summary>Bitta a'zolikni MUZLATISH — hisob-kitobning YAGONA manbai: yakka
    /// <see cref="FreezeMember"/> ham, OMMAVIY <see cref="BulkApplyAsync"/> ham AYNAN shuni chaqiradi.
    /// <para><c>SaveChangesAsync</c> QILMAYDI — chaqiruvchi saqlaydi.</para></summary>
    /// <returns><c>Already</c> — allaqachon muzlatilgan; <c>Error</c> — holat mos emas (hech narsa o'zgarmadi).</returns>
    /// <param name="yearFreeze">«AKTIV MUZLATISH» — yangi o'quv yiliga o'tish belgisi. ⚠️ FAQAT
    /// ko'rsatuv: <c>Status</c> baribir "frozen", hisob-kitob (qisman to'lov, keyingi oylarni bekor
    /// qilish) oddiy muzlatish bilan AYNAN bir xil — quyida bu bayroq hech qanday shartga kirmaydi.</param>
    /// <param name="lessonFee">Kursning bir dars yaxlit narxi, OLDINDAN hisoblangan bo'lsa (OMMAVIY
    /// amalda — <see cref="TuitionService.LessonFeesForCoursesAsync"/>). ⚠️ FAQAT tezlik uchun:
    /// <c>null</c> bo'lsa AYNAN o'sha qiymat pastda yakka so'rov bilan olinadi. Yakka
    /// <see cref="FreezeMember"/> uni bermaydi va avvalgidek ishlaydi.</param>
    /// <param name="reasonId">Tanlangan sabab (<see cref="ActionReason.Id"/>) — tuzilgan yozuv uchun
    /// (<see cref="MembershipReasons.Record"/>). <c>null</c> — sababsiz (yozuv yo'q).</param>
    private async Task<(bool Already, decimal Restored, string? Error)> FreezeCoreAsync(
        Group cls, StudentGroup sg, string date, string reasonLabel, bool yearFreeze,
        decimal? lessonFee = null, PricingBook? pricing = null, string? reasonId = null)
    {
        if (sg.Status == "frozen") return (true, 0m, null);
        if (sg.Status != "active" && sg.Status != "trial")
            return (false, 0m, $"A'zolik holatini o'zgartirib bo'lmadi (hozirgi holat: {sg.Status})");

        var activatedAt = sg.ActivatedAt;
        sg.Status = "frozen";
        sg.FrozenAt = date;
        // ⚠️ YANGI STATUS EMAS — muzlatishning ikkinchi USULI. Bayroq faqat o'quvchilar ro'yxatida
        // ajratib sanash uchun ("yangi yilga nechta o'quvchi bilan o'tyapmiz").
        sg.YearFreeze = yearFreeze;

        var s = await db.Students.FindAsync(sg.StudentId);
        var restored = 0m;
        if (s is not null)
        {
            // Muzlatish OYINING qisman to'lovi (shu sanagacha qatnashgan darslar) + ORQAGA SANALGAN
            // muzlatishda keyingi oylar hisobini bekor qilish — hammasi YAGONA manbada
            // (guruhni yopish / tugatish / guruh almashtirish bilan aynan bir xil).
            restored = (await MembershipBilling.SettleFreezeAsync(
                db, s, cls, activatedAt, date, lessonFee, sg, pricing)).Restored;
        }

        audit.Record("Membership", $"{cls.Id}:{sg.StudentId}", "update",
            (yearFreeze
                ? $"Aktiv muzlatildi (yangi o'quv yili) ({date}, guruh: {cls.Name})"
                : $"Muzlatildi ({date}, guruh: {cls.Name})")
                + (restored > 0 ? $" — keyingi oylar hisobi bekor qilindi: {AuditService.Money(restored)} so'm" : "")
                + (reasonLabel.Length > 0 ? $" — sabab: {reasonLabel}" : ""),
            studentId: sg.StudentId);
        // Tuzilgan sabab — muzlatilgan o'quvchi keyinroq SABABSIZ chiqarilsa, «Ketish sabablari»
        // hisoboti haqiqiy sababni shu yerdan oladi (MembershipReasons.Resolve, 2-qadam).
        MembershipReasons.Record(db, sg.StudentId, cls.Id, MembershipReasons.ActionFreeze, date, reasonId, reasonLabel);

        return (false, restored, null);
    }

    /* ---------- OMMAVIY (bir paytda ko'p o'quvchi) muzlatish / aktivlashtirish ---------- */

    /// <summary>Bir so'rovda ko'rib chiqiladigan A'ZOLIKLAR chegarasi — qoida
    /// <see cref="MembershipBulk"/> da (sof funksiya, testlangan).</summary>
    private const int MaxBulkMembers = MembershipBulk.MaxTargets;

    /// <summary>Javobga tushadigan xato xabarlari chegarasi — qolganlari faqat logda.</summary>
    private const int MaxBulkErrors = 20;

    /// <summary>GURUH ichida tanlangan o'quvchilarni BIR PAYTDA muzlatish.</summary>
    [HttpPost("{id}/members/bulk-freeze")]
    public Task<ActionResult<BulkMembershipResultDto>> BulkFreezeMembers(string id, BulkMembershipRequest req)
        => BulkApplyAsync(id, req, freeze: true);

    /// <summary>GURUH ichida tanlangan o'quvchilarni BIR PAYTDA aktivlashtirish.</summary>
    [HttpPost("{id}/members/bulk-activate")]
    public Task<ActionResult<BulkMembershipResultDto>> BulkActivateMembers(string id, BulkMembershipRequest req)
        => BulkApplyAsync(id, req, freeze: false);

    /// <summary>O'QUVCHILAR RO'YXATIDAN: tanlanganlarning BARCHA guruhlardagi faol a'zoliklarini muzlatish.</summary>
    [HttpPost("members/bulk-freeze")]
    public Task<ActionResult<BulkMembershipResultDto>> BulkFreezeAllGroups(BulkMembershipRequest req)
        => BulkApplyAsync(null, req, freeze: true);

    /// <summary>O'QUVCHILAR RO'YXATIDAN: tanlanganlarning BARCHA guruhlardagi faol a'zoliklarini aktivlashtirish.</summary>
    [HttpPost("members/bulk-activate")]
    public Task<ActionResult<BulkMembershipResultDto>> BulkActivateAllGroups(BulkMembershipRequest req)
        => BulkApplyAsync(null, req, freeze: false);

    /// <summary>
    /// OMMAVIY muzlatish/aktivlashtirish. Hisob-kitob yakka amallar bilan AYNAN bir xil —
    /// <see cref="FreezeCoreAsync"/> / <see cref="ActivateCoreAsync"/> chaqiriladi, ya'ni
    /// qisman oy to'lovi, orqaga sanalgan hisoblar, avans ko'chishi va AUDIT yozuvi
    /// (har o'quvchiga ALOHIDA qator) o'zgarishsiz qoladi.
    /// </summary>
    /// <param name="groupId">Guruh (guruh sahifasidan) yoki <c>null</c> — o'quvchining BARCHA faol a'zoliklari
    /// (o'quvchilar ro'yxatidan).</param>
    /// <remarks>
    /// ⚠️ Har a'zolik ALOHIDA saqlanadi (o'z <c>SaveChanges</c>i bilan): bittasi xato bersa qolganlari
    /// baribir bajariladi — 100 ta tanlangandan bittasi tufayli hech kim muzlamay qolmasin
    /// (`contacts` bulk qoidasi bilan bir xil mantiq). Xato bergan a'zolikning yarim o'zgarishlari
    /// <c>ChangeTracker.Clear()</c> bilan bekor qilinadi, aks holda ular KEYINGI o'quvchining
    /// saqlashi bilan birga bazaga tushib ketardi.
    /// </remarks>
    private async Task<ActionResult<BulkMembershipResultDto>> BulkApplyAsync(
        string? groupId, BulkMembershipRequest req, bool freeze)
    {
        // «Aktiv muzlatish» — FAQAT superadmin va FAQAT muzlatish tarmog'ida. Aktivlashtirishda
        // bayroq ma'nosiz (u belgini TOZALAYDI), shuning uchun u yerda tekshiruv ham kerak emas.
        var yearFreeze = freeze && req.YearFreeze == true;
        if (yearFreeze && !MaySetYearFreeze)
            return StatusCode(403, new { message = "«Aktiv muzlatish» faqat superadmin uchun" });

        var ids = (req.StudentIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .ToList();
        if (ids.Count == 0) return BadRequest(new { message = "O'quvchi tanlanmagan" });
        if (ids.Count > MaxBulkMembers)
            return BadRequest(new { message = $"Bir vaqtda ko'pi bilan {MaxBulkMembers} ta o'quvchi tanlash mumkin" });

        var date = string.IsNullOrWhiteSpace(req.Date) ? AppClock.Today.ToString("yyyy-MM-dd") : req.Date!.Trim();
        if (date.Length < 10 || !DateOnly.TryParse(date, out _))
            return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });

        if (groupId is not null && await db.Classes.FindAsync(groupId) is null)
            return NotFound(new { message = "Guruh topilmadi" });

        // Tanlanganlarning FAOL a'zoliklari — faqat KALITLAR (tracking'siz): har bir a'zolik sikl
        // ichida QAYTA o'qiladi, chunki xato bo'lganda `ChangeTracker.Clear()` oldindan yuklangan
        // obyektlarni uzib qo'yardi.
        var q = db.StudentGroups.AsNoTracking().Where(x => x.IsActive && ids.Contains(x.StudentId));
        if (groupId is not null) q = q.Where(x => x.GroupId == groupId);
        var all = await q
            .OrderBy(x => x.GroupId).ThenBy(x => x.StudentId)
            .Select(x => new { x.GroupId, x.StudentId, x.Status })
            .ToListAsync();
        if (all.Count > MaxBulkMembers)
            return BadRequest(new { message = $"Bir vaqtda ko'pi bilan {MaxBulkMembers} ta a'zolik o'zgartiriladi (topildi: {all.Count})" });

        // ⚠️ Holat filtri SQL'da EMAS, `MembershipBulk.IsEligible` da (sof funksiya, testlangan):
        // qoida bir joyda tursin. Yon foydasi — allaqachon kerakli holatdagilar YO'QOLMAYDI,
        // ular javobda "o'tkazib yuborildi" bo'lib sanaladi ("nega bu odam o'zgarmadi?" savoli
        // "a'zoligi yo'q" bilan ARALASHMASIN).
        var keys = all.Where(x => MembershipBulk.IsEligible(x.Status, freeze)).ToList();
        var skippedNotEligible = all.Count - keys.Count;

        var studentRows = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.IsArchived })
            .ToListAsync();
        var names = studentRows.ToDictionary(x => x.Id, x => x.FullName);
        var archivedStudents = studentRows.Where(x => x.IsArchived).Select(x => x.Id).ToHashSet();

        var reasonLabel = await ReasonLabelAsync(req.ReasonId);

        // GURUHLAR OLDINDAN — BITTA so'rov. Ilgari halqa ichida har a'zolik uchun
        // `db.Classes.FindAsync(key.GroupId)` chaqirilardi va `ChangeTracker.Clear()` har iteratsiyada
        // EF keshini tozalagani uchun bu HAQIQATAN bazaga borardi: `keys` GroupId bo'yicha saralangan,
        // ya'ni guruh sahifasidan kelgan oqimda AYNAN BIR XIL qator 500 marta o'qilardi.
        //
        // ⚠️ NEGA `AsNoTracking`: bu yo'lda `Group` obyekti HECH QAYERDA o'zgartirilmaydi — na
        // `FreezeCoreAsync`/`ActivateCoreAsync` da, na ular chaqiradigan `MembershipBilling`,
        // `TuitionService`, `RetentionBonusService`, `AuditService` da (faqat Id/Name/MonthlyFee/
        // Days/CourseId O'QILADI; yoziladigan narsalar — StudentGroup, Student.Balance, MonthlyCharge,
        // FinanceTransaction). Kuzatilmagan obyekt esa har iteratsiyadagi `ChangeTracker.Clear()` dan
        // MUSTAQIL — u lug'atda tirik qoladi va uzilib qolmaydi.
        //
        // ⚠️ Kelishilgan murosa: guruh endi halqa BOSHIDAGI holatida (bir marta o'qilgan). Amal
        // davomida parallel o'chirilgan guruh "topilmadi" bo'lib sanalmaydi — lekin a'zoliklar
        // ro'yxati (`all`) ham allaqachon shunday suratdan olingan, ya'ni oqim avvaldan shu
        // konvensiyada. A'zolikning O'ZI esa avvalgidek har iteratsiyada QAYTA o'qiladi.
        var groupIds = keys.Select(k => k.GroupId).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking()
            .Where(c => groupIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        // KURSNING BIR DARS NARXI (LessonPrice) — bir marta, DISTINCT kurslar uchun. Ilgari uni
        // `ChargeFreezeProrateAsync` va `ChargeActivationProrateAsync` har a'zolik uchun qaytadan
        // so'rardi, holbuki ommaviy amalda guruh (demak kurs) odatda bitta — bir xil qiymat 500
        // marta o'qilardi. Qiymat halqada `lessonFee` sifatida pastga uzatiladi (IKKALA tarmoqda
        // ham); hisob-kitob formulasi O'ZGARMAYDI — parametr berilmasa har ikki metod aynan shu
        // qiymatni o'zi so'raydi.
        var lessonFees = await TuitionService.LessonFeesForCoursesAsync(db, groups.Values.Select(g => g.CourseId));
        decimal FeeFor(string? courseId) =>
            !string.IsNullOrEmpty(courseId) && lessonFees.TryGetValue(courseId, out var f) ? f : 0m;
        // Kurslarning sinf oraliqlari — xuddi shu sabab bilan BIR MARTA (`TuitionPricing`).
        var pricing = await PricingBook.LoadAsync(db);

        var changed = 0;
        var skipped = skippedNotEligible;
        // Aktivlashtirish TAQIQLANGAN a'zoliklar (arxivdagi guruh / arxivdagi o'quvchi) — "allaqachon
        // shu holatda" (`skipped`) bilan ARALASHMASIN: sababi alohida sanaladi va nomma-nom qaytadi.
        var skippedArchived = 0;
        var skipNotes = new List<string>();
        var failed = 0;
        var restored = 0m;
        var movedAdvance = 0m;
        var catchUpMonths = 0;
        var errors = new List<string>();
        var touched = new HashSet<string>();

        // KESH BUMP'INI BIR MARTAGA YIG'ISH. Har a'zolik o'z `SaveChanges`i bilan saqlanadi, ya'ni
        // `CacheInvalidationInterceptor` har iteratsiyada `DataCache.Bump` chaqiradi — 500 a'zolikda
        // ~1500 versiya oshirilishi. Natijada bosh sahifa, reyting, ball, kurslar analitikasi va
        // baholash keshlari butun jarayon davomida qayta-qayta eskirib, bir necha marta boshqatdan
        // hisoblanadi (hisoblangani esa keyingi iteratsiyada darhol yana eskiradi). To'plash rejimida
        // turlar yig'iladi va blok tugaganda BIR MARTA qo'llanadi.
        //
        // ⚠️ Kelishilgan murosa: to'plash davom etayotgan bir necha soniya ichida parallel
        // foydalanuvchilar keshning ESKI qiymatini ko'radi — bu ATAYIN va hozirgidan yaxshiroq.
        // ⚠️ `using` BLOK javob qaytarilishidan OLDIN yopiladi: aks holda klient darhol yangi
        // ma'lumot so'raganda kesh hali eski bo'lardi.
        using (dataCache.BeginBatch())
        {
            foreach (var key in keys)
            {
                var who = names.TryGetValue(key.StudentId, out var n) ? n : key.StudentId;
                try
                {
                    groups.TryGetValue(key.GroupId, out var cls);
                    var sg = await db.StudentGroups
                        .FirstOrDefaultAsync(x => x.GroupId == key.GroupId && x.StudentId == key.StudentId && x.IsActive);
                    // Oradan o'chib ketgan bo'lsa (parallel amal) — jim o'tkazamiz.
                    if (cls is null || sg is null) { skipped++; continue; }

                    if (freeze)
                    {
                        var r = await FreezeCoreAsync(cls, sg, date, reasonLabel, yearFreeze, FeeFor(cls.CourseId), pricing, req.ReasonId);
                        if (r.Error is not null)
                        {
                            failed++;
                            if (errors.Count < MaxBulkErrors) errors.Add($"{who} ({cls.Name}): {r.Error}");
                            continue;
                        }
                        if (r.Already) { skipped++; continue; }
                        restored += r.Restored;
                    }
                    else
                    {
                        // Sentabrdagi ommaviy aktivlashtirish ESKI yopilgan guruhdagi muzlatilgan
                        // a'zolikni ham uyg'otib, ikki guruhdan oylik yozardi — yakka amaldagi bilan bir qoida.
                        if (MembershipBulk.ActivationBlockReason(cls.IsArchived, archivedStudents.Contains(key.StudentId)) is { } block)
                        {
                            skippedArchived++;
                            if (skipNotes.Count < MaxBulkErrors) skipNotes.Add($"{who} ({cls.Name}): {block}");
                            continue;
                        }
                        var r = await ActivateCoreAsync(cls, sg, date, req.RetentionBonus, FeeFor(cls.CourseId), pricing);
                        if (r.Already) { skipped++; continue; }
                        movedAdvance += r.MovedAdvance;
                        catchUpMonths += r.CatchUpMonths;
                    }

                    await db.SaveChangesAsync();
                    changed++;
                    touched.Add(key.StudentId);
                }
                catch (Exception ex)
                {
                    failed++;
                    if (errors.Count < MaxBulkErrors) errors.Add($"{who}: {ex.Message}");
                    logger.LogError(ex, "Bulk {Action} error: group={GroupId}, student={StudentId}: {Message}",
                        freeze ? "freeze" : "activate", key.GroupId, key.StudentId, ex.Message);
                }
                finally
                {
                    // Saqlangandan keyin ham tozalanadi: har a'zolik MUSTAQIL bo'lsin (bittasining
                    // yarim o'zgarishi keyingisining saqlashiga qo'shilib ketmasin) va 500 ta
                    // kuzatilayotgan obyekt har saqlashda qayta tekshirilmasin.
                    db.ChangeTracker.Clear();
                }
            }
        }

        // "Faol a'zoligi umuman yo'q" — allaqachon kerakli holatda bo'lganlar bunga KIRMAYDI
        // (ular `skipped` da), aks holda ikki butunlay boshqa sabab bitta songa qo'shilib ketardi.
        var noMembership = ids.Count - all.Select(k => k.StudentId).Distinct().Count();

        logger.LogInformation(
            "BulkMembership: action={Action} group={GroupId} date={Date} requested={Requested} memberships={Memberships} changed={Changed} skipped={Skipped} failed={Failed}",
            freeze ? "freeze" : "activate", groupId ?? "*", date, ids.Count, all.Count, changed, skipped, failed);

        return Ok(new BulkMembershipResultDto(
            Requested: ids.Count,
            Memberships: all.Count,
            Changed: changed,
            Students: touched.Count,
            Skipped: skipped,
            Failed: failed,
            NoMembership: noMembership,
            Restored: restored,
            MovedAdvance: movedAdvance,
            CatchUpMonths: catchUpMonths,
            Errors: errors.ToArray(),
            SkippedArchived: skippedArchived,
            SkipNotes: skipNotes.ToArray()));
    }

    /// <summary>
    /// O'quvchini BOSHQA GURUHGA O'TKAZISH: joriy guruh (<paramref name="id"/>) a'zoligi
    /// <c>FreezeDate</c>dan YOPILADI — hisob muzlatishdagi kabi (shu sanagacha qatnashgan darslar uchun
    /// qisman to'lov — <see cref="TuitionService.ChargeFreezeProrateAsync"/>), a'zolik esa CHIQADI
    /// (<c>IsActive=false</c>, <c>LeftAt</c>; muzlatilgan bo'lib qolmaydi),
    /// maqsad guruhda (<c>ToGroupId</c>) a'zolik yaratiladi/tiklanadi va <c>ActivateDate</c>dan DARHOL
    /// AKTIVLASHTIRILADI (oddiy "Aktivlashtirish" bilan bir xil <see cref="TuitionService.ChargeActivationProrateAsync"/>
    /// mantig'i — qisman oy to'lovi hisoblanadi). Yangi billing hisob-kitob YO'Q — ikkala tomon ham
    /// mavjud freeze/activate primitivlaridan foydalanadi. <see cref="Student.ClassName"/> eski guruh
    /// nomiga teng bo'lsa — yangi guruh nomiga ko'chiriladi (eski ko'rinishlar: chat, xabar tokenlari,
    /// hisobotlar shu bilan yangi guruhga ergashadi).
    /// </summary>
    [HttpPost("{id}/members/{studentId}/transfer")]
    public async Task<IActionResult> TransferMember(string id, string studentId, TransferMemberRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ToGroupId))
            return BadRequest(new { message = "Maqsad guruh tanlanmagan" });
        if (req.ToGroupId == id)
            return BadRequest(new { message = "Maqsad guruh joriy guruh bilan bir xil bo'lishi mumkin emas" });

        var fromGroup = await db.Classes.FindAsync(id);
        if (fromGroup is null) return NotFound(new { message = "Joriy guruh topilmadi" });
        var toGroup = await db.Classes.FindAsync(req.ToGroupId);
        if (toGroup is null) return NotFound(new { message = "Maqsad guruh topilmadi" });
        // Arxivdagi (yopilgan) guruhga o'tkazish = yopiq guruhda aktivlashtirish (oylik yoziladi, dars yo'q).
        if (toGroup.IsArchived)
            return BadRequest(new { message = "Maqsad guruh arxivda — unga o'tkazib bo'lmaydi" });

        var fromSg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == id && x.StudentId == studentId && x.IsActive);
        if (fromSg is null) return NotFound(new { message = "Faol a'zolik topilmadi" });

        var s = await db.Students.FindAsync(studentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        // ARXIVDAGI o'quvchi (eski arxivning tirik a'zoligi) — yangi guruhda AKTIVLASHTIRILMAYDI:
        // aktivlashtirish/qo'shish bilan bir xil qoida (`MembershipBulk.ActivationBlockReason`). Aks holda
        // ro'yxatlarda ko'rinmaydigan odamga qisman oy + orqaga oylar yozilardi (audit R1-9).
        if (MembershipBulk.ActivationBlockReason(toGroup.IsArchived, s.IsArchived) == MembershipBulk.StudentArchivedReason)
            return BadRequest(new { message = "O'quvchi arxivda — avval o'quvchini arxivdan qaytaring, keyin guruhga qo'shing." });

        if (toGroup.Capacity > 0)
        {
            // Sig'im — faqat o'rin band qilganlar (active + trial); muzlatilganlar va arxivdagi
            // o'quvchilar SANALMAYDI (MembershipLifecycle.SeatHolders).
            var enrolled = await MembershipLifecycle
                .SeatHolders(db.StudentGroups.Where(x => x.GroupId == req.ToGroupId), db.Students)
                .CountAsync();
            if (enrolled >= toGroup.Capacity)
                return BadRequest(new { message = $"Maqsad guruh to'lgan ({toGroup.Capacity} o'rin)" });
        }

        var toSg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.GroupId == req.ToGroupId && x.StudentId == studentId);
        if (toSg is { IsActive: true })
            return BadRequest(new
            {
                message = toSg.Status == "frozen"
                    ? "O'quvchi maqsad guruhda muzlatilgan — o'tkazish o'rniga o'sha guruhda «Aktivlashtirish» qiling"
                    : "O'quvchi allaqachon maqsad guruhda",
            });

        var freezeDate = string.IsNullOrWhiteSpace(req.FreezeDate)
            ? AppClock.Today.ToString("yyyy-MM-dd") : req.FreezeDate!.Trim();
        var activateDate = string.IsNullOrWhiteSpace(req.ActivateDate) ? freezeDate : req.ActivateDate!.Trim();
        // Sanalar tekshiriladi (`CompleteAndTransfer` dagi kabi): aktivlashtirish chiqishdan OLDIN bo'lsa
        // ustma-ust kunlar IKKALA guruhda ham hisoblanardi; buzuq sana esa hisobni jimgina o'tkazib yuborardi.
        if (!DateOnly.TryParse(freezeDate, out var fz) || !DateOnly.TryParse(activateDate, out var ac))
            return BadRequest(new { message = "Sana noto'g'ri" });
        if (ac < fz)
            return BadRequest(new { message = "Yangi guruhda aktivlashtirish sanasi eski guruhdan chiqish sanasidan oldin bo'lmaydi" });
        // Chiqish — guruhdan chiqarishdagi (`RemoveMember`) bilan bir xil: kelajak sana bo'lmaydi
        // (ertangi chiqish bugun yozilsa, oradagi darslar ikkala guruhda ham yo'qolardi).
        if (fz > AppClock.Today)
            return BadRequest(new { message = "Eski guruhdan chiqish sanasi kelajakda bo'lishi mumkin emas" });
        freezeDate = fz.ToString("yyyy-MM-dd");
        activateDate = ac.ToString("yyyy-MM-dd");

        // Eski guruhda bekor qilingan oylar — (4) avans ko'chirishga uzatiladi (pastdagi izohga qarang).
        var purgedMonths = new List<string>();

        // 1) Eski guruh — HISOB muzlatishdagi kabi yopiladi (allaqachon muzlatilgan bo'lsa qisman
        //    to'lov qayta hisoblanmaydi), lekin a'zolik MUZLATILGAN bo'lib QOLMAYDI — o'quvchi eski
        //    guruhdan CHIQADI (`IsActive=false`, `LeftAt`, guruhda «Guruhdan chiqarilganlar» tarixida).
        //    ⚠️ Ilgari a'zolik `frozen` + `IsActive=true` bo'lib qolardi: o'quvchi ikki guruhda birdan
        //    ko'rinar (eski guruhda «Muzlatilgan»), "duplikat" bo'lib tuyulardi (foydalanuvchi, 2026-10-06).
        //    Holat (`Status`) ATAYIN o'zgartirilmaydi — chiqarish (`RemoveMember`) bilan bir xil
        //    konvensiya: chegara `LeftAt` (billing `NotLeftBeforeMonth`, ledger chiqish oyida to'xtaydi).
        //    Hisob — YAGONA manbada (qisman to'lov + keyingi oylarni bekor qilish), chiqarish bilan bir xil.
        //    DIQQAT: bu (4) AVANS KO'CHIRISHDAN OLDIN bajarilishi SHART — hisob bekor qilingach
        //    o'sha oylarga to'langan pul "ortiqcha" bo'lib qoladi va yangi guruhga ko'chadi. Aks
        //    holda o'quvchi eski guruhda ham hisoblanib, yangi guruhda ham qarzdor bo'lib ko'rinardi.
        purgedMonths = (await MembershipBilling.LeaveGroupAsync(db, s, fromGroup, fromSg, freezeDate)).PurgedMonths;

        // 2) Maqsad guruh — a'zolik yaratish yoki tiklash (AddMember bilan bir xil mantiq).
        if (toSg is not null)
        {
            // ⚠️ ENG BIRINCHI QATOR: maqsad guruhda o'quvchi ILGARI ham o'qigan bo'lishi mumkin
            // (qaytib kelgan). Uning oldingi faol davri LeftAt/ActivatedAt tozalanishidan OLDIN
            // tarixga ko'chirilishi SHART — aks holda tugash sanasi yo'qolib, davr bugungi sanagacha
            // cho'zilib ketardi va o'quvchi guruhda umuman bo'lmagan oylarga qarz yozilardi.
            MembershipLifecycle.ClosePeriod(toSg, activateDate);
            toSg.IsActive = true;
            toSg.LeftAt = null;
            toSg.JoinedAt = activateDate;
        }
        else
        {
            toSg = new StudentGroup
            {
                StudentId = studentId, GroupId = req.ToGroupId, JoinedAt = activateDate, IsActive = true,
                Status = "trial",
            };
            db.StudentGroups.Add(toSg);
        }

        // 3) Maqsad guruh — DARHOL AKTIVLASHTIRISH. (Oldingi davr yuqorida, LeftAt tozalanishidan
        // OLDIN yopilgan — bu yerda kech bo'lardi.)
        toSg.Status = "active";
        toSg.ActivatedAt = activateDate;
        toSg.FrozenAt = string.Empty;
        // FrozenAt tozalangan HAR joyda «aktiv muzlatish» belgisi ham tozalanadi: maqsad guruhda
        // eski (o'tgan yilgi) muzlatish qolgan bo'lsa, u faol a'zolikda yolg'on ko'rinib qolardi.
        toSg.YearFreeze = false;
        // RecordedAt — HAQIQIY bugungi sana (activateDate ORQAGA sanalgan bo'lishi mumkin).
        // Jurnalda MemberStart bilan RecordedAt orasidagi, allaqachon davomati olingan darslar
        // avto-"keldi" ✓ bo'lib to'lib qolmasin (AddMember/ActivateMember bilan bir xil qoida).
        toSg.RecordedAt = AppClock.Today.ToString("yyyy-MM-dd");
        // CHEGIRMA: eski guruhga qaratilgan amaldagi chegirma (bir xil fan bo'lsa) yangi guruhga
        // ko'chadi — eski guruh chiqish hisobi (1) da allaqachon yozilgan, yangi guruhniki pastda shu
        // chegirma bilan yoziladi (DiscountTransfer — tartib izohi o'sha yerda).
        var discountMove = await DiscountTransfer.MoveGroupScopedAsync(db, audit, s, fromGroup, toGroup, activateDate);

        // O'quvchi shu OYDA chiqqan guruhiga qaytsa (masalan xato o'tkazishni tuzatish) — o'sha oydagi
        // oldingi segment saqlanib, yangisi USTIGA qo'shiladi (ActivateCoreAsync bilan bir xil qoida).
        await TuitionService.ChargeActivationProrateAsync(db, s, toGroup, activateDate,
            addSegment: MembershipLifecycle.ClosedPeriodEndsInMonth(toSg, activateDate));
        // ORQAGA SANALGAN aktivlashtirish: oraliq oylar DARHOL to'liq oylik bilan (yakka
        // «Aktivlashtirish» dagi kabi) — aks holda 12 soatlik fon xizmatini kutib qarz kam ko'rinardi.
        var catchUpMonths = await TuitionService.AccrueCatchUpAsync(db, s, toGroup, activateDate, toSg);

        // 4) AVANSNI KO'CHIRISH: o'quvchi shu oy uchun ESKI guruhga allaqachon to'lagan bo'lsa, muzlatish
        //    qisman hisobidan ORTIB QOLGAN summa yangi guruhga qayta teglanadi. Aks holda to'lagan o'quvchi
        //    yangi guruhda "qarzdor" (qizil) bo'lib ko'rinardi, puli esa eski guruhda avans bo'lib qolardi.
        //    DIQQAT: (1) da bekor qilingan oylar `purgedMonths` bilan uzatiladi — ular hali bazaga
        //    yozilmagani uchun EF so'rovda baribir qaytaradi va aks holda "hisoblangan" bo'lib
        //    ko'rinib, o'sha oylarga to'langan pul eski guruhda qolib ketardi.
        var movedAdvance = freezeDate.Length >= 7
            ? await TuitionService.CarryGroupAdvanceAsync(
                db, s, fromGroup, toGroup, freezeDate[..7], purgedMonths)
            : 0m;

        // Eski (single-class) ko'rinishlar uchun asosiy guruh nomini ko'chiramiz.
        if (s.ClassName == fromGroup.Name) s.ClassName = toGroup.Name;

        var reason = await ReasonLabelAsync(req.ReasonId);
        // Tuzilgan sabab (eski guruh tomoni). Kurs ichidagi almashtirish ketish EMAS, lekin boshqa
        // KURSGA o'tish kurs kesimida ketish bo'lib sanaladi — sababi shu yerdan olinadi.
        MembershipReasons.Record(db, studentId, id, MembershipReasons.ActionTransfer, freezeDate, req.ReasonId, reason);
        audit.Record("Membership", $"{id}:{studentId}", "update",
            $"Guruh almashtirildi: {fromGroup.Name} → {toGroup.Name} (eski guruhdan chiqdi {freezeDate}, aktivlashtirish {activateDate})"
                + (movedAdvance > 0 ? $" — {AuditService.Money(movedAdvance)} so'm to'lov yangi guruhga ko'chirildi" : "")
                + (catchUpMonths > 0 ? $" — {catchUpMonths} oy uchun oylik yozildi (orqaga sanalgan)" : "")
                + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            studentId: studentId);

        await db.SaveChangesAsync();
        return Ok(new { ok = true, movedAdvance, catchUpMonths, discountMoved = discountMove.Moved });
    }

    /// <summary>
    /// UZAYTIRISH HODISASINI QO'LDA yozish — o'quvchi <c>{id}</c> guruhida kursning bir bosqichini
    /// TUGATIB, <c>ToGroupId</c> guruhida keyingisini boshladi.
    ///
    /// <para>Guruhni "Tugatish (sertifikat bilan)" yo'li bu hodisani O'ZI yozadi; bu endpoint esa
    /// tizimdan TASHQARIDA bo'lgan (yoki o'tmishdagi) o'tishlar uchun. Ikkalasi ham AYNAN bitta
    /// jadvalga yozadi — chiquvchi adminning "Bonus B" manbai bo'linib ketmasin.</para>
    ///
    /// <para>⚠️ TAKROR HIMOYASI: ayni <c>(o'quvchi, eski guruh, yangi guruh, sana)</c> uchun ikkinchi
    /// yozuv OCHILMAYDI (400). Aks holda tugmani ikki marta bosish bonusni ikki marta yozardi.</para>
    ///
    /// <para>⚠️ Guruh a'zoligi TEKSHIRILMAYDI va O'ZGARTIRILMAYDI: bu faqat HODISA qaydi
    /// (a'zolikni ko'chirish — alohida amal, <c>transfer</c>). Ya'ni orqaga sanalgan, allaqachon
    /// yopilgan guruh uchun ham yozish mumkin.</para>
    /// </summary>
    [HttpPost("{id}/members/{studentId}/extension")]
    public async Task<IActionResult> AddExtension(string id, string studentId, MemberExtensionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ToGroupId))
            return BadRequest(new { message = "Yangi guruh tanlanmagan" });
        if (req.ToGroupId == id)
            return BadRequest(new { message = "Yangi guruh joriy guruh bilan bir xil bo'lishi mumkin emas" });

        var fromGroup = await db.Classes.FindAsync(id);
        if (fromGroup is null) return NotFound(new { message = "Joriy guruh topilmadi" });
        var toGroup = await db.Classes.FindAsync(req.ToGroupId);
        if (toGroup is null) return NotFound(new { message = "Yangi guruh topilmadi" });
        var student = await db.Students.FindAsync(studentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var date = string.IsNullOrWhiteSpace(req.Date)
            ? AppClock.Today.ToString("yyyy-MM-dd") : req.Date!.Trim();
        if (date.Length < 10 || !DateOnly.TryParse(date, out _))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        var duplicate = await db.StudentExtensions.AnyAsync(x =>
            x.StudentId == studentId && x.FromGroupId == id && x.ToGroupId == req.ToGroupId && x.Date == date);
        if (duplicate)
            return BadRequest(new { message = "Bu uzaytirish allaqachon yozilgan (o'sha o'quvchi, guruhlar va sana)" });

        // Nomlar SNAPSHOT (`ContactRequest` naqshi): guruh arxivlansa/nomi o'zgarsa ham hisobot buzilmasin.
        db.StudentExtensions.Add(new StudentExtension
        {
            StudentId = studentId,
            StudentName = student.FullName,
            FromGroupId = id,
            FromGroupName = fromGroup.Name,
            ToGroupId = toGroup.Id,
            ToGroupName = toGroup.Name,
            // Kurs — YANGI guruhning kursi (o'quvchi endi o'sha kursda o'qiydi).
            CourseId = toGroup.CourseId ?? "",
            Date = date,
            ByUserId = ActorId,
            ByUserName = Actor,
            Note = string.IsNullOrWhiteSpace(req.Note) ? "Qo'lda belgilandi" : req.Note!.Trim(),
            CreatedAt = AppClock.Iso(),
        });

        // `EntityId` — `Membership` naqshida "{groupId}:{studentId}", ya'ni yozuv guruh sahifasining
        // "Tarix" tabida ham, o'quvchi tarixida ham ko'rinadi (`.claude/rules/audit.md` §2).
        audit.Record("StudentExtension", $"{id}:{studentId}", "create",
            $"Uzaytirish belgilandi: {student.FullName} — «{fromGroup.Name}» → «{toGroup.Name}», sana {date}"
            + (string.IsNullOrWhiteSpace(req.Note) ? "" : $" — izoh: {req.Note!.Trim()}"),
            studentId: studentId);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>
    /// O'quvchining SHU GURUHDAN yozilgan uzaytirish hodisalari (qo'lda ham, «Tugatish» yozgani ham) —
    /// «Uzaytirish qaydi» oynasida ro'yxat va xatoni bekor qilish uchun. Eng yangisi tepada.
    /// </summary>
    [HttpGet("{id}/members/{studentId}/extensions")]
    public async Task<ActionResult<IEnumerable<StudentExtensionDto>>> Extensions(string id, string studentId) =>
        await db.StudentExtensions.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.FromGroupId == id)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt)
            .Select(x => new StudentExtensionDto(x.Id, x.FromGroupName, x.ToGroupId, x.ToGroupName, x.Date,
                x.ByUserName, x.Note ?? "", x.CreatedAt))
            .ToListAsync();

    /// <summary>
    /// XATO yozilgan uzaytirishni BEKOR QILISH — qator o'chiriladi (u faqat hodisa qaydi: a'zolik va
    /// pulga tegmaydi), KPI «Bonus B» sanog'i o'z-o'zidan kamayadi. Ilgari yozilgan uzaytirishni
    /// qaytarishning yo'li yo'q edi.
    /// <para>Ruxsat — sinf darvozasi (<c>classes.list</c>, DELETE → <c>delete</c> amali). Audit —
    /// yaratishdagi bilan bir xil tur va <c>EntityId</c> (guruh/o'quvchi tarixida ko'rinadi).</para>
    /// </summary>
    [HttpDelete("{id}/members/{studentId}/extensions/{extensionId}")]
    public async Task<IActionResult> DeleteExtension(string id, string studentId, string extensionId)
    {
        var ext = await db.StudentExtensions
            .FirstOrDefaultAsync(x => x.Id == extensionId && x.StudentId == studentId && x.FromGroupId == id);
        if (ext is null) return NotFound(new { message = "Uzaytirish qaydi topilmadi (allaqachon o'chirilgan bo'lishi mumkin)" });

        db.StudentExtensions.Remove(ext);
        audit.Record("StudentExtension", $"{id}:{studentId}", "delete",
            $"Uzaytirish bekor qilindi: {ext.StudentName} — «{ext.FromGroupName}» → «{ext.ToGroupName}», sana {ext.Date}" +
            (string.IsNullOrWhiteSpace(ext.ByUserName) ? "" : $" (yozgan: {ext.ByUserName})"),
            studentId: studentId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// GURUH AI TAHLILI — deterministik ko'rsatkichlar (AI'siz ham ko'rinadi): a'zolik oqimi
    /// (kelgan/muzlatilgan/ketgan) va ketish sabablari, davomat, jurnal intizomi, o'zlashtirish,
    /// imtihonlar, to'lovlar, dastur qamrovi, o'quvchilar kesimi. Guruh sahifasidagi "AI tahlil" tabi.
    /// </summary>
    [HttpGet("{id}/ai-snapshot")]
    public async Task<ActionResult<GroupAiMetricsDto>> AiSnapshot(string id, CancellationToken ct)
    {
        var g = await db.Classes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (g is null) return NotFound();
        var (metrics, _) = await GroupSnapshotBuilder.BuildAsync(db, g, CanSeeFinance(), ct);
        return metrics;
    }

    /// <summary>Guruhning saqlangan AI tahlillari tarixi (eng yangisi birinchi).</summary>
    [HttpGet("{id}/ai-analyses")]
    public async Task<ActionResult<IEnumerable<GroupAiRecordDto>>> AiAnalyses(string id, CancellationToken ct) =>
        await GroupAiAnalysisService.HistoryAsync(db, id, ct);

    /// <summary>Guruhning BARCHA ma'lumotini Gemini orqali TANQIDIY tahlil qiladi (kuniga bir marta —
    /// bugungi yozuv bo'lsa Gemini chaqirilmaydi, mavjudi qaytadi).</summary>
    [HttpPost("{id}/ai-analysis")]
    public async Task<ActionResult<GroupAiResponseDto>> AiAnalysis(string id, CancellationToken ct)
    {
        var g = await db.Classes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (g is null) return NotFound();
        return await GroupAiAnalysisService.GenerateAsync(db, config, g, CanSeeFinance(), ct);
    }

    /// <summary>Tahlilga TO'LOV raqamlari kirsinmi: admin/superadmin yoki "moliya" ruxsatli xodim
    /// (guruh sahifasidagi "To'lovlar" tabi bilan bir xil qoida — moliya ruxsati yo'q xodim
    /// summalarni AI tahlilida ham ko'rmaydi).</summary>
    private bool CanSeeFinance() =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin) ||
        User.Claims.Any(c => c.Type == AdminPermAttribute.ClaimType
                             && (c.Value == "finance" || c.Value.StartsWith("finance:")));

    /// <summary>O'quvchining barcha guruh a'zoliklari (faol + o'tgan) — kurs/o'qituvchi/holat/narx/jadval bilan (kartalar uchun).</summary>
    [HttpGet("student/{studentId}/groups")]
    public async Task<ActionResult<IEnumerable<StudentGroupDto>>> StudentGroups(string studentId)
    {
        var memberships = await db.StudentGroups.Where(sg => sg.StudentId == studentId).ToListAsync();
        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var classes = (await db.Classes.Where(c => groupIds.Contains(c.Id)).ToListAsync())
            .ToDictionary(c => c.Id);
        var courseIds = classes.Values.Select(c => c.CourseId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var teacherIds = classes.Values.Select(c => c.TeacherId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var courseNames = (await db.Subjects.Where(s => courseIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id, s => s.Name);
        var teacherNames = (await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id, t => t.FullName);
        // Kartadagi va to'lov oynasidagi "oylik" — o'quvchining SINFIGA qarab (`TuitionPricing`).
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId);
        var pricing = await PricingBook.LoadAsync(db);
        var month = TuitionService.CurrentMonth();

        var rows = memberships
            .Where(m => classes.ContainsKey(m.GroupId))
            .Select(m =>
            {
                var c = classes[m.GroupId];
                return new StudentGroupDto(
                    m.Id, c.Id, c.Name, m.JoinedAt, m.LeftAt, m.IsActive,
                    m.Status ?? "trial",
                    string.IsNullOrEmpty(c.CourseId) ? "" : courseNames.GetValueOrDefault(c.CourseId, ""),
                    string.IsNullOrEmpty(c.TeacherId) ? "" : teacherNames.GetValueOrDefault(c.TeacherId, ""),
                    student is null ? c.MonthlyFee : pricing.MonthlyFee(c, student, month),
                    c.Days, c.StartTime, c.EndTime, c.Room ?? "",
                    m.ActivatedAt ?? "", m.FrozenAt ?? "", m.YearFreeze);
            })
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return rows;
    }

    /// <summary>Guruh to'ldirish hisoboti: har guruhda nechta o'quvchi, nechta bo'sh o'rin.</summary>
    [HttpGet("fill")]
    public async Task<ActionResult<IEnumerable<GroupFillRowDto>>> Fill()
    {
        var groups = await db.Classes.Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
        // "Band o'rin" = active + trial (MembershipLifecycle.OccupiesSeat). Muzlatilgan a'zolik
        // o'rin egallamaydi — aks holda hisobot bo'sh guruhni "to'lgan" deb ko'rsatardi.
        // Arxivdagi o'quvchining (eski ma'lumotda qolgan) tirik a'zoligi ham o'rin egallamaydi.
        var counts = (await MembershipLifecycle.SeatHolders(db.StudentGroups, db.Students)
                .GroupBy(sg => sg.GroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.GroupId, x => x.Count);
        // Joriy MUZLATILGAN a'zoliklar — o'rin egallamaydi, lekin Guruhlar ro'yxatidagi
        // "Muzlatilgan o'quvchilar soni" uchun alohida sanaladi.
        var frozen = (await db.StudentGroups
                .Where(sg => sg.IsActive && sg.Status == "frozen"
                             && db.Students.Any(s => s.Id == sg.StudentId && !s.IsArchived))
                .GroupBy(sg => sg.GroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.GroupId, x => x.Count);
        return groups.Select(c =>
        {
            var enrolled = counts.GetValueOrDefault(c.Id, 0);
            var free = c.Capacity > 0 ? Math.Max(0, c.Capacity - enrolled) : 0;
            return new GroupFillRowDto(c.Id, c.Name, c.Grade, c.Capacity, enrolled, free, c.Status,
                frozen.GetValueOrDefault(c.Id, 0));
        }).ToList();
    }

    /// <summary>
    /// Guruhni YAKUNLAB ARXIVLAYDI va YANGI guruh ochadi (Hybrid — "Tugatish (sertifikat bilan)").
    /// <list type="bullet">
    ///   <item><b>FAQAT superadmin</b> (<see cref="MayCompleteGroups"/>) — UI bilan bir xil darvoza.</item>
    ///   <item><b>BITTA TRANZAKSIYA</b>: eski guruhni yopish, yangi guruh, ko'chirish, sertifikatlar va
    ///     audit — yo hammasi, yo hech narsa. Ilgari oraliq <c>SaveChanges</c> lar alohida edi: o'rtada
    ///     xato bo'lsa eski guruh yopilib, yangi guruh ochilmay qolardi.</item>
    ///   <item><b>KURSNI TUGATGANLAR</b> (<see cref="GroupCompletionRules.Completes"/> — faqat
    ///     <c>active</c> a'zolar): hisob <c>CloseDate</c> gacha yopiladi
    ///     (<see cref="MembershipBilling.SettleFreezeAsync"/>), a'zolik <c>Status="completed"</c>,
    ///     <c>IsActive=false</c>, <c>LeftAt=FrozenAt=CloseDate</c>, SERTIFIKAT beriladi.</item>
    ///   <item><b>SINOVDAGI va MUZLATILGAN</b> a'zolar kursni TUGATMAGAN — ular ODDIY CHIQISH bilan
    ///     yopiladi (<see cref="MembershipBilling.LeaveGroupAsync"/>: <c>LeftAt</c>, holat o'zgarmaydi),
    ///     sertifikat OLMAYDI, sabab jurnaliga tizim sababi yoziladi
    ///     (<see cref="MembershipReasons.CompletedNotFinishedLabel"/>). Ilgari ular ham "completed" va
    ///     sertifikat olib, hisobotlarda bitiruvchi bo'lib ko'rinardi.</item>
    ///   <item>Eski guruh IsArchived=true, Status="archived", ArchivedAt=<c>CloseDate</c>; yopish
    ///     kunidan keyingi o'rinbosar sanalari olib tashlanadi.</item>
    ///   <item>YANGI guruh — nomi NOYOB (<see cref="GroupNames"/>), o'qituvchi tarixi ochiladi
    ///     (<see cref="GroupTeacherHistory.AssignAsync"/>, boshlanishi = aktivlashtirish sanasi).</item>
    ///   <item><c>autoEnrollNewGroup</c>=true — yangi guruhga FAQAT kursni tugatganlar ko'chiriladi;
    ///     <c>activateInNewGroup</c>=true bo'lsa <c>ActivateDate</c> dan aktivlashtiriladi (qisman oylik,
    ///     catch-up, eski guruhda ortib qolgan avans va bir xil fandagi chegirma yangi guruhga ko'chadi).</item>
    ///   <item>Ko'chirilMAGAN a'zolarning «asosiy guruh» yorlig'i (<c>Student.ClassName</c>) bo'shatiladi
    ///     (aks holda ular nom orqali YANGI guruh chatiga kirib qolardi).</item>
    ///   <item>Audit: har a'zoga alohida <c>Membership</c> qatori + yig'ma qator — SAQLASHDAN OLDIN.</item>
    /// </list>
    /// </summary>
    [HttpPost("{id}/complete-and-transfer")]
    [Authorize]
    public async Task<ActionResult<CompleteAndTransferResultDto>> CompleteAndTransfer(
        string id, CompleteAndTransferRequest req)
    {
        if (!MayCompleteGroups)
            return StatusCode(403, new { message = "Guruhni tugatish (sertifikat bilan) faqat superadmin uchun" });

        var group = await db.Classes.FindAsync(id);
        if (group is null) return NotFound(new { message = "Guruh topilmadi" });
        if (group.IsArchived) return BadRequest(new { message = "Guruh allaqachon arxivda" });

        // Faol a'zoliklarni olish.
        var activeMembers = await db.StudentGroups
            .Where(sg => sg.GroupId == id && sg.IsActive)
            .ToListAsync();

        if (activeMembers.Count == 0)
            return BadRequest(new { message = "Guruhda faol a'zo yo'q" });

        // YOPISH sanasi — eski guruh hisobi AYNAN shu sanagacha (bo'sh bo'lsa bugun).
        var closeDate = string.IsNullOrWhiteSpace(req.CloseDate)
            ? AppClock.Today.ToString("yyyy-MM-dd") : req.CloseDate!.Trim();
        if (closeDate.Length < 10 || !DateOnly.TryParse(closeDate, out _))
            return BadRequest(new { message = "Yopish sanasi noto'g'ri (YYYY-MM-DD)" });

        // YANGI guruhda aktivlashtirish sanasi (bo'sh bo'lsa — yopish sanasi).
        var activateDate = string.IsNullOrWhiteSpace(req.ActivateDate)
            ? closeDate : req.ActivateDate!.Trim();
        if (activateDate.Length < 10 || !DateOnly.TryParse(activateDate, out _))
            return BadRequest(new { message = "Aktivlashtirish sanasi noto'g'ri (YYYY-MM-DD)" });
        if (string.CompareOrdinal(activateDate, closeDate) < 0)
            return BadRequest(new { message = "Aktivlashtirish sanasi yopish sanasidan oldin bo'lmasligi kerak" });

        // Sertifikat eski kurs uchun beriladi.
        var oldCourseId = group.CourseId ?? "";

        // Yangi guruh kursi: TargetCourseId ko'rsatilsa shu, aks holda eski kurs.
        var targetCourseId = !string.IsNullOrWhiteSpace(req.TargetCourseId)
            ? req.TargetCourseId!.Trim()
            : oldCourseId;

        // Maqsad kursni tekshiramiz (agar ko'rsatilgan bo'lsa).
        Subject? targetCourse = null;
        if (!string.IsNullOrEmpty(targetCourseId))
            targetCourse = await db.Subjects.FindAsync(targetCourseId);

        if (!string.IsNullOrWhiteSpace(req.TargetCourseId) && targetCourse is null)
            return BadRequest(new { message = "Tanlangan kurs topilmadi" });

        // YANGI GURUH NOMI — NOYOB (arxivdagi eski guruh ham shu nomda qoladi, ya'ni eski nomni
        // takrorlash mumkin emas): chat nom bilan bog'langan, yangi guruh eski guruhning (ko'chirilmagan
        // o'quvchilar ham yozgan) yozishmasini meros olmasin.
        var newGroupName = GroupNames.Clean(!string.IsNullOrWhiteSpace(req.NewGroupName) ? req.NewGroupName : group.Name);
        if (await GroupNames.IsTakenAsync(db, newGroupName, null))
            return BadRequest(new
            {
                message = $"{GroupNames.TakenMessage} — yangi guruh uchun boshqa nom kiriting (eski guruh «{group.Name}» nomi bilan arxivda qoladi)",
            });

        var memberStudentIds = activeMembers.Select(m => m.StudentId).Distinct().ToList();
        var students = (await db.Students.Where(s => memberStudentIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id);
        // Eski ma'lumotda arxivlangan o'quvchining a'zoligi tirik qolgan bo'lishi mumkin — u kursni
        // tugatmagan (sertifikat ham, ko'chirish ham yo'q), oddiy chiqish bilan yopiladi.
        var archivedIds = students.Values.Where(s => s.IsArchived).Select(s => s.Id).ToHashSet();

        // KURSNI TUGATGANLAR = YANGI GURUHGA KO'CHADIGANLAR — qoida YAGONA manbada
        // (GroupCompletionRules): faqat eski guruhda Status=="active" bo'lganlar.
        // DIQQAT: ro'yxat quyidagi (1) sikldan OLDIN olinadi — u yerda Status "completed"ga
        // o'zgaradi va keyin "kim aktiv edi" degan ma'lumot yo'qoladi.
        var transferIds = GroupCompletionRules.TransferableStudentIds(activeMembers, archivedIds);
        var completedIds = transferIds.ToHashSet();
        // Ko'chirilmaydiganlar soni (natija/audit uchun) — a'zoligi bor, lekin kursni tugatmagan.
        var skippedNotActive = memberStudentIds.Count - transferIds.Count;

        // ⚠️ TRANZAKSIYA — EXECUTION STRATEGY ICHIDA. `EnableRetryOnFailure` (Program.cs) qo'lda
        // ochilgan tranzaksiyani faqat shu yo'l bilan qabul qiladi; aks holda birinchi so'rovdayoq
        // InvalidOperationException (500) — amal UMUMAN ishlamasdi. Vaqtinchalik uzilishdan keyingi
        // QAYTA URINISHDA kuzatuvdagi o'zgarishlar tashlanadi va holat bazadan QAYTA o'qiladi: birinchi
        // urinishda xotirada "completed" bo'lib qolgan a'zolik ikkinchi marta noto'g'ri hisoblanmasin.
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync<ActionResult<CompleteAndTransferResultDto>>(async () =>
        {
            if (attempt++ > 0)
            {
                db.ChangeTracker.Clear();
                group = await db.Classes.FindAsync(id)
                        ?? throw new InvalidOperationException("Guruh topilmadi");
                activeMembers = await db.StudentGroups
                    .Where(sg => sg.GroupId == id && sg.IsActive)
                    .ToListAsync();
                memberStudentIds = activeMembers.Select(m => m.StudentId).Distinct().ToList();
                students = (await db.Students.Where(s => memberStudentIds.Contains(s.Id)).ToListAsync())
                    .ToDictionary(s => s.Id);
                archivedIds = students.Values.Where(s => s.IsArchived).Select(s => s.Id).ToHashSet();
                transferIds = GroupCompletionRules.TransferableStudentIds(activeMembers, archivedIds);
                completedIds = transferIds.ToHashSet();
                skippedNotActive = memberStudentIds.Count - transferIds.Count;
            }

            await using var trx = await db.Database.BeginTransactionAsync();

            // 0. O'rinbosarlar: yopish kunidan KEYINGI sanalar olib tashlanadi (guruh maydonlari
            //    o'zgarishidan OLDIN — eski tayinlov guruh umri bo'yicha yoyiladi).
            await SubstituteGroupLifecycle.TrimAfterAsync(db, audit, group, closeDate, "guruh tugatildi");

            // 1. ESKI GURUH: kursni tugatganlarning hisobi yopiladi va a'zoligi "completed"; qolganlari
            //    (sinov/muzlatilgan/arxivdagi) ODDIY CHIQISH bilan yopiladi.
            var chargedOldGroup = 0;
            var restored = 0m;
            var leftNotFinished = 0;
            foreach (var m in activeMembers)
            {
                students.TryGetValue(m.StudentId, out var s);
                if (GroupCompletionRules.Completes(m, archivedIds))
                {
                    if (s is not null)
                    {
                        var settle = await MembershipBilling.SettleFreezeAsync(
                            db, s, group, m.ActivatedAt, closeDate, membership: m);
                        if (settle.Charged) chargedOldGroup++;
                        restored += settle.Restored;
                    }
                    // Hisob-kitob chegarasi (StudentGroupLedger, GroupBalanceService, SalaryLedger,
                    // RetentionBonusService hammasi FrozenAt'ga qaraydi) — "Guruhni yopish" bilan bir xil.
                    m.FrozenAt = closeDate;
                    m.Status = "completed";
                    m.IsActive = false;
                    m.LeftAt = closeDate;
                    audit.Record("Membership", $"{id}:{m.StudentId}", "update",
                        $"Kursni tugatdi (sertifikat bilan): {s?.FullName ?? m.StudentId} — {group.Name} ({closeDate})",
                        studentId: m.StudentId);
                    continue;
                }

                var status = m.Status;
                if (s is not null)
                    restored += (await MembershipBilling.LeaveGroupAsync(db, s, group, m, closeDate)).Restored;
                else
                {
                    m.IsActive = false;
                    m.LeftAt = closeDate;
                }
                leftNotFinished++;
                MembershipReasons.Record(db, m.StudentId, id, MembershipReasons.ActionClose, closeDate,
                    null, MembershipReasons.CompletedNotFinishedLabel);
                var statusLabel = status == "frozen" ? "muzlatilgan" : status == "trial" ? "sinovdagi" : status;
                audit.Record("Membership", $"{id}:{m.StudentId}", "delete",
                    $"Guruh tugatildi — guruhdan chiqdi ({statusLabel}, {closeDate}, guruh: {group.Name}): " +
                    $"kursni tugatmagan, sertifikat berilmadi" + (archivedIds.Contains(m.StudentId) ? " (o'quvchi arxivda)" : ""),
                    studentId: m.StudentId);
            }

            // 2. Eski guruhni arxivlaymiz (o'quvchilar arxivlanmaydi — faqat guruh).
            group.IsArchived = true;
            group.ArchivedAt = closeDate;
            group.Status = "archived";
            if (string.IsNullOrWhiteSpace(group.EndDate)) group.EndDate = closeDate;

            await db.SaveChangesAsync();   // tranzaksiya ichida — bekor qilingan oylar keyingi so'rovlarga ko'rinsin

            // 3. YANGI guruh — maqsad kurs + eski guruh o'qituvchi/xona/kunlar/vaqt.
            // Yangi guruh oyligi: maqsad kurs narxidan olinadi; kurs yo'q bo'lsa eski narx.
            var newMonthlyFee = targetCourse?.Price ?? group.MonthlyFee;

            var newGroup = new Group
            {
                Name = newGroupName,
                Grade = group.Grade,
                Language = group.Language,
                MonthlyFee = newMonthlyFee,
                Room = group.Room,
                RoomId = group.RoomId,
                Status = "active",
                StartDate = activateDate,
                EndDate = null,
                Capacity = group.Capacity,
                CourseId = targetCourseId,
                TeacherId = group.TeacherId,
                Note = group.Note,
                Days = new List<int>(group.Days),
                StartTime = group.StartTime,
                EndTime = group.EndTime,
                IsArchived = false,
            };
            db.Classes.Add(newGroup);
            // O'QITUVCHI TARIXI — yangi guruh "kim qachondan o'qitadi" bilan ochiladi (aks holda keyingi
            // o'qituvchi almashuvida bonus/ketish hisobotlari va maosh o'tgan oylarni noto'g'ri odamga
            // yozardi). Boshlanishi — guruhning o'zi boshlangan (aktivlashtirish) sana.
            await GroupTeacherHistory.AssignAsync(db, newGroup.Id, newGroup.TeacherId, Actor);
            foreach (var a in db.GroupTeacherAssignments.Local.Where(a => a.GroupId == newGroup.Id && a.ToDate == null))
                a.FromDate = activateDate;
            audit.Record(AuditService.EntityGroup, newGroup.Id, "create",
                $"Guruh yaratildi ({newGroup.Name}) — «{group.Name}» tugatilib, uning davomi sifatida",
                after: AuditService.GroupSnapshot(newGroup));
            await db.SaveChangesAsync();

            // 4. Auto-enroll — FAQAT kursni tugatganlar (`transferIds`). `activateInNewGroup` yoqilgan
            //    bo'lsa ular DARHOL aktivlashtiriladi — "Guruh almashtirish" (TransferMember) bilan AYNAN
            //    bir xil: chegirma (bir xil fan bo'lsa) ko'chadi, qisman oylik `activateDate`dan YANGI
            //    guruhga yoziladi, orqaga sanalgan bo'lsa oraliq oylar to'ldiriladi va ESKI guruhda ortib
            //    qolgan (avans) pul yangi guruhga qayta teglanadi. Aks holda — "sinov" (to'lov yo'q).
            var enrolledCount = 0;
            var activatedInNew = 0;
            var extensionCount = 0;
            var discountsMoved = 0;
            var movedAdvance = 0m;
            var recordedAt = AppClock.Today.ToString("yyyy-MM-dd");
            var moved = new HashSet<string>();
            if (req.AutoEnrollNewGroup)
            {
                foreach (var sid in transferIds)
                {
                    var activate = req.ActivateInNewGroup;
                    var sg = new StudentGroup
                    {
                        StudentId = sid,
                        GroupId = newGroup.Id,
                        JoinedAt = activate ? activateDate : recordedAt,
                        IsActive = true,
                        Status = activate ? "active" : "trial",
                        ActivatedAt = activate ? activateDate : string.Empty,
                        // Jurnaldagi avto-"keldi" qoidasi HAQIQIY bugungi sanadan boshlanadi — activateDate
                        // orqaga sanalgan bo'lsa, allaqachon o'tilgan darslar avto-"keldi" bo'lib to'lib
                        // qolmasin (AddMember/ActivateMember/TransferMember bilan bir xil qoida).
                        RecordedAt = recordedAt,
                    };
                    db.StudentGroups.Add(sg);
                    enrolledCount++;
                    moved.Add(sid);
                    if (!students.TryGetValue(sid, out var st)) continue;

                    audit.Record("Membership", $"{newGroup.Id}:{sid}", "create",
                        $"Guruhga qo'shildi: {st.FullName} → {newGroup.Name} " +
                        (activate ? $"(aktiv, {activateDate})" : $"(sinov, {recordedAt})") +
                        $" — «{group.Name}» tugatilib ko'chirildi",
                        studentId: sid);

                    // Chegirma — eski guruh hisobi (1) da yopilgandan KEYIN, yangi guruh hisobidan OLDIN.
                    discountsMoved += (await DiscountTransfer.MoveGroupScopedAsync(
                        db, audit, st, group, newGroup, activate ? activateDate : recordedAt)).Moved;

                    // «Asosiy guruh» yorlig'i — faqat u eski guruh (yoki bo'sh) bo'lsa: o'quvchining boshqa
                    // guruhdagi yorlig'i jimgina almashib ketmasin (TransferMember bilan bir xil).
                    if (string.IsNullOrWhiteSpace(st.ClassName) || st.ClassName == group.Name) st.ClassName = newGroupName;

                    if (!activate) continue;

                    await TuitionService.ChargeActivationProrateAsync(db, st, newGroup, activateDate);
                    await TuitionService.AccrueCatchUpAsync(db, st, newGroup, activateDate);
                    // Eski guruhga to'langan, ammo yopish hisobidan ORTIB QOLGAN summa yangi guruhga
                    // ko'chadi (aks holda to'lagan o'quvchi yangi guruhda "qarzdor" bo'lib ko'rinardi).
                    // Eski guruhdagi bekor qilingan oylar (1) da allaqachon bazaga yozilgan, shuning
                    // uchun `zeroOwedMonths` kerak emas.
                    if (closeDate.Length >= 7)
                        movedAdvance += await TuitionService.CarryGroupAdvanceAsync(
                            db, st, group, newGroup, closeDate[..7]);
                    activatedInNew++;
                }

                // UZAYTIRISH HODISALARI (`StudentExtension`) — chiquvchi adminning "Bonus B" manbai.
                //
                // ⚠️ SHART: maqsad kurs ESKI kursdan BOSHQA bo'lishi kerak. "Uzaytirish" = o'quvchi
                // kursning bir bosqichini TUGATIB keyingisini boshladi. AYNI kurs ichida guruh
                // almashtirish (jadval mos kelmadi, o'qituvchi o'zgardi, guruh bo'lindi) uzaytirish
                // EMAS — uni sanasak bonus har guruh yopilishida to'lanib, ko'rsatkich ma'nosini
                // yo'qotardi.
                //
                // ⚠️ Faqat HAQIQATDA ko'chgan VA yangi guruhda AKTIVLASHTIRILGAN o'quvchi sanaladi:
                // "sinov" statusida qo'shilgan odam hali keyingi bosqichni boshlagani yo'q.
                //
                // Nomlar SNAPSHOT sifatida yoziladi (`ContactRequest` naqshi — `.claude/rules/contacts.md`
                // §3): guruh keyin arxivlansa yoki qayta nomlansa ham o'tgan oyning tasdiqlangan
                // hisoboti "noma'lum guruh" bo'lib qolmasin.
                if (req.ActivateInNewGroup && !string.IsNullOrEmpty(targetCourseId)
                    && !string.Equals(targetCourseId, oldCourseId, StringComparison.Ordinal))
                {
                    foreach (var sid in transferIds)
                    {
                        if (!students.TryGetValue(sid, out var st)) continue;
                        db.StudentExtensions.Add(new StudentExtension
                        {
                            StudentId = sid,
                            StudentName = st.FullName,
                            FromGroupId = id,
                            FromGroupName = group.Name,
                            ToGroupId = newGroup.Id,
                            ToGroupName = newGroupName,
                            CourseId = targetCourseId,
                            Date = activateDate,
                            ByUserId = ActorId,
                            ByUserName = Actor,
                            Note = "Guruhni tugatib keyingi bosqichga o'tkazish",
                            CreatedAt = AppClock.Iso(),
                        });
                        extensionCount++;
                    }
                    if (extensionCount > 0)
                        // ⚠️ `EntityId` — `Membership` naqshida "{groupId}:{studentId}"; bu yerda BITTA
                        // yig'ma yozuv, shuning uchun guruhning o'zi (tugatilgan guruh sahifasidagi
                        // "Tarix" tabida ko'rinadi).
                        audit.Record("StudentExtension", id, "create",
                            $"Uzaytirish (kurs bosqichi tugadi): {extensionCount} o'quvchi " +
                            $"«{group.Name}» → «{newGroupName}», yangi kurs: {targetCourse?.Name ?? targetCourseId}, sana {activateDate}");
                }
            }

            // 5. Ko'chirilMAGAN a'zolarning «asosiy guruh» yorlig'i eski (endi arxiv) guruh nomida qolmasin —
            //    boshqa tirik guruhi bo'lsa o'sha, bo'lmasa bo'sh (RemoveMember bilan bir xil qoida).
            foreach (var sid in memberStudentIds.Where(x => !moved.Contains(x)))
                if (students.TryGetValue(sid, out var st))
                    await ReleasePrimaryGroupNameAsync(st, group);

            await db.SaveChangesAsync();

            // 6. Sertifikatlar — FAQAT kursni tugatganlarga, eski kurs bo'yicha (eski kurs yo'q bo'lsa
            //    targetCourseId). Servis o'zi saqlaydi — o'sha tranzaksiya ichida.
            var certCount = 0;
            var certCourseId = !string.IsNullOrEmpty(oldCourseId) ? oldCourseId : targetCourseId;
            if (!string.IsNullOrEmpty(certCourseId) && completedIds.Count > 0)
            {
                var teacherName = string.IsNullOrEmpty(group.TeacherId)
                    ? null
                    : await db.Teachers
                        .Where(t => t.Id == group.TeacherId)
                        .Select(t => t.FullName)
                        .FirstOrDefaultAsync();

                logger.LogInformation("CompleteAndTransfer: {Count} o'quvchi uchun sertifikat yaratish boshlanmoqda (kurs={CourseId})", completedIds.Count, certCourseId);

                foreach (var sid in completedIds)
                {
                    try
                    {
                        await certSvc.GenerateCertificateAsync(
                            sid, certCourseId, req.CompletionNotes,
                            teacherName: teacherName);
                        certCount++;
                    }
                    catch (Exception ex)
                    {
                        // Saqlanmay qolgan sertifikat qatori keyingi saqlashni ham yiqitmasin.
                        foreach (var e in db.ChangeTracker.Entries<StudentCertificate>()
                                     .Where(e => e.State == EntityState.Added).ToList())
                            e.State = EntityState.Detached;
                        logger.LogWarning(ex, "Sertifikat yaratishda xato: student={S}, course={C}: {Msg}", sid, certCourseId, ex.Message);
                    }
                }
                logger.LogInformation("CompleteAndTransfer: {CertCount}/{Total} sertifikat yaratildi", certCount, completedIds.Count);
            }
            else if (string.IsNullOrEmpty(certCourseId))
            {
                logger.LogWarning("CompleteAndTransfer: guruhda kurs (CourseId) yo'q — sertifikat yaratilmadi (groupId={GroupId})", id);
            }

            // 7. YIG'MA AUDIT — saqlashdan OLDIN (ilgari oxirgi SaveChanges'dan KEYIN yozilib, bazaga
            //    umuman tushmasdi).
            var targetCourseName = targetCourse?.Name ?? "";
            audit.Record(
                "Group", id, "complete-and-transfer",
                $"Guruh yakunlandi va arxivlandi ({group.Name}, yopish sanasi {closeDate}): {activeMembers.Count} a'zo, " +
                $"kursni tugatdi={completedIds.Count}, sertifikat={certCount}, eski guruhga qisman oylik yozildi={chargedOldGroup}" +
                (leftNotFinished > 0 ? $", kursni tugatmay chiqdi (sinov/muzlatilgan)={leftNotFinished}" : "") +
                (restored > 0 ? $", keyingi oylar hisobi bekor qilindi: {AuditService.Money(restored)} so'm" : "") +
                $". Yangi guruh: «{newGroupName}», kurs: {(targetCourseName.Length > 0 ? targetCourseName : "—")}, ko'chirildi={enrolledCount}" +
                (activatedInNew > 0 ? $", {activateDate} sanasidan aktivlashtirildi={activatedInNew}" : "") +
                (extensionCount > 0 ? $", uzaytirish yozildi={extensionCount}" : "") +
                (discountsMoved > 0 ? $", chegirma ko'chirildi={discountsMoved}" : "") +
                (movedAdvance > 0 ? $", avans ko'chirildi: {AuditService.Money(movedAdvance)} so'm" : ""));
            await db.SaveChangesAsync();
            await trx.CommitAsync();

            return Ok(new CompleteAndTransferResultDto(
                Ok: true,
                ArchivedGroupId: id,
                NewGroupId: newGroup.Id,
                CertificatesGenerated: certCount,
                EnrolledInNew: enrolledCount,
                TargetCourseName: targetCourseName,
                CloseDate: closeDate,
                ActivateDate: activatedInNew > 0 ? activateDate : "",
                ChargedOldGroup: chargedOldGroup,
                RestoredCharges: restored,
                ActivatedInNew: activatedInNew,
                MovedAdvance: movedAdvance,
                SkippedNotActive: skippedNotActive));
        });
    }

    /// <summary>
    /// GURUHNI YOPISH (sertifikatsiz, yangi guruhsiz) — "Tugatish"dan farqli o'laroq faqat yopadi:
    /// <list type="bullet">
    ///   <item>Guruhning BARCHA faol a'zolari berilgan <c>Date</c> sanasidan MUZLATILADI (bitta-bitta
    ///     "Muzlatish" bilan AYNAN bir xil hisob — <see cref="TuitionService.ChargeFreezeProrateAsync"/>:
    ///     shu oyda muzlatish SANASIGACHA (shu sana ham) qatnashgan darslar uchun qisman to'lov).</item>
    ///   <item>Muzlatish sanasidan KEYINGI oylarga allaqachon yozilgan hisoblar bekor qilinadi
    ///     (<see cref="TuitionService.PurgeChargesAfterMonthAsync"/>) — orqaga sanalgan yopishda ham
    ///     qarzdorlik AYNAN muzlatish sanasigacha bo'ladi va keyin o'smaydi.</item>
    ///   <item>SINOVDAGI (trial) a'zolar muzlatilmaydi — ularda hisob umuman ochilmagan, muzlatilsa
    ///     hisobda "qarz" bo'lib ko'rinardi; ular guruhdan chiqariladi (tarix saqlanadi).</item>
    ///   <item>Guruh ARXIVGA olinadi (<c>IsArchived=true</c>, <c>Status="archived"</c>) — faol guruhlar
    ///     ro'yxatidan chiqadi. O'quvchilar ARXIVLANMAYDI (boshqa guruhlarda o'qiyverishi mumkin).</item>
    /// </list>
    /// A'zoliklar (IsActive) saqlanadi — muzlatilgan a'zolikka KEYIN ham to'lov qilish mumkin
    /// (to'lov oynasi muzlatilgan/arxiv guruhlarni ham ko'rsatadi).
    /// </summary>
    /// <summary>
    /// GURUH A'ZOLIKLARINI YOPISH — «Guruhni yopish» va «Arxivlash» uchun YAGONA joy.
    ///
    /// <para>Har FAOL a'zolik <c>frozen</c> qilinadi va qisman to'lov + keyingi oylar hisobini
    /// bekor qilish <see cref="MembershipBilling.SettleFreezeAsync"/> ga topshiriladi (muzlatish
    /// bilan AYNAN bir xil). SINOVDAGI a'zolik esa guruhdan chiqariladi: unda hisob umuman
    /// ochilmagan, muzlatilsa hisob-kitobda soxta "qarz" bo'lib ko'rinardi.</para>
    ///
    /// <para>⚠️ <b>O'QUVCHI ARXIVLANMAYDI.</b> Guruh yopilgani/arxivlangani o'quvchining o'zini
    /// arxivga tiqish uchun sabab emas — u boshqa fanga yozilishi mumkin va tarixi ochiq qolishi
    /// kerak. A'zolik muzlatilgani uchun o'quvchi o'z-o'zidan "Aktiv emas" bo'lib qoladi
    /// (<see cref="MembershipLifecycle.MemberState"/>) va oylik ham hisoblanmaydi
    /// (<c>TuitionService.AccrueMonth</c> faqat <c>active</c> a'zolikni oladi).</para>
    ///
    /// <para>⚠️ <c>IsActive</c> SAQLANADI — muzlatilgan a'zolikka KEYIN ham to'lov qilish mumkin
    /// (to'lov oynasi muzlatilgan/arxiv guruhlarni ham ko'rsatadi).</para>
    /// </summary>
    /// <param name="what">Audit matnining boshi: «Guruh yopildi» yoki «Guruh arxivlandi».</param>
    /// <param name="reasonId">«Guruhni yopish» da tanlangan sabab — har yopilgan a'zolikka tuzilgan
    /// yozuv sifatida ham yoziladi (<see cref="MembershipReasons.ActionClose"/>). Arxivlashda yo'q.</param>
    /// <param name="reasonLabel">O'sha sababning nomi (bo'sh — yozuv yo'q).</param>
    private async Task<(int Frozen, int AlreadyFrozen, int TrialClosed, decimal Restored)> CloseMembersAsync(
        Group group, string date, string what, string? reasonId = null, string reasonLabel = "")
    {
        var members = await db.StudentGroups
            .Where(sg => sg.GroupId == group.Id && sg.IsActive)
            .ToListAsync();
        var studentIds = members.Select(m => m.StudentId).Distinct().ToList();
        var students = (await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id);

        var frozen = 0;
        var alreadyFrozen = 0;
        var trialClosed = 0;
        var restored = 0m;

        // «Asosiy guruh» yorlig'i (Student.ClassName) yopilayotgan guruh nomida qolmasin — boshqa tirik
        // guruhi bo'lsa o'sha, bo'lmasa bo'sh. Aks holda o'quvchi nom orqali shu guruh chatida, ro'yxat
        // belgisida "qolib ketardi" (RemoveMember / Tugatish bilan bir xil qoida).
        foreach (var st in students.Values)
            await ReleasePrimaryGroupNameAsync(st, group);

        foreach (var m in members)
        {
            if (m.Status == "frozen") { alreadyFrozen++; continue; }

            // SINOV a'zoligi — hech qachon hisob ochilmagan. Muzlatilsa hisob-kitobda (StudentGroupLedger)
            // guruh narxi bo'yicha "qarz" bo'lib ko'rinardi, shuning uchun guruhdan chiqaramiz (tarix qoladi).
            if (m.Status != "active")
            {
                m.IsActive = false;
                m.LeftAt = date;
                trialClosed++;
                audit.Record("Membership", $"{group.Id}:{m.StudentId}", "update",
                    $"{what} — sinovdagi a'zolik yakunlandi ({date}, guruh: {group.Name})",
                    studentId: m.StudentId);
                MembershipReasons.Record(db, m.StudentId, group.Id, MembershipReasons.ActionClose, date, reasonId, reasonLabel);
                continue;
            }

            var activatedAt = m.ActivatedAt;
            m.Status = "frozen";
            m.FrozenAt = date;

            // Qisman to'lov (yopish sanasigacha o'qilgan darslar) + yopishdan keyingi oylar hisobini
            // bekor qilish — YAGONA manbada ("Muzlatish"/"Tugatish" bilan aynan bir xil).
            if (students.TryGetValue(m.StudentId, out var s))
                restored += (await MembershipBilling.SettleFreezeAsync(
                    db, s, group, activatedAt, date, membership: m)).Restored;

            frozen++;
            audit.Record("Membership", $"{group.Id}:{m.StudentId}", "update",
                $"{what} — muzlatildi ({date}, guruh: {group.Name})",
                studentId: m.StudentId);
            MembershipReasons.Record(db, m.StudentId, group.Id, MembershipReasons.ActionClose, date, reasonId, reasonLabel);
        }

        return (frozen, alreadyFrozen, trialClosed, restored);
    }

    [HttpPost("{id}/close")]
    [Authorize]
    public async Task<ActionResult<CloseGroupResultDto>> Close(string id, CloseGroupRequest req)
    {
        if (!MayCloseGroups) return CloseForbidden();
        var group = await db.Classes.FindAsync(id);
        if (group is null) return NotFound(new { message = "Guruh topilmadi" });
        if (group.IsArchived) return BadRequest(new { message = "Guruh allaqachon arxivda" });

        var date = string.IsNullOrWhiteSpace(req.Date)
            ? AppClock.Today.ToString("yyyy-MM-dd") : req.Date!.Trim();
        if (date.Length < 10 || !DateOnly.TryParse(date, out _))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        // Yopish kunidan KEYINGI o'rinbosar sanalari olib tashlanadi (guruh maydonlari o'zgarishidan OLDIN).
        await SubstituteGroupLifecycle.TrimAfterAsync(db, audit, group, date, "guruh yopildi");

        // Sabab AVVAL o'qiladi — u har yopilgan a'zolikka ham tuzilgan yozuv bo'lib tushadi.
        var reason = await ReasonLabelAsync(req.ReasonId);
        var (frozen, alreadyFrozen, trialClosed, restored) =
            await CloseMembersAsync(group, date, "Guruh yopildi", req.ReasonId, reason);

        // Guruhni arxivga (NotActive) olamiz — o'quvchilar ARXIVLANMAYDI.
        group.IsArchived = true;
        group.ArchivedAt = date;
        group.Status = "archived";
        if (string.IsNullOrWhiteSpace(group.EndDate)) group.EndDate = date;

        audit.Record(AuditService.EntityGroup, id, "update",
            $"Guruh yopildi ({group.Name}) — {date} sanasidan {frozen} a'zo muzlatildi, guruh arxivga olindi"
            + (trialClosed > 0 ? $", {trialClosed} sinovdagi a'zolik yakunlandi" : "")
            + (reason.Length > 0 ? $" — sabab: {reason}" : ""));

        await db.SaveChangesAsync();

        logger.LogInformation(
            "CloseGroup: group={GroupId} ({GroupName}) date={Date} frozen={Frozen} already={Already} trial={Trial} restored={Restored}",
            id, group.Name, date, frozen, alreadyFrozen, trialClosed, restored);

        return Ok(new CloseGroupResultDto(
            Ok: true,
            GroupId: id,
            GroupName: group.Name,
            FreezeDate: date,
            FrozenCount: frozen,
            AlreadyFrozen: alreadyFrozen,
            TrialClosed: trialClosed,
            RestoredCharges: restored));
    }

}
