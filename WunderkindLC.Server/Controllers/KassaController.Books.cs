using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KASSA → o'quvchi to'lov oynasidagi «Kitob sotuvi» turi (2026-10-13, owner: "to'lov turlari ichida kitob sotuvi
/// bo'lishi kerak"). Kassir `kassa` ruxsati bilan o'quvchiga kitob sotadi — «Kitoblar» bo'limi ruxsati shart emas
/// (o'qish to'lovidagi bilan bir xil sabab: `KassaController`). Mantiq — `BookManualSale` (Kitoblar bo'limidagi qo'lda
/// sotuv bilan BITTA): qoldiq ayiriladi, naqd/karta pul kassirning kassasiga kirim (`BookFinance`), audit.
/// ⚠️ NASIYA bu yerda YO'Q — pul shu zahoti olinadi; nasiya «Kitoblar» bo'limida qoladi (qarzni o'sha yerda kuzatadi).
/// </summary>
public partial class KassaController
{
    public record KassaBookDto(string Id, string Title, decimal Price, int Stock);
    public record KassaBookSaleRequest(string BookId, int Qty, string PaymentMethod, string? CardLast4, string? PaidTime);
    public record KassaBookSaleDto(string Id, int Number, string BookTitle, int Qty, decimal Total, string PaymentMethod);

    /// <summary>Sotuvdagi kitoblar (faol), qoldig'i bilan — qoldiq 0 bo'lsa ham ko'rsatiladi (oynada o'chiq).</summary>
    [HttpGet("books")]
    public async Task<List<KassaBookDto>> Books() =>
        (await db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Title).ToListAsync())
        .Select(b => new KassaBookDto(b.Id, b.Title, b.Price, b.Stock)).ToList();

    [HttpPost("students/{id}/book-sale")]
    public async Task<ActionResult<KassaBookSaleDto>> SellBook(string id, KassaBookSaleRequest req)
    {
        if (req.PaymentMethod is not (BookSalesService.PayCash or BookSalesService.PayCard))
            return BadRequest(new { message = "To'lov usuli — naqd yoki karta (nasiya «Kitoblar» bo'limida)" });
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (student.IsArchived) return BadRequest(new { message = "O'quvchi arxivda" });

        var payload = new BookManualSalePayload(req.BookId, id, req.Qty, req.PaymentMethod, req.CardLast4, req.PaidTime);
        var (order, error) = await BookManualSale.CreateAsync(db, audit, payload,
            User.FindFirst(ClaimTypes.Name)?.Value ?? "Kassir", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        if (order is null) return BadRequest(new { message = error });
        return new KassaBookSaleDto(order.Id, order.Number, order.BookTitle, order.Qty, order.Total, order.PaymentMethod);
    }
}
