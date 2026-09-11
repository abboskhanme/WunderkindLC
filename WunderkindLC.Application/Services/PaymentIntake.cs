using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHI TO'LOVINI QABUL QILISHNING YAGONA YO'LI. Ilgari bu mantiq faqat
/// <c>StudentsController.AddPayment</c> ichida edi; endi <b>Kassa</b> bo'limi ham (kassir "students"
/// ruxsatisiz ishlaydi) aynan shu xizmatdan foydalanadi — ikki nusxa mantiq bo'lmasin (kvitansiya
/// nazorati, idempotentlik, avans hisobi, audit va avto-xabar bir joyda).
///
/// <para>Chaqiruvchi (controller) faqat HTTP tarjimasini qiladi:
/// <see cref="PaymentIntakeResult.Duplicate"/> → 409, <see cref="PaymentIntakeResult.Error"/> → 400,
/// aks holda 200 + tranzaksiya id'si (chek uchun).</para>
/// </summary>
public static class PaymentIntake
{
    /// <summary>
    /// To'lovni yozadi: balansni oshiradi, moliyaga kirim (tuition) tranzaksiyasi qo'shadi,
    /// auditga yozadi va "To'lov qabul qilinganda" avto-xabarini yuboradi.
    /// <paramref name="createdBy"/> — to'lovni kiritgan xodim F.I.Sh (chekdagi "Mas'ul"),
    /// <paramref name="createdById"/> — o'sha xodimning akkaunt id'si (kassir hisoboti uchun).
    /// </summary>
    public static async Task<PaymentIntakeResult> AddAsync(
        IAppDbContext db, AuditService audit, AutoMessageService autoMsg,
        Student student, PaymentRequest req, string? createdBy, string? createdById = null)
    {
        if (req.Amount <= 0)
            return PaymentIntakeResult.Invalid("To'lov summasi musbat bo'lishi kerak");

        // To'lov sanasi — kiritilmasa bugungi, kiritilsa (masalan kechroq tizimga yozilgan
        // to'lov uchun) o'sha eski sana ishlatiladi. Kelajakdagi sanaga ruxsat berilmaydi.
        var paidDate = (req.Date ?? "").Trim();
        if (paidDate.Length == 0) paidDate = AppClock.Today.ToString("yyyy-MM-dd");
        else if (!DateOnly.TryParse(paidDate, out var parsedDate))
            return PaymentIntakeResult.Invalid("To'lov sanasi noto'g'ri");
        else if (parsedDate > AppClock.Today)
            return PaymentIntakeResult.Invalid("To'lov sanasi kelajakda bo'lishi mumkin emas");

        // OY MAJBURIY — to'lov har doim aniq oyga bog'lanadi (per-guruh billing).
        var month = (req.Month ?? "").Trim();
        if (month.Length < 7)
            return PaymentIntakeResult.Invalid("To'lov qaysi oy uchun ekanini tanlang");

        // KARTA to'lovining haqiqiy vaqti ("HH:mm") — kiritilgan bo'lsa formati tekshiriladi.
        if (!PaymentFields.TryNormalizeTime(req.PaidTime, out var paidTime))
            return PaymentIntakeResult.Invalid("To'lov vaqti noto'g'ri (HH:mm)");

        // KARTA raqamining oxirgi 4 raqami — faqat shu qismi saqlanadi (to'liq raqam emas).
        if (!PaymentFields.TryNormalizeCardLast4(req.CardLast4, out var cardLast4))
            return PaymentIntakeResult.Invalid("Karta raqamining oxirgi 4 raqamini kiriting");

        // QOG'OZ KVITANSIYA raqami BIR MARTA ishlatiladi — band bo'lsa 409 va allaqachon kiritilgan
        // to'lov ma'lumoti qaytadi (kassir ekranida kartochka bo'lib chiqadi).
        // "Baribir saqlash" (ForceReceipt) bosilgan bo'lsa — kassir ataylab davom etyapti, o'tkazamiz.
        var receiptNo = PaymentFields.NormalizeReceiptNo(req.ReceiptNo);
        var dupReceipt = req.ForceReceipt ? null : await ReceiptGuard.FindDuplicateAsync(db, receiptNo);
        if (dupReceipt is not null)
            return PaymentIntakeResult.DuplicateReceipt(dupReceipt);

        // O'quvchining billable (sinovdan boshqa) guruhlari — QARZ QOLDIRGAN ESKI a'zoliklar HAM.
        //
        // DIQQAT — `IsActive` SHARTI ATAYIN YO'Q. Guruhdan chiqarilgan, "tugatgan" (sertifikat bilan
        // yopilgan guruh) yoki muzlatilgan a'zolikda ham qarz qolishi mumkin va kassir uni KEYIN ham
        // qabul qila olishi SHART (Kassa bo'limi eski/arxiv guruhlarni ataylab ko'rsatadi, `PaymentModal`
        // ham "— chiqarilgan" yorlig'i bilan tanlashga ruxsat beradi). Ilgari `sg.IsActive` talab
        // qilinar edi va bu ikki xil buzilish berardi:
        //   (1) boshqa faol guruhi bor o'quvchida — 400 "To'lov qaysi guruh uchun ekanini tanlang"
        //       (tanlangan eski guruh ro'yxatda yo'q edi);
        //   (2) faol guruhi BITTA bo'lsa — to'lov jimgina O'SHA (noto'g'ri) guruhga teglanardi.
        var billableGroups = await db.StudentGroups
            .Where(sg => sg.StudentId == student.Id && sg.Status != "trial")
            .Select(sg => sg.GroupId).Distinct().ToListAsync();

        var groupId = string.IsNullOrWhiteSpace(req.GroupId) ? null : req.GroupId.Trim();
        if (billableGroups.Count >= 2)
        {
            // Bir nechta guruh — guruh MAJBURIY va o'quvchining billable guruhi bo'lishi shart.
            if (groupId is null || !billableGroups.Contains(groupId))
                return PaymentIntakeResult.Invalid("To'lov qaysi guruh uchun ekanini tanlang");
        }
        else if (billableGroups.Count == 1)
            groupId = billableGroups[0]; // yagona guruh — avtomatik
        else
            groupId = null; // guruhsiz (eski ClassName) o'quvchi

        // IDEMPOTENTLIK: oxirgi ~6 soniyada AYNAN shu to'lov (o'quvchi, guruh, oy, summa) yozilgan bo'lsa —
        // dublikat qo'shmaymiz (admin double-click / tarmoq retry). Balansni ikki marta oshirmaslik uchun
        // EnsureCharge/balans o'zgarishidan OLDIN tekshiramiz (FinanceController.Create bilan bir xil mantiq).
        var dupCutoff = DateTime.UtcNow.AddSeconds(-6);
        var recentDup = await db.FinanceTransactions
            .Where(t => t.StudentId == student.Id && t.Direction == "income" && t.Category == "tuition"
                && t.Amount == req.Amount && t.Month == month && t.GroupId == groupId
                && t.CreatedAt >= dupCutoff)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        if (recentDup is not null)
            return PaymentIntakeResult.Saved(recentDup.Id, idempotent: true);

        // AVANS: to'lov tushadigan (guruh, oy) hisobi hali yo'q bo'lsa — shu zahoti ochamiz
        // (kelajak oyga oldindan to'lov; balans hisob miqdorida kamayadi, to'lov esa oshiradi).
        await TuitionService.EnsureChargeAsync(db, student, groupId, month);

        student.Balance += req.Amount;

        // To'lov qaysi guruh (kurs) uchun ekani — audit izohi va avto-xabar ({guruh}/{kurs}) uchun.
        var payGroup = groupId is null ? null : await db.Classes.FirstOrDefaultAsync(c => c.Id == groupId);
        var groupName = payGroup?.Name;
        var teacherName = string.IsNullOrEmpty(payGroup?.TeacherId) ? null
            : await db.Teachers.Where(t => t.Id == payGroup!.TeacherId).Select(t => t.FullName).FirstOrDefaultAsync();
        var courseName = string.IsNullOrEmpty(payGroup?.CourseId) ? null
            : await db.Subjects.Where(su => su.Id == payGroup!.CourseId).Select(su => su.Name).FirstOrDefaultAsync();

        // To'lovni moliyaviy kirim (o'quvchi to'lovi) sifatida qayd etamiz.
        var tx = new FinanceTransaction
        {
            Date = paidDate,
            Direction = "income",
            Category = "tuition",
            Amount = req.Amount,
            StudentId = student.Id,
            GroupId = groupId,
            Month = month,
            Note = $"O'quvchi to'lovi ({month})"
                + (groupName is null ? "" : $" [{groupName}]")
                + $" — {student.FullName}",
            Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
            Method = string.IsNullOrWhiteSpace(req.Method) ? null : req.Method.Trim().ToLowerInvariant(),
            // Naqd to'lovda qog'oz kvitansiya raqami ("KV" + raqam), kartada esa to'lov vaqti ("HH:mm").
            ReceiptNo = receiptNo,
            PaidTime = paidTime,
            CardLast4 = cardLast4,
            CreatedBy = createdBy,     // mas'ul (chek uchun)
            CreatedById = createdById,  // kim kiritgani (kassir hisoboti)
        };
        db.FinanceTransactions.Add(tx);

        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            $"To'lov qabul qilindi: +{AuditService.Money(req.Amount)} so'm ({month} uchun)"
                + (groupName is null ? "" : $" — {groupName}")
                + (teacherName is null ? "" : $" · {teacherName}")
                // Kassir "Baribir saqlash"ni bosgan bo'lsa — izda qolsin (band raqam bilan yozildi).
                + (req.ForceReceipt && receiptNo is not null ? $" [takroriy kvitansiya {receiptNo} — ataylab saqlandi]" : ""),
            after: AuditService.Snapshot(tx), studentId: student.Id);

