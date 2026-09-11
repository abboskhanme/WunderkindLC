using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Daraja (placement) testi mantig'i: admin testlarini DTO'ga yig'ish, ommaviy ko'rinish,
/// ball/daraja hisoblash va topshiruvdan CRM LID yaratish.
/// </summary>
public static class LevelTestService
{
    /// <summary>Kurs nomini (Subject) id bo'yicha oladi — bo'sh/yo'q bo'lsa "".</summary>
    private static async Task<string> CourseNameAsync(IAppDbContext db, string courseId)
    {
        if (string.IsNullOrEmpty(courseId)) return "";
        return await db.Subjects.Where(s => s.Id == courseId).Select(s => s.Name).FirstOrDefaultAsync() ?? "";
    }

    /// <summary>Admin uchun bitta testning to'liq tafsiloti (savollar + diapazonlar).</summary>
    public static async Task<LevelTestDetailDto> BuildDetailAsync(IAppDbContext db, LevelTest t)
    {
        var questions = await db.LevelTestQuestions.Where(q => q.TestId == t.Id)
            .OrderBy(q => q.Order).ToListAsync();
        var bands = await db.LevelTestBands.Where(x => x.TestId == t.Id)
            .OrderBy(x => x.MinPercent).ToListAsync();
        return new LevelTestDetailDto(
            t.Id, t.Title, t.CourseId, await CourseNameAsync(db, t.CourseId), t.Slug, t.Intro,
            t.IsActive, t.CreatedAt,
            questions.Select(q => new LevelTestQuestionDto(q.Id, q.Text, q.Options, q.CorrectIndex, q.Order, q.Kind, q.Multiple)).ToList(),
            bands.Select(x => new LevelTestBandDto(x.Id, x.Label, x.MinPercent, x.Order)).ToList());
    }

    /// <summary>Test nomidan o'qiladigan, NOYOB slug yasaydi (`ingliz-tili-3f2a`).</summary>
    public static Task<string> GenerateSlugAsync(IAppDbContext db, string title) =>
        SlugUtil.UniqueAsync(title, slug => db.LevelTests.AnyAsync(t => t.Slug == slug), "test");

    /// <summary>Ommaviy ko'rinish (to'g'ri javobSIZ). Test yo'q yoki faol emas — null.</summary>
    public static async Task<PublicTestDto?> GetPublicAsync(IAppDbContext db, string slug)
    {
        var test = await db.LevelTests.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
        if (test is null) return null;
        var questions = await db.LevelTestQuestions.Where(q => q.TestId == test.Id)
            .OrderBy(q => q.Order).ToListAsync();
        return new PublicTestDto(
            test.Title, test.Intro, await CourseNameAsync(db, test.CourseId),
            questions.Select(q => new PublicTestQuestionDto(q.Id, q.Text, q.Options, q.Kind, q.Multiple)).ToList());
    }

    /// <summary>
    /// Topshiruvlar ro'yxatini boyitilgan statistika qatorlariga aylantiradi: har bir topshiruvchi
    /// AKTIV o'quvchiga aylandimi (Status=="active" guruh a'zoligi), qaysi guruh(lar) + o'qituvchi (FISH),
    /// va lid o'chirilganmi. Natija KIRISH tartibida qaytadi. Bitta test (`/stats`) va UMUMIY statistika
    /// uchun bitta umumiy mantiq (takrorlanmasligi uchun).
    /// </summary>
    public static async Task<List<LevelTestStatRowDto>> BuildStatRowsAsync(
        IAppDbContext db, List<LevelTestSubmission> subs)
    {
        // "Lid → o'quvchi → faol a'zolik" zanjiri YAGONA joyda (lid formalari statistikasi ham
        // shundan o'qiydi) — qarang: LeadOutcome.
        var outcome = await LeadOutcome.BuildAsync(db, subs.Select(s => s.LeadId));

        return subs.Select(s =>
        {
            var info = outcome.GroupInfo(s.LeadId);
            var stage = outcome.StageOf(s.LeadId);
            return new LevelTestStatRowDto(
                s.Id, s.FullName, s.Phone, s.Level, s.Percent, s.CreatedAt, s.LeadId,
                outcome.StudentOf(s.LeadId), outcome.IsActive(s.LeadId),
                info.Groups, info.Teachers, outcome.IsDeletedLead(s.LeadId),
                stage.Title, stage.Color,
                outcome.HasPaid(s.LeadId), outcome.PaidTotal(s.LeadId), outcome.FirstPaidAt(s.LeadId));
        }).ToList();
    }

