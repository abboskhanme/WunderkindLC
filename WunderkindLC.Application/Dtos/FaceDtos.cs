namespace WunderkindLC.Application.Dtos;

/* =================================================================================================
 *  YUZ BILAN KIRISH (face login) — o'quvchi ilovasi va admin paneli uchun DTO'lar.
 *
 *  ⚠️ Model TELEFONDA ishlaydi, serverga faqat VEKTOR keladi (`FACE-DETEKT-PLAN.md` §3).
 *  Shu sabab bu yerda rasm/model emas, sonlar va chegaralar yuritiladi.
 * ============================================================================================== */

/// <summary>Ilova mahalliy (telefondagi) tekshiruvlarda ishlatadigan sifat chegaralari.
/// ⚠️ Server bilan AYNAN bir xil bo'lishi uchun ular <c>FaceMatch.DefaultLimits</c> dan olinadi —
/// ilovada qo'lda takrorlanmasin (telefonda "yaxshi", serverda "yomon" bo'lib qolmasin).</summary>
public record FaceQualityLimitsDto(
    double MinSharpness, double MinBrightness, double MaxBrightness,
    double MinFaceRatio, double MaxYaw, double MaxRoll);

/// <summary>`GET /api/student/face/status` javobi — ilova selfi ekranini shundan quradi.</summary>
/// <param name="Enrolled">Etalon bormi (false — birinchi marta, profil rasmi vektori kerak bo'ladi).</param>
/// <param name="HasPhoto">O'quvchining profil rasmi bormi (false + etalon yo'q → `pending` yo'li).</param>
/// <param name="ModelVersion">Ilova AYNAN shu model bilan vektor hisoblashi shart.</param>
/// <param name="Threshold">Kosinus chegarasi (ilova buni faqat ko'rsatish uchun ishlatadi — qaror serverda).</param>
/// <param name="AttemptsLeft">Shu soatda qolgan urinishlar.</param>
/// <param name="RequireLiveness">Ilova AVVAL `POST face/challenge` ni chaqirishi va harakatlarni
/// o'lchashi SHARTmi. `false` bo'lsa eski (nonce'siz) oqim ham qabul qilinadi.</param>
/// <param name="RequireAttestation">`integrityToken` MAJBURIYmi (Play Integrity).</param>
/// <param name="LivenessActions">Mumkin bo'lgan harakatlar KATALOGI — ilova qaysi harakatlarni
/// qo'llab-quvvatlashi kerakligini shundan biladi (aniq ketma-ketlik `challenge` dan keladi).</param>
public record FaceStatusDto(
    bool Enabled, bool Enrolled, bool HasPhoto, string ModelVersion, double Threshold,
    int AttemptsLeft, FaceQualityLimitsDto Quality,
    bool RequireLiveness = false, bool RequireAttestation = false,
    IReadOnlyList<string>? LivenessActions = null,
    int LivenessMinMs = 0, int LivenessMaxMs = 0);

/// <summary>`POST /api/student/face/challenge` javobi — BIR MARTALIK tiriklik chaqiruvi.
/// <para>⚠️ <paramref name="Actions"/> TARTIBI muhim: `verify` da natija AYNAN shu tartibda
/// kelishi shart. <paramref name="Nonce"/> bir marta ishlatiladi va Play Integrity tokeniga
/// ham qo'yiladi (server ikkalasini solishtiradi).</para></summary>
public record FaceChallengeDto(
    string Nonce, IReadOnlyList<string> Actions, string ExpiresAt, int TtlSeconds,
    int MinActionMs, int MaxActionMs);

/// <summary>`POST /api/student/face/verify` javobi.
/// ⚠️ Rad etilgan urinish ham HTTP 200 bilan qaytadi (ilova sababni ko'rsatadi); 4xx faqat
/// texnik xatolarda (buzuq vektor, katta fayl, ruxsat yo'q).</summary>
/// <param name="Token">Muvaffaqiyatda — TO'LIQ JWT (cheklangan token o'rniga qo'yiladi).</param>
/// <param name="RefreshToken">Muvaffaqiyatda — 30 kunlik refresh token (MOBIL uchun; web cookie'dan oladi).</param>
public record FaceVerifyResponse(
    bool Ok, string Status, string Reason, double? Score, int AttemptsLeft,
    string? Token = null, bool Enrolled = false, string? RefreshToken = null);

/* ---------- Admin ---------- */

/// <summary>Admin ro'yxatidagi bitta urinish.</summary>
/// <param name="ImageUrl">⚠️ Bu `/uploads/...` MANZILI EMAS — avtorizatsiyalangan admin
/// endpointi (`/api/admin/face/checks/{id}/image`). Selfilar `uploads/face/` da yotadi va u
/// papka statik yo'l bilan UMUMAN berilmaydi (`PrivateFolderFileProvider`), zaxira arxiviga ham
/// kirmaydi. Bo'sh satr — rasm yo'q.</param>
/// <param name="Attested">Ilova haqiqiyligi: ok | failed | unavailable | notConfigured.</param>
public record FaceCheckDto(
    string Id, string StudentId, string StudentName, string CreatedAt,
    string Status, string Reason, double? Score, string ImageUrl,
    string DeviceId, string DeviceName, string Platform, string AppVersion,
    string Ip, string ModelVersion, string Quality, bool CanApprove,
    string Attested = "", string AttestReason = "");

/// <summary>Ishonchli qurilma (admin ro'yxati).</summary>
public record FaceDeviceDto(
    string Id, string UserId, string StudentId, string StudentName,
    string DeviceId, string DeviceName, string Platform,
    string CreatedAt, string LastSeenAt, string? RevokedAt);

/// <summary>Etalon holati (admin uchun) — o'quvchi profilida ko'rsatish uchun.</summary>
/// <param name="SampleUrl">⚠️ `FaceCheckDto.ImageUrl` bilan bir xil siyosat: bu admin endpointi
/// (`/api/admin/face/profile/{studentId}/image`), `/uploads/...` manzili EMAS.</param>
public record FaceProfileDto(
    string StudentId, string StudentName, string ModelVersion, string Source,
    string SampleUrl, int Dim, string CreatedAt, string UpdatedAt);

/// <summary>`GET|PUT /api/admin/face/settings`.</summary>
/// <param name="Enabled">⚠️ GET javobida bu "sozlama YOQILGAN <b>va</b> kalit bor" degani.
/// <paramref name="VaultReady"/> false bo'lsa modul kalitsiz ishlay olmaydi — UI shuni
/// tushuntirib turishi kerak (PUT esa sozlamaning O'ZINI yozadi).</param>
/// <param name="RequireLiveness">Tiriklik tekshiruvi majburiyligi (default TRUE).</param>
/// <param name="RequireAttestation">Play Integrity majburiyligi (default FALSE).</param>
/// <param name="VaultReady">`.env` da `FACE_VECTOR_KEY` bormi (faqat O'QISH — PUT'da e'tiborsiz).</param>
/// <param name="AttestationConfigured">`PLAY_INTEGRITY_*` sozlanganmi (faqat O'QISH).</param>
public record FaceSettingsDto(
    bool Enabled, double Threshold, string ModelVersion, int KeepChecks,
    bool RequireLiveness = true, bool RequireAttestation = false,
    bool VaultReady = true, bool AttestationConfigured = false);

/// <summary>Urinishni rad etishda ixtiyoriy izoh (foydalanuvchiga ko'rinadigan sabab).</summary>
public record FaceRejectPayload(string? Note);
