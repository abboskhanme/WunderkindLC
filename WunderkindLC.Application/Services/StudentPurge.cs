using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINI BUTUNLAY O'CHIRISH — xato kiritilgan yoki DUBLIKAT o'quvchi uchun.
///
/// <para>Oddiy "o'chirish" (<c>DELETE api/admin/students/{id}</c>) o'quvchini «O'chirilganlar
/// arxivi»ga (<see cref="ArchiveService"/>) yozadi va faqat 4 ta jadvalni tozalaydi — qolgan
/// bog'liq qatorlar yetim qolardi. Bu yerda esa o'quvchiga tegishli HAR BIR qator o'chiriladi,
/// arxivga HECH NARSA yozilmaydi va fayllari (rasm, passport, selfi, sertifikat) diskdan
/// ham o'chiriladi.</para>
///
/// <para>⚠️ <b>PUL YOZUVI O'CHIRILMAYDI.</b> Tirik (bekor qilinmagan) pul amali bor o'quvchini
/// o'chirib bo'lmaydi — <see cref="BlockReason"/>. Sabab: moliya yozuvlari HECH QACHON
/// o'chirilmaydi (<c>billing.md</c>, «Tranzaksiyani BEKOR QILISH»); to'lovni o'chirish kassa
/// hisobotini orqaga qarab o'zgartirardi. Dublikatga adashib to'lov yozilgan bo'lsa — avval
/// bekor qilinadi (sabab bilan), keyin to'g'ri o'quvchiga qayta kiritiladi.</para>
///
/// <para><b>O'chirilmaydi, faqat AJRATILADI</b> (<c>StudentId = null</c>) — boshqa bo'limning
/// yozuvi bo'lgan qatorlar: bekor qilingan pul amallari, qo'ng'iroqlar, kitob sotuvi, lid,
/// support darsi. Ular o'quvchiniki emas, markazniki.</para>
///
/// <para><b>Tegilmaydi:</b> shartnomalar — ular ota-ona TELEFONIGA bog'langan
/// (<see cref="Contract.RecipientKey"/>) va aka-ukalar bilan umumiy.</para>
///
/// <para><see cref="PurgeAsync"/> <c>SaveChanges</c> QILMAYDI — chaqiruvchi audit yozuvini qo'shib,
/// BITTA saqlash bilan yakunlaydi (hammasi yoki hech narsa). Fayllar saqlashdan KEYIN o'chiriladi.</para>
/// </summary>
public static class StudentPurge
{
    /// <summary>Oynada ko'rsatiladigan bitta qator: nima va nechta. <paramref name="Detach"/> —
    /// o'chirilmaydi, faqat o'quvchidan ajratiladi.</summary>
    public sealed record Item(string Key, string Label, int Count, bool Detach);

    public sealed record Preview(
        string StudentId, string FullName, bool CanPurge, string? BlockReason,
        int LiveTransactions, decimal LiveTransactionsTotal, IReadOnlyList<Item> Items);

    /// <summary>Saqlashdan keyin diskdan o'chiriladigan fayl manzillari ("/uploads/...").</summary>
    public sealed record Result(string FullName, IReadOnlyList<string> FileUrls);

    /// <summary>Tirik pul amali bor o'quvchi uchun rad etish sababi (bo'lmasa <c>null</c>).</summary>
    public static string? BlockReason(int liveCount, decimal liveTotal) =>
        liveCount == 0
            ? null
            : $"Bu o'quvchida {liveCount} ta pul amali bor ({AuditService.Money(liveTotal)} so'm). " +
              "Pul yozuvlari o'chirilmaydi — avval ularni bekor qiling (Moliya yoki to'lov tarixi), " +
              "kerak bo'lsa to'g'ri o'quvchiga qayta kiriting.";

    public static async Task<Preview?> PreviewAsync(IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return null;

        var live = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId).Select(t => t.Amount).ToListAsync(ct);
        var liveTotal = live.Sum();

