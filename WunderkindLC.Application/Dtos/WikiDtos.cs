namespace WunderkindLC.Application.Dtos;

/* =================================================================================================
 *  BILIMLAR BAZASI (wiki) — shaxsiy eslatmalar. API: `api/me/notes/*` (o'zi) va `api/admin/wiki/team/*`
 *  (xodimlarniki, ruxsat `wiki.team`). Qoidalar: `.claude/rules/wiki.md`.
 * ================================================================================================= */

/// <summary>Chap paneldagi daraxt tuguni — matnsiz (matn sahifa ochilganda alohida yuklanadi).</summary>
/// <param name="Locked">Shu sahifaning O'Z qulfi (meros — klient otalardan hisoblaydi, server baribir tekshiradi).</param>
/// <param name="IsSection">Bo'lim (papka) — sahifa sifatida ochilmaydi, chap panelda sarlavha bo'lib turadi.</param>
public record WikiTreeNodeDto(string Id, string ParentId, string Title, string Icon, int Order, bool Locked, bool IsSection);

/// <summary>Bitta eslatma — matni bilan. <c>Locked</c> — o'z qulfi; <c>EffectiveLocked</c> — o'zi yoki otasi qulflangan.</summary>
/// <param name="Version">Saqlashda qaytarib yuboriladi: eski versiya ustidan yozilsa 409.</param>
public record WikiPageDto(
    string Id, string OwnerUserId, string ParentId, string Title, string Icon, string ContentJson,
    int Version, string CreatedAt, string CreatedBy, string UpdatedAt, string UpdatedBy,
    bool Locked, bool EffectiveLocked);

/// <summary>Yangi eslatma. <paramref name="IsSection"/> — sahifa emas, BO'LIM (faqat yuqori darajada).</summary>
public record WikiPageCreatePayload(string? ParentId, string? Title, string? Icon, bool? IsSection = null);

/// <summary>Saqlash. <paramref name="BaseVersion"/> — klient tahrirlashni boshlagan versiya.</summary>
public record WikiPageSavePayload(string? Title, string? Icon, string? ContentJson, int BaseVersion);

/// <summary>Ko'chirish (shu egada): boshqa ota ostiga va/yoki tartibni o'zgartirish.</summary>
/// <param name="Index">Yangi ota ichidagi o'rni (0 — eng tepada). Chegaradan tashqari — oxiriga.</param>
public record WikiPageMovePayload(string? ParentId, int Index);

/// <summary>Qidiruv natijasi.</summary>
public record WikiSearchHitDto(string PageId, string Title, string Icon, string Snippet);

/// <summary>Qulf qo'yish/olish.</summary>
public record WikiLockPayload(bool Locked);

/// <summary>Eslatmalar daraxti (egasi bilan).</summary>
public record WikiNotesTreeDto(string OwnerUserId, string OwnerName, List<WikiTreeNodeDto> Pages);

/// <summary>Rahbariyat/menejer ro'yxatidagi xodim — eslatmalari soni va oxirgi yozgan vaqti.</summary>
public record WikiTeamMemberDto(
    string UserId, string FullName, string Position, string HrRole, int NoteCount, string LastUpdatedAt);

/// <summary>Sozlama (faqat rahbariyat). <c>Positions</c> — tanlash uchun HR lavozimlari.</summary>
public record WikiSettingsDto(bool ManagersSeeTeamNotes, List<string> ManagerVisiblePositionIds, List<WikiPositionDto> Positions);

public record WikiPositionDto(string Id, string Name);

public record WikiSettingsPayload(bool ManagersSeeTeamNotes, List<string>? ManagerVisiblePositionIds);
