namespace WunderkindLC.Application.Dtos;

/* ---------- KASSADAN KASSAGA KO'CHIRISH (`CashboxTransferService`, billing.md) ---------- */

/// <summary>
/// Bitta ko'chirish (tarix ro'yxati). Kassa NOMLARI joriy (arxivlangan kassa ham o'z nomi bilan).
/// Bekor qilingan qator ham qaytadi — ro'yxatda ustiga chizilgan holda ko'rinadi.
/// </summary>
public record CashboxTransferDto(
    string Id,
    string FromCashboxId, string FromCashboxName,
    string ToCashboxId, string ToCashboxName,
    decimal Amount, string Date, string? Note,
    string CreatedAt, string? CreatedByName,
    bool IsVoided, string? VoidedAt, string? VoidedByName, string? VoidReason);

/// <summary>
/// Yangi ko'chirish. <paramref name="Date"/> bo'sh — bugun. <paramref name="RequestId"/> — oyna kaliti
/// (qayta bosilgan "Saqlash" ikkinchi ko'chirish yozmasin, `PaymentIdempotency`).
/// </summary>
public record CashboxTransferPayload(
    string? FromCashboxId, string? ToCashboxId, decimal Amount,
    string? Date = null, string? Note = null, string? RequestId = null);
