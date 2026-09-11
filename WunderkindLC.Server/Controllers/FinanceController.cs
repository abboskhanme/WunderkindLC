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
[AdminPerm("finance.main")]
[Route("api/admin/finance")]
public class FinanceController(AppDbContext db, AuditService audit, AutoMessageService autoMsg) : ControllerBase
{
    private async Task<Dictionary<string, string>> StudentNames() =>
        await db.Students.ToDictionaryAsync(s => s.Id, s => s.FullName);

    private async Task<Dictionary<string, string>> TeacherNames() =>
        await db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName);

    private async Task<Dictionary<string, string>> GroupNames() =>
        await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);

    private static FinanceTransactionDto ToDto(
        FinanceTransaction t,
        IReadOnlyDictionary<string, string> students,
        IReadOnlyDictionary<string, string> teachers,
        IReadOnlyDictionary<string, string>? groups = null,
        IReadOnlyDictionary<string, decimal>? refunded = null) =>
        new(t.Id, t.Date, t.Direction, t.Category, t.Amount, t.Note,
            t.StudentId, t.StudentId is not null && students.TryGetValue(t.StudentId, out var s) ? s : null,
            t.TeacherId, t.TeacherId is not null && teachers.TryGetValue(t.TeacherId, out var te) ? te : null,
            t.Month, t.GroupId, t.Comment, t.Method,
            t.GroupId is not null && groups is not null && groups.TryGetValue(t.GroupId, out var g) ? g : null,
            // Kiritilgan vaqt — UTC saqlangan CreatedAt'ni markaz mintaqasiga (UTC+5) o'tkazib beramiz.
            t.CreatedAt == default ? null : AppClock.ToLocal(t.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss"),
            refunded is not null && refunded.TryGetValue(t.Id, out var rf) ? rf : 0m,
            t.RefundOfId,
            t.ReceiptNo, t.PaidTime, t.CardLast4, t.CreatedBy, t.CreatedById);

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<FinanceTransactionDto>>> GetTransactions(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? direction, [FromQuery] string? category)
    {
        var query = db.FinanceTransactions.AsQueryable();
        if (!string.IsNullOrEmpty(from)) query = query.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrEmpty(to)) query = query.Where(t => string.Compare(t.Date, to) <= 0);
        if (!string.IsNullOrEmpty(direction)) query = query.Where(t => t.Direction == direction);
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);

        var list = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
        var students = await StudentNames();
        var teachers = await TeacherNames();
        var groups = await GroupNames();
        var refunded = await RefundedByPaymentAsync();
        return list.Select(t => ToDto(t, students, teachers, groups, refunded)).ToList();
    }

    /// <summary>Har asl to'lov (paymentId) uchun jami qaytarilgan summa (vozvrat yozuvlaridan).</summary>
    private async Task<Dictionary<string, decimal>> RefundedByPaymentAsync() =>
        (await db.FinanceTransactions
            .Where(t => t.Direction == "expense" && t.Category == "refund" && t.RefundOfId != null)
            .Select(t => new { t.RefundOfId, t.Amount }).ToListAsync())
        .GroupBy(t => t.RefundOfId!)
        .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

    /// <summary>Bitta to'lov uchun chek (termal kvitansiya) ma'lumotlari — barcha maydonlar + markaz sarlavhasi
    /// + chek sozlamalari (JSON). Frontend shu ma'lumotdan termal chekni chizadi/print qiladi.</summary>
    [HttpGet("receipt/{id}")]
    public async Task<ActionResult<ReceiptDto>> Receipt(string id)
    {
        var tx = await db.FinanceTransactions.FirstOrDefaultAsync(t => t.Id == id);
        if (tx is null) return NotFound();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();

        var studentName = tx.StudentId is null ? "" : (await db.Students.FindAsync(tx.StudentId))?.FullName ?? "";
        var groupName = "";
        var teacherName = "";
        if (tx.GroupId is not null && await db.Classes.FindAsync(tx.GroupId) is { } grp)
        {
            groupName = grp.Name;
            if (!string.IsNullOrWhiteSpace(grp.TeacherId))
                teacherName = (await db.Teachers.FindAsync(grp.TeacherId))?.FullName ?? "";
        }

        var dt = (tx.CreatedAt == default ? AppClock.Now : AppClock.ToLocal(tx.CreatedAt)).ToString("yyyy-MM-dd HH:mm");
        return new ReceiptDto(
            ReceiptNumber(tx.Id), dt, studentName, teacherName, tx.CreatedBy ?? "", groupName,
            tx.Method ?? "", string.IsNullOrWhiteSpace(tx.Comment) ? tx.Note : tx.Comment, tx.Amount,
            meta?.Name ?? "", meta?.Phone ?? "", meta?.Address ?? "", meta?.LogoUrl ?? "",
            meta?.CheckSettings ?? "",
            KvNo: tx.ReceiptNo);
    }

    /// <summary>GUID'dan barqaror 9 xonali chek raqami (lid sinov cheki ham shu formatdan foydalanadi).</summary>
    public static string ReceiptNumber(string guid)
    {
        var hash = 0;
        foreach (var c in guid) hash = unchecked(hash * 31 + c);
        return ((long)(uint)hash % 1_000_000_000).ToString("D9");
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<FinanceTransactionDto>> Create(FinanceTransactionPayload p)
    {
        if (p.Amount <= 0)
            return BadRequest(new { message = "Summa musbat bo'lishi kerak" });
        if (!PaymentFields.TryNormalizeTime(p.PaidTime, out var newPaidTime))
            return BadRequest(new { message = "To'lov vaqti noto'g'ri (HH:mm)" });
        if (!PaymentFields.TryNormalizeCardLast4(p.CardLast4, out var newCardLast4))
            return BadRequest(new { message = "Karta raqamining oxirgi 4 raqamini kiriting" });

        // QOG'OZ KVITANSIYA raqami BIR MARTA ishlatiladi (StudentsController.AddPayment bilan bir xil qoida).
        var newReceiptNo = PaymentFields.NormalizeReceiptNo(p.ReceiptNo);
        var dupReceipt = p.ForceReceipt ? null : await ReceiptGuard.FindDuplicateAsync(db, newReceiptNo);
        if (dupReceipt is not null)
            return Conflict(new { message = $"{newReceiptNo} kvitansiya raqami allaqachon kiritilgan", duplicate = dupReceipt });

        // IDEMPOTENCY CHECK: oxirgi 5 soniyada bir xil tranzaksiya bo'lsa — dublikat qo'shmasdan
        // mavjudni qaytaramiz (admin double-click yoki network retry uchun).
        // Shartlar: bir xil StudentId, Amount, Direction, Category, Type (tuition/salary/other),
        // Month va GroupId → bitta tranzaksiya (summa yoxud sana o'zgargan bo'lsa yangi).
        var txType = p.Category == "tuition" ? "tuition"
                   : (p.Category == "salary" ? "salary" : "other");
        var recentDuplicate = await db.FinanceTransactions
            .Where(t => t.StudentId == p.StudentId
                && t.TeacherId == p.TeacherId
                && t.Amount == p.Amount
                && t.Direction == p.Direction
                && t.Category == p.Category
                && t.Month == (string.IsNullOrWhiteSpace(p.Month) ? null : p.Month)
                && t.GroupId == (string.IsNullOrWhiteSpace(p.GroupId) ? null : p.GroupId))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (recentDuplicate != null
            && DateTime.UtcNow.Subtract(recentDuplicate.CreatedAt).TotalSeconds < 5)
        {
            // Idempotent: oxirgi 5s ichida bir xil qiymatli tranzaksiya — qaytaramiz.
            return ToDto(recentDuplicate, await StudentNames(), await TeacherNames());
        }

        var tx = new FinanceTransaction
        {
            Date = p.Date,
            Direction = p.Direction,
            Category = p.Category,
            Amount = p.Amount,
            Note = p.Note,
            StudentId = p.StudentId,
            TeacherId = p.TeacherId,
            // Tuition kirimi uchun oy/guruh teglari (foizli maosh + per-guruh hisobot shularga tayanadi).
            Month = string.IsNullOrWhiteSpace(p.Month) ? null : p.Month,
            GroupId = string.IsNullOrWhiteSpace(p.GroupId) ? null : p.GroupId,
            Comment = p.Comment,
            Method = string.IsNullOrWhiteSpace(p.Method) ? null : p.Method.Trim().ToLowerInvariant(),
            // Qog'oz kvitansiya raqami (naqd) + karta to'lovining haqiqiy vaqti.
            ReceiptNo = newReceiptNo,
            PaidTime = newPaidTime,
            CardLast4 = newCardLast4,
            CreatedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value, // mas'ul (chek uchun)
            CreatedById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        };
        db.FinanceTransactions.Add(tx);
        // O'quvchiga bog'langan tuition kirimi balansni oshiradi (izchillik — o'chirishda qaytariladi).
        await ApplyBalanceAsync(tx.StudentId, StudentBalanceEffect(tx));

        // To'lov qaysi guruh (kurs) uchun ekani — audit izohi va avto-xabar ({guruh}/{kurs}) uchun.
        var payGroup = tx.GroupId is null ? null : await db.Classes.FindAsync(tx.GroupId);
        var payTeacherName = string.IsNullOrEmpty(payGroup?.TeacherId) ? null
            : await db.Teachers.Where(t => t.Id == payGroup!.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync();
        var payCourseName = string.IsNullOrEmpty(payGroup?.CourseId) ? null
            : await db.Subjects.Where(su => su.Id == payGroup!.CourseId).Select(su => su.Name).FirstOrDefaultAsync();

        var dir = tx.Direction == "income" ? "Kirim" : "Chiqim";
        var summary = tx is { Category: "salary", TeacherId: not null }
            ? $"Maosh berildi: {AuditService.Money(tx.Amount)} so'm"
            : $"{dir} qo'shildi: {tx.Category} — {AuditService.Money(tx.Amount)} so'm"
                + (payGroup is null ? "" : $" — {payGroup.Name}")
                + (payTeacherName is null ? "" : $" · {payTeacherName}");
        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            summary, after: AuditService.Snapshot(tx), studentId: tx.StudentId, teacherId: tx.TeacherId);

        await db.SaveChangesAsync();

        // Avto xabar — o'quvchi tuition to'lovi qabul qilinganda ("To'lov qabul qilinganda" hodisasi):
        // yoqilgan qoidalar bo'yicha SMS + push + telegram. {sana} = to'lovning HAQIQIY sanasi
        // (tx.Date — orqaga sanalgan bo'lishi mumkin, bugun emas). {oy} = to'lov QAYSI OY uchun
        // (tx.Month — "yyyy-MM"), bugungi oy EMAS (masalan avgustda iyun oyi uchun to'lansa ham "iyun").
        if (tx is { Direction: "income", Category: "tuition", StudentId: not null })
        {
            var student = await db.Students.FindAsync(tx.StudentId);
            if (student is not null)
            {
                var monthName = tx.Month is { Length: >= 7 } tm && int.TryParse(tm.Substring(5, 2), out var mm)
                    ? MessageTokenizer.MonthNameUz(mm) : "";
                await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.PaymentReceived, student,
                    new Dictionary<string, string>
                    {
                        ["{summa}"] = MessageTokenizer.MoneyPlain(tx.Amount),
                        ["{sana}"] = tx.Date.Length >= 10 ? $"{tx.Date[8..10]}.{tx.Date[5..7]}.{tx.Date[..4]}" : tx.Date,
                        ["{oy}"] = monthName,
                        // {kurs}/{guruh} — to'lov qaysi guruh (kurs) uchun; har guruh to'lovi alohida xabar.
                        ["{kurs}"] = payCourseName ?? payGroup?.Name ?? "",
                        ["{guruh}"] = payGroup?.Name ?? student.ClassName,
                    },
                    group: payGroup);
            }
        }

        return ToDto(tx, await StudentNames(), await TeacherNames());
    }

    [HttpPut("transactions/{id}")]
    public async Task<ActionResult<FinanceTransactionDto>> Update(string id, FinanceTransactionPayload p)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();

        if (p.Amount <= 0)
            return BadRequest(new { message = "Summa musbat bo'lishi kerak" });

        // Kvitansiya raqami boshqa YOZUVDA band bo'lmasin (o'zining raqami — dublikat emas).
        if (p.ReceiptNo is not null && !p.ForceReceipt)
        {
            var upReceiptNo = PaymentFields.NormalizeReceiptNo(p.ReceiptNo);
            var upDup = await ReceiptGuard.FindDuplicateAsync(db, upReceiptNo, excludeTxId: id);
            if (upDup is not null)
                return Conflict(new { message = $"{upReceiptNo} kvitansiya raqami allaqachon kiritilgan", duplicate = upDup });
        }

        var before = AuditService.Snapshot(tx);
        // Eski balans ta'sirini (eski o'quvchida) hisoblab olamiz — tahrirdan keyin delta qo'llaymiz.
        var oldEffect = StudentBalanceEffect(tx);
        var oldStudentId = tx.StudentId;
        var changes = new List<string>();
        if (tx.Amount != p.Amount)
            changes.Add($"summa {AuditService.Money(tx.Amount)} → {AuditService.Money(p.Amount)} so'm");
        if (tx.Date != p.Date) changes.Add($"sana {tx.Date} → {p.Date}");
        if (tx.Direction != p.Direction) changes.Add($"yo'nalish {tx.Direction} → {p.Direction}");
        if (tx.Category != p.Category) changes.Add($"toifa {tx.Category} → {p.Category}");
        if (tx.Note != p.Note) changes.Add("izoh o'zgartirildi");

        tx.Date = p.Date;
        tx.Direction = p.Direction;
        tx.Category = p.Category;
        tx.Amount = p.Amount;
        tx.Note = p.Note;
        tx.StudentId = p.StudentId;
        tx.TeacherId = p.TeacherId;
        // Oy/guruh teglarini SAQLAB QOLAMIZ: tahrir formasi ularni yubormasa (bo'sh kelsa) eski qiymat qoladi —
        // aks holda tuition to'lovni tahrirlaganda Month/GroupId yo'qolib, foizli maosh + per-guruh hisobot buzilardi.
        if (!string.IsNullOrWhiteSpace(p.Month)) tx.Month = p.Month;
        if (!string.IsNullOrWhiteSpace(p.GroupId)) tx.GroupId = p.GroupId;
        if (p.Comment is not null) tx.Comment = p.Comment;
        if (!string.IsNullOrWhiteSpace(p.Method)) tx.Method = p.Method.Trim().ToLowerInvariant();
        // Kvitansiya raqami / to'lov vaqti — forma yubormasa (null) eski qiymat saqlanadi.
        if (p.ReceiptNo is not null) tx.ReceiptNo = PaymentFields.NormalizeReceiptNo(p.ReceiptNo);
        if (p.PaidTime is not null && PaymentFields.TryNormalizeTime(p.PaidTime, out var upPaidTime))
            tx.PaidTime = upPaidTime;
        if (p.CardLast4 is not null && PaymentFields.TryNormalizeCardLast4(p.CardLast4, out var upCardLast4))
            tx.CardLast4 = upCardLast4;

        // Balansni moslaymiz: o'quvchi o'zgarmasa — delta; o'zgarsa — eskidan qaytarib, yangisiga qo'llaymiz.
        var newEffect = StudentBalanceEffect(tx);
        if (oldStudentId == tx.StudentId)
            await ApplyBalanceAsync(tx.StudentId, newEffect - oldEffect);
        else
        {
            await ApplyBalanceAsync(oldStudentId, -oldEffect);
            await ApplyBalanceAsync(tx.StudentId, newEffect);
        }

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "update",
            changes.Count > 0 ? "Tahrirlandi: " + string.Join(", ", changes) : "Tahrirlandi",
            before: before, after: AuditService.Snapshot(tx),
            studentId: tx.StudentId, teacherId: tx.TeacherId);

        await db.SaveChangesAsync();
        return ToDto(tx, await StudentNames(), await TeacherNames());
    }

    /// <summary>O'qituvchilarga berilgan maoshlar hisoboti (davr bo'yicha): oylik, kerakli
    /// (oylik × davr oylari), berilgan va qoldiq.</summary>
    [HttpGet("salary-report")]
    public async Task<ActionResult<IEnumerable<SalaryReportRowDto>>> SalaryReport(
        [FromQuery] string? from, [FromQuery] string? to)
    {
        // Maosh o'quv yili boshidan hisoblanadi (yanvardan emas) — choraklardagi eng erta oydan.
        var fromMonth = string.IsNullOrEmpty(from)
            ? await TuitionService.AcademicYearStartMonthAsync(db) : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        var teachers = await db.Teachers.OrderBy(t => t.FullName).ToListAsync();
        // YAGONA MANTIQ: har o'qituvchi uchun SalaryLedger ishlatamiz — u "fixed" ham, "percent" (guruh
        // to'lovidan foiz) ham hisoblaydi. Ilgari bu yer faqat te.Salary'ga tayanardi → foizli oylik
        // moliyada 0 bo'lib ko'rinmasdi (bug).
        var result = new List<SalaryReportRowDto>();
        foreach (var te in teachers)
        {
            var ledger = await Application.Services.SalaryLedger.BuildAsync(db, te, from, to);
            result.Add(new SalaryReportRowDto(
                te.Id, te.FullName, ledger.Salary, ledger.TotalPaid, ledger.Payments.Count,
                ledger.Months.Count, ledger.TotalExpected, ledger.Remaining,
                te.SalaryMode, te.SalaryPercent,
                ledger.TotalDeduction, ledger.Months.Sum(m => m.MissedLessons)));
        }
        return result;
    }

    /// <summary>O'quvchilar bo'yicha to'lov hisoboti (joriy holat):
    /// Hisoblangan (to'liq guruh narxi yig'indi), Chegirma (berilgan), To'langan (HAQIQIY naqd to'lov),
    /// Qarz va Avans. Eng katta qarzdorlar yuqorida.</summary>
    [HttpGet("student-report")]
    public async Task<ActionResult<IEnumerable<StudentFinanceRowDto>>> StudentReport()
    {
        var students = await db.Students.ToListAsync();
        var charges = await db.MonthlyCharges.ToListAsync();
        var chargedByStudent = charges.GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var discountByStudent = charges.GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Discount));
        // Haqiqiy naqd to'lov (NET) — chegirma TA'SIR QILMAYDI. VOZVRAT (expense+refund) ayriladi:
        // qaytarilgan pul "to'langan"dan chiqadi (to'langan = kirim tuition − vozvrat).
        var paidByStudent = (await db.FinanceTransactions
                .Where(t => t.StudentId != null
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .Select(t => new { t.StudentId, t.Amount, t.Direction })
                .ToListAsync())
            .GroupBy(t => t.StudentId!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Direction == "expense" ? -x.Amount : x.Amount));

        // GURUH USTUNI — TIRIK a'zoliklardan (pul raqamlariga TEGMAYDI, faqat ko'rsatish).
        // ⚠️ Ilgari `Student.ClassName` chiqarilardi: eski guruhida muzlatilib yangisida
        // o'qiyotgan o'quvchi moliya hisobotida ESKI guruh nomi bilan turardi.
        var liveMemberships = await db.StudentGroups.AsNoTracking().Where(m => m.IsActive).ToListAsync();
        var groupNames = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
        var groupsByStudent = liveMemberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key,
                g => string.Join(", ", StudentMembershipView.DisplayGroupNames(g, groupNames)));

        return students.Select(s =>
        {
            var charged = chargedByStudent.GetValueOrDefault(s.Id, 0m);
            var discount = discountByStudent.GetValueOrDefault(s.Id, 0m);
            var paid = paidByStudent.GetValueOrDefault(s.Id, 0m);
            var debt = s.Balance < 0 ? -s.Balance : 0m;
            var advance = s.Balance > 0 ? s.Balance : 0m;
            var groups = groupsByStudent.GetValueOrDefault(s.Id, "");
            return new StudentFinanceRowDto(
                s.Id, s.FullName, groups.Length > 0 ? groups : s.ClassName,
                charged, discount, paid, debt, advance,
                s.DiscountPct, s.DiscountAmount);
        }).OrderByDescending(r => r.Debt).ThenBy(r => r.FullName).ToList();
    }

    /// <summary>Tranzaksiyaning o'quvchi BALANSIGA ta'siri: o'quvchiga bog'langan tuition KIRIMI balansni
    /// shu summaga oshiradi (qarzni kamaytiradi); VOZVRAT (expense+refund) esa shu summaga KAMAYTIRADI
    /// (avansni qaytaradi) — o'chirilganda teskarisi qo'llanadi. Boshqa turdagilar (maosh/guruhsiz) — 0.</summary>
    private static decimal StudentBalanceEffect(FinanceTransaction tx)
    {
        if (string.IsNullOrEmpty(tx.StudentId)) return 0m;
        if (tx.Direction == "income" && tx.Category == "tuition") return tx.Amount;
        if (tx.Direction == "expense" && tx.Category == "refund") return -tx.Amount;
        return 0m;
    }

    private async Task ApplyBalanceAsync(string? studentId, decimal delta)
    {
        if (delta == 0m || string.IsNullOrEmpty(studentId)) return;
        var st = await db.Students.FindAsync(studentId);
        if (st is not null) st.Balance += delta;
    }

    /// <summary>
    /// O'QUVCHI TO'LOVINI tahrirlash ("To'lovlar" bo'limidagi qalam tugmasi) — "Moliya" ruxsati
    /// (tahrir amali) kerak; superadmin/admin har doim, xodim faqat shu ruxsat berilgan bo'lsa.
    /// Sana, summa, qaysi oy uchun, qaysi guruh, to'lov usuli va kassir izohi o'zgartiriladi.
    ///
    /// Tahrir HAMMA bog'liq joyni moslaydi:
    ///   • <b>Balans</b> — summa farqi (yangi − eski) o'quvchi balansiga qo'llanadi;
    ///   • <b>Oylik hisob</b> — yangi (guruh, oy) uchun hisob yo'q bo'lsa ochiladi
    ///     (<see cref="TuitionService.EnsureChargeAsync"/>, to'lov qabul qilishdagi kabi avans mantiqi);
    ///   • <b>Izoh</b> — avtomatik izoh (oy/guruh yozilgan) yangi qiymatlar bilan qayta yoziladi;
    ///   • <b>Hisobotlar</b> (o'quvchi qarzi, guruh/kurs tushumi, o'qituvchining FOIZLI maoshi, chek)
    ///     to'lovdan agregat qilinadi — saqlanmaydi, shuning uchun avtomatik yangilanadi;
    ///   • <b>Audit</b> — "update" yozuvi o'zgarishlar ro'yxati + before/after surati bilan.
    ///
    /// O'quvchini almashtirish bu yerda MUMKIN EMAS (o'chirib qaytadan kiritiladi).
    /// Eski oyning hisobi (agar to'lov boshqa oyga ko'chirilsa) AVTOMATIK o'chirilmaydi — u
    /// o'quvchining haqiqiy o'quv oyi bo'lishi mumkin; kerak bo'lsa qo'lda tahrirlanadi.
    /// </summary>
    [HttpPut("payments/{id}")]
    public async Task<ActionResult<FinanceTransactionDto>> UpdatePayment(string id, PaymentEditPayload p)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();
        if (tx.Direction != "income" || tx.Category != "tuition" || string.IsNullOrEmpty(tx.StudentId))
            return BadRequest(new { message = "Bu yozuv o'quvchi to'lovi emas — uni bu yerda tahrirlab bo'lmaydi" });

        var student = await db.Students.FindAsync(tx.StudentId);
        if (student is null) return BadRequest(new { message = "O'quvchi topilmadi" });

        if (p.Amount <= 0) return BadRequest(new { message = "Summa musbat bo'lishi kerak" });

        var date = (p.Date ?? "").Trim();
        if (!DateOnly.TryParse(date, out var parsed))
            return BadRequest(new { message = "Sana noto'g'ri" });
        date = parsed.ToString("yyyy-MM-dd");
        if (string.CompareOrdinal(date, AppClock.Today.ToString("yyyy-MM-dd")) > 0)
            return BadRequest(new { message = "Kelajak sanaga to'lov kiritib bo'lmaydi" });

        var month = (p.Month ?? "").Trim();
        if (month.Length < 7) return BadRequest(new { message = "To'lov qaysi oy uchun ekanini tanlang" });
        month = month[..7];

        // Guruh: bo'sh = guruhsiz. Aks holda o'quvchining a'zoligi bo'lishi (yoki eski tegi) shart.
        var groupId = string.IsNullOrWhiteSpace(p.GroupId) ? null : p.GroupId.Trim();
        if (groupId is not null && groupId != tx.GroupId)
        {
            var isMember = await db.StudentGroups.AnyAsync(sg => sg.StudentId == student.Id && sg.GroupId == groupId);
            if (!isMember) return BadRequest(new { message = "Tanlangan guruh o'quvchiga tegishli emas" });
        }

        var before = AuditService.Snapshot(tx);
        var groupNames = await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);
        string GName(string? gid) => gid is null ? "guruhsiz" : groupNames.GetValueOrDefault(gid, gid);

        var changes = new List<string>();
        if (tx.Amount != p.Amount) changes.Add($"summa {AuditService.Money(tx.Amount)} → {AuditService.Money(p.Amount)} so'm");
        if (tx.Date != date) changes.Add($"sana {tx.Date} → {date}");
        if (tx.Month != month) changes.Add($"oy {tx.Month ?? "—"} → {month}");
        if (tx.GroupId != groupId) changes.Add($"guruh {GName(tx.GroupId)} → {GName(groupId)}");
        var method = string.IsNullOrWhiteSpace(p.Method) ? null : p.Method.Trim().ToLowerInvariant();
        if (tx.Method != method) changes.Add($"usul {tx.Method ?? "—"} → {method ?? "—"}");
        var comment = string.IsNullOrWhiteSpace(p.Comment) ? null : p.Comment.Trim();
        if (tx.Comment != comment) changes.Add("izoh o'zgartirildi");
        // Qog'oz kvitansiya raqami (naqd) va to'lov vaqti (karta) — tuzatish mumkin, lekin yangi raqam
        // BOSHQA to'lovda band bo'lmasligi kerak (o'zining raqami — dublikat emas).
        var receiptNo = PaymentFields.NormalizeReceiptNo(p.ReceiptNo);
        if (tx.ReceiptNo != receiptNo)
        {
            var editDup = p.ForceReceipt ? null : await ReceiptGuard.FindDuplicateAsync(db, receiptNo, excludeTxId: id);
            if (editDup is not null)
                return Conflict(new { message = $"{receiptNo} kvitansiya raqami allaqachon kiritilgan", duplicate = editDup });
            changes.Add($"kvitansiya {tx.ReceiptNo ?? "—"} → {receiptNo ?? "—"}");
        }
        if (!PaymentFields.TryNormalizeTime(p.PaidTime, out var editPaidTime))
            return BadRequest(new { message = "To'lov vaqti noto'g'ri (HH:mm)" });
        if (tx.PaidTime != editPaidTime) changes.Add($"to'lov vaqti {tx.PaidTime ?? "—"} → {editPaidTime ?? "—"}");
        if (!PaymentFields.TryNormalizeCardLast4(p.CardLast4, out var editCardLast4))
            return BadRequest(new { message = "Karta raqamining oxirgi 4 raqamini kiriting" });
        if (tx.CardLast4 != editCardLast4) changes.Add($"karta raqami {tx.CardLast4 ?? "—"} → {editCardLast4 ?? "—"}");

        // 1) BALANS — faqat summa farqi (o'quvchi va toifa o'zgarmaydi).
        student.Balance += p.Amount - tx.Amount;

        // 2) Yozuvning o'zi.
        var oldAutoNote = $"O'quvchi to'lovi ({tx.Month})"
            + (tx.GroupId is null ? "" : $" [{GName(tx.GroupId)}]")
            + $" — {student.FullName}";
        var wasAutoNote = string.IsNullOrWhiteSpace(tx.Note) || tx.Note == oldAutoNote;

        tx.Date = date;
        tx.Amount = p.Amount;
        tx.Month = month;
        tx.GroupId = groupId;
        tx.Method = method;
        tx.Comment = comment;
        tx.ReceiptNo = receiptNo;
        tx.PaidTime = editPaidTime;
        tx.CardLast4 = editCardLast4;
        if (wasAutoNote)
            tx.Note = $"O'quvchi to'lovi ({month})"
                + (groupId is null ? "" : $" [{GName(groupId)}]")
                + $" — {student.FullName}";

        // 3) OYLIK HISOB — yangi (guruh, oy) hisobi yo'q bo'lsa ochamiz (to'lov qabul qilishdagi avans mantiqi).
        await TuitionService.EnsureChargeAsync(db, student, groupId, month);

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "update",
            changes.Count > 0
                ? $"To'lov tahrirlandi ({student.FullName}): " + string.Join(", ", changes)
                : $"To'lov tahrirlandi ({student.FullName})",
            before: before, after: AuditService.Snapshot(tx), studentId: tx.StudentId);

        await db.SaveChangesAsync();
        return ToDto(tx, await StudentNames(), await TeacherNames());
    }

    /// <summary>
    /// O'quvchi to'lovini (income+tuition) qisman/to'liq VOZVRAT (pul qaytarish) — "Moliya" ruxsati
    /// (qo'shish amali) kerak; superadmin/admin har doim, xodim faqat shu ruxsat berilgan bo'lsa.
    ///
    /// Muzlatish bilan bog'liqlik: o'quvchi oy o'rtasida MUZLATILGANDA shu oy hisobi qatnashilgan darslarga
    /// qayta hisoblanadi (<see cref="TuitionService.ChargeFreezeProrateAsync"/>) va o'quvchida AVANS (ortiqcha
    /// to'lov) paydo bo'ladi. Shu avans aynan qaytariladigan summa — vozvrat balansni SHU miqdorda kamaytiradi,
    /// natijada balans 0 ga tushadi. O'qituvchining foizli maoshi net (to'langan − vozvrat) dan hisoblanadi
    /// (<see cref="SalaryLedger"/>, <see cref="CourseFinanceReport"/> vozvratni ayiradi — avtomatik).
    ///
    /// Vozvrat alohida yozuv sifatida saqlanadi (Direction="expense", Category="refund", RefundOfId=asl to'lov),
    /// shuning uchun kassa chiqimi (Summary/Monthly) va "Vozvratlar tarixi"da ko'rinadi. Bir to'lovni bir necha
    /// marta (qisman) qaytarish mumkin — jami asl to'lov summasidan oshmaydi.
    /// </summary>
    [HttpPost("payments/{id}/refund")]
    public async Task<ActionResult<FinanceTransactionDto>> Refund(string id, RefundPayload p)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();
        if (tx.Direction != "income" || tx.Category != "tuition" || string.IsNullOrEmpty(tx.StudentId))
            return BadRequest(new { message = "Vozvrat faqat o'quvchi to'lovi (tuition) uchun qilinadi" });
        if (tx.RefundOfId != null)
            return BadRequest(new { message = "Bu yozuvning o'zi vozvrat — undan vozvrat qilib bo'lmaydi" });

        var student = await db.Students.FindAsync(tx.StudentId);
        if (student is null) return BadRequest(new { message = "O'quvchi topilmadi" });

        if (p.Amount <= 0) return BadRequest(new { message = "Vozvrat summasi musbat bo'lishi kerak" });

        // Shu to'lovdan avval qancha qaytarilgan — jami asl summadan oshmasin.
        var already = await db.FinanceTransactions
            .Where(r => r.Direction == "expense" && r.Category == "refund" && r.RefundOfId == id)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;
        var refundable = tx.Amount - already;
        if (refundable <= 0)
            return BadRequest(new { message = "Bu to'lov to'liq qaytarilgan" });
        if (p.Amount > refundable)
            return BadRequest(new { message = $"Ko'pi bilan {AuditService.Money(refundable)} so'm qaytarish mumkin" });

        var date = string.IsNullOrWhiteSpace(p.Date) ? AppClock.Today.ToString("yyyy-MM-dd") : p.Date.Trim();
        if (!DateOnly.TryParse(date, out var parsed)) return BadRequest(new { message = "Sana noto'g'ri" });
        date = parsed.ToString("yyyy-MM-dd");
        if (string.CompareOrdinal(date, AppClock.Today.ToString("yyyy-MM-dd")) > 0)
            return BadRequest(new { message = "Kelajak sanaga vozvrat kiritib bo'lmaydi" });

        var reason = string.IsNullOrWhiteSpace(p.Reason) ? null : p.Reason.Trim();
        var refund = new FinanceTransaction
        {
            Date = date,
            Direction = "expense",
            Category = "refund",
            Amount = p.Amount,
            StudentId = tx.StudentId,
            GroupId = tx.GroupId,     // o'qituvchi foizini to'g'ri guruhdan ayirish uchun asl tegni ko'chiramiz
            Method = tx.Method,       // pul qaysi shaklda kelgan bo'lsa shunday qaytadi ("Naqd jami" to'g'ri hisoblansin)
            Month = tx.Month,
            RefundOfId = tx.Id,
            Note = $"Vozvrat ({student.FullName})" + (reason is null ? "" : $" — {reason}"),
            Comment = reason,
            CreatedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
            CreatedById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        };
        db.FinanceTransactions.Add(refund);

        // Balans: vozvrat to'lovga TESKARI — muzlatishdan hosil bo'lgan avansni qaytaradi (balans kamayadi).
        student.Balance -= p.Amount;

        audit.Record(AuditService.EntityFinanceTransaction, refund.Id, "create",
            $"Vozvrat qaytarildi ({student.FullName}): {AuditService.Money(p.Amount)} so'm"
                + (reason is null ? "" : $" — sabab: {reason}"),
            after: AuditService.Snapshot(refund), studentId: tx.StudentId);

        await db.SaveChangesAsync();

        var students = await StudentNames();
        var teachers = await TeacherNames();
        var groups = await GroupNames();
        return ToDto(refund, students, teachers, groups);
    }

    /// <summary>VOZVRATLAR TARIXI — qaytarilgan pullar (Direction=expense, Category=refund), asl to'lov ma'lumoti bilan.</summary>
    [HttpGet("refunds")]
    public async Task<ActionResult<IEnumerable<RefundDto>>> Refunds(
        [FromQuery] string? from, [FromQuery] string? to)
    {
        var query = db.FinanceTransactions
            .Where(t => t.Direction == "expense" && t.Category == "refund");
        if (!string.IsNullOrEmpty(from)) query = query.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrEmpty(to)) query = query.Where(t => string.Compare(t.Date, to) <= 0);
        var list = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();

        var students = await StudentNames();
        var groups = await GroupNames();
        // Asl to'lovlar (RefundOfId) — summa/sana ko'rsatish uchun.
        var paymentIds = list.Where(t => t.RefundOfId != null).Select(t => t.RefundOfId!).Distinct().ToList();
        var payments = (await db.FinanceTransactions.Where(t => paymentIds.Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id);

        return list.Select(t =>
        {
            payments.TryGetValue(t.RefundOfId ?? "", out var pay);
            return new RefundDto(
                t.Id, t.Date, t.Amount, t.StudentId,
                t.StudentId is not null && students.TryGetValue(t.StudentId, out var s) ? s : null,
                t.GroupId, t.GroupId is not null && groups.TryGetValue(t.GroupId, out var g) ? g : null,
                t.Month, t.Comment,
                t.RefundOfId, pay?.Amount, pay?.Date, t.CreatedBy,
                t.CreatedAt == default ? null : AppClock.ToLocal(t.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss"));
        }).ToList();
    }

    [HttpDelete("transactions/{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? reasonId = null)
    {
        var tx = await db.FinanceTransactions.FindAsync(id);
        if (tx is null) return NotFound();

        var reason = string.IsNullOrWhiteSpace(reasonId) ? "" : (await db.ActionReasons.Where(r => r.Id == reasonId).Select(r => r.Label).FirstOrDefaultAsync() ?? "");
        var dir = tx.Direction == "income" ? "Kirim" : "Chiqim";
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
        // To'lov o'quvchiga tegishli bo'lsa — arxiv sarlavhasida kimning to'lovi ekanini ko'rsatamiz.
        var sName = tx.StudentId is null ? null : await db.Students.Where(s => s.Id == tx.StudentId).Select(s => s.FullName).FirstOrDefaultAsync();
        ArchiveService.Snapshot(db, "finance", tx.Id,
            sName != null ? $"{sName} — to'lov" : $"{(tx.Direction == "income" ? "Kirim" : "Chiqim")} {tx.Category}",
            $"{tx.Amount} so'm" + (string.IsNullOrEmpty(tx.Month) ? "" : $" · {tx.Month}"),
            tx, reason.Length > 0 ? reason : null, actor);
        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "delete",
            $"O'chirildi: {dir} {tx.Category} — {AuditService.Money(tx.Amount)} so'm" + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            before: AuditService.Snapshot(tx), studentId: tx.StudentId, teacherId: tx.TeacherId);

        // To'lov o'chirilsa — o'quvchi balansiga qo'shilgan summani QAYTARAMIZ (qarz tiklanadi).
        await ApplyBalanceAsync(tx.StudentId, -StudentBalanceEffect(tx));
        db.FinanceTransactions.Remove(tx);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Tanlangan davr bo'yicha umumiy moliyaviy xulosa.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<FinanceSummaryDto>> Summary([FromQuery] string? from, [FromQuery] string? to)
    {
        var query = db.FinanceTransactions.AsQueryable();
        if (!string.IsNullOrEmpty(from)) query = query.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrEmpty(to)) query = query.Where(t => string.Compare(t.Date, to) <= 0);
        var txs = await query.ToListAsync();

        var income = txs.Where(t => t.Direction == "income").ToList();
        var expense = txs.Where(t => t.Direction == "expense").ToList();

        var totalIncome = income.Sum(t => t.Amount);
        var totalExpense = expense.Sum(t => t.Amount);
        var tuition = income.Where(t => t.Category == "tuition").Sum(t => t.Amount);

        var incomeByCat = income.GroupBy(t => t.Category)
            .Select(g => new CategoryAmountDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(c => c.Amount).ToList();
        var expenseByCat = expense.GroupBy(t => t.Category)
            .Select(g => new CategoryAmountDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(c => c.Amount).ToList();

        var balances = await db.Students.Select(s => s.Balance).ToListAsync();
        var studentDebt = balances.Where(b => b < 0).Sum(b => -b);
        var studentAdvance = balances.Where(b => b > 0).Sum();

        return new FinanceSummaryDto(
            totalIncome, totalExpense, totalIncome - totalExpense,
            tuition, totalIncome - tuition,
            incomeByCat, expenseByCat,
            studentDebt, studentAdvance, txs.Count);
    }

    /// <summary>Kurs/guruh kesimida moliyaviy hisobot: qaysi kurs ko'p daromad keltiradi,
    /// qaysi kurs o'quvchilari to'lovni to'liq qildi, qaysi guruh (o'qituvchi) to'lov yig'ishda faolroq.</summary>
    [HttpGet("course-report")]
    public async Task<ActionResult<CourseFinanceReportDto>> CourseReport(
        [FromQuery] string? from, [FromQuery] string? to) =>
        await CourseFinanceReport.BuildAsync(db, from, to);

    /// <summary>Bitta guruh ichidagi to'lov holati — kim to'ladi, kim to'lamadi (davr bo'yicha).</summary>
    [HttpGet("group-payments/{groupId}")]
    public async Task<ActionResult<GroupPaymentsReportDto>> GroupPayments(
        string groupId, [FromQuery] string? from, [FromQuery] string? to) =>
        await CourseFinanceReport.BuildGroupPaymentsAsync(db, groupId, from, to);

    /// <summary>O'qituvchining BARCHA guruhlari bo'yicha to'lov holati — bitta guruhnikidek ko'rinish,
    /// lekin o'quvchilar hamma guruhlaridan yig'ilgan holda ("Barchasini ko'rish").</summary>
    [HttpGet("teacher-payments/{teacherId}")]
    public async Task<ActionResult<GroupPaymentsReportDto>> TeacherPayments(
        string teacherId, [FromQuery] string? from, [FromQuery] string? to) =>
        await CourseFinanceReport.BuildTeacherPaymentsAsync(db, teacherId, from, to);

    /// <summary>
    /// KASSIRLAR KESIMI — davr ichida kim qancha pul qabul qilgan (soni, jami, naqd/karta/bank).
    /// Moliya → "Kassirlar" jadvali. Eski (CreatedById'siz) yozuvlar ism bo'yicha guruhlanadi.
    /// </summary>
    [HttpGet("cashiers")]
    public async Task<ActionResult<IEnumerable<CashierSummaryDto>>> Cashiers(
        [FromQuery] string? from, [FromQuery] string? to)
    {
        if (!CanSeeCashiers()) return Forbid();
        return await CashierReport.SummaryAsync(db, from ?? "", to ?? "");
    }

    /// <summary>
    /// Kassirlar hisobotini KIM ko'radi: admin/superadmin yoki "moliya" ruxsati berilgan xodim.
    /// DIQQAT: odatda xodimga GET har doim ochiq (<see cref="AdminPermAttribute"/>), lekin bu
    /// yerda ATAYIN qattiqroq — kassir boshqa kassirlarning tushumini ko'rmasligi kerak
    /// (o'zinikini <c>GET /api/admin/kassa/my-payments</c> orqali ko'radi).
    /// </summary>
    private bool CanSeeCashiers() =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin) ||
        User.Claims.Any(c => c.Type == AdminPermAttribute.ClaimType
                             && (c.Value == "finance" || c.Value.StartsWith("finance:")));

    /// <summary>Bitta kassir kiritgan to'lovlar ro'yxati (jadvaldagi qatorni bosganda) + jami.
    /// Kalit: <paramref name="cashierId"/> (yangi yozuvlar) yoki <paramref name="cashierName"/> (eski).</summary>
    [HttpGet("cashier-payments")]
    public async Task<ActionResult<CashierPaymentsDto>> CashierPayments(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? cashierId, [FromQuery] string? cashierName)
    {
        if (!CanSeeCashiers()) return Forbid();
        return await CashierReport.PaymentsAsync(db, from ?? "", to ?? "", cashierId, cashierName);
    }

    /// <summary>
    /// "KIRITGAN" FILTRI ro'yxati — to'lov kirita oladigan xodim/adminlarning HAMMASI (hali to'lov
    /// kiritmagan bo'lsa ham) + davr ichida to'lov kiritgan eski/ruxsati olib tashlangan akkauntlar.
    /// Moliya → "To'lovlar" tabidagi "Kiritgan: barchasi" tanlovi shu ro'yxatdan to'ladi.
    /// </summary>
    [HttpGet("payment-authors")]
    public async Task<ActionResult<IEnumerable<PaymentAuthorDto>>> PaymentAuthors(
        [FromQuery] string? from, [FromQuery] string? to)
    {
        // Kim kiritgani kesimi — "Kassirlar" hisoboti bilan bir xil darajadagi ma'lumot.
        if (!CanSeeCashiers()) return Forbid();
        return await CashierReport.AuthorsAsync(db, from ?? "", to ?? "");
    }

    /// <summary>Oylik to'lovni qo'lda hisoblash. month berilmasa — hisoblanmagan barcha oylar.</summary>
    [HttpPost("accrue")]
    public async Task<ActionResult<AccrueResultDto>> Accrue([FromQuery] string? month)
    {
        if (!string.IsNullOrEmpty(month))
        {
            var (count, total, created) = await TuitionService.AccrueMonth(db, month);
            // Avto xabar — har YANGI hisob uchun ota-onaga ("Oylik hisob yaratilganda" hodisasi).
            await autoMsg.DispatchMonthlyChargesAsync(db,
                created.Select(c => (c.StudentId, month, c.Amount)).ToList());
            return new AccrueResultDto([month], count, total);
        }

        var (accrued, createdDue) = await TuitionService.AccrueDue(db);
        await autoMsg.DispatchMonthlyChargesAsync(db, createdDue);
        var sum = accrued.Count == 0
            ? 0m
            : await db.MonthlyCharges.Where(c => accrued.Contains(c.Month)).SumAsync(c => c.Amount);
        var cnt = accrued.Count == 0
            ? 0
            : await db.MonthlyCharges.CountAsync(c => accrued.Contains(c.Month));
        return new AccrueResultDto(accrued, cnt, sum);
    }

    /// <summary>Bir yil bo'yicha oylik kirim/chiqim (grafik uchun, 12 oy).</summary>
    [HttpGet("monthly")]
    public async Task<ActionResult<IEnumerable<FinanceMonthlyDto>>> Monthly([FromQuery] int? year)
    {
        var y = year ?? AppClock.Now.Year;
        var prefix = y.ToString("D4") + "-";
        var txs = await db.FinanceTransactions
            .Where(t => t.Date.StartsWith(prefix))
            .ToListAsync();

        var result = new List<FinanceMonthlyDto>();
        for (var m = 1; m <= 12; m++)
        {
            var month = $"{y:D4}-{m:D2}";
            var monthTxs = txs.Where(t => t.Date.StartsWith(month)).ToList();
            result.Add(new FinanceMonthlyDto(
                month,
                monthTxs.Where(t => t.Direction == "income").Sum(t => t.Amount),
                monthTxs.Where(t => t.Direction == "expense").Sum(t => t.Amount)));
        }
        return result;
    }

    /* ================= MOLIYA → "BONUS" TABI (o'quvchini ushlab turish bonuslari) =================
     *
     * DIQQAT: bonus PUL CHIQIMI EMAS. RetentionBonusAward/Share — bu faqat QAYD; ular
     * FinanceTransaction ham, SalaryLedger ham EMAS. Shuning uchun bu hisobot Moliyaning
     * kirim/chiqim/summary raqamlariga UMUMAN ta'sir qilmaydi va alohida turadi — haqiqiy pul
     * o'qituvchiga maosh to'lovi orqali beriladi. Bu yerdagi endpointlar FAQAT O'QISH uchun;
     * bonus BERISH/bekor qilish "O'quvchilar → Bonus hisoboti" (RetentionBonusController) da qoladi.
     *
     * RUXSAT: sinfdagi [AdminPerm("finance.main")] yetadi — GET xodim uchun ochiq (loyiha qoidasi).
     * Kassirlar hisobotidagi kabi qattiqroq CanSeeCashiers() gate KERAK EMAS: bonus qaydi
     * kassirlar tushumi kabi maxfiy emas.
     */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Bonuslar hisoboti: davr ichida qaysi o'qituvchi qancha bonus oldi, qaysi oyda qancha berildi
    /// va har bir ulushning batafsil qatori. Davr bonus BERILGAN sana (<c>CreatedAt</c>) bo'yicha.
    /// </summary>
    [HttpGet("retention-bonuses")]
    public async Task<ActionResult<RetentionBonusFinanceDto>> RetentionBonuses(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct) =>
        await BuildRetentionBonusReportAsync(from, to, ct);

    /// <summary>Bonuslar hisobotini Excel'ga yuklash (batafsil ro'yxat — har ulush bitta qator).</summary>
    [HttpGet("retention-bonuses/export")]
    public async Task<IActionResult> ExportRetentionBonuses(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var report = await BuildRetentionBonusReportAsync(from, to, ct);

        var headers = new List<string>
        {
            "Berilgan sana", "Berilgan oy", "O'qituvchi", "O'quvchi", "Fan",
            "Davr", "Oy", "Summa", "Holat", "Kim bergan",
        };
        var rows = report.Rows.Select(r => (IReadOnlyList<string>)new List<string>
        {
            r.GivenAt.ToString("yyyy-MM-dd HH:mm"),
            r.GivenMonth,
            r.TeacherName,
            r.StudentName,
            r.CourseName,
            $"{r.PeriodFrom} — {r.PeriodTo}",
            r.Months.ToString("0.##"),
            AuditService.Money(r.Amount),
            r.Status == RetentionBonusAward.StatusCancelled ? "Bekor qilingan" : "Berilgan",
            r.GivenBy,
        }).ToList();

        return File(ExcelExport.Build("Bonuslar", headers, rows), XlsxMime,
            $"bonus_moliya_{report.From}_{report.To}.xlsx");
    }

    /// <summary>
    /// Hisobotni yig'ish. <paramref name="from"/>/<paramref name="to"/> — "YYYY-MM" (ikkalasi
    /// inklyuziv); bo'sh bo'lsa joriy yil boshidan joriy oygacha (FinancePage'dagi standart davr
    /// ham shunday — yanvardan bugungacha).
    /// </summary>
    private async Task<RetentionBonusFinanceDto> BuildRetentionBonusReportAsync(
        string? from, string? to, CancellationToken ct)
    {
        // Davr chegaralari. "YYYY-MM-DD" berilsa ham ishlasin — birinchi 7 belgi olinadi.
        var now = AppClock.Now;
        var fromMonth = NormalizeMonth(from) ?? $"{now.Year:D4}-01";
        var toMonth = NormalizeMonth(to) ?? $"{now.Year:D4}-{now.Month:D2}";
        if (string.CompareOrdinal(fromMonth, toMonth) > 0) (fromMonth, toMonth) = (toMonth, fromMonth);

        // CreatedAt — AppClock.Now bilan yoziladi, ya'ni MARKAZ vaqti (UTC+5) sifatida saqlanadi
        // (Program.cs: Npgsql.EnableLegacyTimestampBehavior → `timestamp` timezonesiz). Shuning
        // uchun bu yerda AppClock.ToLocal QO'LLANMAYDI — qiymat allaqachon local (RetentionBonusService
        // ham CreatedAt'ni aynan shunday, o'zgartirmasdan qaytaradi).
        var start = MonthStart(fromMonth);
        var endExclusive = MonthStart(toMonth).AddMonths(1);

        var awards = await db.RetentionBonusAwards.AsNoTracking()
            .Where(a => a.CreatedAt >= start && a.CreatedAt < endExclusive)
            .ToListAsync(ct);

        // N+1 bo'lmasin: barcha ulushlar BITTA so'rovda (award id'lari bo'yicha).
        var awardIds = awards.Select(a => a.Id).ToList();
        var shares = awardIds.Count == 0
            ? []
            : await db.RetentionBonusShares.AsNoTracking()
                .Where(s => awardIds.Contains(s.AwardId))
                .ToListAsync(ct);

        var awardById = awards.ToDictionary(a => a.Id);

        // HAR ULUSH — bitta qator (award × o'qituvchi), eng yangisi tepada.
        var rows = shares
            .Where(s => awardById.ContainsKey(s.AwardId))
            .Select(s =>
            {
                var a = awardById[s.AwardId];
                return new RetentionBonusFinanceRowDto(
                    a.Id, a.CreatedAt.ToString("yyyy-MM"),
                    s.TeacherId, s.TeacherName,
                    a.StudentId, a.StudentName, a.CourseName,
                    a.PeriodFrom, a.PeriodTo,
                    s.Months, s.Amount,
                    a.Status, a.CreatedAt, a.GivenBy);
            })
            .OrderByDescending(r => r.GivenAt)
            .ThenBy(r => r.TeacherName)
            .ToList();

        // Kesimlar FAQAT "given" bo'yicha — bekor qilingan bonus hech qayerda jamiga qo'shilmaydi.
        var given = rows.Where(r => r.Status == RetentionBonusAward.StatusGiven).ToList();
        var cancelled = rows.Where(r => r.Status != RetentionBonusAward.StatusGiven).ToList();

        // SONLAR — BONUSLAR soni, ulushlar soni EMAS. Qator = award × o'qituvchi bo'lgani uchun
        // ikki o'qituvchiga bo'lingan BITTA bonus qatorlarni sanaganda IKKI marta chiqardi
        // ("Bonuslar soni: 2" — aslida 1 ta bonus). Summalar to'g'ri edi (ulushlar yig'indisi =
        // bonus summasi), faqat SANOQ noto'g'ri edi — shuning uchun award id'lari bo'yicha Distinct.
        var byTeacher = given
            .GroupBy(r => new { r.TeacherId, r.TeacherName })
            .Select(g => new RetentionBonusByTeacherDto(
                g.Key.TeacherId, g.Key.TeacherName,
                g.Select(x => x.AwardId).Distinct().Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.TeacherName)
            .ToList();

        var byMonth = given
            .GroupBy(r => r.GivenMonth)
            .Select(g => new RetentionBonusByMonthDto(
                g.Key, g.Select(x => x.AwardId).Distinct().Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(m => m.Month, StringComparer.Ordinal)
            .ToList();

        return new RetentionBonusFinanceDto(
            fromMonth, toMonth,
            given.Sum(r => r.Amount), given.Select(r => r.AwardId).Distinct().Count(),
            cancelled.Sum(r => r.Amount), cancelled.Select(r => r.AwardId).Distinct().Count(),
            byTeacher, byMonth, rows);
    }

    /// <summary>"YYYY-MM" yoki "YYYY-MM-DD" dan oy kalitini ajratadi; noto'g'ri/bo'sh bo'lsa null.</summary>
    private static string? NormalizeMonth(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length < 7) return null;
        var head = v[..7];
        return DateTime.TryParseExact(head + "-01", "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _)
            ? head : null;
    }

    /// <summary>"YYYY-MM" → o'sha oyning birinchi kuni (00:00).</summary>
    private static DateTime MonthStart(string month) =>
        new(int.Parse(month[..4]), int.Parse(month[5..7]), 1, 0, 0, 0, DateTimeKind.Unspecified);
}