        await db.SaveChangesAsync();

        // Avto xabar — o'quvchi tuition to'lovi qabul qilinganda ("To'lov qabul qilinganda" hodisasi).
        // Moliya bo'limidagi to'lov bilan bir xil xulq (FinanceController). {summa} = faqat raqam,
        // {sana} = to'lovning HAQIQIY sanasi (paidDate — orqaga sanalgan bo'lishi mumkin, bugun emas).
        // {oy} = to'lov QAYSI OY uchun (tanlangan `month`), bugungi oy EMAS.
        // {kurs}/{guruh} = to'lov QAYSI guruh (kurs) uchun ekani — o'quvchi bir necha guruhda o'qisa
        // har to'lov alohida yoziladi, demak har biriga o'z kursi nomi bilan alohida xabar ketadi.
        await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.PaymentReceived, student,
            new Dictionary<string, string>
            {
                ["{summa}"] = MessageTokenizer.MoneyPlain(req.Amount),
                ["{sana}"] = $"{paidDate[8..10]}.{paidDate[5..7]}.{paidDate[..4]}",
                ["{oy}"] = int.TryParse(month.Substring(5, 2), out var payMm) ? MessageTokenizer.MonthNameUz(payMm) : "",
                ["{kurs}"] = courseName ?? groupName ?? "",
                ["{guruh}"] = groupName ?? student.ClassName,
            },
            group: payGroup);

        // Chek (kvitansiya) uchun yaratilgan tranzaksiya id'si.
        return PaymentIntakeResult.Saved(tx.Id);
    }
}