    // ==================== UMUMIY statistika (barcha testlar) ====================

    /// <summary>Kunlik grafik uzunligi — lid formalari statistikasi bilan bir xil (30 kun).</summary>
    public const int DailyDays = LeadFormService.DailyDays;

    /// <summary>
    /// Umumiy statistikada qaytadigan topshiruvchi qatorlari chegarasi (eng yangilari).
    /// <para>⚠️ Natija <c>DataCache</c> da saqlanadi va bog'liq jadvallar (to'lov, a'zolik) tez-tez
    /// o'zgargani uchun bir necha nusxa bir vaqtda xotirada bo'lishi mumkin — cheklovsiz ro'yxat
    /// 1GB serverda xavfli. Chegaradan oshgani UI'da JIM YO'QOLMAYDI: javobda
    /// <c>RowsTotal</c> (jami) qaytadi va sahifa "N tadan oxirgi M tasi" deb yozadi.</para>
    /// </summary>
    public const int MaxRows = 500;

    /// <summary>
    /// Har LIDdan bitta qator qoldiradi (eng yangisi — kirish tartibi bo'yicha birinchisi).
    /// <para>Statistikaning ASOSIY qoidasi: bir odam testni ikki marta topshirsa ham u BITTA
    /// mijoz. Bitta test sahifasi ham, umumiy sahifa ham AYNAN shu funksiyani chaqiradi — aks
    /// holda ikki ekranda ikki xil "aktiv"/"to'ladi" soni chiqardi.</para>
    /// </summary>
    public static List<LevelTestStatRowDto> DistinctByLead(IEnumerable<LevelTestStatRowDto> rows) =>
        rows.Where(r => !string.IsNullOrEmpty(r.LeadId))
            .GroupBy(r => r.LeadId).Select(g => g.First()).ToList();

