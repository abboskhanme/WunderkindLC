using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KASSA — pul qabul qilish ish o'rni (alohida "kassa" ruxsati). Kassir o'quvchini IKKI YO'L bilan
/// topadi: (1) F.I.Sh / telefon bo'yicha qidirish, (2) o'qituvchi → guruh → o'quvchi. Topilgach
/// "To'lov qilish" — odatdagi to'lov oynasi ochiladi.
///
/// <para>NEGA ALOHIDA CONTROLLER: to'lov yozish yo'li <c>StudentsController</c> da edi va u
/// <c>[AdminPerm("students.list")]</c> darvozasi ortida — ya'ni kassirga to'lov qabul qilish uchun
/// o'quvchi YARATISH/TAHRIRLASH huquqini ham berish kerak bo'lardi. Kassa bo'limi shuni ajratadi:
/// <b>faqat to'lov</b> yozadi ("kassa:create"), boshqa hech narsa. Mantiq esa NUSXALANMAGAN —
/// ikkala yo'l ham <see cref="PaymentIntake"/> orqali o'tadi.</para>
///
/// <para>Ro'yxatlar (o'qituvchilar, guruhlar, guruh a'zolari, o'quvchi profili, oylik hisob) uchun
/// yangi endpoint YO'Q — ular mavjud GET'lardan olinadi (xodimga GET har doim ochiq,
/// <see cref="AdminPermAttribute"/>). Bu yerda faqat qidiruv bor: butun o'quvchilar ro'yxatini
/// (900+ to'liq profil) har bosishda yuklamaslik uchun.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("kassa")]
[Route("api/admin/kassa")]
public class KassaController(AppDbContext db, AuditService audit, AutoMessageService autoMsg) : ControllerBase
{
    /// <summary>Bir qidiruvda qaytariladigan maksimal o'quvchi (kassaga shundan ortig'i kerak emas).</summary>
    private const int SearchLimit = 30;

    /// <summary>
    /// O'quvchini F.I.Sh yoki TELEFON (o'zi/ota/ona/ota-ona) bo'yicha qidiradi. Kamida 2 belgi kerak.
    /// Arxivlanganlar ham qaytadi (<c>IsArchived</c> bilan) — arxivdagi o'quvchining qarzi to'lanishi
    /// mumkin. Natija F.I.Sh bo'yicha tartiblanadi va <see cref="SearchLimit"/> ta bilan cheklanadi.
    /// </summary>
    [HttpGet("students")]
    public async Task<ActionResult<IEnumerable<KassaStudentDto>>> SearchStudents([FromQuery] string q)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return new List<KassaStudentDto>();

        // F.I.Sh — registrga bog'liq emas ("alijon" ham "Alijon"ni topadi).
        var name = term.ToLower();
        // TELEFON bo'yicha: bazada raqam "+998-90-123-45-67" ko'rinishida saqlanadi (PhoneUtil.Normalize),
        // shuning uchun kiritilgan raqamlarni SQL'da to'g'ridan-to'g'ri solishtirib bo'lmaydi. Yengil
        // proyeksiya (id + telefonlar) olib, raqamlar bo'yicha xotirada moslashtiramiz.
        // Kamida 4 raqam: "998" kabi qisqa bo'lak BARCHA raqamlarga mos kelardi (raqamlar
        // +998 bilan saqlanadi) — ya'ni butun ro'yxatni qaytarardi.
        var digits = PhoneUtil.DigitsOnly(term);
        var phoneIds = new List<string>();
        if (digits.Length >= 4)
        {
            var rows = await db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
                .ToListAsync();
            phoneIds = rows
                .Where(r => new[] { r.Phone, r.ParentPhone, r.FatherPhone, r.MotherPhone }
                    .Any(p => PhoneUtil.DigitsOnly(p).Contains(digits)))
                .Select(r => r.Id)
                .Take(SearchLimit * 4) // juda keng mos kelishda IN ro'yxati shishib ketmasin
                .ToList();
        }

        var students = await db.Students.AsNoTracking()
            .Where(s => s.FullName.ToLower().Contains(name) || phoneIds.Contains(s.Id))
            .OrderBy(s => s.IsArchived).ThenBy(s => s.FullName)
            .Take(SearchLimit)
            .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone, s.Balance, s.IsArchived })
            .ToListAsync();

        // Guruh nomlari — kassir bir xil ismli o'quvchilarni ajrata olishi uchun (faol a'zoliklar).
        var ids = students.Select(s => s.Id).ToList();
        var groups = await (from sg in db.StudentGroups.AsNoTracking()
                            join c in db.Classes.AsNoTracking() on sg.GroupId equals c.Id
                            where sg.IsActive && ids.Contains(sg.StudentId)
                            select new { sg.StudentId, c.Name }).ToListAsync();
        var byStudent = groups.GroupBy(g => g.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());

        return students.Select(s => new KassaStudentDto(
            s.Id, s.FullName, s.Phone, s.ParentPhone,
            byStudent.GetValueOrDefault(s.Id) ?? new List<string>(),
            s.Balance, s.IsArchived)).ToList();
    }

    /// <summary>
    /// To'lov qabul qilish. Mantiq <see cref="PaymentIntake"/> da — o'quvchilar bo'limidagi to'lov
    /// bilan AYNAN bir xil (kvitansiya nazorati, idempotentlik, avans hisobi, audit, avto-xabar).
    /// Javob: <c>{ id }</c> — chek (kvitansiya) chiqarish uchun tranzaksiya id'si.
    /// </summary>
    [HttpPost("students/{id}/payments")]
    public async Task<IActionResult> AddPayment(string id, PaymentRequest req)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();

        var res = await PaymentIntake.AddAsync(db, audit, autoMsg, student, req,
            User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        return PaymentIntakeHttp.ToActionResult(this, res);
    }

    /// <summary>
    /// KASSIR O'ZI kiritgan to'lovlar (davr bo'yicha) + jami. Kassir FAQAT o'zinikini ko'radi —
    /// filtr so'rovdan emas, TOKENDAN olinadi (boshqa kassirning hisobotini so'rab bo'lmaydi).
    /// <paramref name="from"/>/<paramref name="to"/> — "yyyy-MM-dd"; berilmasa BUGUNGI kun.
    /// </summary>
    [HttpGet("my-payments")]
    public async Task<ActionResult<CashierPaymentsDto>> MyPayments(
        [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        return await CashierReport.PaymentsAsync(db,
            string.IsNullOrWhiteSpace(from) ? today : from,
            string.IsNullOrWhiteSpace(to) ? today : to,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value);
    }
}

/// <summary>
/// <see cref="PaymentIntakeResult"/> → HTTP javobi. Ikkala to'lov yo'li (o'quvchilar bo'limi va
/// kassa) bir xil javob shaklini berishi uchun — klientdagi <c>receiptDuplicateOf</c> (409 →
/// kvitansiya band kartochkasi) ikkalasida ham ishlaydi.
/// </summary>
public static class PaymentIntakeHttp
{
    public static IActionResult ToActionResult(ControllerBase c, PaymentIntakeResult res)
    {
        // Kvitansiya raqami band — 409 + allaqachon kiritilgan to'lov ma'lumoti.
        if (res.Duplicate is not null)
            return c.Conflict(new { message = res.Error, duplicate = res.Duplicate });
        if (res.Error is not null)
            return c.BadRequest(new { message = res.Error });
        return c.Ok(new { id = res.TxId, idempotent = res.Idempotent });
    }
}