/// <summary>
/// <see cref="PaymentIntake.AddAsync"/> natijasi — HTTP'ga bog'liq emas (Application qatlami).
/// Uchta holat: saqlandi (<see cref="TxId"/>), kvitansiya band (<see cref="Duplicate"/> + 409),
/// noto'g'ri ma'lumot (<see cref="Error"/> + 400).
/// </summary>
public sealed record PaymentIntakeResult
{
    /// <summary>Yaratilgan (yoki idempotentlik natijasida topilgan) tranzaksiya id'si — chek uchun.</summary>
    public string? TxId { get; init; }

    /// <summary>Shu zahoti takrorlangan so'rov (double-click) — yangi yozuv YARATILMADI.</summary>
    public bool Idempotent { get; init; }

    /// <summary>Foydalanuvchiga ko'rsatiladigan xato matni (bo'sh bo'lsa — muvaffaqiyat).</summary>
    public string? Error { get; init; }

    /// <summary>Kvitansiya raqami band bo'lsa — allaqachon kiritilgan to'lov ma'lumoti.</summary>
    public DuplicateReceiptDto? Duplicate { get; init; }

    public static PaymentIntakeResult Saved(string txId, bool idempotent = false) =>
        new() { TxId = txId, Idempotent = idempotent };

    public static PaymentIntakeResult Invalid(string message) => new() { Error = message };

    public static PaymentIntakeResult DuplicateReceipt(DuplicateReceiptDto dup) =>
        new() { Duplicate = dup, Error = $"{dup.ReceiptNo} kvitansiya raqami allaqachon kiritilgan" };
}