        var id = studentId;
        var suffix = ":" + id;
        var userId = await DeletableUserIdAsync(db, s, ct);

        var items = new List<Item>
        {
            new("groups", "Guruh a'zoliklari", await db.StudentGroups.CountAsync(x => x.StudentId == id, ct), false),
            new("charges", "Oylik hisoblar (qarz)", await db.MonthlyCharges.CountAsync(x => x.StudentId == id, ct), false),
            new("journal", "Davomat, baho va ball yozuvlari",
                await db.JournalEntries.CountAsync(x => x.StudentId == id, ct)
                + await db.CriterionGrades.CountAsync(x => x.StudentId == id, ct)
                + await db.StudentBallAdjustments.CountAsync(x => x.StudentId == id, ct), false),
            new("discounts", "Chegirmalar", await db.StudentDiscounts.CountAsync(x => x.StudentId == id, ct), false),
            new("notes", "Izohlar", await db.StudentNotes.CountAsync(x => x.StudentId == id, ct), false),
            new("contacts", "Bog'lanish talablari", await db.ContactRequests.CountAsync(x => x.StudentId == id, ct), false),
            new("tests", "Test natijalari va sertifikatlar",
                await db.TestScores.CountAsync(x => x.StudentId == id, ct)
                + await db.TestCertificates.CountAsync(x => x.StudentId == id, ct)
                + await db.TestBotSessions.CountAsync(x => x.StudentId == id, ct)
                + await db.StudentCertificates.CountAsync(x => x.StudentId == id, ct), false),
            new("learning", "Onlayn kurs natijalari",
                await db.CourseProgresses.CountAsync(x => x.StudentId == id, ct)
                + await db.CourseItemAttempts.CountAsync(x => x.StudentId == id, ct), false),
            new("ai", "AI tahlil va tekshiruvlar",
                await db.StudentAiAnalyses.CountAsync(x => x.StudentId == id, ct)
                + await db.AiChecks.CountAsync(x => x.StudentId == id, ct)
                + await db.StudentAiAccesses.CountAsync(x => x.StudentId == id, ct), false),
            new("feedback", "Fikrlar (ota-ona va o'qituvchi)",
                await db.Feedbacks.CountAsync(x => x.StudentId == id, ct)
                + await db.TeacherReviews.CountAsync(x => x.StudentId == id, ct), false),
            new("face", "Yuz bilan kirish ma'lumotlari",
                await db.StudentFaceProfiles.CountAsync(x => x.StudentId == id, ct)
                + await db.LoginFaceChecks.CountAsync(x => x.StudentId == id, ct)
                + await db.FaceChallenges.CountAsync(x => x.StudentId == id, ct), false),
            new("bonus", "Ushlab turish bonusi va uzaytirish yozuvlari",
                await db.RetentionBonusAwards.CountAsync(x => x.StudentId == id, ct)
                + await db.RetentionBonusTracks.CountAsync(x => x.StudentId == id, ct)
                + await db.StudentExtensions.CountAsync(x => x.StudentId == id, ct), false),
            new("telegram", "Telegram bot bog'lanishi", await db.TelegramRegistrations.CountAsync(x => x.StudentId == id, ct), false),
            new("account", "Login akkaunti (ilova)", userId is null ? 0 : 1, false),
            new("history", "O'zgarishlar tarixi yozuvlari",
                await db.AuditLogs.CountAsync(a => a.StudentId == id || a.EntityId == id || a.EntityId.EndsWith(suffix), ct), false),

            new("voided", "Bekor qilingan pul amallari",
                await db.FinanceTransactions.IgnoreQueryFilters().CountAsync(t => t.StudentId == id && t.IsVoided, ct), true),
            new("calls", "Qo'ng'iroqlar yozuvi",
                await db.Calls.CountAsync(x => x.StudentId == id, ct)
                + await db.CtiCallRecords.CountAsync(x => x.StudentId == id, ct), true),
            new("books", "Kitob sotuvlari", await db.BookOrders.CountAsync(x => x.StudentId == id, ct), true),
            new("leads", "Lid (CRM)", await db.Leads.CountAsync(x => x.ConvertedStudentId == id, ct), true),
            new("support", "Support darslari", await db.SupportSlots.CountAsync(x => x.StudentId == id, ct), true),
        };