    /// <summary>
    /// BARCHA daraja testlari bo'yicha voronka: <b>topshirdi → lid → o'quvchi → TO'LADI</b>,
    /// test / bosqich / daraja kesimida + oxirgi 30 kunlik oqim.
    ///
    /// <para>Bu "Formalar → Test statistikasi" sahifasining yagona manbai: ilgari bunday ko'rinish
    /// YO'Q edi — sotuv raqamlarini (bosqich, to'lov) ko'rish uchun HAR BIR testning ichiga kirish
    /// kerak bo'lardi va testlarni bir-biriga solishtirib bo'lmasdi.</para>
    ///
    /// <para>⚠️ Foizlar <b>TAKRORSIZ LIDLAR</b> bo'yicha — lid formalaridagi bilan AYNAN bir xil
    /// qoida (<see cref="LeadFormService.BuildStatsAsync"/>): bir odam testni ikki marta topshirsa
    /// ham u bitta mijoz, aks holda ko'p topshirilgan test sun'iy ravishda yomon ko'rinardi.
    /// "Aktiv" va "to'ladi" ta'rifi ham yagona (<see cref="LeadOutcome"/>).</para>
    /// </summary>
    public static async Task<LevelTestOverallStatsDto> BuildOverallStatsAsync(IAppDbContext db)
    {
        var tests = await db.LevelTests.AsNoTracking()
            .Select(t => new { t.Id, t.Title, t.IsActive }).ToListAsync();
        // ⚠️ Faqat KERAKLI ustunlar o'qiladi (`SurveyJson` — eng og'ir ustun — statistikaga
        // umuman kirmaydi), keyin xotirada entity ko'rinishiga yig'iladi: `BuildStatRowsAsync`
        // bitta test statistikasi bilan UMUMIY bo'lgani uchun kirish turi o'zgarmadi.
        var subs = (await db.LevelTestSubmissions.AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id, s.TestId, s.FullName, s.Phone, s.Percent, s.Level, s.CreatedAt, s.LeadId,
                })
                .ToListAsync())
            .Select(s => new LevelTestSubmission
            {
                Id = s.Id, TestId = s.TestId, FullName = s.FullName, Phone = s.Phone,
                Percent = s.Percent, Level = s.Level, CreatedAt = s.CreatedAt, LeadId = s.LeadId,
            })
            .ToList();
        // Takliflar test bo'yicha BIR MARTA guruhlanadi (ilgari har test uchun butun ro'yxat
        // qaytadan skanerlanardi — O(testlar × takliflar)).
        var invitesByTest = (await db.LevelTestInvites.AsNoTracking()
                .Select(i => new { i.TestId, i.UsedAt }).ToListAsync())
            .ToLookup(i => i.TestId);
        var inviteCount = invitesByTest.Sum(g => g.Count());
        var inviteUsed = invitesByTest.Sum(g => g.Count(x => !string.IsNullOrEmpty(x.UsedAt)));

        // Bitta test statistikasidagi MANTIQ (bosqich/to'lov/aktiv — LeadOutcome orqali), barcha
        // testlarga. Qatorlar `subs` tartibida qaytadi, lekin bog'lash id bo'yicha (tartibga
        // tayanmaymiz — kelajakda saralash o'zgarsa jimgina noto'g'ri hisob chiqmasin).
        var rows = await BuildStatRowsAsync(db, subs);
        var testIdBySubmission = subs.ToDictionary(s => s.Id, s => s.TestId);
        var rowsByTest = rows.GroupBy(r => testIdBySubmission.GetValueOrDefault(r.SubmissionId, ""))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Bir guruh qatorlar uchun voronka — TAKRORSIZ lid bo'yicha (yuqoridagi izohga qarang).
        static (int Leads, int Converted, int Active, int Paid, decimal Revenue,
            double ConvertRate, double PayRate) Funnel(IEnumerable<LevelTestStatRowDto> items)
        {
            var byLead = DistinctByLead(items);
            var converted = byLead.Count(r => r.StudentId != null);
            var active = byLead.Count(r => r.Active);
            var paid = byLead.Count(r => r.Paid);
            // Tushum — faqat MUSBAT sof summalar: to'liq qaytarilgan pul "daromad" emas.
            var revenue = byLead.Sum(r => Math.Max(0m, r.PaidTotal));
            var n = byLead.Count;
            return (n, converted, active, paid, revenue,
                n > 0 ? Math.Round(converted * 100.0 / n, 1) : 0,
                n > 0 ? Math.Round(paid * 100.0 / n, 1) : 0);
        }

        var titleById = tests.ToDictionary(t => t.Id, t => t.Title);

        var byTest = tests
            .Select(t =>
            {
                var tr = rowsByTest.GetValueOrDefault(t.Id, new List<LevelTestStatRowDto>());
                var ti = invitesByTest[t.Id].ToList();
                var fn = Funnel(tr);
                return new TestStatRowDto(
                    t.Id, t.Title, t.IsActive,
                    tr.Count, ti.Count, ti.Count(x => !string.IsNullOrEmpty(x.UsedAt)),
                    tr.Count > 0 ? Math.Round(tr.Average(r => (double)r.Percent), 1) : 0,
                    fn.Leads, fn.Converted, fn.Active, fn.Paid, fn.Revenue,
                    fn.ConvertRate, fn.PayRate);
            })
            .OrderByDescending(r => r.Submissions).ThenBy(r => r.Title).ToList();

        var byLevel = subs.GroupBy(s => string.IsNullOrEmpty(s.Level) ? "—" : s.Level)
            .Select(g => new LevelCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();

        // BOSQICHLAR — testdan kelgan TAKRORSIZ lidlar hozir kanbanning qaysi ustunida.
        // Bosqichi yo'q (yoki ustuni o'chirilgan) lid ro'yxatga kirmaydi — kanbanda ham ko'rinmaydi.
        var byStage = DistinctByLead(rows)
            .Where(r => r.StageTitle.Length > 0)
            .GroupBy(r => (r.StageTitle, r.StageColor))
            .Select(g => new LeadStageCountDto(g.Key.StageTitle, g.Key.StageColor, g.Count()))
            .OrderByDescending(x => x.Leads).ThenBy(x => x.Stage).ToList();

        // Kunlik oqim — oxirgi DailyDays kun, BO'SH kunlar ham (grafik uzilib qolmasin).
        var today = AppClock.Now.Date;
        var counts = subs.GroupBy(s => (s.CreatedAt ?? "") is { Length: >= 10 } c ? c[..10] : "")
            .ToDictionary(g => g.Key, g => g.Count());
        var daily = Enumerable.Range(0, DailyDays)
            .Select(i => today.AddDays(-(DailyDays - 1 - i)).ToString("yyyy-MM-dd"))
            .Select(d => new DayCountDto(d, counts.GetValueOrDefault(d, 0)))
            .ToList();

        // Qatorlar CHEKLANADI (eng yangi `MaxRows` ta) — sabab konstanta izohida. Jami son
        // javobda alohida qaytadi, ya'ni sahifada "N tadan oxirgi M tasi" deb ko'rinadi.
        var rowDtos = rows.Take(MaxRows).Select(r =>
        {
            var testId = testIdBySubmission.GetValueOrDefault(r.SubmissionId, "");
            return new LevelTestOverallRowDto(
                r.SubmissionId, testId, titleById.GetValueOrDefault(testId, ""),
                r.FullName, r.Phone, r.Level, r.Percent, r.CreatedAt, r.LeadId,
                r.StudentId, r.Active, r.GroupName, r.TeacherName, r.IsDeleted,
                r.StageTitle, r.StageColor, r.Paid, r.PaidTotal, r.FirstPaidAt);
        }).ToList();

        var total = Funnel(rows);
        return new LevelTestOverallStatsDto(
            tests.Count, tests.Count(t => t.IsActive), subs.Count,
            inviteCount, inviteUsed,
            subs.Count > 0 ? Math.Round(subs.Average(s => (double)s.Percent), 1) : 0,
            total.Leads, total.Converted, total.Active, total.Paid, total.Revenue,
            byLevel, byTest, byStage, daily, rows.Count, rowDtos,
            // BUTUN CRM manzarasi — lid formalari statistikasi ham AYNAN shu funksiyadan oladi,
            // ya'ni ikkala sahifada "qo'lda kiritilgan" va "to'ladi" bir xil hisoblanadi.
            Overview: await LeadCrmOverview.BuildAsync(db));
    }

    /// <summary>Ball foiziga mos daraja yorlig'i — foiz ≥ MinPercent bo'lgan ENG YUQORI diapazon.</summary>
    private static string ResolveLevel(IReadOnlyList<LevelTestBand> bands, int percent)
    {
        var match = bands.Where(b => percent >= b.MinPercent)
            .OrderByDescending(b => b.MinPercent).FirstOrDefault();
        return match?.Label ?? "";
    }

    /// <summary>
    /// Testni topshiradi: ball/daraja hisoblaydi, topshiruvni saqlaydi va CRM'da yangi LID yaratadi
    /// (Source="Daraja testi", InterestSubject=kurs). Test yo'q/faol emas bo'lsa null qaytaradi.
    /// SaveChanges shu yerda bajariladi.
    /// </summary>
    public static async Task<TestResultDto?> SubmitAsync(
        IAppDbContext db, string slug, TestSubmitRequest req, TelegramService? telegram = null, AutoMessageService? autoMsg = null)
    {
        var test = await db.LevelTests.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
        if (test is null) return null;

        var items = await db.LevelTestQuestions.Where(q => q.TestId == test.Id).ToListAsync();
        var bands = await db.LevelTestBands.Where(x => x.TestId == test.Id).ToListAsync();

        // Baholash FAQAT savollar ("question") bo'yicha; so'rovnoma ("survey") chiqarib tashlanadi.
        var graded = items.Where(q => q.Kind != "survey").ToList();
        var total = graded.Count;
        var score = 0;
        foreach (var q in graded)
            if (req.Answers != null && req.Answers.TryGetValue(q.Id, out var picked) && picked == q.CorrectIndex)
                score++;

        // So'rovnoma javoblari (baholanmaydi) — tanlangan variant matnlarini yig'amiz.
        var surveyAnswers = new List<SurveyAnswerDto>();
        foreach (var s in items.Where(q => q.Kind == "survey").OrderBy(x => x.Order))
        {
            var picks = new List<string>();
            if (req.SurveyAnswers != null && req.SurveyAnswers.TryGetValue(s.Id, out var idxs) && idxs != null)
                foreach (var i in idxs.Distinct())
                    if (i >= 0 && i < s.Options.Count) picks.Add(s.Options[i]);
            surveyAnswers.Add(new SurveyAnswerDto(s.Text, picks));
        }
        var surveyJson = surveyAnswers.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(surveyAnswers)
            : "";
        var surveyText = surveyAnswers.Count > 0
            ? "\nSo'rovnoma:\n" + string.Join("\n", surveyAnswers.Select(
                a => $"• {a.Question}: {(a.Answers.Count > 0 ? string.Join(", ", a.Answers) : "—")}"))
            : "";

        var percent = total > 0 ? (int)Math.Round(score * 100.0 / total) : 0;
        var level = ResolveLevel(bands, percent);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var courseName = await CourseNameAsync(db, test.CourseId);

        var levelText = string.IsNullOrEmpty(level) ? "" : $" — {level}";
        var noteLine = $"Daraja testi: {score}/{total} ({percent}%){levelText}"
                       + (req.Age > 0 ? $". Yoshi: {req.Age}" : "") + surveyText;

        // CRM LID — bir xil telefon (oxirgi 9 raqam) bo'yicha MAVJUD lid bo'lsa, DUBLIKAT
        // yaratmasdan natijani o'shaning tagiga qo'shamiz; aks holda yangi lid ochamiz.
        var phone = PhoneUtil.Normalize(req.Phone);
        // Mavjud lidni izlash qoidasi YAGONA joyda (lid formalari ham shundan foydalanadi).
        var existing = await LeadIntake.FindByPhoneAsync(db, req.Phone);

        Lead lead;
        var isNewLead = existing is null;
        if (existing is not null)
        {
            // Mavjud lidga biriktiramiz (yangi lid YARATILMAYDI).
            existing.Note = ((existing.Note ?? "").TrimEnd() + "\n" + noteLine).Trim();
            if (string.IsNullOrWhiteSpace(existing.InterestSubject))
                existing.InterestSubject = string.IsNullOrEmpty(courseName) ? test.Title : courseName;
            // Ism oldin bo'sh/"Noma'lum" bo'lib, endi kiritilgan bo'lsa — to'ldiramiz.
            if (!string.IsNullOrWhiteSpace(req.FullName)
                && (string.IsNullOrWhiteSpace(existing.FullName) || existing.FullName.StartsWith("Noma'lum")))
                existing.FullName = req.FullName.Trim();
            // TAKRORIY MUROJAAT belgisi (lid formasi bilan bir xil qoida): bosqich o'zgarmaydi,
            // lekin kanban kartasida "yana murojaat qildi" ko'rinib turadi.
            existing.RepeatCount += 1;
            existing.LastRepeatAt = now;
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = existing.Id, Type = "note", ActorName = "Daraja testi", CreatedAt = now,
                Text = $"Yana daraja testini ishladi: {score}/{total} ({percent}%){levelText}",
            });
            lead = existing;
        }
        else
        {
            // Yangi lid — birinchi (Order) bosqichga tushadi.
            var firstStage = await LeadIntake.FirstStageIdAsync(db);
            lead = new Lead
            {
                FullName = string.IsNullOrWhiteSpace(req.FullName) ? "Noma'lum (daraja testi)" : req.FullName.Trim(),
                Phone = phone,
                Source = "Daraja testi",
                InterestSubject = string.IsNullOrEmpty(courseName) ? test.Title : courseName,
                Note = noteLine,
                Stage = firstStage,
                CreatedAt = now,
            };
            db.Leads.Add(lead);
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "created", ActorName = "Daraja testi", CreatedAt = now,
                Text = $"Daraja testi orqali keldi: {score}/{total} ({percent}%){levelText}",
                // Voronka analitikasi uchun: lid birinchi bosqichga tushdi (ActorUserId yo'q — o'quvchi o'zi topshirdi).
                ToStage = firstStage,
            });
        }
        var submission = new LevelTestSubmission
        {
            TestId = test.Id, FullName = lead.FullName, Phone = lead.Phone, Age = req.Age,
            Score = score, Total = total, Percent = percent, Level = level, CreatedAt = now, LeadId = lead.Id,
            SurveyJson = surveyJson,
        };
        db.LevelTestSubmissions.Add(submission);
        await db.SaveChangesAsync();

        // Botda ro'yxatdan o'tgan admin/xodimlarga yangi lid xabarnomasi — test natijasi bilan (batafsil).
        // isNewLead=false bo'lsa (mavjud lidga biriktirildi) — sarlavha "yangi lid" emas.
        if (telegram is not null)
            await LeadNotifier.NotifyNewLeadAsync(db, telegram, lead, submission, test.Title, isNewLead,
                createdBy: "Daraja testi (o'quvchi o'zi topshirdi)");
        // Avto xabar — "Test natijasi" hodisasiga yoqilgan qoidalar bo'yicha abituriyentga (lidga) SMS.
        // {natija}/{daraja}/{ball}/{foiz} tokenlari test natijasi bilan to'ldiriladi.
        if (autoMsg is not null)
        {
            var natija = total > 0
                ? $"{score}/{total} ({percent}%)" + (string.IsNullOrEmpty(level) ? "" : $" — {level}")
                : "";
            await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.TestResult, lead, extraTokens:
                new Dictionary<string, string>
                {
                    ["{natija}"] = natija,
                    ["{daraja}"] = level ?? "",
                    ["{ball}"] = total > 0 ? $"{score}/{total}" : "",
                    ["{foiz}"] = total > 0 ? $"{percent}%" : "",
                });
        }

        var msg = total == 0
            ? "Rahmat! Ma'lumotlaringiz qabul qilindi — tez orada bog'lanamiz."
            : $"Rahmat! Siz {total} ta savoldan {score} tasiga to'g'ri javob berdingiz"
              + (string.IsNullOrEmpty(level) ? "." : $". Sizning darajangiz: {level}.")
              + " Tez orada siz bilan bog'lanamiz.";
        return new TestResultDto(score, total, percent, level, msg);
    }

    // ==================== Bir martalik havola (invite) ====================

    /// <summary>Token bo'yicha testni oladi (lid nomi/telefoni oldindan to'ldirilgan). Token yo'q/test
    /// faol emas — null. Allaqachon ishlatilgan bo'lsa Used=true (test ko'rsatilmaydi).</summary>
    public static async Task<PublicInviteDto?> GetByInviteAsync(IAppDbContext db, string token)
    {
        var inv = await db.LevelTestInvites.FirstOrDefaultAsync(i => i.Token == token);
        if (inv is null) return null;
        if (!string.IsNullOrEmpty(inv.UsedAt)) return new PublicInviteDto(null, "", "", true);
        var test = await db.LevelTests.FirstOrDefaultAsync(t => t.Id == inv.TestId && t.IsActive);
        if (test is null) return null;
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == inv.LeadId);
        var questions = await db.LevelTestQuestions.Where(q => q.TestId == test.Id).OrderBy(q => q.Order).ToListAsync();
        var pub = new PublicTestDto(test.Title, test.Intro, await CourseNameAsync(db, test.CourseId),
            questions.Select(q => new PublicTestQuestionDto(q.Id, q.Text, q.Options, q.Kind, q.Multiple)).ToList());
        return new PublicInviteDto(pub, lead?.FullName ?? "", lead?.Phone ?? "", false);
    }

    /// <summary>Bir martalik havola orqali topshirish: baholaydi, natijani MAVJUD lidga bog'laydi,
    /// havolani yopadi (UsedAt). Token yo'q/test yo'q/allaqachon ishlatilgan — null.</summary>
    public static async Task<TestResultDto?> SubmitInviteAsync(
        IAppDbContext db, string token, TestSubmitRequest req, TelegramService? telegram = null, AutoMessageService? autoMsg = null)
    {
        var inv = await db.LevelTestInvites.FirstOrDefaultAsync(i => i.Token == token);
        if (inv is null || !string.IsNullOrEmpty(inv.UsedAt)) return null; // yo'q yoki allaqachon ishlatilgan
        var test = await db.LevelTests.FirstOrDefaultAsync(t => t.Id == inv.TestId);
        if (test is null) return null;
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == inv.LeadId);

        var items = await db.LevelTestQuestions.Where(q => q.TestId == test.Id).ToListAsync();
        var bands = await db.LevelTestBands.Where(x => x.TestId == test.Id).ToListAsync();

        var graded = items.Where(q => q.Kind != "survey").ToList();
        var total = graded.Count;
        var score = 0;
        foreach (var q in graded)
            if (req.Answers != null && req.Answers.TryGetValue(q.Id, out var picked) && picked == q.CorrectIndex)
                score++;

        var surveyAnswers = new List<SurveyAnswerDto>();
        foreach (var s in items.Where(q => q.Kind == "survey").OrderBy(x => x.Order))
        {
            var picks = new List<string>();
            if (req.SurveyAnswers != null && req.SurveyAnswers.TryGetValue(s.Id, out var idxs) && idxs != null)
                foreach (var i in idxs.Distinct())
                    if (i >= 0 && i < s.Options.Count) picks.Add(s.Options[i]);
            surveyAnswers.Add(new SurveyAnswerDto(s.Text, picks));
        }
        var surveyJson = surveyAnswers.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(surveyAnswers) : "";
        var surveyText = surveyAnswers.Count > 0
            ? "\nSo'rovnoma:\n" + string.Join("\n", surveyAnswers.Select(
                a => $"• {a.Question}: {(a.Answers.Count > 0 ? string.Join(", ", a.Answers) : "—")}"))
            : "";

        var percent = total > 0 ? (int)Math.Round(score * 100.0 / total) : 0;
        var level = ResolveLevel(bands, percent);
        var levelText = string.IsNullOrEmpty(level) ? "" : $" — {level}";
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var courseName = await CourseNameAsync(db, test.CourseId);

        // Natijani MAVJUD lidga bog'laymiz (yangi lid yaratilmaydi).
        if (lead is not null)
        {
            lead.Note = ((lead.Note ?? "").TrimEnd() + $"\nDaraja testi: {score}/{total} ({percent}%){levelText}" + surveyText).Trim();
            if (string.IsNullOrWhiteSpace(lead.InterestSubject))
                lead.InterestSubject = string.IsNullOrEmpty(courseName) ? test.Title : courseName;
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "note", ActorName = "Daraja testi", CreatedAt = now,
                Text = $"Daraja testini ishladi: {score}/{total} ({percent}%){levelText}",
            });
        }

        var submission = new LevelTestSubmission
        {
            TestId = test.Id, FullName = lead?.FullName ?? "", Phone = lead?.Phone ?? "", Age = req.Age,
            Score = score, Total = total, Percent = percent, Level = level, CreatedAt = now,
            LeadId = inv.LeadId, SurveyJson = surveyJson,
        };
        db.LevelTestSubmissions.Add(submission);

        inv.UsedAt = now;
        inv.SubmissionId = submission.Id;
        inv.Percent = percent;
        inv.Level = level ?? "";
        await db.SaveChangesAsync();

        // Bir martalik havola — natija MAVJUD lidga bog'landi, shuning uchun "yangi lid" emas.
        if (telegram is not null && lead is not null)
            await LeadNotifier.NotifyNewLeadAsync(db, telegram, lead, submission, test.Title, isNewLead: false,
                createdBy: "Daraja testi (taklif havolasi)");

        var msg = total == 0
            ? "Rahmat! Javoblaringiz qabul qilindi."
            : $"Rahmat! Siz {total} ta savoldan {score} tasiga to'g'ri javob berdingiz"
              + (string.IsNullOrEmpty(level) ? "." : $". Sizning darajangiz: {level}.");
        return new TestResultDto(score, total, percent, level, msg);
    }
}
