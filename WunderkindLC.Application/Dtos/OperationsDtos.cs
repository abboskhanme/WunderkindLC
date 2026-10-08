namespace WunderkindLC.Application.Dtos;

/* ---------- KAMERALAR (cameras.md) ---------- */

/// <summary>
/// «Kamera kuzatuvini yoqish» holati — Kameralar sahifasi jonli pleyerlarni chizishdan oldin so'raydi.
/// <paramref name="Message"/> — o'chiq bo'lsa nima qilish kerakligi (bo'sh — yoqilgan).
/// </summary>
public record CameraStatusDto(bool Enabled, string Message);

/* ---------- KITOBLAR: olib ketishda to'lanadigan bot buyurtmasi (books.md §2.6) ---------- */

/// <summary>Tasdiqlangan, lekin hali to'lanmagan (mijoz olib ketmagan) buyurtmani BEKOR qilish sababi.</summary>
public record BookCancelPayload(string? Reason);

/* ---------- TAKLIF VA SHIKOYATLAR ---------- */

/// <summary>Murojaat holatini o'zgartirish natijasi (hal qilindi / qayta ochildi).</summary>
public record FeedbackStatusDto(string Id, string Status);