        var reason = BlockReason(live.Count, liveTotal);
        return new Preview(s.Id, s.FullName, reason is null, reason, live.Count, liveTotal,
            items.Where(i => i.Count > 0).ToList());
    }

    /// <summary>
    /// O'chirishni TAYYORLAYDI (saqlamaydi). O'quvchi topilmasa <c>null</c>. Tirik pul amali bo'lsa
    /// <see cref="InvalidOperationException"/> (matni — <see cref="BlockReason"/>), hech narsa o'zgarmaydi.
    /// </summary>
    public static async Task<Result?> PurgeAsync(IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return null;

        var live = await db.FinanceTransactions.Where(t => t.StudentId == studentId).Select(t => t.Amount).ToListAsync(ct);
        var block = BlockReason(live.Count, live.Sum());
        if (block is not null) throw new InvalidOperationException(block);

        var id = studentId;
        var suffix = ":" + id;
        var files = new List<string?> { s.BirthCertificateUrl, s.ParentPassportUrl };

        // ---- O'quvchining O'Z qatorlari — o'chiriladi ----
        db.StudentGroups.RemoveRange(await db.StudentGroups.Where(x => x.StudentId == id).ToListAsync(ct));
        db.MonthlyCharges.RemoveRange(await db.MonthlyCharges.Where(x => x.StudentId == id).ToListAsync(ct));
        db.JournalEntries.RemoveRange(await db.JournalEntries.Where(x => x.StudentId == id).ToListAsync(ct));
        db.CriterionGrades.RemoveRange(await db.CriterionGrades.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentBallAdjustments.RemoveRange(await db.StudentBallAdjustments.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentDiscounts.RemoveRange(await db.StudentDiscounts.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentNotes.RemoveRange(await db.StudentNotes.Where(x => x.StudentId == id).ToListAsync(ct));
        db.ContactAttempts.RemoveRange(await db.ContactAttempts.Where(x => x.StudentId == id).ToListAsync(ct));
        db.ContactRequests.RemoveRange(await db.ContactRequests.Where(x => x.StudentId == id).ToListAsync(ct));
        db.TestScores.RemoveRange(await db.TestScores.Where(x => x.StudentId == id).ToListAsync(ct));
        db.TestBotSessions.RemoveRange(await db.TestBotSessions.Where(x => x.StudentId == id).ToListAsync(ct));
        db.CourseProgresses.RemoveRange(await db.CourseProgresses.Where(x => x.StudentId == id).ToListAsync(ct));
        db.CourseItemAttempts.RemoveRange(await db.CourseItemAttempts.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentAiAnalyses.RemoveRange(await db.StudentAiAnalyses.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentAiAccesses.RemoveRange(await db.StudentAiAccesses.Where(x => x.StudentId == id).ToListAsync(ct));
        db.TeacherReviews.RemoveRange(await db.TeacherReviews.Where(x => x.StudentId == id).ToListAsync(ct));
        db.FaceChallenges.RemoveRange(await db.FaceChallenges.Where(x => x.StudentId == id).ToListAsync(ct));
        db.RetentionBonusAwards.RemoveRange(await db.RetentionBonusAwards.Where(x => x.StudentId == id).ToListAsync(ct));
        db.RetentionBonusTracks.RemoveRange(await db.RetentionBonusTracks.Where(x => x.StudentId == id).ToListAsync(ct));
        db.StudentExtensions.RemoveRange(await db.StudentExtensions.Where(x => x.StudentId == id).ToListAsync(ct));
        db.TelegramRegistrations.RemoveRange(await db.TelegramRegistrations.Where(x => x.StudentId == id).ToListAsync(ct));
        db.ArchivedRecords.RemoveRange(await db.ArchivedRecords.Where(x => x.EntityId == id).ToListAsync(ct));
        // Tarix: o'quvchi bo'yicha yozuvlar + a'zolik hodisalari ("{groupId}:{studentId}").
        db.AuditLogs.RemoveRange(await db.AuditLogs
            .Where(a => a.StudentId == id || a.EntityId == id || a.EntityId.EndsWith(suffix)).ToListAsync(ct));

        // Fayli bor qatorlar — manzil yig'iladi, fayl saqlashdan KEYIN o'chiriladi.
        var aiChecks = await db.AiChecks.Where(x => x.StudentId == id).ToListAsync(ct);
        files.AddRange(aiChecks.Select(x => x.AudioUrl));
        db.AiChecks.RemoveRange(aiChecks);

        var feedbacks = await db.Feedbacks.Where(x => x.StudentId == id).ToListAsync(ct);
        files.AddRange(feedbacks.Select(x => x.ImageUrl));
        db.Feedbacks.RemoveRange(feedbacks);

        var faceProfiles = await db.StudentFaceProfiles.Where(x => x.StudentId == id).ToListAsync(ct);
        files.AddRange(faceProfiles.Select(x => x.SampleUrl));
        db.StudentFaceProfiles.RemoveRange(faceProfiles);

        var faceChecks = await db.LoginFaceChecks.Where(x => x.StudentId == id).ToListAsync(ct);
        files.AddRange(faceChecks.Select(x => x.ImageUrl));
        db.LoginFaceChecks.RemoveRange(faceChecks);

        var testCerts = await db.TestCertificates.Where(x => x.StudentId == id).ToListAsync(ct);
        files.AddRange(testCerts.SelectMany(x => new[] { x.DocxUrl, x.PdfUrl }));
        db.TestCertificates.RemoveRange(testCerts);

        var certs = await db.StudentCertificates.Where(x => x.StudentId == id).ToListAsync(ct);
        var certIds = certs.Select(c => c.Id).ToList();
        files.AddRange(certs.Select(x => x.FilePath));
        db.CertificateVerifications.RemoveRange(
            await db.CertificateVerifications.Where(v => certIds.Contains(v.StudentCertificateId)).ToListAsync(ct));
        db.StudentCertificates.RemoveRange(certs);

        // ---- Boshqa bo'limning yozuvi — SAQLANADI, faqat o'quvchidan ajratiladi ----
        foreach (var t in await db.FinanceTransactions.IgnoreQueryFilters().Where(t => t.StudentId == id).ToListAsync(ct))
            t.StudentId = null;   // faqat bekor qilinganlar qoladi (tiriklari yuqorida rad etilgan)
        foreach (var c in await db.Calls.Where(x => x.StudentId == id).ToListAsync(ct)) c.StudentId = null;
        foreach (var c in await db.CtiCallRecords.Where(x => x.StudentId == id).ToListAsync(ct)) c.StudentId = null;
        foreach (var b in await db.BookOrders.Where(x => x.StudentId == id).ToListAsync(ct)) b.StudentId = null;
        foreach (var l in await db.Leads.Where(x => x.ConvertedStudentId == id).ToListAsync(ct)) l.ConvertedStudentId = null;
        foreach (var slot in await db.SupportSlots.Where(x => x.StudentId == id).ToListAsync(ct))
        {
            slot.StudentId = null;
            // Hali o'tilmagan bron — slot qayta BO'SHAYDI (support o'qituvchi uni boshqasiga beradi).
            if (slot.Status == "booked") { slot.Status = "open"; slot.BookedAt = null; }
        }

        // ---- Login akkaunti ----
        var userId = await DeletableUserIdAsync(db, s, ct);
        if (userId is not null)
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            files.Add(user.AvatarUrl);
            db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(x => x.UserId == userId).ToListAsync(ct));
            db.LoginOtpCodes.RemoveRange(await db.LoginOtpCodes.Where(x => x.UserId == userId).ToListAsync(ct));
            db.UserSettings.RemoveRange(await db.UserSettings.Where(x => x.UserId == userId).ToListAsync(ct));
            db.DeviceTokens.RemoveRange(await db.DeviceTokens.Where(x => x.UserId == userId).ToListAsync(ct));
            db.UserNotifications.RemoveRange(await db.UserNotifications.Where(x => x.UserId == userId).ToListAsync(ct));
            db.TrustedDevices.RemoveRange(await db.TrustedDevices.Where(x => x.UserId == userId).ToListAsync(ct));
            db.ChatMessages.RemoveRange(await db.ChatMessages.Where(x => x.SenderUserId == userId).ToListAsync(ct));
            db.Users.Remove(user);
        }

        // Rasm/passport manzili boshqa o'quvchida ham turgan bo'lsa (nusxa ko'chirilgan yozuv) —
        // fayl o'sha o'quvchiga kerak, o'chirilmaydi.
        var own = new[] { s.BirthCertificateUrl, s.ParentPassportUrl }
            .Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).ToList();
        var keep = own.Count == 0
            ? new HashSet<string>()
            : (await db.Students.AsNoTracking()
                .Where(x => x.Id != id)
                .Where(x => own.Contains(x.BirthCertificateUrl!) || own.Contains(x.ParentPassportUrl!))
                .Select(x => new { x.BirthCertificateUrl, x.ParentPassportUrl })
                .ToListAsync(ct))
              .SelectMany(x => new[] { x.BirthCertificateUrl, x.ParentPassportUrl })
              .Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).ToHashSet();

        db.Students.Remove(s);

        var urls = files
            .Where(u => !string.IsNullOrWhiteSpace(u) && !keep.Contains(u!))
            .Select(u => u!)
            .Distinct()
            .ToList();
        return new Result(s.FullName, urls);
    }

    /// <summary>
    /// O'quvchining login akkaunti — faqat u O'QUVCHI roli bo'lsa va boshqa hech bir o'quvchi unga
    /// bog'lanmagan bo'lsa (aks holda <c>null</c>): xodim/admin akkauntini tasodifan o'chirmaslik uchun.
    /// </summary>
    private static async Task<string?> DeletableUserIdAsync(IAppDbContext db, Student s, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(s.UserId)) return null;
        var uid = s.UserId;
        var isStudentUser = await db.Users.AnyAsync(u => u.Id == uid && u.Role == Roles.Student, ct);
        if (!isStudentUser) return null;
        var sharedWithOther = await db.Students.AnyAsync(x => x.UserId == uid && x.Id != s.Id, ct);
        return sharedWithOther ? null : uid;
    }

    /// <summary>
    /// "/uploads/..." manzilini diskdagi yo'lga aylantiradi. Faqat <c>uploads/</c> ildizi va uning
    /// <c>face/</c>, <c>certificates/</c> papkalari; manzilda boshqa yo'l segmenti bo'lsa (<c>..</c>,
    /// teskari chiziq, begona papka) — <c>null</c>. Fayl mavjudligi TEKSHIRILMAYDI.
    /// </summary>
    public static string? ResolveUploadPath(string contentRoot, string? url)
    {
        const string prefix = "/uploads/";
        if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = url[prefix.Length..];

        string? folder = null;
        foreach (var f in new[] { FaceStorage.FolderName, "certificates" })
            if (rest.StartsWith(f + "/", StringComparison.Ordinal)) { folder = f; rest = rest[(f.Length + 1)..]; break; }

        if (rest.Length == 0 || rest.Contains('\\') || rest == "." || rest == ".."
            || !string.Equals(rest, Path.GetFileName(rest), StringComparison.Ordinal))
            return null;

        return folder is null
            ? Path.Combine(contentRoot, "uploads", rest)
            : Path.Combine(contentRoot, "uploads", folder, rest);
    }
}
