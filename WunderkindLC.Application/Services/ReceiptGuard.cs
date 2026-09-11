using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// QOG'OZ KVITANSIYA raqamining TAKRORLANMASLIGI. Bitta blank raqami faqat BIR MARTA kiritilishi
/// kerak — aks holda bitta qog'oz kvitansiya bo'yicha ikki marta to'lov yozilib, kassa hisobi
/// buziladi. Barcha yozish yo'llari (o'quvchi to'lovi, moliya amali, to'lovni tahrirlash) shu
/// yerdan o'tadi; topilsa chaqiruvchi 409 Conflict qaytaradi va kassir ekranida ALLAQACHON
/// kiritilgan to'lov kartochkasi (kim, qaysi guruh, qancha, qachon, qaysi o'qituvchi) chiqadi.
/// </summary>
public static class ReceiptGuard
{
    /// <summary>
    /// Shu kvitansiya raqami bilan BOSHQA to'lov bormi? Bor bo'lsa uning to'liq ma'lumoti,
    /// yo'q bo'lsa null. <paramref name="receiptNo"/> allaqachon normallashtirilgan bo'lishi kerak
    /// (<see cref="PaymentFields.NormalizeReceiptNo"/>). <paramref name="excludeTxId"/> — tahrirlashda
    /// yozuvning O'ZINI hisobga olmaslik uchun.
    /// </summary>
    public static async Task<DuplicateReceiptDto?> FindDuplicateAsync(
        IAppDbContext db, string? receiptNo, string? excludeTxId = null)
    {
        if (string.IsNullOrWhiteSpace(receiptNo)) return null;

        var tx = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.ReceiptNo == receiptNo && (excludeTxId == null || t.Id != excludeTxId))
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        if (tx is null) return null;

        var studentName = tx.StudentId is null ? "" :
            await db.Students.AsNoTracking().Where(s => s.Id == tx.StudentId)
                .Select(s => s.FullName).FirstOrDefaultAsync() ?? "";

        // Guruh → kurs va o'qituvchi (to'lov qaysi o'qituvchining guruhiga tushgani ko'rinishi uchun).
        var groupName = "";
        var teacherName = "";
        var courseName = "";
        if (tx.GroupId is not null)
        {
            var g = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == tx.GroupId);
            if (g is not null)
            {
                groupName = g.Name;
                if (!string.IsNullOrEmpty(g.TeacherId))
                    teacherName = await db.Teachers.AsNoTracking().Where(t => t.Id == g.TeacherId)
                        .Select(t => t.FullName).FirstOrDefaultAsync() ?? "";
                if (!string.IsNullOrEmpty(g.CourseId))
                    courseName = await db.Subjects.AsNoTracking().Where(s => s.Id == g.CourseId)
                        .Select(s => s.Name).FirstOrDefaultAsync() ?? "";
            }
        }

        return new DuplicateReceiptDto(
            ReceiptNo: receiptNo,
            TransactionId: tx.Id,
            StudentId: tx.StudentId,
            StudentName: studentName,
            GroupName: groupName,
            CourseName: courseName,
            TeacherName: teacherName,
            Amount: tx.Amount,
            Date: tx.Date,
            Month: tx.Month ?? "",
            Method: tx.Method ?? "",
            CreatedBy: tx.CreatedBy ?? "",
            CreatedAt: tx.CreatedAt);
    }
}
