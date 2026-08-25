namespace IntellectCRM.Domain;

// Frontend (IntellectCRM.Client/src/types/index.ts) dagi tiplarga mos keluvchi
// EF Core entity'lari. ID'lar string (frontend uid() — UUID ishlatadi),
// sanalar esa ISO ("YYYY-MM-DD") ko'rinishida string sifatida saqlanadi.

/// <summary>
/// Dars o'zlashtirish darajasi (mastery level) — o'qituvchi darsda o'quvchining
/// o'zlashtirish holati qaysi darajada ekanini belgilaydi.
/// </summary>
public enum MasteryLevel
{
    /// <summary>0 — reaktiv emas (o'rgani emas, tushunarli emas).</summary>
    NonReactive = 0,

    /// <summary>1 — reaktiv (o'rgani lekin yordam bilan).</summary>
    Reactive = 1,

    /// <summary>2 — faol (o'rgani va mustaqil ishlay oladi).</summary>
    Active = 2,

    /// <summary>3 — proaktiv (chuqur o'rgani va boshqalarga o'rgata oladi).</summary>
    ProActive = 3
}

/// <summary>Tizim foydalanuvchisi (autentifikatsiya uchun).</summary>
public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    /// <summary>admin | teacher | student | parent</summary>
    public string Role { get; set; } = "admin";
    public string Email { get; set; } = string.Empty;
    /// <summary>Telefon raqami — admin/xodim Telegram botda ro'yxatdan o'tib (yangi lid) xabarnomalarini
    /// olishi uchun shu raqam bo'yicha moslashtiriladi. Bo'sh = botda moslab bo'lmaydi.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>
    /// Admin yaratgan/tiklagan dastlabki parol — OCHIQ matnda, FAQAT foydalanuvchi hali u bilan
    /// kirmaguncha. Superadmin'ga ko'rsatish/eksport uchun. Birinchi login'da yoki foydalanuvchi
    /// o'zi parolni o'zgartirsa null bo'ladi (faqat hash qoladi).
    /// </summary>
    public string? InitialPassword { get; set; }
    /// <summary>Birinchi muvaffaqiyatli login vaqti (ISO "yyyy-MM-ddTHH:mm:ss") — "ilova aktivlashtirilgan" sifatida ishlatiladi.</summary>
    public string? FirstLoginAt { get; set; }
    /// <summary>Oxirgi muvaffaqiyatli login vaqti — har kirilganda yangilanadi.</summary>
    public string? LastLoginAt { get; set; }
    /// <summary>Ketma-ket noto'g'ri parol urinishlari soni (brute-force himoyasi). Muvaffaqiyatli
    /// login yoki bloklashda 0 ga tushadi.</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>Akkaunt vaqtincha bloklangan bo'lsa — blok tugash vaqti (ISO "yyyy-MM-ddTHH:mm:ss").
    /// null yoki o'tmishda = bloklanmagan. 5 ketma-ket noto'g'ri urinishdan keyin 3 daqiqaga o'rnatiladi.</summary>
    public string? LockoutUntil { get; set; }
    /// <summary>Xodim (role="staff") lavozimi — Kassir/Administrator/... (faqat ko'rsatish uchun yorliq).</summary>
    public string Position { get; set; } = string.Empty;
    /// <summary>
    /// Xodimga ochiq admin bo'limlari (adminPermissions kalitlari). FAQAT role="staff" uchun ishlatiladi;
    /// admin/superadmin uchun bo'sh (ular hamma narsani ko'radi). EF Core 8 primitive collection (JSON).
    /// </summary>
    public List<string> Permissions { get; set; } = new();
}

/// <summary>
/// REFRESH TOKEN — uzoq muddatli (30 kun) opaque tasodifiy token. Access token (60 daq) eskirganda
/// mijoz `POST /api/auth/refresh` orqali yangi access + refresh juftligini oladi (qayta login shart
/// emas). ⚠️ Xom (raw) token HECH QACHON saqlanmaydi — bazada faqat uning SHA-256 hashi (<see
/// cref="TokenHash"/>) turadi, ya'ni baza sizib chiqsa ham tokenlarni tiklab bo'lmaydi
/// (<see cref="LoginOtpCode.CodeHash"/> bilan bir xil naqsh).
///
/// <para><b>Rotatsiya + nasl-nasab (family):</b> har refresh'da eski token bekor qilinadi
/// (<see cref="RevokedAt"/>) va yangisi AYNI <see cref="FamilyId"/> bilan chiqadi. Bekor qilingan
/// tokenni qayta ishlatishga urinish (reuse) — o'g'irlik belgisi: butun family bekor qilinadi
/// (`RefreshTokenService`). 60 soniyalik grace oynasi ko'p-tab / parallel so'rovni hujjatdan
/// ajratadi.</para>
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Egasi (<see cref="AppUser.Id"/>).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Xom tokenning SHA-256 hashi (hex). Raw token saqlanmaydi.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Rotatsiya nasl-nasabi — bir login zanjiridagi barcha tokenlar bir xil FamilyId.
    /// Reuse aniqlanganda shu family butunlay bekor qilinadi.</summary>
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    /// <summary>Yaratilgan vaqt (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Amal qilish muddati (UTC) — CreatedAt + 30 kun.</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>Bekor qilingan vaqt (UTC). null = bekor qilinmagan. Rotatsiyada yoki family
    /// bekor qilinganda to'ldiriladi.</summary>
    public DateTime? RevokedAt { get; set; }
    /// <summary>Rotatsiyada — shu tokenni ALMASHTIRGAN yangi tokenning hashi (audit izi).</summary>
    public string? ReplacedByHash { get; set; }
    /// <summary>Yaratilgan so'rov IP'si (ixtiyoriy — kuzatuv uchun).</summary>
    public string? CreatedByIp { get; set; }
    /// <summary>Yaratilgan so'rov User-Agent'i (ixtiyoriy — kuzatuv uchun).</summary>
    public string? UserAgent { get; set; }

    /// <summary>Hozir faolmi (bekor qilinmagan va muddati o'tmagan).</summary>
    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}

/// <summary>O'quv markazi filiali — nomi, manzil, GPS joylashuv va radius (mobil geo-yo'qlama uchun).</summary>
public class Branch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Ruxsat etilgan radius (metr) — shu doira ichida yo'qlama hisoblanadi.</summary>
    public int RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Tuman (hudud) — o'quvchi qaysi tumandan ekanini tanlash uchun. Sozlamalardan boshqariladi.
/// Ichida maktablar (<see cref="School"/>) bo'ladi.</summary>
public class District
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Ko'rsatish tartibi.</summary>
    public int Order { get; set; }
}

/// <summary>Maktab — tumanga tegishli (raqami yoki nomi). O'quvchi tuman → maktabni tanlaydi.
/// Sozlamalardan har tuman ichida yaratiladi.</summary>
public class School
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Tegishli tuman (<see cref="District"/> id).</summary>
    public string DistrictId { get; set; } = string.Empty;
    /// <summary>Maktab raqami yoki nomi (masalan "1", "23-son maktab").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Ko'rsatish tartibi (tuman ichida).</summary>
    public int Order { get; set; }
}

/// <summary>
/// AI tekshiruv yozuvi (Speaking yoki Writing). Bitta yozuv = bitta tekshiruv.
/// Writing: o'quvchi <see cref="InputText"/> yozadi → Gemini tahlil qiladi.
/// Speaking: o'quvchi gapiradi (ovoz <see cref="AudioUrl"/> saqlanadi) → Azure talaffuzni baholaydi
/// (<see cref="AzureJson"/>), tanilgan matn <see cref="RecognizedText"/>, so'ng Gemini tahlil qiladi.
/// Natija (diagramma/so'z tahlili) <see cref="AnalysisJson"/> da. Tarix saqlanadi.
/// </summary>
public class AiCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>"speaking" | "writing".</summary>
    public string Type { get; set; } = "writing";
    /// <summary>Writing rejimi: "" (umumiy) | "ielts_task1" | "ielts_task2" — IELTS band bahosi uchun.</summary>
    public string TaskType { get; set; } = string.Empty;
    /// <summary>Mavzu/topshiriq (ixtiyoriy — o'quvchi yoki tizim bergan).</summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>Writing: o'quvchi yozgan matn. Speaking: o'qish uchun berilgan matn (reference, ixtiyoriy).</summary>
    public string InputText { get; set; } = string.Empty;
    /// <summary>Speaking: nutqdan tanilgan matn (Azure recognized). Writing: bo'sh.</summary>
    public string RecognizedText { get; set; } = string.Empty;
    /// <summary>Speaking: saqlangan ovoz fayli ("/uploads/aicheck-...wav") — qayta eshitish uchun. Writing: bo'sh.</summary>
    public string AudioUrl { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100).</summary>
    public double Score { get; set; }
    /// <summary>Speaking: Azure natijasi JSON (SpeakingResultDto). Writing: bo'sh.</summary>
    public string AzureJson { get; set; } = string.Empty;
    /// <summary>Gemini strukturali tahlil JSON (AiCheckAnalysisDto — diagramma/tuzatish/so'z tahlili).</summary>
    public string AnalysisJson { get; set; } = string.Empty;
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Tekshiruv sanasi ("yyyy-MM-dd") — kunlik limit hisobi uchun.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// O'quvchining AI tekshiruvdan foydalanish ruxsati/cheklovi (per-o'quvchi). Yozuv bo'lmasa —
/// global standart kunlik limit (<see cref="CenterMeta"/>.AiCheckDailyLimit) ishlatiladi.
/// </summary>
public class StudentAiAccess
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Kunlik limit (per-o'quvchi override). 0 = global standart ishlatiladi.</summary>
    public int DailyLimit { get; set; }
    /// <summary>Premium — cheksiz foydalanish (limit qo'llanmaydi).</summary>
    public bool IsPremium { get; set; }
    /// <summary>Bloklangan — AI tekshiruvdan umuman foydalana olmaydi.</summary>
    public bool IsBlocked { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — o'qituvchini rivojlantirish uchun yig'iladigan
/// MATNLI ma'lumot.
///
/// <para><b>Kim yozadi:</b> FAQAT admin/superadmin, o'quvchi profilidagi «Fikr-mulohazalar»
/// bo'limida (o'quvchi yoki ota-ona O'ZI yozmaydi — bu ichki, boshqaruv yozuvi). O'quvchi bir
/// nechta guruhda o'qisa, HAR GURUH o'qituvchisi uchun alohida yoziladi — shuning uchun kalit
/// (o'quvchi, o'qituvchi, guruh) uchligi.</para>
///
/// <para><b>MAXFIYLIK — ENG MUHIM QOIDA:</b> XOM matn O'QITUVCHIGA VA UNING PROFILIGA
/// KO'RSATILMAYDI. U faqat (1) o'quvchi profilida (admin ko'radi) va (2) AI TAHLIL uchun
/// manba sifatida ishlatiladi. O'qituvchi profilidagi «Tahlillar» bo'limida faqat AI
/// UMUMLASHTIRGAN xulosa chiqadi — aks holda o'quvchi ismini taniб, munosabat buzilardi.</para>
///
/// <para>Tahlil MATN asosida: baho/ball emas, aynan yozilgan fikrlar AI'ga beriladi va u
/// takrorlanuvchi naqshlarni (kuchli tomon, o'sish nuqtasi) ajratadi.</para>
/// </summary>
public class TeacherReview
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Kimning fikri (<see cref="Student.Id"/>).</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Kim haqida (<see cref="Teacher.Id"/>).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Qaysi guruh konteksti (<see cref="Group.Id"/>) — bir o'qituvchining bir nechta
    /// guruhi bo'lishi mumkin, fikr aynan shu guruhdagi ish haqida.</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Fikr matni (AI tahlilining asosiy manbai).</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Yozilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss") — ro'yxat shu bo'yicha kamayish
    /// tartibida (eng yangisi tepada).</summary>
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Kim yozgani — admin F.I.Sh (ko'rsatish uchun).</summary>
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Kim yozgani — akkaunt id'si (<see cref="AppUser.Id"/>).</summary>
    public string? CreatedById { get; set; }
}

/// <summary>Ota-ona ilova orqali yuborgan taklif yoki shikoyat.</summary>
public class Feedback
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Yuborgan o'quvchi (ota-ona o'quvchi akkaunti orqali) id'si.</summary>
    public string StudentId { get; set; } = string.Empty;
    public string ParentName { get; set; } = string.Empty;
    /// <summary>suggestion | complaint</summary>
    public string Type { get; set; } = "suggestion";
    public string Text { get; set; } = string.Empty;
    /// <summary>Ixtiyoriy biriktirilgan rasm (kameradan) — "/uploads/...". Yo'q bo'lsa null.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Yuboruvchi roli: parent | teacher.</summary>
    public string SenderRole { get; set; } = "parent";
    /// <summary>Yuboruvchining ko'rsatiladigan ismi (ota-ona FISH yoki o'qituvchi FISH).</summary>
    public string SenderName { get; set; } = string.Empty;
    /// <summary>O'qituvchi yuborgan bo'lsa — uning id'si (parent bo'lsa null).</summary>
    public string? TeacherId { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>new | resolved</summary>
    public string Status { get; set; } = "new";
}

/// <summary>
/// O'quvchining BITTA guruhdagi a'zoligi qisqacha (ro'yxat/qidiruv uchun). DB'ga yozilmaydi —
/// <see cref="Student.GroupStates"/> ichida faqat javobda bo'ladi.
/// </summary>
public class StudentGroupState
{
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Guruh o'qituvchisi (<see cref="Group.TeacherId"/>) — o'qituvchi filtri uchun.
    /// Guruh NOMI bo'yicha moslash ishonchsiz edi (bir xil nomli guruhlar bo'lishi mumkin).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>active | trial | frozen</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>O'quvchi.</summary>
public class Student
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>To'liq FISH — saqlanadi (ko'rsatish, qidiruv, hisobotlar). Parts'dan join qilinadi.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Familiya (alohida). FullName parts'dan join qilinadi.</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>Ism (alohida).</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>Otasining ismi / Sharifi (alohida).</summary>
    public string MiddleName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    /// <summary>
    /// O'QUVCHINING RASMI (profil surati) manzili ("/uploads/...").
    ///
    /// <para><b>NOMI ESKI</b> — ilgari tug'ilganlik guvohnomasi uchun edi, lekin butun tizim uni
    /// RASM deb ishlatadi: admin formasidagi yorlig'i "O'quvchi rasmi", o'quvchi ilovasiga
    /// <c>StudentProfileDto.PhotoUrl</c> bo'lib chiqadi, admin profilida esa dumaloq avatarda
    /// ko'rinadi. Ikkinchi "rasm" ustuni OCHILMAYDI — aks holda qaysi biri ko'rsatilishi chalkashardi.</para>
    ///
    /// <para><b>QAYTA NOMLAMANG</b> (2026-08-04 da ko'rib chiqilgan va RAD ETILGAN):
    /// EF Core property rename'ni ANIQLAY OLMAYDI — <c>migrations add</c> buni
    /// <c>DropColumn("BirthCertificateUrl")</c> + <c>AddColumn("PhotoUrl")</c> qilib yozadi, va
    /// <c>Program.cs</c> dagi <c>db.Database.Migrate()</c> uni deployda AVTOMATIK bajaradi →
    /// prodda yig'ilgan barcha rasm manzillari yo'qoladi (fayllar <c>/uploads</c> da qoladi, lekin
    /// nomi tasodifiy GUID — qaysi o'quvchiniki ekani tiklanmaydi). Bundan tashqari eski SQL/JSON
    /// zaxira nusxalari (<c>BackupService</c> entity property nomlari bilan yozadi) yangi sxemaga
    /// mos kelmay qoladi. Zarur bo'lsa — YAGONA xavfsiz yo'l: property nomini o'zgartirib,
    /// Fluent config'da <c>.HasColumnName("BirthCertificateUrl")</c> bilan eski ustunga bog'lash.</para>
    /// </summary>
    public string? BirthCertificateUrl { get; set; }
    public string Address { get; set; } = string.Empty;
    /// <summary>male | female</summary>
    public string Gender { get; set; } = "male";
    /// <summary>O'quvchining o'z telefon raqami (lid formasiga mos).</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>To'liq ota-ona FISH — ASOSIY kontakt (ota, bo'lmasa ona) dan to'ldiriladi. Ota-ona
    /// portali login (telefon), Telegram, e'lonlar shunga tayanadi — shuning uchun saqlanadi.</summary>
    public string ParentFullName { get; set; } = string.Empty;
    /// <summary>Ota-ona familiyasi (alohida).</summary>
    public string ParentLastName { get; set; } = string.Empty;
    /// <summary>Ota-ona ismi (alohida).</summary>
    public string ParentFirstName { get; set; } = string.Empty;
    /// <summary>Ota-ona otasining ismi / sharifi (alohida).</summary>
    public string ParentMiddleName { get; set; } = string.Empty;
    /// <summary>ASOSIY ota-ona telefoni (ota, bo'lmasa ona) — portal login/Telegram/e'lon uchun.</summary>
    public string ParentPhone { get; set; } = string.Empty;
    /// <summary>Otasi F.I.SH (lid formasiga mos).</summary>
    public string FatherFullName { get; set; } = string.Empty;
    /// <summary>Otasi telefon raqami.</summary>
    public string FatherPhone { get; set; } = string.Empty;
    /// <summary>Onasi F.I.SH.</summary>
    public string MotherFullName { get; set; } = string.Empty;
    /// <summary>Onasi telefon raqami.</summary>
    public string MotherPhone { get; set; } = string.Empty;
    /// <summary>Ota-onaning rasmi (profil surati) manzili (`/uploads/...`). Formadan olib tashlandi —
    /// eski yozuvlar uchun saqlanadi.</summary>
    public string? ParentPassportUrl { get; set; }
    public string ClassName { get; set; } = string.Empty;
    /// <summary>O'quvchi tegishli tuman (<see cref="District"/> id). Bo'sh = tanlanmagan.</summary>
    public string DistrictId { get; set; } = string.Empty;
    /// <summary>O'quvchi tegishli maktab (<see cref="School"/> id). Bo'sh = tanlanmagan.</summary>
    public string SchoolId { get; set; } = string.Empty;
    /// <summary>Tuman nomi (DB'ga yozilmaydi — ro'yxat/profil endpointida DistrictId'dan to'ldiriladi).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DistrictName { get; set; } = string.Empty;
    /// <summary>Maktab nomi/raqami (DB'ga yozilmaydi — SchoolId'dan to'ldiriladi).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string SchoolName { get; set; } = string.Empty;
    /// <summary>
    /// O'quvchi HOZIR QATNAYOTGAN guruh nomlari (ro'yxat ustunida ko'rinadi; DB'ga yozilmaydi —
    /// ro'yxat endpointida M2M a'zoliklardan to'ldiriladi).
    ///
    /// <para>⚠️ MUZLATILGAN a'zoliklar bu ro'yxatga KIRMAYDI. Ilgari kirardi va o'quvchi eski
    /// guruhida muzlatilib, yangisida aktiv bo'lsa ro'yxatda IKKALA guruh ko'rinardi — go'yo u
    /// hali ham eski o'qituvchida o'qiyotgandek. Sinov (trial) a'zoliklar QOLADI: ular haqiqatan
    /// darsga qatnaydi va ularni yashirsak sinovdagi o'quvchi "guruhsiz" bo'lib chiqardi.</para>
    ///
    /// <para>Batafsil kesim (holat + o'qituvchi) — <see cref="GroupStates"/>.</para>
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<string> Groups { get; set; } = new();
    /// <summary>
    /// Har bir a'zolikning TO'LIQ kesimi: guruh, o'qituvchi va HOLAT. Filtrlar aynan shundan
    /// ishlaydi — "falon o'qituvchining AKTIV o'quvchilari" savoliga faqat shu ma'lumot javob
    /// bera oladi (guruh NOMI va o'quvchi darajasidagi "aktiv" bayrog'i yetarli emas).
    /// DB'ga yozilmaydi.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<StudentGroupState> GroupStates { get; set; } = new();
    /// <summary>Kursda FAOL — kamida bitta a'zoligi Status=="active" (sinov/muzlatilgan/guruhsiz emas).
    /// DB'ga yozilmaydi; ro'yxat endpointida M2M a'zoliklardan hisoblanadi.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Active { get; set; }
    /// <summary>A'zolik holati yorlig'i (ro'yxat/qidiruvda "Aktiv / Sinovda / Muzlatilgan" belgisi uchun):
    /// "active" | "trial" | "frozen" | "" (guruhsiz). Bir nechta guruhda turlicha bo'lsa ustunlik tartibi:
    /// active &gt; trial &gt; frozen. DB'ga yozilmaydi — ro'yxat endpointida M2M a'zoliklardan hisoblanadi.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string MemberState { get; set; } = string.Empty;
    /// <summary>Markazga kelgan (qabul) sanasi (ISO "YYYY-MM-DD"). Oylik to'lov shu oydan boshlanadi.</summary>
    public string EnrollmentDate { get; set; } = string.Empty;
    /// <summary>Tizimga kiritilgan vaqt (ISO). Ro'yxatni "yangi kiritilgani tepada" tartiblash uchun.
    /// Eski yozuvlarda bo'sh — tartiblashda EnrollmentDate'ga tushiladi.</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Balans (so'm): manfiy = qarzdor, 0 = qarzsiz, musbat = avans.</summary>
    public decimal Balance { get; set; }
    /// <summary>Shu o'quvchiga biriktirilgan tizim akkaunti (AppUser) id'si.</summary>
    public string? UserId { get; set; }
    /// <summary>
    /// Oylik to'lov chegirmasi — foiz (0..100). Avval shu foiz olib tashlanadi, keyin
    /// <see cref="DiscountAmount"/> ayriladi. Hisoblangan oylik 0 dan past bo'lmaydi.
    /// </summary>
    public int DiscountPct { get; set; }
    /// <summary>Oylik to'lov chegirmasi — aniq summa (so'm). Foizdan keyin ayriladi.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Chegirma sababi/izohi (admin uchun, ko'rsatish uchun saqlanadi).</summary>
    public string DiscountNote { get; set; } = string.Empty;
    /// <summary>Chegirma amal qilish boshlanish oyi ("yyyy-MM"). Bo'sh — cheklovsiz (boshidan).</summary>
    public string DiscountStartMonth { get; set; } = string.Empty;
    /// <summary>Chegirma amal qilish tugash oyi ("yyyy-MM"). Bo'sh — cheklovsiz (oxirigacha).
    /// Ikkala chegara bo'sh bo'lsa chegirma har doim qo'llanadi (orqaga moslik).</summary>
    public string DiscountEndMonth { get; set; } = string.Empty;
    /// <summary>Chegirma qaysi GURUHGA tegishli (Classes.Id). Null/bo'sh — BARCHA guruh hisoblariga
    /// (eski xatti-harakat). To'ldirilgan bo'lsa — faqat o'sha guruh hisobiga qo'llanadi
    /// (ko'p guruhli o'quvchida qaysi guruhga berilgani aniq bo'lishi uchun).</summary>
    public string? DiscountGroupId { get; set; }
    /// <summary>
    /// O'quvchi arxivga ko'chirilganmi (boshqa maktabga ketgan, o'qishdan chiqarilgan, ...).
    /// Arxivlangan o'quvchi faol ro'yxatdan yashirinadi, oylik to'lov hisoblanmaydi, login bloklanadi,
    /// lekin tarixiy ma'lumotlari (jurnal, davomat, to'lovlar) saqlanadi.
    /// </summary>
    public bool IsArchived { get; set; }
    /// <summary>Admin o'quvchi login'ini vaqtincha cheklagan — kira olmaydi, 'hali aktiv emas' xabari.</summary>
    public bool LoginBlocked { get; set; }
    /// <summary>Arxivga ko'chirilgan sana (ISO "YYYY-MM-DD").</summary>
    public string? ArchivedAt { get; set; }
    /// <summary>Arxivga ko'chirish sababi (admin kiritadi: "boshqa maktabga ketdi", ...).</summary>
    public string? ArchiveReason { get; set; }
    /// <summary>Guruh arxivlanishi tufayli arxivlangan bo'lsa true — guruh arxivdan chiqarilganda
    /// faqat shu o'quvchilar avtomatik qaytariladi (alohida arxivlanganlar tegilmaydi).</summary>
    public bool ArchivedWithClass { get; set; }
    /// <summary>O'quvchi uy joylashuvi — kenglik (latitude). Mobil ilovadan GPS orqali keladi.</summary>
    public double? Latitude { get; set; }
    /// <summary>O'quvchi uy joylashuvi — uzunlik (longitude).</summary>
    public double? Longitude { get; set; }
    /// <summary>Joylashuv manzili (reverse geocode'dan keladigan matn, ixtiyoriy).</summary>
    public string? LocationAddress { get; set; }
    /// <summary>Joylashuv oxirgi yangilangan vaqt (ISO).</summary>
    public string? LocationUpdatedAt { get; set; }
    /// <summary>Turniket/FaceID qurilmasidagi shaxs ID'si (personId/employeeNo). Turniket o'tish
    /// hodisalari shu ID orqali o'quvchiga bog'lanadi (kirgan/chiqqan vaqt). Bo'sh = moslanmagan.</summary>
    public string DeviceUserId { get; set; } = string.Empty;

    // ---------- O'quvchini ushlab turish bonusi (retention) ----------
    /// <summary>Shu o'quvchi USHLAB TURISH BONUSI tizimiga kiradimi (admin qo'lda belgilaydi).
    /// false = bonus hisoboti ro'yxatida umuman ko'rinmaydi. Avtomatik yoqilmaydi — markaz egasi
    /// kimga bonus tizimi tegishli ekanini o'zi hal qiladi (retroaktiv "eski o'quvchilarga
    /// birdaniga bonus chiqib ketishi" xavfi shu bilan yopiladi).</summary>
    public bool RetentionBonus { get; set; }
    /// <summary>Bonus sanog'i QAYSI OYDAN boshlanadi ("YYYY-MM"). Admin QO'LDA kiritadi —
    /// avtomatik to'ldirilmaydi. Bo'sh = "hali boshlanmagan" (sanoq ko'rsatilmaydi).
    /// <see cref="RetentionBonus"/> yoqilgan, lekin bu bo'sh bo'lsa — o'quvchi ro'yxatda
    /// "boshlanish oyi kiritilmagan" holatida turadi.</summary>
    public string RetentionBonusStartMonth { get; set; } = string.Empty;
}

/// <summary>
/// BERILGAN USHLAB TURISH BONUSI — bitta yakunlangan sikl (o'quvchi N oy uzluksiz o'qidi).
///
/// <para>Nega faqat YAKUNIY natija saqlanadi: oylik holatlar (✅/⏳/❄️) HECH QAYERDA saqlanmaydi,
/// har so'rovda qayta hisoblanadi — chunki superadmin <see cref="MonthlyCharge"/>ni tahrirlashi,
/// to'lov tuzatilishi yoki vozvrat qilinishi mumkin. Saqlansa jadval haqiqatdan uzilib qolardi.
/// Maosh (<c>SalaryLedger</c>) ham aynan shunday ishlaydi.</para>
///
/// <para><b>Pul chiqimi EMAS:</b> bonus berish — hisoblash/qayd. Haqiqiy pul odatdagi maosh
/// to'lovi (<see cref="FinanceTransaction"/> expense/salary) orqali beriladi. Aks holda Kassa va
/// Moliya bir xil pulni ikki marta hisoblardi.</para>
/// </summary>
public class RetentionBonusAward
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>O'quvchi F.I.Sh — SNAPSHOT (o'quvchi o'chirilsa/arxivlansa ham tarix o'qiladi).</summary>
    public string StudentName { get; set; } = string.Empty;
    /// <summary>Qaysi FAN (kurs) bo'yicha berilgan — <see cref="Group.CourseId"/> (Subject id);
    /// kursi biriktirilmagan eski guruhda esa guruh id'si. Sikl HAR FAN uchun ALOHIDA yuritiladi
    /// (o'quvchi 2 fanga qatnasa — 2 mustaqil sanoq, 2 mustaqil bonus).</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Fan nomi — SNAPSHOT (kurs o'chirilsa/nomi o'zgarsa ham tarix o'qiladi).</summary>
    public string CourseName { get; set; } = string.Empty;
    /// <summary>Nechanchi sikl (1, 2, 3 …). <c>(StudentId, CourseId, CycleNo)</c> NOYOB — takroriy
    /// bonus mumkin emas. Bekor qilingan bonus ham raqamni BAND qiladi (sikl qaytarilmaydi).</summary>
    public int CycleNo { get; set; } = 1;
    /// <summary>Sikl boshlanish oyi ("YYYY-MM").</summary>
    public string PeriodFrom { get; set; } = string.Empty;
    /// <summary>Sikl tugash oyi ("YYYY-MM") — hisobga kirgan oxirgi oy.</summary>
    public string PeriodTo { get; set; } = string.Empty;
    /// <summary>Jami bonus summasi (so'm) — admin berish paytida kiritadi.</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>"given" (berilgan) | "cancelled" (bekor qilingan — xato kiritilgan bo'lsa).</summary>
    public string Status { get; set; } = StatusGiven;
    public const string StatusGiven = "given";
    public const string StatusCancelled = "cancelled";
    /// <summary>Bekor qilish sababi (Status=="cancelled" bo'lsa).</summary>
    public string CancelReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Kim bergani (admin F.I.Sh).</summary>
    public string GivenBy { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Bitta bonusning BIR O'QITUVCHIGA tegishli ulushi. Bir sikl → N qator (o'quvchi davr ichida
/// o'qituvchi/guruh almashtirgan bo'lsa). Ulush o'qigan oylar nisbatida hisoblanadi
/// (<c>RetentionBonusService</c>), lekin admin berish modalida QO'LDA o'zgartira oladi.
/// </summary>
public class RetentionBonusShare
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AwardId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>O'qituvchi F.I.Sh — SNAPSHOT (o'qituvchi o'chirilsa ham tarix o'qiladi).</summary>
    public string TeacherName { get; set; } = string.Empty;
    /// <summary>Shu o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin: o'quvchi bir vaqtda ikki
    /// guruhda o'qisa oy vazni 1.0 guruhlar narxi nisbatida bo'linadi).</summary>
    public decimal Months { get; set; }
    /// <summary>Shu o'qituvchiga tegadigan summa (so'm).</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Bonus sanog'ining HAR FAN uchun joriy holati. Nega alohida jadval: o'quvchi bir nechta fanga
/// qatnashi mumkin va har fanning sikli mustaqil boshlanadi/tugaydi
/// (<see cref="Student.RetentionBonusStartMonth"/> — faqat BOSHLANG'ICH qiymat, hamma fan uchun).
///
/// <para>Qator bo'lmasa — <see cref="Student.RetentionBonusStartMonth"/> ishlatiladi (orqaga moslik).
/// Bonus BERILGANDA bu qator upsert qilinadi (<c>StartMonth = keyingi oy</c>); «Qayta boshlash» ham
/// shu qatorni yozadi. Bonus BEKOR qilinganda qator QAYTARILMAYDI — bekor qilingan sikl qaytmaydi.</para>
/// </summary>
public class RetentionBonusTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Kurs (Subject id); kursi yo'q eski guruhda — guruh id'si.</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Shu fan uchun joriy sikl qaysi oydan sanaladi ("YYYY-MM").</summary>
    public string StartMonth { get; set; } = string.Empty;
    /// <summary>
    /// Shu fan bo'yicha bonus HISOBLANADIMI. Admin buni a'zolikni AKTIVLASHTIRISH oynasida
    /// belgilaydi — aynan o'sha paytda, chunki o'quvchi guruhga bir oyda qo'shilib, keyingi
    /// oydan aktivlashtirilishi mumkin va sanoq AKTIVLASHTIRILGAN oydan boshlanishi kerak.
    /// <para>false — fan bonus hisobotida umuman ko'rinmaydi (sanoq oyi saqlanib qoladi, ya'ni
    /// qayta yoqilsa tarix yo'qolmaydi).</para>
    /// </summary>
    public bool Enabled { get; set; } = true;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// O'quvchi profilidagi ERKIN IZOH (xodim yozadigan eslatma: ota-ona bilan suhbat, to'lov kelishuvi,
/// sog'lig'i va h.k.). Lid izohlari (<see cref="LeadEvent"/>) kabi TARIX: har yozuv o'z muallifi va
/// vaqti bilan saqlanadi, ustiga yozilmaydi. O'quvchi o'chirilsa izohlari ham o'chadi.
/// </summary>
public class StudentNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi o'quvchiga (Student.Id).</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Izoh matni.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Yozgan xodim F.I.Sh (faqat ko'rsatish uchun — xodim o'chsa ham izoh muallifi qoladi).</summary>
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>Yozgan xodim (AppUser.Id) — "faqat o'zi o'chira/tahrirlay oladi" qoidasi uchun.</summary>
    public string AuthorId { get; set; } = string.Empty;
    /// <summary>Yozilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Oxirgi TAHRIRLANGAN vaqt (ISO). null = tahrirlanmagan. Izoh tarix bo'lgani uchun
    /// tahrirlangani ro'yxatda "(tahrirlangan)" deb ko'rinadi.</summary>
    public string? EditedAt { get; set; }
}

/// <summary>O'qituvchi.</summary>
public class Teacher
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    /// <summary>O'qituvchining rasmi (profil surati) manzili (`/uploads/...`).</summary>
    public string? PhotoUrl { get; set; }
    /// <summary>Telefon raqami — Telegram bot orqali ro'yxatdan o'tishda moslashtiriladi (shartnoma).</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Turniket/FaceID qurilmasidagi xodim ID'si (personId/employeeNo). Davomat hodisalari shu
    /// ID orqali o'qituvchiga bog'lanadi. Bo'sh = qurilmada moslashtirilmagan.</summary>
    public string DeviceUserId { get; set; } = string.Empty;
    /// <summary>Guruh rahbari bo'lsa biriktirilgan guruh nomi; aks holda bo'sh.</summary>
    public string HomeroomClass { get; set; } = string.Empty;
    /// <summary>Dars beradigan fanlar (Subject id'lari). EF Core 8 primitive collection.</summary>
    public List<string> SubjectIds { get; set; } = new();
    /// <summary>Maosh rejimi: "fixed" (qat'iy oylik summa — <see cref="Salary"/>) | "percent" (foizli —
    /// o'qituvchi o'tadigan guruh(lar) o'quvchilaridan SHU OYDA haqiqatan yig'ilgan to'lovning
    /// <see cref="SalaryPercent"/> foizi). Standart: "fixed".</summary>
    public string SalaryMode { get; set; } = "fixed";
    /// <summary>Qat'iy oylik ish haqi (so'm). <see cref="SalaryMode"/>=="fixed" da ishlatiladi (admin qo'lda kiritadi).</summary>
    public decimal Salary { get; set; }
    /// <summary>Foizli maosh ulushi (%). <see cref="SalaryMode"/>=="percent" da: o'qituvchi guruhlaridan shu oyda
    /// yig'ilgan to'lovning shu foizi maosh sifatida hisoblanadi. Masalan 40 → yig'ilganning 40%i.</summary>
    public decimal SalaryPercent { get; set; }
    /// <summary>O'qituvchi toifasi — bir soat dars narxini belgilaydi: "oliy" | "1" | "2" | "mutaxasis"
    /// (bo'sh = hali belgilanmagan, narxi 0). Soat narxlari CenterMeta'da toifa bo'yicha saqlanadi.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Ustama foizi (%). Oylik maoshga shu foiz qo'shiladi (0 = ustama yo'q). Masalan 50 → +50%.</summary>
    public decimal BonusPct { get; set; }
    /// <summary>
    /// Oylik qaysi oydan hisoblana boshlasin ("YYYY-MM"). ESKI maydon — endi <see cref="SalaryStartDate"/>
    /// ishlatiladi (to'liq sana). Zaxira sifatida qoldirilgan (SalaryStartDate bo'sh bo'lsa o'qiladi).
    /// </summary>
    public string SalaryStartMonth { get; set; } = string.Empty;
    /// <summary>
    /// Maosh qaysi KUNdan hisoblana boshlasin ("YYYY-MM-DD"). O'qituvchi oy o'rtasida kelsa — birinchi
    /// oy shu kundan oy oxirigacha QISMAN (haqiqiy darslar soni bo'yicha) hisoblanadi. Keyingi oylar to'liq.
    /// </summary>
    public string SalaryStartDate { get; set; } = string.Empty;
    /// <summary>Shu o'qituvchiga biriktirilgan tizim akkaunti (AppUser) id'si.</summary>
    public string? UserId { get; set; }
    /// <summary>
    /// O'qituvchi web panelida foydalana oladigan bo'limlar (TeacherPermissions kalitlari).
    /// Admin belgilaydi. Bo'sh = faqat Bosh sahifa. EF Core 8 primitive collection (JSON).
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>SUPPORT o'qituvchimi — bo'sh vaqt slotlarini e'lon qiladi, o'quvchilar bron qiladi
    /// (qo'shimcha/yordam darslari). Admin O'qituvchi formasida belgilaydi. Bo'lim: "Ilova → Support".</summary>
    public bool IsSupport { get; set; }

    /// <summary>Arxivlanganmi (ishdan ketgan/to'xtatilgan). Faol ro'yxatdan yashiriladi, login bloklanadi.</summary>
    public bool IsArchived { get; set; }
    /// <summary>Arxivga olingan sana ("YYYY-MM-DD").</summary>
    public string? ArchivedAt { get; set; }
    /// <summary>Arxivga olish sababi.</summary>
    public string? ArchiveReason { get; set; }

    /// <summary>
    /// VAQTINCHA AKTIV EMAS (ta'til, to'xtatib turish, intizomiy chora) — o'qituvchi tizimga
    /// KIRA OLMAYDI (login ham, eski token ham rad etiladi), lekin ro'yxatdan yo'qolmaydi:
    /// guruhlari, maoshi, jurnali va butun tarixi joyida qoladi.
    ///
    /// <para>Arxivlashdan farqi: arxivda PAROL O'CHIRILADI (<c>AppUser.BlockLogin</c>) va
    /// qaytarishda yangi parol kerak bo'ladi; bu yerda parol TEGILMAYDI — bir tugma bilan
    /// qaytariladi. <see cref="Group.IsBlocked"/> (guruhni vaqtincha bloklash) bilan bir xil g'oya.</para>
    /// </summary>
    public bool IsBlocked { get; set; }
    /// <summary>Vaqtincha faolsizlantirilgan sana ("YYYY-MM-DD").</summary>
    public string? BlockedAt { get; set; }
    /// <summary>Vaqtincha faolsizlantirish izohi (ixtiyoriy) — faqat admin ko'radi.</summary>
    public string BlockNote { get; set; } = string.Empty;
}

/// <summary>
/// Support o'qituvchining bo'sh vaqt SLOTi + bron. Support slot e'lon qiladi (open); o'quvchi
/// uni bron qiladi (StudentId qo'yiladi, booked); support dars o'tgach mavzu/izoh yozib yopadi (done).
/// Bitta slot = bitta bron = bitta dars yozuvi (1:1).
/// </summary>
public class SupportSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Support o'qituvchi (Teacher.Id, IsSupport=true).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Sana "yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Boshlanish vaqti "HH:mm".</summary>
    public string StartTime { get; set; } = string.Empty;
    /// <summary>Tugash vaqti "HH:mm".</summary>
    public string EndTime { get; set; } = string.Empty;
    /// <summary>Holat: "open" (bo'sh) | "booked" (bron qilingan) | "done" (dars o'tildi).</summary>
    public string Status { get; set; } = "open";
    /// <summary>Bron qilgan o'quvchi (Student.Id); null = hali bo'sh.</summary>
    public string? StudentId { get; set; }
    /// <summary>Bron qilingan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string? BookedAt { get; set; }
    /// <summary>Dars mavzusi — support dars o'tgach yozadi.</summary>
    public string Topic { get; set; } = string.Empty;
    /// <summary>Dars izohi (nimalar bo'lgani) — support dars o'tgach yozadi.</summary>
    public string Notes { get; set; } = string.Empty;
    /// <summary>Slot yaratilgan vaqt (ISO).</summary>
    public string CreatedAt { get; set; } = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}

/// <summary>Kurs (oldin "Fan"). Nom + oylik narx.</summary>
public class Subject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Kurs oylik narxi (so'm). Guruh shu kursga biriktirilganda guruh oyligi (MonthlyFee) shundan keladi.</summary>
    public decimal Price { get; set; }
    /// <summary>Bir dars uchun yaxlit narx (so'm). Qisman-oy aktivlashtirishda 12 tadan kam dars
    /// qolganda har bir dars uchun shu summa olinadi (oylik narxdan mustaqil). 0 = kiritilmagan
    /// (eski pro-rata formula ishlatiladi).</summary>
    public decimal LessonPrice { get; set; }
}

/// <summary>O'quv xonasi (auditoriya). Guruhlarga FK orqali bog'lanadi.</summary>
public class Room
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    /// <summary>Xona sig'imi (o'quvchilar soni). Default 30.</summary>
    public int Capacity { get; set; } = 30;
    /// <summary>Bino nomi yoki raqami (ixtiyoriy).</summary>
    public string? Building { get; set; }
    /// <summary>Xona joylashuvi/tavsifi (ixtiyoriy).</summary>
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Guruh.</summary>
public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int Grade { get; set; }
    /// <summary>uz | ru</summary>
    public string Language { get; set; } = "uz";
    public decimal MonthlyFee { get; set; }
    /// <summary>Xona nomi (matnli, eski — backward compat). Yangi guruhlarda RoomId ishlatiladi.</summary>
    public string? Room { get; set; }
    /// <summary>O'quv xonasi FK (Room.Id). Nullable — xona ko'rsatilmasa null.</summary>
    public string? RoomId { get; set; }
    /// <summary>Guruh holati: active (faol) | full (to'lgan) | archived (arxiv).</summary>
    public string Status { get; set; } = "active";
    /// <summary>Kurs boshlanish sanasi (ISO "YYYY-MM-DD"). Ixtiyoriy.</summary>
    public string? StartDate { get; set; }
    /// <summary>Kurs tugash sanasi (ISO "YYYY-MM-DD"). Ixtiyoriy.</summary>
    public string? EndDate { get; set; }
    /// <summary>O'quvchilar soni chegarasi (0 = cheksiz).</summary>
    public int Capacity { get; set; }
    /// <summary>Guruh arxivlangan (faol ro'yxatdan olib qo'yilgan). Arxivlanganda unga bog'langan
    /// o'quvchilar ham arxivlanadi; arxivdan chiqarilganda — qaytariladi.</summary>
    public bool IsArchived { get; set; }
    public string? ArchivedAt { get; set; }

    /// <summary>
    /// VAQTINCHA BLOKLANGAN — guruh o'qituvchi ilovasida UMUMAN ko'rinmaydi (ro'yxat, jurnal,
    /// baholash, testlar, chat) va o'qituvchi unga yoza olmaydi. Admin panelida esa guruh
    /// odatdagidek qoladi (faol ro'yxatda, belgisi bilan) — pul/a'zolik/hisobotga TEGMAYDI.
    ///
    /// <para>Arxivlashdan farqi: arxiv — YAKUNLASH (o'quvchilar ham arxivlanadi/muzlatiladi,
    /// hisob yopiladi), bu esa faqat KO'RINISH darvozasi va bir tugma bilan qaytariladi.</para>
    /// </summary>
    public bool IsBlocked { get; set; }
    /// <summary>Bloklangan sana ("YYYY-MM-DD").</summary>
    public string? BlockedAt { get; set; }
    /// <summary>Bloklash izohi (ixtiyoriy) — nega bloklangani. O'qituvchiga KO'RSATILMAYDI.</summary>
    public string BlockNote { get; set; } = string.Empty;

    // ---------- Kurs / biriktirish (eski "Fan biriktirish" o'rnida — guruh yaratishda kiritiladi) ----------
    /// <summary>Guruh kursi (Subject id). Guruh oyligi (MonthlyFee) shu kurs narxidan keladi.</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Biriktirilgan o'qituvchi (Teacher id).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Izoh.</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Dars kunlari (0=Dushanba ... 6=Yakshanba).</summary>
    public List<int> Days { get; set; } = new();
    /// <summary>Dars boshlanish vaqti "HH:mm".</summary>
    public string StartTime { get; set; } = string.Empty;
    /// <summary>Dars tugash vaqti "HH:mm".</summary>
    public string EndTime { get; set; } = string.Empty;

    // ---------- O'qituvchi maoshi (PER-GURUH) ----------
    /// <summary>
    /// Shu guruh uchun o'qituvchi maoshi qanday hisoblanadi: "" (sozlanmagan — o'qituvchining umumiy
    /// <see cref="Teacher.SalaryMode"/> sozlamasiga ergashadi) | "percent" (shu guruhdan yig'ilgan
    /// to'lovning <see cref="TeacherSalaryPercent"/> foizi) | "fixed" (shu guruh uchun qat'iy summa
    /// <see cref="TeacherSalaryFixed"/>). Bir o'qituvchining har guruhi alohida sozlanishi mumkin
    /// (masalan bir guruhi 40%, keyingisi 60% yoki qat'iy summa) — o'qituvchi oyligi guruhlar yig'indisi.
    /// </summary>
    public string TeacherSalaryMode { get; set; } = string.Empty;
    /// <summary>Shu guruh foizli bo'lsa — o'qituvchiga beriladigan ulush (%). Masalan 40 → guruhdan
    /// yig'ilganning 40%i. <see cref="TeacherSalaryMode"/>=="percent" da ishlatiladi.</summary>
    public decimal TeacherSalaryPercent { get; set; }
    /// <summary>Shu guruh qat'iy bo'lsa — o'qituvchiga shu guruh uchun beriladigan oylik qat'iy summa (so'm).
    /// <see cref="TeacherSalaryMode"/>=="fixed" da ishlatiladi.</summary>
    public decimal TeacherSalaryFixed { get; set; }
}

/// <summary>
/// GURUHNING O'QITUVCHI TARIXI — kim guruhni qachondan qachongacha o'qitgani.
///
/// <para>Nega kerak: <see cref="Group.TeacherId"/> faqat <b>HOZIRGI</b> o'qituvchini saqlaydi va
/// almashganda eski qiymat ustiga yoziladi. Jurnal (<see cref="LessonNote"/>, <see cref="JournalEntry"/>)
/// ham o'qituvchini yozmaydi. Ya'ni "2026-09 da bu guruhni kim o'qitgan?" savoliga bazada javob
/// yo'q edi. O'quvchini ushlab turish bonusi (retention) esa bonusni o'qigan oylar nisbatida
/// o'qituvchilar orasida bo'lishi kerak — shuning uchun aynan shu savolga javob talab qilinadi.</para>
///
/// <para><b>ORQAGA SANALMAYDI:</b> <see cref="FromDate"/>/<see cref="ToDate"/> har doim amal
/// bajarilgan kundagi <c>AppClock.Today</c> bilan yoziladi (<see cref="StudentGroup.RecordedAt"/>
/// bilan bir xil tamoyil) — admin tarixni orqaga o'zgartira olmaydi.</para>
///
/// <para><b>Invariant:</b> bir guruhda bir vaqtda ko'pi bilan BITTA ochiq (<c>ToDate == null</c>)
/// qator bo'ladi. Yozish yagona joyda — <c>GroupTeacherHistory.AssignAsync</c>.</para>
///
/// <para><b>Cheklov:</b> migratsiyadagi backfill har mavjud guruh uchun bitta ochiq qator yaratadi,
/// lekin O'TMISHDAGI almashuvlarni tiklay olmaydi — bunday ma'lumot bazada yo'q edi. Tizim to'g'ri
/// taqsimlashni joriy qilingan kundan boshlaydi.</para>
/// </summary>
public class GroupTeacherAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Guruh (<see cref="Group.Id"/>).</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>O'qituvchi (<see cref="Teacher.Id"/>).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Biriktirilgan sana (ISO "YYYY-MM-DD").</summary>
    public string FromDate { get; set; } = string.Empty;
    /// <summary>Biriktirish tugagan sana (ISO "YYYY-MM-DD"). <c>null</c> = HOZIRGI o'qituvchi.</summary>
    public string? ToDate { get; set; }
    /// <summary>Kim biriktirgani (admin F.I.Sh.) yoki backfill uchun "migratsiya".</summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// O'rinbosar o'qituvchi biriktiruvi — muayyan sana(lar)da guruhga asosiy o'qituvchi o'rniga
/// vaqtincha boshqa o'qituvchi dars o'tishi uchun tayinlanadi.
/// </summary>
public class SubstituteTeacherAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Guruh/Class ID (<see cref="Group.Id"/>).</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Asosiy (doimiy) o'qituvchi ID (<see cref="Teacher.Id"/>).</summary>
    public string OriginalTeacherId { get; set; } = string.Empty;
    /// <summary>O'rinbosar (vaqtincha) o'qituvchi ID (<see cref="Teacher.Id"/>).</summary>
    public string SubstituteTeacherId { get; set; } = string.Empty;
    /// <summary>O'rinbosar dars o'tadigan sana ("YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Vaqtinchalik biriktirish tugash sanasi ("YYYY-MM-DD", bo'sh/null = faqat bitta kun Date).</summary>
    public string? EndDate { get; set; }
    /// <summary>Almashtirish sababi (masalan: "Kasal bo'lib qolgani sababli", "Ta'tilda").</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Biriktirishni yaratgan admin/xodim nomi.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Yaratgan admin/xodim foydalanuvchi ID (<see cref="AppUser.Id"/>).</summary>
    public string? CreatedById { get; set; }
    /// <summary>Yaratilgan vaqti.</summary>
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Biriktiruv faolmi (bekor qilinmaganmi).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Tanlangan aniq dars sanalari ro'yxati (ISO "YYYY-MM-DD").</summary>
    public List<string>? SelectedDates { get; set; } = new();
}

/// <summary>
/// O'quvchi ↔ Guruh a'zoligi (M2M). Bir o'quvchi bir vaqtda bir nechta guruhda bo'lishi mumkin.
/// JoinedAt — qo'shilish sanasi, LeftAt — chiqish sanasi (null = hozir ham a'zo).
/// </summary>
public class StudentGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Guruhga qo'shilgan sana (ISO "YYYY-MM-DD").</summary>
    public string JoinedAt { get; set; } = string.Empty;
    /// <summary>Guruhdan chiqqan sana (ISO). null = hozir ham faol a'zo.</summary>
    public string? LeftAt { get; set; }
    /// <summary>Faol a'zomi (LeftAt null bo'lsa true).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>To'lov holati: "trial" (sinov — oylik hisoblanmaydi) | "active" (faol — oylik hisoblanadi)
    /// | "frozen" (muzlatilgan — to'xtatilgan). Yangi a'zo qo'shilganda "trial".</summary>
    public string Status { get; set; } = "trial";
    /// <summary>Aktivlashtirilgan sana (ISO "YYYY-MM-DD"). Birinchi (qisman) oy = (oylik narx ÷ shu oydagi
    /// jami dars) × shu sanadan oy oxirigacha qolgan darslar (guruh kunlari bo'yicha); keyingi oylar — to'liq.</summary>
    public string ActivatedAt { get; set; } = string.Empty;
    /// <summary>Muzlatilgan sana (ISO). Shu oydan boshlab oylik to'lov hisoblanmaydi. Bo'sh = muzlatilmagan.</summary>
    public string FrozenAt { get; set; } = string.Empty;
    /// <summary>Joriy holat (JoinedAt/ActivatedAt) HAQIQATDA tizimga kiritilgan sana (ISO, ORQAGA SANALMAYDI —
    /// har doim shu amal bajarilgan kundagi AppClock.Today). JoinedAt/ActivatedAt orqaga sanalgan bo'lishi mumkin
    /// (masalan o'quvchi o'tgan oydan aktivlashtirilsa) — jurnaldagi "dars o'tildi + yozuv yo'q = keldi"
    /// konventsiyasi FAQAT shu sanadan (RecordedAt) keyingi darslarga qo'llanadi. MemberStart bilan RecordedAt
    /// orasidagi (orqaga sanalgan, hali ko'rib chiqilmagan) darslar bo'sh qoladi — o'qituvchi ularni qo'lda
    /// belgilashi kerak (bloklanmaydi, faqat avtomatik "keldi" bo'lib ko'rinmaydi).</summary>
    public string RecordedAt { get; set; } = string.Empty;
}

/// <summary>
/// Test natijasi — guruh uchun o'tkazilgan bitta test (nomi, sanasi, olish mumkin bo'lgan maksimal ball).
/// O'quv bo'limi → "Testlar natijalari" bo'limida boshqariladi; o'qituvchi o'z guruhlari uchun ham
/// yarata oladi. Ichida har o'quvchi uchun olgan bali (<see cref="TestScore"/>) bo'ladi.
/// </summary>
public class TestResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Guruh (Group.Id) — test shu guruh o'quvchilariga tegishli.</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Test nomi (masalan "Unit 3 test", "Oraliq nazorat").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Test o'tkazilgan sana (ISO "YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Testdan olish mumkin bo'lgan maksimal ball.</summary>
    public decimal MaxScore { get; set; }
    /// <summary>Yaratilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Yaratgan foydalanuvchi ismi (admin yoki o'qituvchi) — faqat ko'rsatish uchun.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    // ---------- ONLAYN test (bot orqali ishlanadi) ----------
    /// <summary>Rejim: <c>"offline"</c> (an'anaviy — ballni o'qituvchi qo'lda kiritadi) |
    /// <c>"online"</c> (o'quvchi Telegram botdan PDF oladi va javoblarini yuboradi).</summary>
    public string Mode { get; set; } = "offline";
    /// <summary>Savollar PDF fayli (URL "/uploads/xxx.pdf") — onlayn testda botga yuboriladi.</summary>
    public string PdfUrl { get; set; } = string.Empty;
    /// <summary>PDF faylning asl nomi (Telegramda shu nom bilan ko'rinadi).</summary>
    public string PdfName { get; set; } = string.Empty;
    /// <summary>Telegram <c>file_id</c> keshi — PDF bir marta yuklanadi, keyingi o'quvchilarga
    /// shu id bilan (qayta yuklamasdan) yuboriladi. APK yuborish bilan bir xil usul.</summary>
    public string PdfFileId { get; set; } = string.Empty;
    /// <summary>Savollar soni (onlayn). Onlayn testda <see cref="MaxScore"/> shunga teng — har savol 1 ball.</summary>
    public int QuestionCount { get; set; }
    /// <summary>Har savoldagi variantlar soni: 4 → A–D, 5 → A–E (2..6).</summary>
    public int OptionCount { get; set; } = 4;
    /// <summary>To'g'ri javoblar kaliti — har savolga bitta harf ("ABCDA..."), uzunligi = <see cref="QuestionCount"/>.</summary>
    public string AnswerKey { get; set; } = string.Empty;
    /// <summary>Javob qabul qilish oynasi BOSHLANISHI (ISO "yyyy-MM-ddTHH:mm"). Bo'sh = test kuni 00:00.</summary>
    public string StartAt { get; set; } = string.Empty;
    /// <summary>Javob qabul qilish oynasi TUGASHI (ISO "yyyy-MM-ddTHH:mm"). Bo'sh = test kuni 23:59.</summary>
    public string EndAt { get; set; } = string.Empty;

    /// <summary>
    /// TEST KODI (onlayn test) — masalan "K7M4QP". Markazda O'QIMAYDIGAN odam ham botda shu kodni
    /// yuborib testni ishlay oladi (kod → F.I.Sh → test). Kod NOYOB (butun markaz bo'yicha);
    /// oflayn testda bo'sh. Uniklik <c>TestResultService</c> da tekshiriladi.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Test GURUH a'zolariga ham ochiladimi. <c>true</c> (standart) — guruh o'quvchilari botda/ilovada
    /// testni ro'yxatdan ko'radi VA tashqi odam <see cref="Code"/> bilan qo'shila oladi.
    /// <c>false</c> — "FAQAT ONLAYN": guruhga E'LON QILINMAYDI, faqat kod bilan ishlanadi (test guruh
    /// ichida yaratilgani uchun natijalari o'sha guruh ichida ko'rinadi).
    /// </summary>
    public bool GroupOpen { get; set; } = true;

    // ---------- SERTIFIKAT (oflayn ham, onlayn ham) ----------
    /// <summary>
    /// Test natijasi bo'yicha SERTIFIKAT chiqariladimi (test formasidagi ptichka). <c>true</c> bo'lsa
    /// natijalar saqlanganda ball kiritilgan HAR bir o'quvchiga Word shablondan sertifikat yaratiladi.
    /// Standart — <c>false</c> (eski testlar xatti-harakati o'zgarmasin).
    /// </summary>
    public bool CertificateEnabled { get; set; }
    /// <summary>Qaysi Word shablondan chiqariladi (<see cref="TestCertificateTemplate"/> Id).
    /// Bo'sh bo'lsa — standart (<c>IsDefault</c>) shablon ishlatiladi.</summary>
    public string CertificateTemplateId { get; set; } = string.Empty;
}

/// <summary>
/// TEST SERTIFIKATI uchun WORD (.docx) ANDOZASI — "O'quv bo'limi → Testlar natijalari → Sertifikat
/// shablonlari" bo'limida yuklanadi. Bir nechta shablon bo'lishi mumkin (turli kurs/tadbir uchun),
/// test yaratishda qaysi biri ishlatilishi tanlanadi.
///
/// <para>Andoza ichidagi <c>@</c>-o'rinbosarlar (masalan <c>@fish</c>, <c>@ball</c>) sertifikat
/// yaratilganda almashtiriladi — shartnoma andozalari bilan BIR XIL sintaksis
/// (<see cref="IntellectCRM.Application.Services.DocxTemplate"/>).</para>
/// </summary>
public class TestCertificateTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ko'rsatiladigan nom ("Ingliz tili — oraliq test").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Yuklangan .docx fayl manzili ("/uploads/xxx.docx").</summary>
    public string FileUrl { get; set; } = string.Empty;
    /// <summary>Faylning asl nomi (adminga ko'rsatish uchun).</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Standart shablon — test formasida shablon tanlanmasa shu ishlatiladi.
    /// Bir vaqtda FAQAT bitta shablon standart bo'ladi (servis ta'minlaydi).</summary>
    public bool IsDefault { get; set; }
    /// <summary>Ro'yxatda tanlash mumkinmi. <c>false</c> — eski sertifikatlar saqlanadi,
    /// lekin yangi testga tanlanmaydi.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Bitta o'quvchiga bitta test bo'yicha berilgan SERTIFIKAT. Word andozasi to'ldiriladi va
/// LibreOffice orqali PDF ga o'giriladi (ko'rinish o'zgarmaydi — "chop etilgan" holat).
///
/// <para>Kalit — <b>(TestResultId, StudentId)</b> unikal: bir test bo'yicha o'quvchiga bitta
/// sertifikat. Natijalar qayta saqlansa mavjud yozuv YANGILANADI (ball o'zgargan bo'lishi mumkin),
/// yangi qator qo'shilmaydi.</para>
///
/// <para>DIQQAT: bu mavjud <see cref="StudentCertificate"/> (kursni TUGATGANLIK sertifikati, HTML)
/// dan ALOHIDA — u yerda kalit (o'quvchi, kurs, sana) bo'lib, bir kunda ikkita test sertifikati
/// berilsa to'qnashardi va formati ham boshqa.</para>
/// </summary>
public class TestCertificate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TestResultId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    /// <summary>O'quvchi F.I.Sh — SNAPSHOT (keyin ism o'zgarsa sertifikat matni bilan mos qolsin).</summary>
    public string StudentName { get; set; } = string.Empty;
    /// <summary>Qaysi shablondan yaratilgani (o'chirilgan bo'lishi mumkin — shuning uchun FK yo'q).</summary>
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    /// <summary>Ko'rsatiladigan raqam — "SRT-2026-0042".</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>To'ldirilgan Word fayl ("/uploads/certificates/xxx.docx"). HAR DOIM bo'ladi.</summary>
    public string DocxUrl { get; set; } = string.Empty;
    /// <summary>PDF ("/uploads/certificates/xxx.pdf"). LibreOffice mavjud bo'lmasa BO'SH —
    /// bunda <see cref="Status"/> = <c>"docx"</c> bo'ladi va admin buni ro'yxatda ko'radi.</summary>
    public string PdfUrl { get; set; } = string.Empty;
    /// <summary><c>"ready"</c> — PDF tayyor; <c>"docx"</c> — faqat Word (konvertor topilmadi).</summary>
    public string Status { get; set; } = "ready";
    /// <summary>Sertifikat yaratilgan paytdagi ball/maksimal ball va foiz — SNAPSHOT.</summary>
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public int Percent { get; set; }
    public DateTime IssuedAt { get; set; } = AppClock.Now;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// MARKAZDAN TASHQARI ishtirokchining onlayn test natijasi. Markazda o'qimaydigan odam botda test
/// KODINI va F.I.Sh ini yuborib testni ishlaydi — u <see cref="Student"/> emas, shuning uchun bali
/// <see cref="TestScore"/> ga (StudentId FK) yozilmaydi, mana shu jadvalga tushadi.
///
/// <para>Bir chat bir testni BIR MARTA ishlaydi — unikal kalit (TestResultId, ChatId).</para>
/// <para>Test o'chirilsa natijalari ham kaskad o'chadi.</para>
/// </summary>
public class ExternalTestScore
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi test (<see cref="TestResult.Id"/>).</summary>
    public string TestResultId { get; set; } = string.Empty;
    /// <summary>Telegram chat id — kim ishlagani (qayta topshirishning oldini oladi).</summary>
    public long ChatId { get; set; }
    /// <summary>Ishtirokchi o'zi yozgan F.I.Sh.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Telefon raqami (botga ulashgan bo'lsa, <see cref="BotUser.Phone"/> dan) — aks holda bo'sh.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Olgan bali (to'g'ri javoblar soni).</summary>
    public decimal Score { get; set; }
    /// <summary>Yuborgan javoblari ("ABDCA…", javobsiz savol '-').</summary>
    public string Answers { get; set; } = string.Empty;
    /// <summary>Yuborilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string SubmittedAt { get; set; } = string.Empty;
    /// <summary>Manba — hozircha faqat "bot".</summary>
    public string Source { get; set; } = "bot";
}

/// <summary>O'quvchining bitta testdan olgan bali (<see cref="TestResult"/> ↔ o'quvchi).</summary>
public class TestScore
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Test (TestResult.Id).</summary>
    public string TestResultId { get; set; } = string.Empty;
    /// <summary>O'quvchi (Student.Id).</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>O'quvchi olgan ball (0 .. MaxScore).</summary>
    public decimal Score { get; set; }
    /// <summary>ONLAYN test: o'quvchi bot orqali yuborgan javoblar ("ABDCA...", javobsiz savol '-').
    /// Qo'lda kiritilgan ballda bo'sh.</summary>
    public string Answers { get; set; } = string.Empty;
    /// <summary>ONLAYN test: javoblar yuborilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string SubmittedAt { get; set; } = string.Empty;
    /// <summary>Manba: "" (o'qituvchi/admin qo'lda kiritgan) | "bot" (o'quvchi botdan yuborgan).</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Telegram botda ONLAYN testni ishlash sessiyasi (vaqtinchalik holat). Bitta chatda bir vaqtda
/// bitta sessiya bo'ladi: o'quvchi testni ochganda yaratiladi, javoblar yuborilgach yoki bekor
/// qilinganda o'chiriladi. Tugmali rejimda javoblar shu yerda to'planib boradi.
/// </summary>
public class TestBotSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Telegram chat id (kim ishlayapti).</summary>
    public long ChatId { get; set; }
    /// <summary>Qaysi test (TestResult.Id).</summary>
    public string TestResultId { get; set; } = string.Empty;
    /// <summary>Kim uchun (Student.Id) — bir chatda bir nechta farzand bo'lishi mumkin.
    /// <b>Bo'sh</b> = MARKAZDAN TASHQARI ishtirokchi (test kodi bilan kirgan): natijasi
    /// <see cref="ExternalTestScore"/> ga yoziladi, ismi <see cref="ExternalName"/> da.</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Sessiya bosqichi: <c>""</c> — javob kiritilyapti (odatdagi holat) |
    /// <c>"name"</c> — markazdan tashqari ishtirokchidan F.I.Sh kutilyapti (kod tasdiqlangan).</summary>
    public string Stage { get; set; } = string.Empty;
    /// <summary>Markazdan tashqari ishtirokchi yozgan F.I.Sh (<see cref="StudentId"/> bo'sh bo'lganda).</summary>
    public string ExternalName { get; set; } = string.Empty;
    /// <summary>Kiritilgan javoblar; har savol uchun bitta belgi, javobsiz savol '-'. Uzunligi = savollar soni.</summary>
    public string Answers { get; set; } = string.Empty;
    /// <summary>Tugmali rejimda hozir turgan savol (0-based).</summary>
    public int Current { get; set; }
    /// <summary>Tugmali rejimdagi "javob varaqasi" xabari id'si (o'sha xabar joyida yangilanadi).</summary>
    public long MessageId { get; set; }
    /// <summary>Kiritish usuli: "buttons" (tugmalar) | "text" (bitta xabarda).</summary>
    public string InputMode { get; set; } = "buttons";
    public string StartedAt { get; set; } = string.Empty;
}

/// <summary>Lid (markazga qiziqqan).</summary>
public class Lead
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "male";
    public string BirthDate { get; set; } = string.Empty;
    /// <summary>O'quvchining o'z telefon raqami.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>
    /// TELEFON KALITI — <see cref="Phone"/> ning oxirgi 9 raqami (mamlakat kodisiz mahalliy qism,
    /// <c>PhoneUtil.Key</c>). FAQAT qidiruv uchun, ko'rsatilmaydi.
    ///
    /// <para>NEGA: bazada raqamlar turli formatda saqlangan (`+998-90-…`, `998…`, xom kiritilgan),
    /// shuning uchun "shu odamning lidi bormi" savolini SQL tomonda so'rab bo'lmasdi va
    /// <c>LeadIntake.FindByPhoneAsync</c> HAR ARIZADA butun <c>Leads</c> jadvalini xotiraga
    /// o'qirdi (ommaviy forma va daraja testi — anonim endpointlar). Endi indekslangan
    /// (<c>IX_Leads_PhoneKey</c>) ustun bo'yicha bitta so'rov.</para>
    ///
    /// <para>⚠️ QO'LDA TO'LDIRILMAYDI: qiymatni <c>AppDbContext.SaveChanges</c> o'zi
    /// <see cref="Phone"/> dan hisoblab yozadi — lid yaratiladigan yangi joy qo'shilganda
    /// unutilib qolmasin (hozir 4 joyda yaratiladi: lid formasi, daraja testi, CRM formasi,
    /// landing).</para>
    /// </summary>
    public string PhoneKey { get; set; } = string.Empty;
    /// <summary>Otasining F.I.SH.</summary>
    public string FatherFullName { get; set; } = string.Empty;
    /// <summary>Otasining telefon raqami.</summary>
    public string FatherPhone { get; set; } = string.Empty;
    /// <summary>Onasining F.I.SH.</summary>
    public string MotherFullName { get; set; } = string.Empty;
    /// <summary>Onasining telefon raqami.</summary>
    public string MotherPhone { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>Manba: instagram | referral | sayt | telegram | walkin | other ...</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Qiziqqan fani/yo'nalishi (matn yoki Subject id).</summary>
    public string InterestSubject { get; set; } = string.Empty;
    /// <summary>Lid o'qiydigan TASHQI maktab tumani (<see cref="District"/> id). Bo'sh = tanlanmagan.</summary>
    public string DistrictId { get; set; } = string.Empty;
    /// <summary>Lid o'qiydigan TASHQI maktab (<see cref="School"/> id). Bo'sh = tanlanmagan.
    /// O'quvchiga aylantirilganda <see cref="Student.SchoolId"/> ga ko'chiriladi.</summary>
    public string SchoolId { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>O'quvchiga aylantirilgan bo'lsa — yaratilgan Student id'si (null = hali emas).</summary>
    public string? ConvertedStudentId { get; set; }
    /// <summary>Tegishli ustun (LeadStage) id'si.</summary>
    public string Stage { get; set; } = string.Empty;

    // ---- TAKRORIY MUROJAAT ----
    // Odam ommaviy forma yoki daraja testi orqali YANA murojaat qilsa dublikat lid ochilmaydi
    // (`LeadIntake.FindByPhoneAsync`) — natija shu lidning tagiga tushadi. Lekin lid kanbanda
    // qayerda tursa o'sha yerda qolaveradi (first-touch: bosqichi ATAYIN o'zgartirilmaydi), ya'ni
    // "yo'qotilgan" ustunidagi odam qayta murojaat qilganini menejer sezmay qolardi — izoh va
    // Telegram xabaridan boshqa hech qaerda ko'rinmasdi. Shu ikki maydon kanban kartasida
    // «Takroriy N» belgisini chiqaradi.

    /// <summary>Takroriy murojaatlar soni (birinchi murojaat sanalmaydi; 0 = takror yo'q).</summary>
    public int RepeatCount { get; set; }
    /// <summary>Oxirgi takroriy murojaat vaqti (ISO "yyyy-MM-ddTHH:mm:ss"); bo'sh = takror yo'q.</summary>
    public string LastRepeatAt { get; set; } = string.Empty;
}

/// <summary>
/// Lid haqida Telegramga YUBORILGAN xabar (karta) — uni keyin TAHRIRLASH uchun bog'lovchi yozuv.
///
/// <para>NEGA ALOHIDA JADVAL, <c>Lead</c> ga ikkita ustun EMAS: bitta lid kartasi BIR NECHTA chatga
/// ketadi (har superadminning shaxsiy chati + har bir faol guruh — <c>LeadNotifier</c>). Lidda
/// <c>ChatId</c>/<c>MessageId</c> juftligi bo'lsa faqat BITTA chatdagi xabar eslab qolinardi,
/// qolganlari esa yangilanmay, eski (bosqichi o'zgarmagan) holicha osilib qolardi.</para>
///
/// <para>Har lid × chat uchun bitta qator: unikal indeks <c>(LeadId, ChatId)</c> — aks holda bir
/// guruhda ikkita karta paydo bo'lib, ikkalasi ham yangilanaverardi.</para>
/// </summary>
public class LeadTelegramMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi lidning kartasi (<see cref="Lead"/>.Id).</summary>
    public string LeadId { get; set; } = string.Empty;
    /// <summary>Telegram chat (superadminning shaxsiy chati yoki guruh).</summary>
    public long ChatId { get; set; }
    /// <summary>Yuborilgan xabar id'si — TAHRIRLASH (<c>editMessageText</c>) aynan shunga tayanadi.</summary>
    public long MessageId { get; set; }
    /// <summary>
    /// Oxirgi yuborilgan matnning SHA256'si.
    ///
    /// <para>NEGA KERAK: Telegram AYNAN o'sha matn bilan tahrirlashga <c>message is not modified</c>
    /// xatosini qaytaradi. Ya'ni matn o'zgarmaganini oldindan bilmasak, har bir arzimas
    /// o'zgarishda har bir chatga bekorga so'rov ketardi (tezlik chegarasi ham shunga sarflanardi).
    /// Hash tenglashsa — so'rov umuman yuborilmaydi.</para>
    /// </summary>
    public string TextHash { get; set; } = string.Empty;
    /// <summary>
    /// Xabar o'chirilgan / topilmadi (bot guruhdan chiqarilgan, xabar qo'lda o'chirilgan) —
    /// boshqa urinilmaydi.
    ///
    /// <para>NEGA KERAK: yo'q xabarni tahrirlash har safar xato qaytaradi, lekin biz buni
    /// eslab qolmasak, lidning HAR o'zgarishida (bosqich, izoh, birinchi dars, konversiya)
    /// yana urinilardi — bu Telegram tezlik chegarasini yeb, haqiqiy xabarlarni kechiktirardi.</para>
    /// </summary>
    public bool IsDead { get; set; }
    /// <summary>Xabar birinchi yuborilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Oxirgi muvaffaqiyatli tahrir vaqti (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>Lid bosqichi (kanban ustuni).</summary>
public class LeadStage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    /// <summary>slate | blue | emerald | amber | violet | rose | cyan | orange</summary>
    public string Color { get; set; } = "slate";
    /// <summary>Ustunlar tartibi.</summary>
    public int Order { get; set; }
}

/// <summary>Lid hodisasi (tarix) — kim, qachon, nima qildi.</summary>
public class LeadEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LeadId { get; set; } = string.Empty;
    /// <summary>Turi: note | stage | call | trial | convert | created.</summary>
    public string Type { get; set; } = "note";
    /// <summary>Izoh / tafsilot.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Bajargan foydalanuvchi ismi.</summary>
    public string ActorName { get; set; } = string.Empty;
    /// <summary>Vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;

    // ---- Voronka analitikasi uchun O'QILADIGAN maydonlar (Text faqat odam uchun) ----
    // DIQQAT: bu uchta maydon 2026-08 dagi o'zgarishda qo'shildi — UNGACHA yozilgan hodisalarda
    // ular BO'SH. Ya'ni ular ustiga qurilgan hisob (bosqichda o'tirish vaqti, menejerlar kesimi)
    // faqat SHU SANADAN keyingi tarixni qamraydi. Shuning uchun analitika javobida `Samples`
    // qaytariladi — raqam nechta haqiqiy o'lchovga asoslanganini ko'rsatish uchun.

    /// <summary>Qaysi bosqichdan (<see cref="LeadStage"/>.Id). Bo'sh — lid YARATILGANDA (oldingi bosqich yo'q).</summary>
    public string FromStage { get; set; } = string.Empty;
    /// <summary>Qaysi bosqichga (<see cref="LeadStage"/>.Id). Faqat Type=="stage"/"created" da to'ldiriladi.</summary>
    public string ToStage { get; set; } = string.Empty;
    /// <summary>Kim bajargan — <see cref="AppUser"/>.Id (menejerlar kesimi uchun). Nomi <see cref="ActorName"/> da.
    /// Bo'sh/null — tizim yozgan (sayt formasi, daraja testi) yoki eski yozuv.</summary>
    public string? ActorUserId { get; set; }
}

/// <summary>Lid uchun sinov darsi — guruh + sana; natija lid statusini yangilaydi.</summary>
public class TrialLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LeadId { get; set; } = string.Empty;
    /// <summary>Tayinlangan guruh (Group id'si).</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Sinov darsi vaqti (ISO "yyyy-MM-ddTHH:mm").</summary>
    public string ScheduledAt { get; set; } = string.Empty;
    /// <summary>Natija: pending (kutilmoqda) | stayed (qoldi) | left (ketdi).</summary>
    public string Result { get; set; } = "pending";
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Jurnal katagi — baho yoki davomat sababi.</summary>
public class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    /// <summary>Dars raqami (1-10) — bir kunda bir fan bir necha marta bo'lsa farqlash uchun.</summary>
    public int Period { get; set; }
    public int? Grade { get; set; }
    public string? ReasonId { get; set; }
    /// <summary>ANIQ "keldi (bor)" belgisi — o'qituvchi katakda "Keldi" tugmasini bossa yoki "hammasi keldi"
    /// ommaviy davomatida. "Dars o'tildi + yozuv yo'q = keldi" konventsiyasidan farqli, bu RecordedAt
    /// (PresentDefaultFrom) cheklovidan qat'i nazar yashil ✓ ko'rsatiladi (orqaga sanalgan a'zolikda ham).</summary>
    public bool Present { get; set; }
    /// <summary>Uyga vazifa bajarilishi: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi, 3 = chala qildi.</summary>
    public int Homework { get; set; }
    /// <summary>Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon.</summary>
    public int Behavior { get; set; }
    /// <summary>Shu darsni o'zlashtirish darajasi (MasteryLevel enum). null = belgilanmagan.
    /// EF Core database savni int sifatida saqlaydi va enum qiymatiga o'zgartiradi.</summary>
    public MasteryLevel? Mastery { get; set; }
}

/// <summary>Dars mavzusi va uyga vazifa (sana bo'yicha).</summary>
public class LessonNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public int Quarter { get; set; }
    public string Date { get; set; } = string.Empty;
    /// <summary>Dars raqami (1-10) — bir kunda bir fan bir necha marta bo'lsa farqlash uchun.</summary>
    public int Period { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Homework { get; set; }
    /// <summary>Dars o'tildimi (ptichka). false = dars o'tilmadi.</summary>
    public bool Conducted { get; set; }
    /// <summary>RASSMIY davomat olindimi (guruh sahifasida "hammasi keldi"/"hammasi kelmadi" tugmasi
    /// orqali) — bitta o'quvchiga baho/eslatma kiritilganda avtomatik true bo'lmaydi (faqat shu
    /// o'quvchiga tegishli, boshqalarga emas). O'quvchi shaxsiy sahifasidagi jurnalda: bu false bo'lsa,
    /// alohida ma'lumoti (baho/sabab) yo'q o'quvchi standart bo'yicha "keldi" deb HISOBLANMAYDI.</summary>
    public bool AttendanceTaken { get; set; }
}

/// <summary>
/// Bitta darsni BIR MARTALIK boshqa kunga ko'chirish. Guruh darslari <see cref="Group.Days"/> (hafta kunlari)
/// bo'yicha avtomatik quriladi; bu yozuv shu qoidadan bitta chetlanish: <see cref="FromDate"/>dagi dars
/// <see cref="ToDate"/>ga ko'chadi (yangi kun guruh kuni bo'lmasa ham ustun sifatida paydo bo'ladi).
/// Jurnal ustunlari (<see cref="JournalService.EffectiveLessonDatesInMonth"/>) va maosh rejasi
/// (<see cref="SalaryJournalStats"/>) shu ko'chirishni hisobga oladi — ko'chirilgan dars o'tkazib
/// yuborilgan (missed) hisoblanmaydi.
/// </summary>
public class LessonReschedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassId { get; set; } = string.Empty;
    /// <summary>Asl dars sanasi ("yyyy-MM-dd") — bu kunda dars endi bo'lmaydi (ustun olib tashlanadi).</summary>
    public string FromDate { get; set; } = string.Empty;
    /// <summary>Yangi dars sanasi ("yyyy-MM-dd") — dars shu kunga ko'chadi (ustun paydo bo'ladi).</summary>
    public string ToDate { get; set; } = string.Empty;
    /// <summary>Yangi dars boshlanish vaqti ("HH:mm", ixtiyoriy) — faqat ma'lumot uchun ko'rsatiladi.</summary>
    public string? Time { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Ko'chirishni amalga oshirgan admin/o'qituvchi F.I.Sh.</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>Davomat sababi (kelmaganlik turi).</summary>
public class AbsenceReason
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Short { get; set; } = string.Empty;
    /// <summary>
    /// "Kech keldi" turi — o'quvchi DARSDA QATNASHGAN, faqat kech kelgan. Bunday belgi yo'qlik
    /// (absence) sifatida hisoblanmaydi (davomat foiziga ta'sir qilmaydi) va unga BAHO ham qo'yса bo'ladi.
    /// </summary>
    public bool IsLate { get; set; }
}

/// <summary>Baholash MEZONI (kriteriya) — qayta ishlatiladigan pul. Guruhlarga biriktiriladi
/// (har guruhga boshqa-boshqa mezonlar). O'quvchilar guruh ichida shu mezonlar bo'yicha baholanadi.</summary>
public class GradingCriterion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Baho shkalasi yuqori chegarasi (masalan 5 yoki 100).</summary>
    public int MaxScore { get; set; } = 5;
    public int Order { get; set; }
    /// <summary>Mezon egasi (Teacher.Id). Bo'sh (null) — eski/umumiy mezon. Bo'lsa — mezon FAQAT shu
    /// o'qituvchiga tegishli: uning guruhlariga biriktiriladi va ro'yxatda shu o'qituvchi ostida ko'rinadi.</summary>
    public string? TeacherId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Mezonni GURUHGA biriktirish (M2M): qaysi guruhda qaysi mezonlar bo'yicha baholanadi.</summary>
public class GroupGradingCriterion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GroupId { get; set; } = string.Empty;
    public string CriterionId { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>O'quvchining bir mezon bo'yicha HAR DARSGA belgisi (bajardi/bajarmadi) — guruh ichida.
/// Har (Group, Student, Criterion, Date) uchun yagona. Done=true bo'lsa "bajardi".</summary>
public class CriterionGrade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GroupId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string CriterionId { get; set; } = string.Empty;
    /// <summary>Dars sanasi ("yyyy-MM-dd").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Bajardi (true) yoki yo'q (false).</summary>
    public bool Done { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// BALLNI QO'LDA TUZATISH — admin/superadmin kiritgan qo'shimcha (+) yoki ayirma (−).
///
/// <para>Ball loyihada <b>PER-GURUH</b> hisoblanadi (<c>StudentBallService</c>), shuning uchun
/// tuzatish ham har doim BITTA guruhga tegishli: <see cref="GroupId"/> majburiy. "Umumiy"
/// tuzatish bo'lganda "jami = guruhlar ballari yig'indisi" invarianti buzilardi va markaz
/// o'rtachasi (jami ÷ guruhlar soni) ma'nosini yo'qotardi.</para>
///
/// <para>Guruhdagi amaldagi ball = <c>Math.Max(0, hisoblangan + Σ Delta)</c> — MANFIYGA
/// TUSHMAYDI. Sabab: "0 ga tushirish" amali <c>Delta = −(joriy ball)</c> sifatida yoziladi;
/// keyinchalik o'qituvchi eski bahoni o'chirsa yoki kamaytirsa yig'indi manfiy chiqib,
/// o'quvchi reytingda hammadan pastga tushib ketardi.</para>
///
/// <para>Yozuvlar HECH QACHON o'chirilmaydi/tahrirlanmaydi — ular ball tarixining bir qismi
/// (<c>GET /api/admin/students/{id}/ball-history</c>). Bekor qilish = teskari ishorali yangi yozuv.</para>
/// </summary>
public class StudentBallAdjustment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Qaysi guruh bali tuzatildi (Group.Id) — MAJBURIY.</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Qo'shilgan (+) yoki ayirilgan (−) ball.</summary>
    public int Delta { get; set; }
    /// <summary>NEGA — majburiy izoh (bo'sh bo'lsa server 400 qaytaradi).</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>ISO vaqt ("yyyy-MM-ddTHH:mm:ss") — tarix shu bo'yicha tartiblanadi.</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Kim qildi — F.I.Sh. (SNAPSHOT: xodim o'chirilsa ham tarixda qoladi).</summary>
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
}

/// <summary>O'quvchiga oy uchun hisoblangan oylik to'lov (qarz yozuvi/tarix).</summary>
public class MonthlyCharge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>QAYSI GURUH uchun hisoblangan (Group id). Per-guruh billing: har faol a'zolik uchun alohida
    /// hisob qatori. null = guruhsiz o'quvchi (eski ClassName narxi bo'yicha — orqaga moslik).</summary>
    public string? GroupId { get; set; }
    /// <summary>Oy ("YYYY-MM").</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>Hisoblangan TO'LIQ summa (o'sha paytdagi guruh oylik to'lovi). Chegirma ALOHIDA.</summary>
    public decimal Amount { get; set; }
    /// <summary>Shu oy uchun berilgan chegirma summasi (so'm). Haqiqiy to'lash kerak bo'lgan summa = Amount - Discount.</summary>
    public decimal Discount { get; set; }
    /// <summary>Hisoblangan sana (ISO "YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Super admin qo'lda tahrirlagan — avtomatik qayta hisob (Update/kurs-narx) bu yozuvni O'ZGARTIRMAYDI.</summary>
    public bool Locked { get; set; }
}

/// <summary>Moliyaviy amal — kirim yoki chiqim.</summary>
public class FinanceTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Sana (ISO "YYYY-MM-DD").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>income (kirim) | expense (chiqim)</summary>
    public string Direction { get; set; } = "income";
    /// <summary>Toifa: tuition, salary, utilities, supplies, rent, donation, other ...</summary>
    public string Category { get; set; } = "other";
    /// <summary>Summa (har doim musbat; yo'nalish belgini aniqlaydi).</summary>
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    /// <summary>Kassir qo'lda yozgan izoh (ixtiyoriy) — to'lov haqida qo'shimcha ma'lumot.</summary>
    public string? Comment { get; set; }
    /// <summary>O'quvchi to'lovi bo'lsa — tegishli o'quvchi id'si.</summary>
    public string? StudentId { get; set; }
    /// <summary>O'quvchi tuition to'lovi bo'lsa — QAYSI GURUH uchun to'langani (Group id). O'quvchi bir nechta
    /// guruhda o'qisa, to'lov kiritishda guruh tanlanadi; o'qituvchining foizli maoshi shu tegga tayanadi.
    /// null = teglanmagan (eski to'lov yoki bitta guruh — foiz hisobida narx nisbatida taqsimlanadi).</summary>
    public string? GroupId { get; set; }
    /// <summary>O'qituvchi maoshi bo'lsa — tegishli o'qituvchi id'si.</summary>
    public string? TeacherId { get; set; }
    /// <summary>Oylik to'lov bo'lsa — qaysi oy uchun ("YYYY-MM"). Boshqa amallar uchun null.</summary>
    public string? Month { get; set; }
    /// <summary>To'lov usuli (kirim/to'lov uchun): "cash" (Naqd) | "card" (Karta) | "bank" (Bank orqali).
    /// null = belgilanmagan (eski yozuvlar yoki chiqim).</summary>
    public string? Method { get; set; }
    /// <summary>QOG'OZ KVITANSIYA raqami — NAQD to'lovda kassir kiritadi. Seriya "KV" + raqam, to'liq
    /// ko'rinishda saqlanadi (masalan "KV000123"). Moliya → To'lovlar ro'yxatida ko'rinadi va qidiriladi.
    /// null = kiritilmagan (karta/bank yoki eski yozuv). Chek (kvitansiya) `ReceiptNo`si — Id'dan
    /// hosil qilinadigan ICHKI raqam, bu esa QO'LDA kiritilgan qog'oz raqami (ikkisi boshqa-boshqa).</summary>
    public string? ReceiptNo { get; set; }
    /// <summary>To'lov HAQIQATAN qilingan VAQT ("HH:mm") — KARTA orqali to'lovda kiritiladi (bank
    /// ilovasidagi vaqt bilan solishtirish uchun). null = kiritilmagan. Sana `Date` maydonida.</summary>
    public string? PaidTime { get; set; }
    /// <summary>KARTA raqamining OXIRGI 4 RAQAMI (masalan "1234") — karta orqali to'lovda kassir
    /// kiritadi, bank ko'chirmasi bilan solishtirish uchun. Moliya → To'lovlar jadvalida naqd
    /// to'lovning kvitansiya raqami o'rniga shu ko'rinadi ("•••• 1234").
    /// XAVFSIZLIK: FAQAT oxirgi 4 raqam saqlanadi — kassir to'liq raqam kiritsa ham
    /// <c>PaymentFields.NormalizeCardLast4</c> qolganini tashlab yuboradi. null = kiritilmagan.</summary>
    public string? CardLast4 { get; set; }
    /// <summary>Tranzaksiya yaratilgan vaqti (UTC) — idempotency check uchun (5s ichida dublikat).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Mas'ul — to'lovni kiritgan admin/kassir F.I.Sh (chekda "Mas'ul" qatori uchun).</summary>
    public string? CreatedBy { get; set; }
    /// <summary>Mas'ulning AKKAUNT id'si (<see cref="AppUser.Id"/>) — kassir hisoboti aynan kim
    /// kiritganini ism bo'yicha emas, id bo'yicha ajratishi uchun (bir xil ismli xodimlar,
    /// keyinchalik ism o'zgarishi). ESKI yozuvlarda null — hisobotda <see cref="CreatedBy"/>
    /// (ism) bo'yicha guruhlanadi.</summary>
    public string? CreatedById { get; set; }
    /// <summary>Bu yozuv VOZVRAT (pul qaytarish) bo'lsa — qaysi ASL to'lov (income+tuition) uchun qaytarilgani.
    /// Vozvrat: Direction="expense", Category="refund", StudentId/GroupId/Month asl to'lovdan ko'chiriladi.
    /// O'qituvchining foizli maoshi va "yig'ilgan" hisobotlari vozvratni AYIRADI (net = to'langan − vozvrat).
    /// null = oddiy tranzaksiya (vozvrat emas).</summary>
    public string? RefundOfId { get; set; }
}

/// <summary>O'qituvchining bir kunlik ish davomati.</summary>
public class TeacherAttendance
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Sana "yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Holat: "present" (keldi) | "absent" (kelmadi) | "late" (kechikdi).</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Izoh / sabab (ixtiyoriy).</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Kelgan vaqti "HH:mm" (turniketdan birinchi KIRISH). Bo'sh = noma'lum.</summary>
    public string CheckIn { get; set; } = string.Empty;
    /// <summary>Ketgan vaqti "HH:mm" (turniketdan oxirgi CHIQISH). Bo'sh = noma'lum.</summary>
    public string CheckOut { get; set; } = string.Empty;
    /// <summary>Manba: "manual" (admin qo'lda) | "turnstile" (qurilmadan avtomatik). Sinxronlash
    /// "manual" yozuvlarni o'zgartirmaydi (admin qo'lda tuzatgan bo'lsa saqlanadi).</summary>
    public string Source { get; set; } = "manual";
}

/// <summary>Markaz kamerasi (IP/RTSP). Media-shlyuz (MediaMTX) orqali brauzerda jonli + playback.</summary>
public class Camera
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Joylashuvi ("1-qavat koridor", "Hovli" ...).</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Asosiy RTSP oqimi (login/parol bilan): rtsp://user:pass@ip:554/...</summary>
    public string RtspUrl { get; set; } = string.Empty;
    /// <summary>Past sifatli (sub) RTSP — grid (ko'p kamera) uchun. Bo'sh bo'lsa asosiy ishlatiladi.</summary>
    public string RtspSubUrl { get; set; } = string.Empty;
    /// <summary>Yozuv necha KUN saqlansin — undan eski yozuvlar shlyuz tomonidan avtomatik o'chiriladi.
    /// 0 = cheksiz (o'chirilmaydi).</summary>
    public int RetentionDays { get; set; } = 7;
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Turniket/FaceID qurilmasidan kelgan bitta o'tish hodisasi (xom log).</summary>
public class TurnstileEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Bog'langan o'qituvchi (DeviceUserId orqali topilgan). Topilmasa bo'sh.</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Qurilmadagi xodim ID'si.</summary>
    public string DeviceUserId { get; set; } = string.Empty;
    /// <summary>Hodisa vaqti (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string EventAt { get; set; } = string.Empty;
    /// <summary>Yo'nalish: "in" (kirish) | "out" (chiqish).</summary>
    public string Direction { get; set; } = "in";
    /// <summary>Qurilma nomi/manzili (qaysi eshik).</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Tizimga yozilgan vaqt (ISO).</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Markaz umumiy holati va ma'lumotlari (bitta qator) — joriy o'quv yili + markaz profili.</summary>
public class CenterMeta
{
    // Bitta markaz — bitta CenterMeta qatori. Id unikal (Guid).
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Joriy o'quv yili, masalan "2025/2026".</summary>
    public string CurrentYear { get; set; } = string.Empty;
    /// <summary>Ko'p-guruh to'lov rejimi: aggregate (barcha faol guruhlar yig'indisi — bitta oylik hisob) |
    /// perGroup (kelajakda — har guruh uchun alohida). Default: aggregate.</summary>
    public string BillingMode { get; set; } = "aggregate";
    /// <summary>Landing sahifasidagi xarita iframe havolasi / URL.</summary>
    public string MapIframeUrl { get; set; } = string.Empty;
    public string TelegramUrl { get; set; } = "https://t.me/intellect_kokand";
    public string InstagramUrl { get; set; } = "https://instagram.com/intellect_kokand";
    public string YoutubeUrl { get; set; } = "https://youtube.com";
    public string FacebookUrl { get; set; } = "https://facebook.com";
    public string CenterEmail { get; set; } = "info@intellect.uz";
    public string AppStoreUrl { get; set; } = string.Empty;
    public string PlayMarketUrl { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = "+998 (90) 344-44-34";
    public string CenterAddress { get; set; } = "Farg'ona viloyati, Qo'qon shahar, Asqarali charxiy 5A";
    public string WorkingHours { get; set; } = "Dushanba — Shanba: 09:00 – 17:00";

    /* ---------- Jurnal boshqaruvi (tahrirlash siyosati) — admin "Guruhlar → Jurnal boshqaruvi" ---------- */

    /// <summary>Jurnalga kiritish oynasi: "free" — istalgan o'tgan sanaga (default) |
    /// "today" — faqat bugungi kun | "window" — faqat oxirgi <see cref="JournalRetroDays"/> kun.
    /// Kelajak sanalar HAR DOIM taqiqlangan (bu sozlamaga bog'liq emas).</summary>
    public string JournalEditMode { get; set; } = "free";
    /// <summary>"window" rejimida orqaga necha kungacha kiritish mumkin (1-90).</summary>
    public int JournalRetroDays { get; set; } = 3;
    /// <summary>true — baho/davomat faqat "o'tildi" (Conducted) deb belgilangan darsga qo'yiladi
    /// (avval davomat qilinadi, keyin baho). Ommaviy davomat bunga kirmaydi — u darsni o'zi "o'tildi" qiladi.</summary>
    public bool JournalConductedOnly { get; set; }
    /// <summary>true — yuqoridagi cheklovlar ADMIN jurnaliga ham qo'llanadi (default: faqat o'qituvchiga).</summary>
    public bool JournalApplyToAdmins { get; set; }

    /* ---------- To'lov "darvozasi": to'lamagan o'quvchi O'QITUVCHI jurnalida ko'rinmasin ----------
       DIQQAT: bu MUZLATISH EMAS — a'zolik, hisob-kitob, qarz o'sishi hammasi odatdagidek davom etadi.
       Faqat o'qituvchi ilovasidagi jurnal qatori yashiriladi (admin hammani ko'raveradi), to'lov
       kelishi bilan qator o'z-o'zidan qaytadi (hech qanday qo'lda amal kerak emas). */

    /// <summary>true — O'TGAN oy(lar)dan qarzi bor o'quvchi o'qituvchi jurnalida ko'rinmaydi.</summary>
    public bool JournalHideUnpaidPrevMonth { get; set; }
    /// <summary>true — JORIY oyda qarzi bor o'quvchi <see cref="JournalUnpaidCutoffDay"/> kunidan
    /// boshlab (shu kun ham kiradi) o'qituvchi jurnalida ko'rinmaydi.</summary>
    public bool JournalHideUnpaidAfterDay { get; set; }
    /// <summary>Joriy oy qarzi uchun "muddat" kuni (1-28; 28 — har oyda mavjudligi kafolatlangan eng katta kun).</summary>
    public int JournalUnpaidCutoffDay { get; set; } = 10;

    /// <summary>true — o'qituvchi maoshi (qat'iy ham, foizli ham) SHU OYDA jurnalda "o'tildi" deb
    /// belgilangan darslar nisbatiga ko'paytiriladi: belgilanmagan dars = o'tilmagan dars, maoshdan ushlanadi.</summary>
    public bool SalaryRequireJournal { get; set; }
    /// <summary>Jurnalni to'ldirishga beriladigan muhlat (kun). Dars sanasi shu kundan yosh bo'lsa hali
    /// "o'tkazib yuborilgan" hisoblanmaydi (o'qituvchi keyinroq belgilashi mumkin). 0-30.</summary>
    public int SalaryGraceDays { get; set; }

    /* ---------- O'quvchini ushlab turish bonusi (retention) ---------- */

    /// <summary>Bonus uchun necha oy uzluksiz o'qish kerak (default 6). 1-36.</summary>
    public int RetentionMonthsRequired { get; set; } = 6;
    /// <summary>Ketma-ket necha oy a'zoliksiz/muzlatilgan turish KECHIRILADI (default 2). Bundan
    /// oshsa sikl uziladi. 0 = har qanday uzilish siklni buzadi. 0-12.</summary>
    public int RetentionMaxGapMonths { get; set; } = 2;
    /// <summary>Bonus berish modalida oldindan to'ldiriladigan standart summa (so'm). Admin
    /// har safar o'zgartira oladi — bu faqat qulaylik uchun.</summary>
    public decimal RetentionDefaultAmount { get; set; }

    /// <summary>Markaz nomi.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Markaz logotipi (`/uploads/...`) — barcha foydalanuvchi ko'radigan joylarda (login,
    /// daraja testi, portal sarlavhalari) nom yonida ko'rsatiladi. Bo'sh bo'lsa standart ikona.</summary>
    public string LogoUrl { get; set; } = string.Empty;
    /// <summary>Direktor F.I.SH.</summary>
    public string Director { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    // DIQQAT: Telegram bot TOKENI bu yerda YO'Q — barcha maxfiy kalitlar faqat .env dan o'qiladi
    // (AppSecrets). Bazada saqlanmaydi: dump/backup va SQL orqali sizib chiqmasin.
    /// <summary>Telegram bot foydalanuvchi nomi (@siz) — t.me havolasi va ro'yxat taklifi uchun.</summary>
    public string TelegramBotUsername { get; set; } = string.Empty;
    /// <summary>Telegram bot ko'rsatiladigan nomi (masalan "IntellectCRM Bot") — UI/ilovada ko'rsatish uchun.</summary>
    public string TelegramBotName { get; set; } = string.Empty;
    /// <summary>Markaz Telegram kanali (havola yoki @username) — o'quvchi/o'qituvchi ilovasida "kanalga o'tish".</summary>
    public string TelegramChannel { get; set; } = string.Empty;
    /// <summary>Bot orqali telefon ulashilganda o'quvchini QAYSI raqami bo'yicha qidirish: "parent" (default —
    /// Student.ParentPhone) yoki "student" (Student.Phone, o'quvchining o'zi raqami).</summary>
    public string TelegramPhoneMatchField { get; set; } = "parent";
    /// <summary>O'quvchi ilovasi APK fayli — Telegram bot ro'yxatdan o'tgan o'quvchiga yuboradi.
    /// Name = ko'rsatiladigan nom; Path = serverdagi nisbiy yo'l (uploads/...); FileId = Telegram
    /// keshlangan file_id (bir marta yuklangach qayta yuklamasdan yuboriladi, yangi APK yuklanganda bo'shatiladi).</summary>
    public string StudentApkName { get; set; } = string.Empty;
    public string StudentApkPath { get; set; } = string.Empty;
    public string StudentApkFileId { get; set; } = string.Empty;
    /// <summary>O'qituvchi ilovasi APK fayli (yuqoridagi kabi). Bo'sh bo'lsa o'quvchi APK'siga qaytadi.</summary>
    public string TeacherApkName { get; set; } = string.Empty;
    public string TeacherApkPath { get; set; } = string.Empty;
    public string TeacherApkFileId { get; set; } = string.Empty;
    // Firebase SERVICE ACCOUNT JSON (maxfiy, ichida private_key) — .env: FCM_SERVICE_ACCOUNT_JSON
    // (AppSecrets.FcmServiceAccountJson). Quyidagi ikkitasi esa OMMAVIY (brauzerga beriladi) —
    // shuning uchun bazada qoladi va UI'dan kiritiladi.
    /// <summary>
    /// Firebase WEB app konfiguratsiyasi (JSON: apiKey, authDomain, projectId, messagingSenderId,
    /// appId). Web (PWA) push uchun — brauzer FCM token olishi uchun zarur. Firebase Console →
    /// Project Settings → General → Your apps → Web app config. Ommaviy (maxfiy emas).
    /// </summary>
    public string FcmWebConfigJson { get; set; } = string.Empty;
    /// <summary>
    /// Web Push (VAPID) ochiq kaliti — Firebase Console → Cloud Messaging → Web configuration →
    /// "Web Push certificates" (Key pair). Web (PWA) push uchun zarur.
    /// </summary>
    public string FcmVapidKey { get; set; } = string.Empty;

    // Azure Speech kaliti/hududi (.env: AZURE_SPEECH_KEY / AZURE_SPEECH_REGION) va Gemini API
    // kaliti (.env: GEMINI_API_KEY) — AppSecrets orqali; bazada saqlanmaydi. Gemini modeli ham
    // env'dan (GEMINI_MODEL, default gemini-3.1-flash-lite).

    /// <summary>AI tekshiruv (Speaking/Writing) — o'quvchi uchun standart KUNLIK limit (necha marta).
    /// Per-o'quvchi <see cref="StudentAiAccess"/> override qiladi (premium = cheksiz). Default 3.</summary>
    public int AiCheckDailyLimit { get; set; } = 3;

    /// <summary>O'quvchi ilovasidagi «AI tekshiruv» bo'limi ochilganmi. Default FALSE — ataylab:
    /// markaz o'zi ochadi (admin: Ilova → AI check → «Ilovada ochish»). O'chiq bo'lsa o'quvchi
    /// yangi tekshiruv YUBORA OLMAYDI (eski natijalarni o'qish qoladi).
    /// DIQQAT: bu MAXFIY EMAS — `.env` emas, oddiy sozlama (BookSalesEnabled kabi).</summary>
    public bool AiCheckEnabled { get; set; }

    /// <summary>O'qituvchi maoshi hisoblashda toifa bo'yicha BIR SOAT dars narxi (so'm).
    /// Oylik maosh = haftalik darslar soni × 4 × shu narx. Admin "Dars jadvali → Oylik hisoblash"da kiritadi.</summary>
    public decimal SalaryRateOliy { get; set; }
    public decimal SalaryRate1 { get; set; }
    public decimal SalaryRate2 { get; set; }
    public decimal SalaryRateMutaxasis { get; set; }

    // ---------- Turniket / FaceID integratsiyasi (o'qituvchilar davomati avtomatik) ----------
    /// <summary>Integratsiya yoqilganmi.</summary>
    public bool TurnstileEnabled { get; set; }
    /// <summary>Qurilma turi/vendori: "hikvision" | "zkteco".</summary>
    public string TurnstileVendor { get; set; } = "hikvision";
    /// <summary>Qurilma manzili (IP yoki host), masalan "192.168.1.64".</summary>
    public string TurnstileHost { get; set; } = string.Empty;
    /// <summary>Qurilma porti (Hikvision ISAPI odatda 80).</summary>
    public int TurnstilePort { get; set; } = 80;
    // Qurilma login/paroli — .env: TURNSTILE_USERNAME / TURNSTILE_PASSWORD (AppSecrets).
    /// <summary>Ish boshlanish vaqti "HH:mm" — kechikishni aniqlash uchun (dars jadvalidagi birinchi
    /// dars bilan birga, qaysi biri erta bo'lsa). Bo'sh bo'lsa faqat dars jadvali ishlatiladi.</summary>
    public string WorkStartTime { get; set; } = "08:30";
    /// <summary>Kechikishga yo'l qo'yiladigan daqiqalar (grace). Kelgan vaqt kutilgan + grace dan
    /// keyin bo'lsa — "kechikdi".</summary>
    public int LateGraceMinutes { get; set; } = 10;
    /// <summary>Oxirgi muvaffaqiyatli sinxronlash vaqti (ISO).</summary>
    public string TurnstileLastSync { get; set; } = string.Empty;

    // ---------- Kamera (videokuzatuv) integratsiyasi ----------
    /// <summary>Kamera kuzatuvi yoqilganmi.</summary>
    public bool CameraEnabled { get; set; }

    // ---------- Telegram backup ----------
    /// <summary>Telegram admin chat ID — backup faylini yuborish uchun. Faqat raqam (masalan 123456789).
    /// Bo'sh bo'lsa Telegram backup o'chiriladi.</summary>
    public string? TelegramAdminChatId { get; set; }
    /// <summary>Backup yuborish soati (UTC, 0-23). Default 21 (21:00 UTC = 02:00 Toshkent).</summary>
    public int BackupScheduleHour { get; set; } = 21;
    /// <summary>Backup yuborish daqiqasi (0-59). Default 0.</summary>
    public int BackupScheduleMinute { get; set; }
    /// <summary>Telegram backup yoqilganmi (default true).</summary>
    public bool TelegramBackupEnabled { get; set; } = true;
    /// <summary>Oxirgi muvaffaqiyatli Telegram backup yuborish vaqti (tracking uchun).</summary>
    public DateTime? TelegramBackupLastSentAt { get; set; }

    // ---------- Kunlik markaz AI tahlili ----------
    /// <summary>Kunlik avtomatik markaz AI tahlili yoqilganmi (default true). Yoqilgan va Gemini kaliti
    /// sozlangan bo'lsa, fon xizmati har kuni <see cref="AiDailyAnalysisHour"/> (Toshkent) da markazning
    /// bir kun oldingi/joriy oy ma'lumotlari asosida AI tahlil yaratadi (tushum prognozi, baholar
    /// dinamikasi, lidlar, ketganlar sabablari, tavsiyalar). Bosh sahifada "AI Tahlil" bo'limida.</summary>
    public bool AiDailyAnalysisEnabled { get; set; } = true;
    /// <summary>Kunlik AI tahlil soati (0-23, Toshkent). Default 8 (ertalab).</summary>
    public int AiDailyAnalysisHour { get; set; } = 8;

    // ---------- Xodimga topshiriq (checklist) kunlik jo'natish ----------
    /// <summary>"Adminga topshiriq" kunlik jo'natilishi yoqilganmi (default true). Yoqilgan bo'lsa fon
    /// xizmati har kuni <see cref="StaffTaskHour"/>:<see cref="StaffTaskMinute"/> (Toshkent) da har bir
    /// topshiriqli xodimga Telegram bot orqali shu kungi checklistni yuboradi.</summary>
    public bool StaffTaskEnabled { get; set; } = true;
    /// <summary>Xodim checklisti ertalab jo'natiladigan soat (0-23, Toshkent). Default 9.</summary>
    public int StaffTaskHour { get; set; } = 9;
    /// <summary>Xodim checklisti jo'natiladigan daqiqa (0-59). Default 0.</summary>
    public int StaffTaskMinute { get; set; }

    // ---------- Eskiz.uz SMS shlyuzi ----------
    // Kabinet login/paroli — .env: ESKIZ_EMAIL / ESKIZ_PASSWORD (AppSecrets). Bearer token esa
    // endi XOTIRADA keshlanadi (EskizService) — bazada saqlanmaydi.
    /// <summary>SMS jo'natuvchi nomi (sender) — tasdiqlangan nikname yoki test uchun "4546".
    /// Maxfiy emas: admin UI'dan o'zgartiradi (.env'dagi ESKIZ_FROM boshlang'ich qiymat beradi).</summary>
    public string EskizFrom { get; set; } = "4546";
    /// <summary>To'lov cheki (termal kvitansiya) sozlamalari — JSON. Qaysi maydonlar ko'rinishi,
    /// sarlavha (logotip/nom), pastki izoh (footer), aloqa/QR. Bo'sh = standart shablon.</summary>
    public string CheckSettings { get; set; } = string.Empty;

    // ---------- Local SMS (CTI agent telefonining SIM-kartasidan) ----------
    /// <summary>Local SMS (Eskiz o'rniga agent telefonidan) yoqilganmi. Yoqilmagan bo'lsa provider
    /// tanlovi qanday bo'lishidan qat'i nazar Eskiz'ga tushadi.</summary>
    public bool LocalSmsEnabled { get; set; }
    /// <summary>Standart Local SMS agenti (CtiAgent.Id) — provider=local tanlanganda aniq agent
    /// ko'rsatilmasa (yoki avtomatik/fon xabarlarda) shu ishlatiladi.</summary>
    public string? LocalSmsDefaultAgentId { get; set; }
    /// <summary>Massaviy Local SMS yuborishda ikkita SMS orasidagi minimal kutish (soniya) — agent
    /// telefonini/operatorni haddan tashqari yuklamaslik uchun. 0 = kutishsiz (default).</summary>
    public int LocalSmsDelaySeconds { get; set; }

    // ---------- Kitoblar sotuvi (bot orqali buyurtma + to'lov rekvizitlari) ----------
    /// <summary>Botdagi «📚 Kitob sotib olish» tugmasi yoqilganmi (default true).</summary>
    public bool BookSalesEnabled { get; set; } = true;
    /// <summary>Botda ko'rsatiladigan KARTA raqami (P2P o'tkazma uchun). Bo'sh bo'lsa botda
    /// karta orqali to'lash varianti KO'RSATILMAYDI (faqat naqd qoladi). Maxfiy emas — mijozga
    /// baribir ko'rsatiladi, shuning uchun bazada saqlanadi.</summary>
    public string BookCardNumber { get; set; } = string.Empty;
    /// <summary>Karta egasining ismi (mijoz o'tkazma qilishda ko'radi).</summary>
    public string BookCardHolder { get; set; } = string.Empty;
    /// <summary>Rekvizitlar ostida ko'rsatiladigan qo'shimcha izoh (masalan bank nomi/eslatma).</summary>
    public string BookPaymentNote { get; set; } = string.Empty;

    // ---------- YUZ BILAN KIRISH (o'quvchi mobil ilovasi) ----------
    // Bular MAXFIY EMAS (kalit/parol emas) — shuning uchun `.env` emas, CenterMeta to'g'ri joy
    // (BookSalesEnabled bilan bir xil siyosat, CLAUDE.md "KALITLAR — FAQAT .env" qoidasiga mos).
    /// <summary>Yangi qurilmada kirishda selfi so'ralsinmi. <b>Default FALSE</b>: mavjud
    /// o'quvchilarning kirishi deploy bilan birdan buzilmasin (BookSalesEnabled'dagi saboq —
    /// u yerda entity default'i `true` edi, migratsiya esa `false` qo'ygan va farq chalkashlik
    /// tug'dirgan; bu yerda IKKALASI ham `false`).</summary>
    public bool LoginFaceEnabled { get; set; }
    /// <summary>Kosinus o'xshashligi chegarasi (0..1). Bundan past — "Yuz mos kelmadi".</summary>
    public double LoginFaceThreshold { get; set; } = 0.60;
    /// <summary>Vektorlarni yaratadigan model nomi/versiyasi. Ilova AYNAN shu qiymatni yuborishi
    /// shart — aks holda "Ilovani yangilang" (turli modellarning vektorlarini solishtirib bo'lmaydi).
    /// SFace (OpenCV Zoo): face_recognition_sface_2021dec_int8.onnx — 128-o'lchamli vektor.</summary>
    public string LoginFaceModelVersion { get; set; } = "sface-2021dec-int8-v1";
    /// <summary>Bitta o'quvchi uchun saqlanadigan oxirgi selfilar soni. Bundan eskilari (yozuvi ham,
    /// FAYLI ham) o'chiriladi — biometrik ma'lumot cheksiz to'planmasin.</summary>
    public int LoginFaceKeepChecks { get; set; } = 5;
    /// <summary>TIRIKLIK (liveness) tekshiruvi MAJBURIYmi — ilova avval
    /// <c>POST /api/student/face/challenge</c> dan bir martalik nonce va TASODIFIY harakatlar
    /// olishi, keyin ularning natijasini yuborishi shart. <b>Default TRUE</b>: modulning o'zi
    /// yangi, ya'ni "eski klient" degan tushuncha yo'q (kitob sotuvidagi kabi orqaga moslik
    /// muammosi bu yerda YO'Q). O'chirilsa — bosma surat/ekrandagi rasm bilan kirish ochiladi.</summary>
    public bool LoginFaceRequireLiveness { get; set; } = true;
    /// <summary>ILOVA HAQIQIYLIGI (Play Integrity) MAJBURIYmi. <b>Default FALSE</b>: kalit
    /// (<c>PLAY_INTEGRITY_*</c>) sozlanmaguncha va ilovaning yangi versiyasi tarqalmaguncha hech
    /// kim qulflanib qolmasin — natija baribir jurnalga yoziladi (<c>LoginFaceCheck.Attested</c>).
    /// Yoqilsa: <c>failed</c>, <c>notConfigured</c> va <c>unavailable</c> — hammasi RAD etiladi
    /// (fail-closed, <c>AppAttestation.Gate</c>).
    ///
    /// <para>⚠️ <b>YOQISHDAN OLDIN — iOS.</b> App Attest hali yozilmagan, ya'ni
    /// <c>AppAttestation.VerifyAsync</c> iOS uchun HAR DOIM <c>notConfigured</c> qaytaradi.
    /// Fail-closed bilan birga bu shuni bildiradi: yoqilsa HAMMA iOS foydalanuvchisi kira
    /// olmay qoladi. Buni "iOS bo'lsa o'tkazamiz" deb yechib BO'LMAYDI — <c>platform</c>
    /// maydonini klientning O'ZI yuboradi, ya'ni o'zgartirilgan APK <c>platform=ios</c>
    /// deyish bilan butun darvozadan o'tib ketardi. To'g'ri yechim — App Attest'ni yozish.</para></summary>
    public bool LoginFaceRequireAttestation { get; set; }

    // ---------- MARKETING: INSTAGRAM AI AGENTI ----------
    // Bular MAXFIY EMAS (kalit/parol emas), shuning uchun `.env` emas, CenterMeta to'g'ri joy.
    // Maxfiylar — `INSTAGRAM_APP_SECRET` va `INSTAGRAM_VERIFY_TOKEN` — avvalgidek `.env` da,
    // ulangan akkaunt tokeni esa `IgAccount.AccessToken` da (u OAuth orqali ish vaqtida olinadi).
    //
    // ⚠️ XAVFSIZLIK DEFAULTI: quyidagi to'rt bayroq ham entity'da, ham migratsiyada `false`.
    // (BookSalesEnabled saboqi: entity'da `true`, migratsiyada `false` bo'lgani chalkashlik
    // tug'dirgan.) Modul sozlanmagunicha jonli mijozga bironta javob ketmasin.

    /// <summary>Instagram AI agenti umuman yoqilganmi. <b>Default FALSE</b> — o'chiq bo'lsa fon
    /// xizmati navbatni umuman qayta ishlamaydi va HECH QANDAY tashqi so'rov ketmaydi
    /// (webhook baribir qabul qilinadi va navbatga yoziladi, faqat javob berilmaydi).</summary>
    public bool InstagramEnabled { get; set; }
    /// <summary>Post ostidagi IZOHLARGA avtomatik javob berilsinmi. <b>Default FALSE</b> —
    /// izohdagi javob OMMAVIY ko'rinadi, ya'ni xatoning narxi eng yuqori.</summary>
    public bool InstagramAutoReplyComments { get; set; }
    /// <summary>DM (shaxsiy xabar)larga avtomatik javob berilsinmi. <b>Default FALSE</b>.</summary>
    public bool InstagramAutoReplyDm { get; set; }
    /// <summary>Izoh yozgan odamga qo'shimcha ravishda YOPIQ javob (private reply) ham
    /// yuborilsinmi. <b>Default FALSE</b> — Meta buni izohdan keyin 7 kun ichida bir marta
    /// ruxsat beradi va ortiqcha ishlatilsa spam sifatida qabul qilinadi.</summary>
    public bool InstagramPrivateReplyEnabled { get; set; }

    /// <summary>Meta ilovasining App ID'si — maxfiy EMAS (u OAuth havolasida ochiq ko'rinadi),
    /// shuning uchun `.env` emas, shu yerda. Maxfiy juftligi (`App Secret`) `.env` da.</summary>
    public string InstagramAppId { get; set; } = string.Empty;
    /// <summary>Javob yozadigan Gemini modeli. Bo'sh = `GeminiService` default modeli.</summary>
    public string InstagramAiModel { get; set; } = string.Empty;
    /// <summary>Yaratilgan lidlarda `Lead.Source` sifatida yoziladigan manba NOMI (FK emas —
    /// lidlar moduli manbani nom bilan saqlaydi).</summary>
    public string InstagramLeadSource { get; set; } = "Instagram";
    /// <summary>Qaynoq lid va eskalatsiya haqida Telegram'da xabar berilsinmi (mavjud bot orqali).</summary>
    public bool InstagramNotifyTelegram { get; set; } = true;

    /// <summary>Javobdan oldingi kutish (soniya) — tabiiylik uchun. Bir zumda kelgan javob
    /// mijozga "bot" bo'lib ko'rinadi va Instagram tomonidan ham spamga o'xshaydi.</summary>
    public int InstagramReplyDelaySeconds { get; set; } = 5;
    /// <summary>Bir kunda yuboriladigan avtomatik javoblarning MAKSIMAL soni — himoya chegarasi.
    /// Sikl yoki hujum bo'lganda akkaunt bloklanib qolmasin.</summary>
    public int InstagramDailyReplyLimit { get; set; } = 200;
    /// <summary>Birinchi javobga qo'shiladigan BOT OSHKORLIGI matni (masalan "Bu — avtomatik
    /// yordamchi"). Meta platforma qoidalari avtomatlashtirilgan javobni oshkor qilishni talab
    /// qiladi; bo'sh qoldirilsa agent sukut bo'yicha matnni ishlatadi.</summary>
    public string InstagramGreeting { get; set; } = string.Empty;

    /// <summary>REKLAMA LIDLARI (Meta Lead Ads) yoqilganmi. <b>Default FALSE</b> — o'chiq bo'lsa
    /// webhook hodisasi navbatga tushadi, lekin `graph.facebook.com` ga HECH QANDAY so'rov
    /// ketmaydi va lid yaratilmaydi (modul darvozasi qoidasi, `marketing-instagram.md` §3).
    /// <para>⚠️ Avtojavob bayrog'idan (<see cref="InstagramEnabled"/>) MUSTAQIL: markaz AI
    /// agentini ishlatmasdan ham reklama lidlarini olishi mumkin (va aksincha).</para></summary>
    public bool InstagramLeadAdsEnabled { get; set; }

    /// <summary>Reklama formasidan kelgan lidning `Lead.Source` qiymati. DM/izohdan kelgan lid
    /// (<see cref="InstagramLeadSource"/>) bilan ATAYIN har xil: birida odam o'zi yozgan, bunda
    /// esa pul to'langan reklama olib kelgan — voronkada ular aralashib ketmasin.</summary>
    public string InstagramAdsLeadSource { get; set; } = "Instagram reklama";

    /// <summary>REKLAMA STATISTIKASI (Meta Ads Insights) yoqilganmi. <b>Default FALSE</b> —
    /// o'chiq bo'lsa `graph.facebook.com/act_.../insights` ga HECH QANDAY so'rov ketmaydi
    /// (modul darvozasi qoidasi).
    /// <para>⚠️ Boshqa Instagram bayroqlaridan MUSTAQIL: markaz AI agentini ham, reklama
    /// lidlarini ham ishlatmasdan faqat statistikani yig'ishi mumkin.</para></summary>
    public bool InstagramAdsStatsEnabled { get; set; }

    /// <summary>Statistika har kuni SOAT NECHADA sinxronlansin (0..23, server vaqti).
    /// <para>Nega kechasi (default 5)? Insights rate limiti qattiq va backfill og'ir; kunduzi
    /// ishlasa boshqa Meta so'rovlari (lid olish, javob yuborish) limit tufayli rad etilishi
    /// mumkin edi. Bundan tashqari o'tgan kun raqamlari faqat kun yopilgandan keyin
    /// barqarorlashadi.</para></summary>
    public int InstagramAdsSyncHour { get; set; } = 5;

    /// <summary>BIRINCHI yuklashda necha kunlik tarix olinsin (default 90).
    /// <para>⚠️ Bu son to'g'ridan-to'g'ri so'rovlar soniga aylanadi — juda katta qilinsa birinchi
    /// sinxronizatsiya rate limitga urilib, yarim yuklangan holatda to'xtaydi. Har kunlik
    /// sinxronizatsiyada esa faqat oxirgi bir necha kun QAYTA yuklanadi (Meta atributsiyani
    /// keyin ham tuzatadi), ya'ni bu qiymat faqat boshlanishga tegishli.</para></summary>
    public int InstagramAdsBackfillDays { get; set; } = 90;

    /// <summary>KONTENT REJALASHTIRISH (Instagram Content Publishing) yoqilganmi.
    /// <b>Default FALSE</b> — o'chiq bo'lsa rejalashtirilgan post navbatda qoladi, lekin
    /// Meta'ga chop etilmaydi.
    /// <para>⚠️ Modul yangi OAuth ruxsatini talab qiladi
    /// (<c>instagram_business_content_publish</c>), ya'ni bayroqni yoqishning o'zi YETMAYDI —
    /// foydalanuvchi Sozlamalarda «Qayta ulash» bosib akkauntni qayta avtorizatsiya qilishi
    /// kerak. Aks holda chop etish `permission` xatosi bilan yiqilardi va sababi tushunarsiz
    /// bo'lardi.</para></summary>
    public bool InstagramPublishEnabled { get; set; }

    /// <summary>CAPI (Conversions API) — lid sifatini Meta'ga qaytarish yoqilganmi.
    /// <b>Default FALSE</b> — o'chiq bo'lsa hodisa navbatiga yozilmaydi va tashqariga hech narsa
    /// ketmaydi.
    /// <para>⚠️ Bu bayroq MIJOZ MA'LUMOTINI (hashlangan bo'lsa ham) Meta'ga uzatishni yoqadi —
    /// yoqishdan oldin markaz maxfiylik siyosati va Meta'ning Data Protection Assessment
    /// talablarini bajarganiga ishonch hosil qilinishi kerak, shuning uchun default o'chiq.</para></summary>
    public bool InstagramCapiEnabled { get; set; }

    /// <summary>Events Manager'dagi dataset (piksel) ID — hodisalar SHUNGA yuboriladi.
    /// Bo'sh bo'lsa modul yoqilgan bo'lsa ham so'rov qilinmaydi (manzilsiz yuborib bo'lmaydi).</summary>
    public string InstagramCapiDatasetId { get; set; } = string.Empty;

    /// <summary>CAPI uchun Access Token.
    /// <para>⚠️ Qiymat HECH QACHON javobga, DTO'ga, logga yoki auditga TUSHMAYDI — tashqariga
    /// faqat "sozlangan / sozlanmagan" bayrog'i chiqadi (<see cref="IgAdPage.AccessToken"/>
    /// bilan bir xil qoida). Forma bo'sh yuborilsa mavjud token saqlanadi.</para></summary>
    public string InstagramCapiToken { get; set; } = string.Empty;

    /// <summary>"Sifatli lid" bosqichi uchun CAPI hodisasining NOMI.
    /// <para>🔴 Kodga yozib qo'yilmaydi ATAYIN: Conversion Leads oqimida <c>event_name</c> —
    /// ERKIN MATN va u Events Manager'da sozlangan bosqich nomi bilan AYNAN bir xil bo'lishi
    /// shart. Markaz bosqichni "Marketing Qualified Lead" deb nomlagan bo'lsa, kodda qotib
    /// qolgan satr hodisani jimgina yaroqsiz qilib qo'yardi.</para></summary>
    public string InstagramCapiStageQualified { get; set; } = "Sifatli lid";

    /// <summary>"To'lov qildi" bosqichi uchun CAPI hodisasining NOMI (yuqoridagi izoh bilan bir xil
    /// sabab). Bu hodisaga qiymat (<c>value</c>) va valyuta ham qo'shiladi — Meta ROAS bo'yicha
    /// optimallashtira olsin.</summary>
    public string InstagramCapiStageWon { get; set; } = "To'lov qildi";
}

/// <summary>Avto-xabar qoidasi — hodisa (Trigger) yuz berganda tanlangan kanallar orqali
/// shablon asosida xabar yuboriladi. Admin "Xabarlar → Avto xabarlar"da boshqaradi.
/// Eski SmsTemplate+IsAuto (faqat SMS) va ReminderRule (faqat push+telegram) modellarini
/// BIRLASHTIRGAN yagona model — har qoida 3 kanalni (SMS/Push/Telegram) mustaqil yoqadi.</summary>
public class AutoMessageRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Trigger { get; set; } = string.Empty;   // AutoMessageTriggers katalogidan
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool SendSms { get; set; }
    public bool SendPush { get; set; }
    public bool SendTelegram { get; set; }
    /// <summary>SMS qaysi orqali yuborilsin: "eskiz" (default) | "local" (CTI agent telefonidan,
    /// CenterMeta.LocalSmsDefaultAgentId orqali — qoida avtomatik ishga tushgani uchun agent
    /// tanlanmaydi, doim standart agent ishlatiladi).</summary>
    public string SmsProvider { get; set; } = "eskiz";
    public string Audience { get; set; } = "parents";     // parents|students|teachers
    public string Template { get; set; } = string.Empty;  // {ism} {fish} kabi tokenlar bilan
    public int OffsetMinutes { get; set; } = 5;           // lesson_attendance uchun
    public string SendScope { get; set; } = "lesson_start"; // lesson_start|not_filled|all
    public string ScheduleType { get; set; } = "daily";   // daily|monthly (custom_schedule)
    public string ScheduleTime { get; set; } = "09:00";
    public int ScheduleDayOfMonth { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// O'zgarishlar tarixi (audit) yozuvi. Moliyaga oid ma'lumot yaratilganda/tahrirlanganda/
/// o'chirilganda eski va yangi holat shu yerda saqlanadi — keyin "tarix" sifatida ko'riladi.
/// </summary>
public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ob'ekt turi: FinanceTransaction | TeacherSalary | ClassFee.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Tegishli yozuv id'si (amal/ o'qituvchi/ guruh id'si).</summary>
    public string EntityId { get; set; } = string.Empty;
    /// <summary>Amal: create | update | delete.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string Timestamp { get; set; } = string.Empty;
    /// <summary>O'zgartirgan foydalanuvchi id'si (yo'q bo'lsa — tizim).</summary>
    public string? ActorId { get; set; }
    /// <summary>O'zgartirgan foydalanuvchi nomi (yoki "Tizim").</summary>
    public string? ActorName { get; set; }
    /// <summary>O'qiladigan o'zbekcha izoh.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>O'zgarishdan oldingi holat (JSON). create uchun null.</summary>
    public string? Before { get; set; }
    /// <summary>O'zgarishdan keyingi holat (JSON). delete uchun null.</summary>
    public string? After { get; set; }
    /// <summary>Tegishli o'quvchi (o'quvchi to'lovi bo'lsa) — joyida filtrlash uchun.</summary>
    public string? StudentId { get; set; }
    /// <summary>Tegishli o'qituvchi (maosh bo'lsa) — joyida filtrlash uchun.</summary>
    public string? TeacherId { get; set; }
}

/// <summary>
/// Guruh chati xabari. A'zolar: shu guruh o'quvchilari, shu guruhga dars beradigan
/// o'qituvchilar va admin. Chat guruh nomi (ClassName) bo'yicha guruhlanadi.
/// </summary>
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi guruh chati (guruh nomi, masalan "3-A").</summary>
    public string ClassName { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    /// <summary>admin | teacher | student</summary>
    public string SenderRole { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Guruh ota-onalariga Telegram bot orqali yuborilgan e'lon (bir tomonlama xabar).</summary>
public class Broadcast
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClassName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Yuborish vaqtida shu guruhda Telegramda ro'yxatdan o'tgan ota-onalar soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Telegram orqali muvaffaqiyatli yetkazilganlar soni.</summary>
    public int SentCount { get; set; }
}

/// <summary>Ilovaga (FCM push) yuborilgan bildirishnoma — tarix uchun.</summary>
public class PushMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qabul qiluvchi toifa yorlig'i (masalan "Ota-onalar — 9-A", "O'qituvchilar").</summary>
    public string Audience { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Maqsadli qurilma tokenlari soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Muvaffaqiyatli yuborilgan push soni.</summary>
    public int SentCount { get; set; }
}


/// <summary>
/// Ota-onaning Telegram ro'yxati — Telegram chatId o'quvchiga bog'lanadi (guruh o'quvchidan
/// kelib chiqadi). Ota-ona botga kontaktini ulashganda raqami o'quvchining ParentPhone'i
/// bilan solishtirilib yoziladi. Bitta ota-onaning bir nechta farzandi bo'lishi mumkin.
/// </summary>
public class TelegramRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Bog'langan o'quvchi id'si (ota-ona ro'yxati uchun). Xodim yozuvida bo'sh.</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Bog'langan o'qituvchi id'si (xodim ro'yxati uchun). Ota-ona yozuvida null.</summary>
    public string? TeacherId { get; set; }
    /// <summary>Bog'langan tizim foydalanuvchisi (AppUser) id'si — ADMIN/xodim ro'yxati uchun
    /// (yangi lid xabarnomalarini olish). O'quvchi/o'qituvchi yozuvida null.</summary>
    public string? UserId { get; set; }
    /// <summary>Telegram chat (foydalanuvchi) id'si — bot shu manzilga e'lon yuboradi.</summary>
    public long ChatId { get; set; }
    /// <summary>Ulashgan foydalanuvchining Telegram ismi (ko'rsatish uchun).</summary>
    public string ParentName { get; set; } = string.Empty;
    /// <summary>Ulashilgan telefon raqami (faqat raqamlar — normallashtirilgan).</summary>
    public string Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// Bot orqali so'ralgan bir martalik kirish kodi (parol o'rniga tezkor login). Bot "🔑 Yangi kod
/// olish" tugmasi bosilganda shu chatga bog'langan har AppUser uchun yaratiladi (8 belgi, 60 soniya
/// amal qiladi, bir marta ishlatiladi). CodeHash — kodning o'zi EMAS, SHA256 xeshi saqlanadi.
/// </summary>
public class LoginOtpCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Kod tegishli bo'lgan tizim foydalanuvchisi (AppUser.Id).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Kodni so'ragan Telegram chat — cooldown (5 daqiqada bir marta) shu bo'yicha hisoblanadi.</summary>
    public long ChatId { get; set; }
    /// <summary>Kodning SHA256 xeshi (hex) — plaintext saqlanmaydi.</summary>
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime ExpiresAt { get; set; }
    /// <summary>Ishlatilgan (yoki yangisi so'ralgani uchun bekor qilingan) bo'lsa true — qayta ishlatilmaydi.</summary>
    public bool Used { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>
/// Telegram botda /start bosgan HAR BIR foydalanuvchi (admin support ro'yxati uchun). ChatId unikal.
/// "Adminga yozish" rejimida yuborgan matnlar BotSupportMessage sifatida saqlanadi.
/// </summary>
public class BotUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Telegram chat (foydalanuvchi) id'si — unikal.</summary>
    public long ChatId { get; set; }
    /// <summary>Telegram ismi (first + last).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Telegram @username (bo'lsa).</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>Ulashilgan telefon (faqat raqamlar) — bo'lmasa bo'sh.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Tizimdagi moslik yorlig'i (masalan "O'quvchi: Ali (ota-ona)" / "O'qituvchi: Vali" / "Admin").</summary>
    public string Linked { get; set; } = string.Empty;
    /// <summary>Rejim: "" (oddiy) | "support" (adminga murojaat — keyingi matnlar adminga ketadi)
    /// | "testcode" (onlayn test KODI kutilyapti — keyingi matn kod deb o'qiladi).</summary>
    public string Mode { get; set; } = string.Empty;
    /// <summary>Birinchi /start vaqti (ISO).</summary>
    public string StartedAt { get; set; } = string.Empty;
    /// <summary>Oxirgi murojaat (foydalanuvchi xabari) vaqti (ISO) — ro'yxat tartiblanishi shunga tayanadi.</summary>
    public string? LastMessageAt { get; set; }
    /// <summary>Oxirgi murojaat matni (ro'yxatda ko'rsatish uchun qisqa preview).</summary>
    public string LastText { get; set; } = string.Empty;
    /// <summary>Admin o'qimagan murojaatlar soni (ro'yxatdagi qizil belgi).</summary>
    public int AdminUnread { get; set; }
}

/// <summary>
/// Bot QO'SHILGAN Telegram guruh(super-guruh)i — yangi lidlar shu yerga avtomatik yuboriladi.
/// Bot guruhga qo'shilganda (<c>my_chat_member</c> yangilanishi) yoziladi, chiqarilganda IsActive=false.
/// </summary>
public class TelegramGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Guruh chat id'si (guruhlar uchun manfiy) — unikal.</summary>
    public long ChatId { get; set; }
    /// <summary>Guruh nomi (ko'rsatish uchun).</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Bot hozir shu guruh a'zosimi — chiqarilsa false (xabar yuborilmaydi).</summary>
    public bool IsActive { get; set; } = true;
    public DateTime AddedAt { get; set; } = AppClock.Now;
}

/// <summary>Telegram bot foydalanuvchisi ↔ admin support yozishmasidagi bitta xabar.</summary>
public class BotSupportMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi bot foydalanuvchisi (BotUser.ChatId).</summary>
    public long ChatId { get; set; }
    /// <summary>true = foydalanuvchidan (murojaat), false = admindan (javob).</summary>
    public bool FromUser { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Admin javobi bo'lsa — javob bergan adminning F.I.Sh.</summary>
    public string AdminName { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Foydalanuvchining shaxsiy sozlamalari (asosan o'quvchi/o'qituvchi ilovasi uchun): til, tema,
/// bildirishnoma yoqilganmi. Har foydalanuvchi uchun bitta qator (UserId — PK).
/// </summary>
public class UserSettings
{
    /// <summary>AppUser.Id — birlamchi kalit (har foydalanuvchi uchun bitta yozuv).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Ilova tili: uz | ru | en. Default "uz".</summary>
    public string Language { get; set; } = "uz";
    /// <summary>Tema: light | dark | system. Default "system".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Push bildirishnoma yoqilganmi.</summary>
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>Oxirgi yangilanish vaqti (UTC, ISO).</summary>
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// Mobil/desktop ilovaning push bildirishnoma uchun ro'yxatdan o'tgan qurilma tokeni
/// (FCM/APNs/WebPush). Bir foydalanuvchining bir nechta qurilmasi bo'lishi mumkin.
/// </summary>
public class DeviceToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    /// <summary>Push provayder tokeni (unique).</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>android | ios | web</summary>
    public string Platform { get; set; } = "android";
    /// <summary>Qurilma nomi (masalan "Samsung A52", "iPhone 13") — ilova yuboradi.</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>Push provayder ilova identifikatori (app_id) — ilova yuboradi.</summary>
    public string AppId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime LastSeenAt { get; set; } = AppClock.Now;
}

/// <summary>
/// Foydalanuvchiga (o'quvchi/o'qituvchi) yuborilgan bildirishnoma — ilovadagi "Bildirishnomalar"
/// tarixi uchun (push yetib bormasa ham saqlanadi). Har push yuborilganda yoziladi.
/// </summary>
public class UserNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>grade | payment | announcement | pickup | general ...</summary>
    public string Type { get; set; } = "general";
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>O'qilgan vaqti (null = o'qilmagan) — qo'ng'iroq ochilganda.</summary>
    public DateTime? ReadAt { get; set; }
    /// <summary>Foydalanuvchi "Tasdiqlash" tugmasini bosgan vaqti (null = tasdiqlanmagan) — admin ko'radi.</summary>
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>Admin e'loni (broadcast) bo'lsa — manba PushMessage id'si (tasdiqlarni shu broadcast'ga bog'lash uchun).</summary>
    public string PushMessageId { get; set; } = string.Empty;
}

/// <summary>
/// Shartnoma uchun yuklangan Word (.docx) andoza. Har target uchun (ota-ona / xodim) alohida.
/// Ichida `@` bilan boshlanuvchi o'rinbosarlar (masalan @fish) bo'ladi — yuborishda almashtiriladi.
/// </summary>
public class ContractTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>parent | staff</summary>
    public string Target { get; set; } = "parent";
    public string Name { get; set; } = string.Empty;
    /// <summary>Yuklangan fayl manzili ("/uploads/..."). Custom (matnli) andozada bo'sh.</summary>
    public string FileUrl { get; set; } = string.Empty;
    /// <summary>Asl fayl nomi (ko'rsatish uchun). Custom andozada bo'sh.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Custom (matnli) andoza tanasi — @-o'rinbosarli matn. Bo'sh bo'lmasa,
    /// yuborishda shu matndan .docx hosil qilinadi (fayl o'rniga).</summary>
    public string Body { get; set; } = string.Empty;
    /// <summary>Foydalanuvchi aniqlagan qo'shimcha @-o'rinbosarlar (doimiy qiymat bilan) —
    /// JSON: [{"key":"@direktor","value":"Aliyev A."}]. Yuborishda built-in tokenlar bilan
    /// birga almashtiriladi (built-in token nomi ustun).</summary>
    public string FieldsJson { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = AppClock.Now;
}

/// <summary>Yuborilgan shartnoma yozuvi (kim, qachon, qaysi raqam bilan).</summary>
public class Contract
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>parent | staff</summary>
    public string Target { get; set; } = "parent";
    /// <summary>Oluvchi kaliti: ota-ona telefon kaliti (PhoneUtil.Key) yoki teacherId.</summary>
    public string RecipientKey { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    /// <summary>Ketma-ket shartnoma raqami.</summary>
    public int Number { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = AppClock.Now;
    /// <summary>Telegram orqali muvaffaqiyatli yetkazildimi.</summary>
    public bool Delivered { get; set; }
    /// <summary>sent</summary>
    public string Status { get; set; } = "sent";
    /// <summary>Superadmin YUKLAGAN tayyor PDF nusxa ("/uploads/xxx.pdf"). Bo'sh bo'lsa shartnoma
    /// oluvchining ilovasida ko'rinmaydi — faqat PDF yuklangach "Shartnoma" bo'limida chiqadi.</summary>
    public string PdfUrl { get; set; } = string.Empty;
    /// <summary>Tizim hosil qilgan .docx nusxa ("/uploads/xxx.docx") — admin uni qayta yuklab olib,
    /// yakunlab, PDF qilib qaytadan yuklaydi.</summary>
    public string DocxUrl { get; set; } = string.Empty;
    /// <summary>Ko'rsatish uchun fayl nomi, masalan "Shartnoma № 12".</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Andoza nomi (tarixiy nusxa — andoza o'chirilsa ham qoladi).</summary>
    public string TemplateName { get; set; } = string.Empty;
    /// <summary>Ilovada (o'qituvchi/o'quvchi) ko'rinadimi. Superadmin yashira oladi.</summary>
    public bool Visible { get; set; } = true;
}

// ============================ DARAJA TESTI (placement/level test) ============================
// Admin kurs uchun common test yaratadi → ommaviy URL (`/test/{slug}`) shakllanadi → bo'lajak
// o'quvchi (anonim) kirib, ismi/telefoni bilan testni ishlaydi → ball/daraja hisoblanadi va
// CRM'da yangi LID bo'lib tushadi (Source="Daraja testi").

/// <summary>Daraja (level) testi — bitta kursga bog'langan ommaviy savol to'plami.</summary>
public class LevelTest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Test nomi (masalan "Ingliz tili daraja testi").</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Bog'langan kurs (Subject id). Lid InterestSubject'i shu kurs bo'ladi. Ixtiyoriy.</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Ommaviy URL uchun qisqa noyob token (`/test/{slug}`).</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Test boshида ko'rsatiladigan kirish matni / yo'riqnoma.</summary>
    public string Intro { get; set; } = string.Empty;
    /// <summary>Faolmi — faqat faol test ommaviy URL orqali ochiladi.</summary>
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Daraja testi savoli (ko'p variantli — bitta to'g'ri javob).</summary>
public class LevelTestQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TestId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Javob variantlari (EF Core 8 primitive collection).</summary>
    public List<string> Options { get; set; } = new();
    /// <summary>To'g'ri variant indeksi (Options ichida) — faqat Kind=="question" uchun.</summary>
    public int CorrectIndex { get; set; }
    /// <summary>Element turi: "question" (baholanadigan savol, to'g'ri javobli) yoki
    /// "survey" (so'rovnoma — checkbox, to'g'ri javobsiz, BAHOLANMAYDI, javob lidda saqlanadi).</summary>
    public string Kind { get; set; } = "question";
    /// <summary>So'rovnoma uchun: ko'p variant tanlash mumkinmi (checkbox). false = bitta (radio).</summary>
    public bool Multiple { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Amal sababi — turli amallar (muzlatish, o'chirish, sinovga qaytarish, lid/guruh o'chirish) bajarilganda
/// tanlanadigan sozlanadigan sabablar ro'yxati. Davomat (kelmaganlik) sababi alohida — <see cref="AbsenceReason"/>.
/// Kategoriya kalitlari: freeze | return_trial | remove_active | remove_trial | remove_frozen | lead_delete | group_delete.
/// </summary>
public class ActionReason
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Kategoriya kaliti (yuqoridagi ro'yxat).</summary>
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>
/// Lid manbasi ma'lumotnomasi ("Instagram", "Sayt", "Tanish orqali" ...). Admin "O'quv bo'limi →
/// Sabablar" sahifasida boshqaradi; lid yaratish formasi va Lidlar filtri shu ro'yxatdan tanlaydi.
/// <see cref="Lead.Source"/> — shu manbaning NOMI (erkin matn sifatida saqlanadi, eski lidlar buzilmasin).
/// </summary>
public class LeadSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>
/// Arxiv yozuvi — o'chirilgan entity'ning JSON suratini (snapshot) saqlaydi. O'chirish
/// endpointlari entity'ni hard-delete qilishdan OLDIN bu yerga surat oladi, shu sababli
/// o'chirilgan Lid/O'quvchi/O'qituvchi/Xodim/Guruh/Moliya yozuvini keyinchalik ko'rish va
/// TIKLASH mumkin. <see cref="Type"/> ∈ {"lead","student","teacher","staff","group","finance"}.
/// </summary>
public class ArchivedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Entity turi: lead | student | teacher | staff | group | finance.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Asl entity'ning Id'si.</summary>
    public string EntityId { get; set; } = string.Empty;
    /// <summary>Ko'rsatish uchun sarlavha (masalan F.I.SH yoki guruh nomi).</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Ko'rsatish uchun ostsarlavha (masalan telefon yoki summa).</summary>
    public string Subtitle { get; set; } = string.Empty;
    /// <summary>Asl entity'ning to'liq JSON surati (tiklash uchun deserializatsiya qilinadi).</summary>
    public string Json { get; set; } = string.Empty;
    /// <summary>O'chirish sababi (ixtiyoriy).</summary>
    public string? Reason { get; set; }
    /// <summary>O'chirilgan vaqt (ISO, mahalliy Toshkent vaqti).</summary>
    public string DeletedAt { get; set; } = string.Empty;
    /// <summary>O'chirgan foydalanuvchi nomi.</summary>
    public string ActorName { get; set; } = string.Empty;
}

/// <summary>
/// Lidga yuborilgan BIR MARTALIK daraja-testi havolasi. Admin lid uchun "Daraja testi yuborish"
/// bosganida yaratiladi: noyob <see cref="Token"/> bilan URL (`/test/invite/{token}`) SMS orqali
/// yuboriladi. Lid o'z ma'lumotini qayta kiritmaydi (lidda bor). Bir marta ishlangach
/// (<see cref="UsedAt"/> to'ladi) havola yopiladi. Natija o'sha lidga bog'lanadi.
/// </summary>
public class LevelTestInvite
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>URL uchun noyob token (`/test/invite/{token}`).</summary>
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string TestId { get; set; } = string.Empty;
    public string LeadId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>SMS holati: "sent" | "failed" | "" (hali yuborilmagan).</summary>
    public string SmsStatus { get; set; } = string.Empty;
    /// <summary>Eskiz RequestId (yetkazib berish holatini kuzatish uchun).</summary>
    public string SmsRequestId { get; set; } = string.Empty;
    /// <summary>Bir marta ishlatilgan vaqt (ISO). Bo'sh = hali ishlanmagan (qayta kirsa bo'ladi).</summary>
    public string UsedAt { get; set; } = string.Empty;
    /// <summary>Ishlangach yaratilgan topshiruv id'si.</summary>
    public string SubmissionId { get; set; } = string.Empty;
    /// <summary>Natija (stat uchun): ball foizi + daraja.</summary>
    public int Percent { get; set; }
    public string Level { get; set; } = string.Empty;
}

/// <summary>Daraja diapazoni — ball foiziga qarab daraja yorlig'i (masalan ≥75% → "Yuqori").</summary>
public class LevelTestBand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TestId { get; set; } = string.Empty;
    /// <summary>Daraja nomi (masalan "Boshlang'ich", "O'rta", "Yuqori" yoki "A1", "B1"...).</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Shu darajaga tushish uchun MINIMAL ball foizi (0..100).</summary>
    public int MinPercent { get; set; }
    public int Order { get; set; }
}

/// <summary>Daraja testi topshiruvi — kim ishladi, nechi ball, qaysi daraja, va yaratilgan lid.</summary>
public class LevelTestSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TestId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    /// <summary>Yoshi (ixtiyoriy, 0 = kiritilmagan).</summary>
    public int Age { get; set; }
    /// <summary>To'g'ri javoblar soni.</summary>
    public int Score { get; set; }
    /// <summary>Jami savollar soni.</summary>
    public int Total { get; set; }
    /// <summary>Ball foizi (0..100).</summary>
    public int Percent { get; set; }
    /// <summary>Aniqlangan daraja yorlig'i.</summary>
    public string Level { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Shu topshiruvdan yaratilgan Lid id'si.</summary>
    public string LeadId { get; set; } = string.Empty;
    /// <summary>So'rovnoma (survey) javoblari JSON: [{"q":"savol matni","a":["tanlangan variant",...]}].
    /// Baholanmaydi — admin natijalarda va lidda ko'rsatish uchun.</summary>
    public string SurveyJson { get; set; } = string.Empty;
}

// ============================ LID FORMALARI (ariza formalari) ============================
// "Formalar" bo'limining IKKINCHI turi (birinchisi — yuqoridagi DARAJA TESTI). Har bir ijtimoiy
// tarmoq / reklama kanali uchun ALOHIDA forma yaratiladi: Instagram uchun bittasi, Facebook uchun
// boshqasi, Telegram uchun uchinchisi... Har birining o'z ommaviy havolasi (`/forma/{slug}`) va o'z
// MANBASI (<see cref="LeadForm.Source"/>) bor — to'ldirilgan ariza AYNAN shu manba bilan lid bo'lib
// tushadi. Shu sabab "qaysi kanal nechta mijoz keltirdi" savoliga formalar statistikasi javob beradi.

/// <summary>
/// Ommaviy LID FORMASI — bitta kanal (Instagram / Facebook / Telegram / bannerdagi QR ...) uchun
/// alohida ariza formasi. Ism va telefon HAR DOIM so'raladi (lidning eng kam ma'lumoti), qolgani
/// sozlanadi: yosh, kurs tanlash, ota-ona telefoni + istalgancha QO'SHIMCHA savol
/// (<see cref="LeadFormField"/>).
/// </summary>
public class LeadForm
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Forma nomi — ommaviy sahifada sarlavha ("Instagram — bepul sinov darsi").</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Ommaviy URL uchun qisqa noyob token (`/forma/{slug}`).</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>
    /// Lid MANBASI — <see cref="LeadSource"/> NOMI (matn sifatida, <see cref="Lead.Source"/> bilan bir
    /// xil konvensiya). Shu formadan kelgan har bir lid AYNAN shu manba bilan yoziladi — modulning
    /// butun ma'nosi shunda: kanal → forma → manba.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// Formaning kursi — ERKIN MATN (markazdagi <see cref="Subject"/> ro'yxatiga BOG'LANMAGAN).
    /// Lid <see cref="Lead.InterestSubject"/>i shu bo'ladi. <see cref="AskCourse"/> yoqilgan
    /// bo'lsa — mijoz tanlagan variant ustun turadi.
    ///
    /// <para>NEGA erkin matn: reklama formasida ko'pincha markazdagi rasmiy kurs nomi emas,
    /// taklifning O'ZI yoziladi ("Bepul sinov darsi", "Yozgi IELTS intensiv") va u hali kurs
    /// sifatida ochilmagan bo'lishi mumkin.</para>
    /// </summary>
    public string CourseName { get; set; } = string.Empty;
    /// <summary>
    /// Mijozga ko'rsatiladigan kurs VARIANTLARI — formaning O'ZIDA yoziladi (EF Core 8 primitive
    /// collection). <see cref="AskCourse"/> yoqilganda shu ro'yxatdan tanlanadi; ro'yxat bo'sh
    /// bo'lsa savol umuman ko'rsatilmaydi (bo'sh select ma'nosiz).
    /// </summary>
    public List<string> CourseOptions { get; set; } = new();
    /// <summary>Forma tepasidagi tavsif / taklif matni (ixtiyoriy).</summary>
    public string Intro { get; set; } = string.Empty;
    /// <summary>Yuborilgandan keyin ko'rsatiladigan rahmat matni. Bo'sh — standart matn.</summary>
    public string SuccessText { get; set; } = string.Empty;
    /// <summary>Yuborish tugmasi matni. Bo'sh — "Yuborish".</summary>
    public string ButtonText { get; set; } = string.Empty;
    /// <summary>Yosh so'ralsinmi (lid izohiga yoziladi).</summary>
    public bool AskAge { get; set; }
    /// <summary>Mijoz KURSNI o'zi tanlasinmi — <see cref="CourseOptions"/> ro'yxatidan.</summary>
    public bool AskCourse { get; set; }
    /// <summary>Ota-onaning telefoni so'ralsinmi (<see cref="Lead.FatherPhone"/> ga yoziladi —
    /// lidlar qidiruvi shu ustunni ham qamraydi).</summary>
    public bool AskParentPhone { get; set; }
    /// <summary>Faolmi — faqat faol forma ommaviy havola orqali ochiladi.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Ommaviy sahifa necha marta ochilgan (konversiyani hisoblash uchun).</summary>
    public int Views { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Yaratgan foydalanuvchi ismi (ko'rsatish uchun).</summary>
    public string CreatedBy { get; set; } = string.Empty;

    // ---- Ijtimoiy tarmoq havolalari (ariza YUBORILGANDAN KEYIN ikonka bo'lib ko'rinadi) ----
    // Mijoz arizani qoldirgach "Rahmat!" ekranida turadi va u shu yerdan darhol kanalga/profilga
    // obuna bo'la oladi — menejer qo'ng'iroq qilgunicha aloqa uzilmasin. Har formada ALOHIDA:
    // Instagram reklamasidan kelganga Instagram, Telegram kanalidan kelganga kanal ko'rsatiladi.
    // Bo'sh maydon = ikonka umuman chizilmaydi.

    /// <summary>Instagram profili havolasi (`https://...`). Bo'sh — ko'rsatilmaydi.</summary>
    public string InstagramUrl { get; set; } = string.Empty;
    /// <summary>Telegram kanali/akkaunti havolasi.</summary>
    public string TelegramUrl { get; set; } = string.Empty;
    /// <summary>Facebook sahifasi havolasi.</summary>
    public string FacebookUrl { get; set; } = string.Empty;
    /// <summary>YouTube kanali havolasi. DIQQAT: nomi ATAYIN `Youtube` (`YouTube` EMAS) —
    /// camelCase JSON siyosati `YouTube` ni `youTube` qilib yuborardi va klient topa olmasdi
    /// (<see cref="CareerAbout"/> dagi bilan bir xil sabab).</summary>
    public string YoutubeUrl { get; set; } = string.Empty;
    /// <summary>Sayt (yoki boshqa havola).</summary>
    public string WebsiteUrl { get; set; } = string.Empty;
}

/// <summary>
/// Lid formasidagi QO'SHIMCHA savol. Javoblari baholanmaydi — ular lid izohiga va topshiruvning
/// <see cref="LeadFormSubmission.AnswersJson"/> iga tushadi.
/// </summary>
public class LeadFormField
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FormId { get; set; } = string.Empty;
    /// <summary>Savol matni / maydon yorlig'i.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Turi: text | textarea | number | select | radio | checkbox.
    /// select/radio/checkbox uchun <see cref="Options"/> to'ldirilishi SHART.</summary>
    public string Kind { get; set; } = "text";
    /// <summary>Variantlar (EF Core 8 primitive collection) — faqat select/radio/checkbox uchun.</summary>
    public List<string> Options { get; set; } = new();
    /// <summary>Maydon ichidagi yordamchi matn (placeholder).</summary>
    public string Placeholder { get; set; } = string.Empty;
    /// <summary>Majburiymi — bo'sh qoldirilsa forma yuborilmaydi.</summary>
    public bool Required { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Formaga tushgan bitta ARIZA. Lidning O'ZI <see cref="Lead"/> da (topshirish paytida yaratiladi
/// yoki telefon bo'yicha mavjudiga biriktiriladi), bu yerda esa AYNAN shu forma bo'yicha kesim
/// saqlanadi: qaysi formadan, qaysi sub-kanaldan (<see cref="Ref"/>) va qanday javoblar bilan.
/// </summary>
public class LeadFormSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FormId { get; set; } = string.Empty;
    /// <summary>Yaratilgan yoki biriktirilgan lid id'si.</summary>
    public string LeadId { get; set; } = string.Empty;
    /// <summary>Ariza YANGI lid ochdimi (false — mavjud lidga biriktirildi, ya'ni takroriy murojaat).</summary>
    public bool IsNewLead { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ParentPhone { get; set; } = string.Empty;
    /// <summary>Yoshi (0 = so'ralmagan/kiritilmagan).</summary>
    public int Age { get; set; }
    /// <summary>Tanlangan/biriktirilgan kurs NOMI (SNAPSHOT — kurs keyin o'zgarsa ham tarix buzilmasin).</summary>
    public string CourseName { get; set; } = string.Empty;
    /// <summary>
    /// Sub-kanal belgisi — ommaviy havoladagi `?ref=` qiymati (masalan `?ref=story`, `?ref=bio`).
    /// Bir forma ichida bir necha joyga qo'yilgan havolani ajratish uchun. Bo'sh = belgilanmagan.
    /// </summary>
    public string Ref { get; set; } = string.Empty;
    /// <summary>Qo'shimcha savollar javobi JSON: [{"question":"...","answers":["..."]}].</summary>
    public string AnswersJson { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}


/// <summary>O'QUV DASTURI — standalone sillabus (Kurs/Subject'dan MUSTAQIL). Bir dastur bir nechta
/// kursga (<see cref="SubjectCurriculum"/> orqali) biriktirilishi mumkin, va bir kursga bir nechta
/// dastur biriktirilishi mumkin (ko'p-ko'pga). Ichida Modul→Mavzu→Dars→Topshiriq daraxti bor.</summary>
public class Curriculum
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Order { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Kurs (Subject) ↔ O'quv dasturi (Curriculum) ko'p-ko'pga bog'lanishi. <see cref="Order"/> —
/// shu kursga biriktirilgan dasturlar orasidagi tartib (guruh ko'rinishida darslar shu tartibda
/// ketma-ket birlashtiriladi — <see cref="Services.CurriculumForecast"/>).</summary>
public class SubjectCurriculum
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string CurriculumId { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>Dastur moduli (sillabus 1-bosqich): o'quv dasturi (<see cref="Curriculum"/>) ichidagi
/// katta bo'lim, masalan "Beginner", "A1".</summary>
public class CourseModule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurriculumId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>Dastur mavzusi (sillabus 2-bosqich): modul (<see cref="CourseModule"/>) ichidagi mavzu,
/// masalan "Present Simple".</summary>
public class CourseTopic
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurriculumId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>Dastur darsi (sillabus 3-bosqich): mavzu ichidagi bitta dars (nomi bilan). Ichiga
/// kirilganda topshiriqlar (<see cref="CourseItem"/>) ro'yxati ko'rsatiladi — har biri o'z turini
/// tanlaydi (video/matn/audio/lug'at/test/pdf), bitta dars ichida bir nechtasi bo'lishi mumkin.</summary>
public class CourseLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurriculumId { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>Dastur bandi / TOPSHIRIQ (sillabus 4-bosqich): dars ichidagi alohida topshiriq
/// (rasm: Dastur→Modul→Mavzu→Dars→Topshiriq). Kontent olib yuradi: video/matn/audio/lug'at/test/pdf.</summary>
public class CourseItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurriculumId { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    /// <summary>Topshiriq nomi (sarlavha).</summary>
    public string Text { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Order { get; set; }
    /// <summary>Topshiriq turi: text | video | audio | vocab | test | pdf | exercise — yaratishda
    /// tanlanadi, keyinchalik ham o'zgartirilishi mumkin. "exercise" — interaktiv mashq
    /// (topshiriq konstruktori: gap tuzish, bo'sh joy, reading, matching, writing/speaking...).</summary>
    public string Type { get; set; } = "text";
    /// <summary>Video havolasi (YouTube/mp4) yoki yuklangan fayl URL — "video" dars.</summary>
    public string VideoUrl { get; set; } = string.Empty;
    /// <summary>Audio havolasi/fayl — "audio" dars.</summary>
    public string AudioUrl { get; set; } = string.Empty;
    /// <summary>Matnli dars mazmuni (o'qish) yoki video/audio tavsifi.</summary>
    public string TextContent { get; set; } = string.Empty;
    /// <summary>Yuklangan PDF fayl URL (/uploads/...) — "pdf" bo'lim.</summary>
    public string PdfUrl { get; set; } = string.Empty;
    /// <summary>PDF faylning asl nomi (o'quvchiga ko'rsatiladi).</summary>
    public string PdfName { get; set; } = string.Empty;
    /// <summary>Lug'at ("vocab") — JSON: [{"term":"hello","meaning":"salom"}].</summary>
    public string VocabJson { get; set; } = string.Empty;
    /// <summary>Interaktiv mashq ("exercise") TURI — topshiriq konstruktorida tanlangan aniq tur,
    /// masalan "sentence-order", "fill-choose", "reading-truefalse", "matching-audio", "writing".
    /// Bo'sh = mashq turi hali tanlanmagan (konstruktor tur tanlash ekranini ko'rsatadi).</summary>
    public string ExerciseKind { get; set; } = string.Empty;
    /// <summary>Interaktiv mashq MAZMUNI — konstruktor to'ldirgan JSON (tur bo'yicha turlicha:
    /// gaplar/savollar/variantlar/matn/juftliklar...). Front-end shu JSON'ni o'qib mashqni
    /// ham tahrirlaydi, ham o'quvchiga ishlatadi.</summary>
    public string ExerciseJson { get; set; } = string.Empty;
    /// <summary>Qisqa meta yorlig'i (masalan "12 daq"). Test/lug'atda avtomatik sanaladi.</summary>
    public string Meta { get; set; } = string.Empty;
    /// <summary>Yaratilgan sana-vaqt (ISO) — jadval ko'rinishida "Yaratilgan sana" ustuni uchun.
    /// Eski (bu maydon qo'shilishidan oldingi) bandlar uchun bo'sh.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Kurs darsidagi (CourseItem) test savoli: matn + variantlar + to'g'ri javob indeksi.</summary>
public class CourseQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ItemId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Javob variantlari (EF Core 8 primitive collection).</summary>
    public List<string> Options { get; set; } = new();
    /// <summary>To'g'ri variant indeksi (Options ichida).</summary>
    public int CorrectIndex { get; set; }
    public int Order { get; set; }
}

/// <summary>O'quvchining bir sillabus bandi bo'yicha bajarilganlik holati (per-item progress).
/// Progress KONTENTGA (dasturga) tegishli — kurs (Subject)ga emas, shuning uchun bitta dastur
/// bir nechta kursga biriktirilgan bo'lsa ham progress saqlanib qoladi.</summary>
public class CourseProgress
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Qaysi dastur uchun band bajarilgani (Curriculum id, band'dan meros — denormalized).
    /// Optional — tracking/filtrlash uchun; haqiqiy kalit (StudentId, ItemId).</summary>
    public string? CurriculumId { get; set; }
    public bool Done { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// O'quvchining sillabus topshirig'i (<see cref="CourseItem"/>) bo'yicha BITTA URINISHI —
/// "kim, qaysi topshiriqni, qachon, qanday natija bilan ishladi". <see cref="CourseProgress"/>
/// faqat "bajarildi/bajarilmadi" bayrog'ini saqlaydi, bu esa TARIX: har ishlash yangi qator
/// (<see cref="AttemptNo"/> = 1, 2, 3 ...), shuning uchun o'quvchining o'sish dinamikasi ko'rinadi.
///
/// Bitta topshiriq ichida bir nechta bo'lim bo'lishi mumkin (video → matn → lug'at → test → mashq),
/// shuning uchun urinish <see cref="Section"/> bilan ajratiladi — har bo'limning o'z natijasi bor.
/// </summary>
public class CourseItemAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Topshiriqni ishlagan o'quvchi (AppUser/Student id).</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Qaysi topshiriq (<see cref="CourseItem"/>).</summary>
    public string ItemId { get; set; } = string.Empty;
    /// <summary>Dastur id (banddan meros — filtrlash/hisobot uchun denormalized).</summary>
    public string CurriculumId { get; set; } = string.Empty;
    /// <summary>Dars id (banddan meros — denormalized).</summary>
    public string LessonId { get; set; } = string.Empty;
    /// <summary>Qaysi guruh orqali ishlandi (o'quvchining shu dasturdagi faol guruhi), topilmasa bo'sh.</summary>
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Bo'lim turi: <c>exercise</c> (interaktiv mashq) | <c>test</c> (dars ichidagi test) |
    /// <c>view</c> (video/matn/audio/PDF/lug'at — ballsiz, faqat ko'rib chiqildi).</summary>
    public string Section { get; set; } = "exercise";
    /// <summary>Mashq turi (Section=exercise bo'lganda): sentence-order, fill-choose ... Boshqa
    /// bo'limlarda bo'sh.</summary>
    public string ExerciseKind { get; set; } = string.Empty;
    /// <summary>Nechanchi urinish — shu (StudentId, ItemId, Section) uchun 1 dan boshlab sanaladi.</summary>
    public int AttemptNo { get; set; } = 1;
    /// <summary>To'g'ri javoblar soni. Section=view uchun 0.</summary>
    public int Correct { get; set; }
    /// <summary>Jami savol/element soni. Section=view uchun 0.</summary>
    public int Total { get; set; }
    /// <summary>Natija foizi 0..100 (Total=0 bo'lsa 0) — reyting/diagramma uchun oldindan hisoblangan.</summary>
    public int ScorePct { get; set; }
    /// <summary>Boshidan oxirigacha sarflangan vaqt (soniya).</summary>
    public int DurationSec { get; set; }
    /// <summary>Javoblar tafsiloti — JSON massiv:
    /// <c>[{"i":0,"prompt":"...","answer":"...","expected":"...","ok":true,"sec":12}]</c>.
    /// O'qituvchi "qayerda xato qildi" ni ko'radi; keyinchalik AI tahlil uchun ham asos.</summary>
    public string AnswersJson { get; set; } = string.Empty;
    /// <summary>Bo'lim ochilgan vaqt.</summary>
    public DateTime StartedAt { get; set; } = AppClock.Now;
    /// <summary>Yakunlangan vaqt (yozuv shu paytda yaratiladi).</summary>
    public DateTime FinishedAt { get; set; } = AppClock.Now;
}

/// <summary>Guruh darajasida sillabus o'tilishi: o'tilgan band (ItemId, IsRevision=false) yoki
/// takrorlash darsi (ItemId="", IsRevision=true — sillabusni ilgarilatmaydi).</summary>
public class GroupCurriculumLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GroupId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public bool IsRevision { get; set; }
    public string Date { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

// ============================ SERTIFIKAT ============================

/// <summary>
/// Sertifikat andozasi (HTML shablon): kurs bo'yicha o'quvchiga beriladigan
/// sertifikatning HTML shabloni. Tokenlar: {{student_name}}, {{course_name}},
/// {{issue_date}}, {{certificate_number}}, {{expires_date}}.
/// </summary>
public class CertificateTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Andoza nomi (admin ko'rishi uchun, masalan "Ingliz tili A1 sertifikat").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Bog'langan kurs (Subject) id'si.</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>HTML shablon matni — @-o'rinbosarlar bilan (@fish, @kurs, @sana, @muddati, @kod).</summary>
    public string HtmlTemplate { get; set; } = string.Empty;
    /// <summary>Amal qilish muddati (kunlarda). 0 — muddatsiz.</summary>
    public int ValidityDays { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}


/// <summary>
/// Berilgan sertifikat (yangi model): o'quvchi + kurs + HTML fayl.
/// SHA-256 hash bilan himoyalangan. Status: active | revoked | expired.
/// </summary>
public class StudentCertificate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Sertifikat berilgan o'quvchi.</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Sertifikat kurs (Subject) id'si.</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Fayl nomi (masalan "CERT-20260618-A1B2C3.html").</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Fayl yo'li — /uploads/certificates/... (server tomoni).</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>Fayl SHA-256 hash (hex) — hujjat butunligini tekshirish uchun.</summary>
    public string FileHash { get; set; } = string.Empty;
    /// <summary>Fayl hajmi (bayt).</summary>
    public long FileSize { get; set; }
    /// <summary>Sertifikat berilgan sana.</summary>
    public DateTime IssuedAt { get; set; } = AppClock.Now;
    /// <summary>Amal qilish muddati. Null — muddatsiz.</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>active | revoked | expired</summary>
    public string Status { get; set; } = "active";
    /// <summary>Bekor qilingan sana. Null — bekor qilinmagan.</summary>
    public DateTime? RevokedAt { get; set; }
    /// <summary>Bekor qilish sababi.</summary>
    public string? RevokeReason { get; set; }
    /// <summary>Qo'shimcha meta ma'lumotlar (JSON).</summary>
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Birinchi yuklab olish vaqti.</summary>
    public DateTime? DownloadedAt { get; set; }
    /// <summary>Jami yuklab olishlar soni.</summary>
    public int DownloadCount { get; set; }
}

/// <summary>
/// O'quvchi AI tahlili (Gemini) — saqlanadigan yozuv. Bir o'quvchiga KUNIGA BIR MARTA
/// yaratiladi (Date bo'yicha cheklov). ResultJson — strukturali natija (matn bo'limlari +
/// baholar/diagramma uchun sonlar + oldingi tahlilga nisbatan o'zgarishlar). Tarix sifatida
/// o'quvchi sahifasida "AI Tahlil" bo'limida ko'rsatiladi; keyingi tahlil oldingisiga tayanadi.
/// </summary>
public class StudentAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Tahlil sanasi "yyyy-MM-dd" (Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt ISO ("yyyy-MM-ddTHH:mm:ss", Toshkent).</summary>
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa xulosa (umumiy holat) — ro'yxat ko'rinishi uchun.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100) — tarix grafigi/badge uchun.</summary>
    public int OverallScore { get; set; }
    /// <summary>To'liq strukturali natija (JSON) — diagramma + matn bo'limlari.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>
/// O'QITUVCHI AI tahlili (Gemini) — saqlanadigan yozuv. Bir o'qituvchiga KUNIGA BIR MARTA
/// yaratiladi (Date bo'yicha cheklov). ResultJson — { ai, metrics }: AI narrativ (o'quvchi oqimi,
/// ketish sabablari, jurnal intizomi, rivojlanish, tavsiyalar) + DETERMINISTIK hisoblangan
/// raqamlar (<see cref="Application.Services.TeacherSnapshotBuilder"/>). O'qituvchi profilidagi
/// "AI tahlil" tabida ko'rsatiladi; keyingi tahlil oldingisiga tayanib o'zgarishlarni aytadi.
/// </summary>
public class TeacherAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Tahlil sanasi "yyyy-MM-dd" (Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt ISO ("yyyy-MM-ddTHH:mm:ss", Toshkent).</summary>
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa xulosa (umumiy holat) — ro'yxat ko'rinishi uchun.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100) — tarix grafigi/badge uchun.</summary>
    public int OverallScore { get; set; }
    /// <summary>To'liq strukturali natija (JSON): { ai, metrics }.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>
/// GURUH AI tahlili (Gemini) — saqlanadigan yozuv. Bir guruhga KUNIGA BIR MARTA yaratiladi
/// (Date bo'yicha cheklov). ResultJson — { ai, metrics }: AI narrativ (davomat, muzlatish/ketish,
/// o'zlashtirish, imtihonlar, to'lovlar, jurnal intizomi — TANQIDIY tahlil) + DETERMINISTIK
/// hisoblangan raqamlar (<see cref="Application.Services.GroupSnapshotBuilder"/>). Guruh
/// sahifasidagi "AI tahlil" tabida ko'rsatiladi.
/// </summary>
/// <summary>
/// VORONKA AI tahlili (Gemini) — "Formalar" bo'limidagi ikkita statistika sahifasi uchun:
/// <b>lid formalari</b> va <b>daraja testlari</b>. Ikkalasi bitta jadvalda, farqi
/// <see cref="Kind"/> da: savol ham, ma'lumot shakli ham bir xil (keldi → lid → o'quvchi →
/// to'lov), shuning uchun ikkita ayri jadval/servis yasalmadi.
///
/// <para>Guruh/o'qituvchi tahlili bilan bir xil qoida: raqamlar DETERMINISTIK (kod hisoblaydi),
/// AI faqat narrativ yozadi; natija <c>ResultJson</c> = <c>{ ai, metrics }</c> bo'lib saqlanadi
/// (eski tahlil ochilganda ham diagrammalar ishlaydi) va KUNIGA BIR MARTA yaratiladi.</para>
/// </summary>
public class FunnelAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi voronka: <c>lead-forms</c> | <c>level-tests</c>
    /// (<c>FunnelAiAnalysisService.KindLeadForms/KindLevelTests</c>).</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Tahlil sanasi "yyyy-MM-dd" (Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa xulosa — tarix ro'yxatida ko'rinadi.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100) — tarix/badge uchun.</summary>
    public int OverallScore { get; set; }
    /// <summary>To'liq strukturali natija (JSON): { ai, metrics }.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

public class GroupAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string GroupId { get; set; } = string.Empty;
    /// <summary>Tahlil sanasi "yyyy-MM-dd" (Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt ISO ("yyyy-MM-ddTHH:mm:ss", Toshkent).</summary>
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa xulosa (umumiy holat) — ro'yxat ko'rinishi uchun.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100) — tarix/badge uchun.</summary>
    public int OverallScore { get; set; }
    /// <summary>To'liq strukturali natija (JSON): { ai, metrics }.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>
/// Markaz (butun o'quv markazi) AI tahlili (Gemini) — KUNIGA BIR MARTA (ertalab soat ~8da fon
/// xizmati orqali, yoki admin qo'lda). ResultJson — strukturali natija: AI narrativ (umumiy holat,
/// tushum tahlili, baholar dinamikasi, lidlar, ketganlar, xavflar, tavsiyalar) + deterministik
/// hisoblangan raqamlar (moliya prognozi, ko'rsatkichlar, diagramma nuqtalari). Bosh sahifada
/// "AI Tahlil" bo'limida ko'rsatiladi.
/// </summary>
public class CenterAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Tahlil sanasi "yyyy-MM-dd" (Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Yaratilgan vaqt ISO (Toshkent).</summary>
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa umumiy xulosa — ro'yxat/badge uchun.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Markaz salomatligi (0-100) — tarix/badge uchun.</summary>
    public int Health { get; set; }
    /// <summary>To'liq strukturali natija (JSON): { ai, revenue, metrics }.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>
/// SMS yuborish partiyasi (Xabarlar → SMS yuborish) — bitta yuborish (bir nechta raqamga).
/// Push/E'lon tarixiga o'xshash: kim/qachon/qancha. Har bir raqam yozuvi <see cref="SmsLog"/>da.
/// </summary>
public class SmsBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qabul qiluvchi guruh yorlig'i (masalan "Ota-onalar — 9-A", "O'qituvchilar").</summary>
    public string Audience { get; set; } = string.Empty;
    /// <summary>Yuborilgan matn (andoza — o'rinbosarlar bilan).</summary>
    public string Message { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Jami oluvchilar (raqamlar) soni.</summary>
    public int RecipientCount { get; set; }
    /// <summary>Eskiz qabul qilgan (yuborishga ketgan) soni.</summary>
    public int SentCount { get; set; }
    /// <summary>Yuborish manbai: "eskiz" (default) | "local" (CTI agent telefonidan). Butun partiya
    /// bitta provider orqali yuboriladi (provider tanlovi bitta yuborish amalida bir marta qilinadi).</summary>
    public string Provider { get; set; } = "eskiz";
}

/// <summary>
/// SMS andozasi (shablon) — admin "Sozlamalar → SMS (Eskiz)"da yaratadi/tahrirlaydi. SMS yuborishda
/// (o'quvchi/ota-ona/lid) tanlanadi. Faqat QO'LDA yuborish uchun — avto-hodisalar
/// <see cref="AutoMessageRule"/>da. Matnda o'rinbosarlar: {fish} {sinf} {telefon}...
/// </summary>
public class SmsTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Andoza nomi (ko'rsatish uchun).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Andoza matni (o'rinbosarlar bilan).</summary>
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
    public string CreatedAt { get; set; } = AppClock.Iso();
}

/// <summary>
/// Yuborilgan bitta SMS jurnali (raqam bo'yicha). Eskiz qaytargan <c>RequestId</c> orqali yetkazib
/// berish holati (callback webhook) yangilanadi.
/// </summary>
public class SmsLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi partiyaga tegishli (ixtiyoriy — OTP/avto-SMS uchun null).</summary>
    public string? BatchId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>Oluvchi nomi (ko'rsatish uchun — o'quvchi/ota-ona/o'qituvchi).</summary>
    public string RecipientName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>Eskiz qaytargan so'rov identifikatori (UUID) — callback shu bo'yicha topadi.</summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Holat: waiting | NEW | ACCEPTED | DELIVRD | UNDELIV | REJECTD | EXPIRED | error | ...
    /// (provider=local uchun granular yetkazish holati yo'q — "yuborildi"/"yetkazilmadi").</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Yuborish manbai: "eskiz" (default) | "local" (CTI agent telefonidan).</summary>
    public string Provider { get; set; } = "eskiz";
    /// <summary>Provider=local bo'lsa — qaysi agent (CtiAgent.Id) orqali yuborilgani.</summary>
    public string? AgentId { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// Sertifikat tekshiruvi yozuvi: /verify/{id} so'rovida qoldiriladi.
/// IsValid: hash to'g'ri va status==active bo'lsa true.
/// </summary>
public class CertificateVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Tekshirilgan StudentCertificate id'si.</summary>
    public string StudentCertificateId { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; } = AppClock.Now;
    /// <summary>Tekshiruvchi IP manzili.</summary>
    public string VerifiedFrom { get; set; } = string.Empty;
    /// <summary>Sertifikat haqiqiy va amal qiladimi.</summary>
    public bool IsValid { get; set; }
    /// <summary>Hash to'g'ri tekshirilganmi (SHA-256 mos kelgan).</summary>
    public bool HashMatched { get; set; }
}

/// <summary>Xodim roli shabloni — standart roller (Qo'ng'iroq operatori, Kassir, Administrator).
/// Yangi xodim qo'shishda shablonni tanlab olsa, default ruxsatlari avtomatik belgilanadi.
/// Keyin qo'shimcha ruxsatlarni qo'shish mumkin.</summary>
public class StaffRoleTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Shablonning kodli nomi (system uchun): call_operator, cashier, administrator.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Ko'rsatiladigan nomi: "Qo'ng'iroq operatori", "Kassir", "Administrator".</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Izoh (ixtiyoriy): "Qo'ng'iroq qabul qiladi va lidlarni boshqaradi" va h.k.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Default ruxsatlari (adminPermissions kalitlari)  — JSON massiv stringlar:
    /// ["leads","messages"] — yangi xodimga belgilanadi. Keyin qo'shimcha ruxsatlarni qo'shish mumkin.</summary>
    public List<string> DefaultPermissions { get; set; } = new();
    /// <summary>Yaratilgan vaqt — faqat info uchun.</summary>
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}


/// <summary>
/// Call Center qo'ng'irog'i (provayder telefoniya orqali). O'quvchi topilsa StudentId to'ladi
/// (qo'lda terilgan/notanish raqamda null). Operator = CRM foydalanuvchisi (Users).
/// AsteriskUniqueId — provayder hodisalarini (ringing/answer/finish) shu yozuvga bog'lash kaliti.
/// </summary>
public class Call
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Raqam o'quvchiga tegishli bo'lsa — Students.Id, aks holda null.</summary>
    public string? StudentId { get; set; }
    /// <summary>Qo'ng'iroqni boshlagan/qabul qilgan operator (Users.Id).</summary>
    public string? OperatorUserId { get; set; }
    /// <summary>Normallashtirilgan telefon raqam (+998...).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>"outbound" (chiquvchi) | "inbound" (kiruvchi).</summary>
    public string Direction { get; set; } = "outbound";
    /// <summary>originating | ringing | answered | completed | no_answer | busy | failed.</summary>
    public string Status { get; set; } = "originating";
    public DateTime StartedAt { get; set; } = AppClock.Now;
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>Gaplashuv davomiyligi soniyada (javobdan tugashgacha; javobsiz — 0).</summary>
    public int DurationSeconds { get; set; }
    /// <summary>Provayder qo'ng'iroq id'si: MoiZvonki event_pbx_call_id —
    /// jonli hodisalarni shu yozuvga bog'lash kaliti. (Maydon nomi tarixiy sabab bilan qoldi.)</summary>
    public string AsteriskUniqueId { get; set; } = string.Empty;
    /// <summary>MoiZvonki db_call_id — calls.list sinxronizatsiyasida takrorlanmaslik kaliti
    /// (webhook call.finish ham to'ldiradi). Bo'sh — sinxron qilinmagan/boshqa provayder.</summary>
    public string ProviderDbId { get; set; } = string.Empty;
    /// <summary>Suhbatning SO'ZMA-SO'Z transkripti (Azure Speech, hech qanday moslashtirishsiz).
    /// Bo'sh — hali transkript qilinmagan ("Transkriptga o'girish" tugmasi bilan yaratiladi).</summary>
    public string Transcript { get; set; } = string.Empty;
    /// <summary>Transkript bo'yicha Gemini AI tahlili (operator nima deyishi mumkin edi, tavsiyalar).</summary>
    public string AiAnalysis { get; set; } = string.Empty;
    /// <summary>Suhbat yozuvi: provayder yozuv URL'i (MoiZvonki) yoki lokal fayl nomi. Bo'sh — yozuv yo'q.</summary>
    public string RecordingFile { get; set; } = string.Empty;
    /// <summary>Operator izohi (ixtiyoriy).</summary>
    public string Note { get; set; } = string.Empty;
}

/* ---------- CTI (Local Call) — Android agent-ilovalar bilan lokal call-center ---------- */

/// <summary>
/// CTI agent — xodim telefoniga o'rnatilgan Android ilova akkaunti (mavjud <see cref="AppUser"/>dan
/// alohida; login/parol shu yerda). Ilova qo'ng'iroq metadata+audio yuboradi, WebSocket orqali
/// serverdan <c>dial</c> buyrug'ini oladi. Oflaynda FCM push (data-message) bilan uyg'otiladi.
/// </summary>
public class CtiAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ilovaga kirish logini (unikal).</summary>
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Operator paneli va tarixda ko'rinadigan nom (masalan xodim ismi).</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Faol emas bo'lsa — login qila olmaydi (o'chirish o'rniga).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Oflaynda uyg'otish uchun FCM qurilma tokeni (bo'sh — hali ro'yxatdan o'tmagan).</summary>
    public string FcmToken { get; set; } = string.Empty;
    /// <summary>Hozir WebSocket orqali ulanganmi (heartbeat/ulanish yangilaydi; jonli haqiqat
    /// konnektsiya menejeridan olinadi).</summary>
    public bool IsOnline { get; set; }
    /// <summary>Oxirgi faollik (heartbeat/presence).</summary>
    public DateTime? LastSeenAt { get; set; }
    /// <summary>Biriktirilgan xodim (<see cref="AppUser.Id"/>), ixtiyoriy. Berilgan bo'lsa — shu
    /// agentning qo'ng'iroqlari/audiolari FAQAT shu xodimga (va SuperAdmin'ga) ko'rinadi. Bo'sh —
    /// hech kimga biriktirilmagan (faqat SuperAdmin ko'radi).</summary>
    public string? StaffUserId { get; set; }
}

/// <summary>
/// CTI qo'ng'iroq yozuvi (Android ilovadan yuboriladi). Id = <c>serverCallId</c> (ilova audio va
/// hodisalarni shu bo'yicha yuklaydi). O'quvchi telefon bo'yicha topilsa <see cref="StudentId"/> to'ladi.
/// </summary>
public class CtiCallRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi agent (telefon) qildi/qabul qildi.</summary>
    public string AgentId { get; set; } = string.Empty;
    /// <summary>"incoming" (kiruvchi) | "outgoing" (chiquvchi) | "missed" (javobsiz).</summary>
    public string Direction { get; set; } = "outgoing";
    /// <summary>Suhbatdosh raqami (agent telefonidagi ikkinchi tomon).</summary>
    public string RemoteNumber { get; set; } = string.Empty;
    /// <summary>Ilovadagi kontakt nomi (bo'lsa).</summary>
    public string ContactName { get; set; } = string.Empty;
    /// <summary>Raqam o'quvchiga (yoki ota-onaga) tegishli bo'lsa — Students.Id; aks holda null.</summary>
    public string? StudentId { get; set; }
    public DateTime StartedAt { get; set; } = AppClock.Now;
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>Gaplashuv davomiyligi soniyada.</summary>
    public int DurationSec { get; set; }
    /// <summary>Yozuv fayli — recordings papkasiga NISBIY fayl nomi (bo'sh — hali yuklanmagan).</summary>
    public string AudioPath { get; set; } = string.Empty;
    /// <summary>Audio serverga yuklanganmi.</summary>
    public bool AudioUploaded { get; set; }
    /// <summary>Operator izohi (admin paneldan).</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Azure Speech (diarizatsiya) orqali so'zma-so'z transkript — so'zlovchilar ajratilgan
    /// ("1-suhbatdosh: ..."). Bo'sh — hali transkript qilinmagan.</summary>
    public string Transcript { get; set; } = string.Empty;
    /// <summary>Transkript asosida Gemini AI tahlili (suhbat mazmuni, tavsiyalar, baho). Bo'sh — hali tahlil qilinmagan.</summary>
    public string AiAnalysis { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>CTI qo'ng'irog'ining oraliq hodisasi (jonli holat tarixi). Qo'ng'iroq o'chsa — kaskad o'chadi.</summary>
public class CtiCallEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi qo'ng'iroqqa tegishli (CtiCallRecord.Id).</summary>
    public string CallId { get; set; } = string.Empty;
    /// <summary>"ringing" | "answered" | "ended".</summary>
    public string Type { get; set; } = string.Empty;
    public DateTime At { get; set; } = AppClock.Now;
}

/// <summary>Server→ilova yuborilgan buyruq jurnali (masalan click-to-call <c>dial</c>) va yetkazish holati.</summary>
public class CtiCommandLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qaysi agentga yuborildi.</summary>
    public string AgentId { get; set; } = string.Empty;
    /// <summary>Buyruq turi, masalan "dial".</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Buyruq yuki (masalan teriladigan raqam).</summary>
    public string Payload { get; set; } = string.Empty;
    /// <summary>"pending" | "sent" | "acked" | "failed".</summary>
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Xodimga (admin/staff) biriktirilgan CUSTOM topshiriq (checklist bandi). "Adminga topshiriq"
/// bo'limida superadmin har xodim uchun alohida topshiriqlar ro'yxatini tuzadi. Har kuni ertalab
/// shu topshiriqlar Telegram bot orqali xodimga "bajarildi" tugmasi bilan yuboriladi.</summary>
public class StaffTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Egasi (AppUser.Id) — admin yoki staff.</summary>
    public string StaffUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Xodim topshirig'ining KUNLIK yozuvi (tarix): har (xodim, sana) uchun shu kungi checklist
/// bandlari va bajarildi/bajarilmadi holati. Kunlik jo'natishda snapshot sifatida yaratiladi (Title
/// nusxasi bilan — topshiriq keyin o'chirilsa ham tarix saqlanadi). Xodim botda "bajarildi" bosganda
/// <see cref="Done"/> yangilanadi.</summary>
public class StaffTaskLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Manba topshiriq (StaffTask.Id). Topshiriq o'chirilsa ham yozuv (Title bilan) qoladi.</summary>
    public string TaskId { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    /// <summary>Kun ("yyyy-MM-dd").</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Topshiriq nomi nusxasi (o'sha kundagi holat).</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Checklistdagi tartib (topshiriq Order'idan nusxa).</summary>
    public int Order { get; set; }
    public bool Done { get; set; }
    /// <summary>Bajarildi deb belgilangan vaqt (ISO). Bo'sh — bajarilmagan.</summary>
    public string? DoneAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

// =====================================================================================
//  KITOBLAR SOTUVI (O'quv bo'limi → Kitoblar sotuvi + Telegram bot orqali buyurtma)
// =====================================================================================

/// <summary>
/// Sotuvdagi kitob (tovar). Narx va ombor qoldig'i shu yerda; qoldiqning har bir o'zgarishi
/// <see cref="BookStockMove"/> da tarix sifatida qoladi (kirim/sotuv/korreksiya).
/// </summary>
public class Book
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Kitob nomi (botda ko'rinadi).</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Muallif (ixtiyoriy).</summary>
    public string Author { get; set; } = string.Empty;
    /// <summary>Qisqa tavsif — botda kitob tanlanganda ko'rsatiladi.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Muqova rasmi (`/uploads/...`) — botda va admin panelida ko'rinadi.</summary>
    public string CoverUrl { get; set; } = string.Empty;
    /// <summary>Telegram keshlagan muqova <c>file_id</c>'si — bir marta yuklangach botga qayta
    /// yuklanmasdan yuboriladi (APK/test PDF bilan bir xil usul). Muqova o'zgarsa bo'shatiladi.</summary>
    public string CoverFileId { get; set; } = string.Empty;
    /// <summary>Bir dona narxi (so'm).</summary>
    public decimal Price { get; set; }
    /// <summary>JORIY ombor qoldig'i (ostatka). Buyurtma TASDIQLANGANDA ayiriladi.</summary>
    public int Stock { get; set; }
    /// <summary>Botda ko'rinadimi (qoldiq 0 bo'lsa ham "tugagan" deb ko'rsatiladi, o'chirilmaydi).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Kitob tizimga qo'shilgan vaqt — "kitoblar qo'shilish tarixi" hisobotida.</summary>
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Kim qo'shgani (admin F.I.Sh.).</summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Ombor harakati (kitob qoldig'ining har bir o'zgarishi). <see cref="Qty"/> musbat = KIRIM
/// (yangi kitob qo'shildi / qoldiq to'ldirildi), manfiy = CHIQIM (sotuv yoki korreksiya).
/// "Kitoblar qo'shilish tarixi" hisoboti — Qty &gt; 0 bo'lgan yozuvlar.
/// </summary>
public class BookStockMove
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BookId { get; set; } = string.Empty;
    /// <summary>Kitob nomi nusxasi (kitob o'chirilsa ham tarix o'qiladi).</summary>
    public string BookTitle { get; set; } = string.Empty;
    /// <summary>O'zgarish miqdori: +N kirim, -N chiqim.</summary>
    public int Qty { get; set; }
    /// <summary>Sabab: "initial" (kitob yaratildi) | "restock" (qoldiq to'ldirildi) |
    /// "sale" (buyurtma tasdiqlandi) | "correction" (qo'lda tuzatish).</summary>
    public string Reason { get; set; } = "restock";
    /// <summary>Sotuv bo'lsa — tegishli buyurtma (<see cref="BookOrder.Id"/>).</summary>
    public string? OrderId { get; set; }
    /// <summary>Izoh (masalan "Nashriyotdan 20 dona").</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Shu harakatdan KEYINGI qoldiq — hisobotda "ostatka" ustuni uchun.</summary>
    public int StockAfter { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Kim bajargani (admin F.I.Sh. yoki "Bot").</summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Telegram bot orqali tushgan kitob buyurtmasi. Oqim: mijoz kitob+soni+to'lov turini tanlaydi →
/// karta bo'lsa chek rasmini yuklaydi → buyurtma <b>pending</b> holatda adminga boradi → admin
/// tasdiqlasa qoldiq ayiriladi va mijozga xabar ketadi, rad etsa sababi bilan xabar ketadi.
/// Kitob nomi/narxi SNAPSHOT sifatida saqlanadi — keyin narx o'zgarsa hisobot buzilmaydi.
/// </summary>
public class BookOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ko'rsatiladigan ketma-ket buyurtma raqami (#1, #2 ...).</summary>
    public int Number { get; set; }
    /// <summary>Buyurtma bergan Telegram chat id'si (xabar qaytarish uchun).</summary>
    public long ChatId { get; set; }
    /// <summary>Mijoz ismi (Telegram profilidan yoki markaz ma'lumotidan).</summary>
    public string CustomerName { get; set; } = string.Empty;
    /// <summary>Telefon (faqat raqamlar).</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Telefon markazdagi o'quvchiga mos kelsa — shu o'quvchi id'si (ixtiyoriy).</summary>
    public string? StudentId { get; set; }
    public string BookId { get; set; } = string.Empty;
    /// <summary>Kitob nomi nusxasi (buyurtma vaqtidagi).</summary>
    public string BookTitle { get; set; } = string.Empty;
    /// <summary>Buyurtma vaqtidagi bir dona narxi.</summary>
    public decimal UnitPrice { get; set; }
    public int Qty { get; set; } = 1;
    /// <summary>Umumiy summa = UnitPrice × Qty (buyurtma vaqtida hisoblanadi).</summary>
    public decimal Total { get; set; }
    /// <summary>To'lov turi: "cash" (naqd) | "card" (karta raqamiga o'tkazma) |
    /// "credit" (NASIYA — kitob berildi, pul keyin olinadi; faqat markazda qo'lda sotuvda).</summary>
    public string PaymentMethod { get; set; } = "cash";
    /// <summary>Karta to'lovida mijoz yuborgan chek rasmi/PDF'i (`/uploads/...`). Naqdda bo'sh.</summary>
    public string ReceiptUrl { get; set; } = string.Empty;
    /// <summary>Holat: "pending" (kutilmoqda) | "approved" (tasdiqlandi) | "rejected" (rad etildi).</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Rad etish sababi (admin kiritadi) — mijozga shu matn yuboriladi.</summary>
    public string RejectReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    /// <summary>Tasdiqlangan/rad etilgan vaqt.</summary>
    public DateTime? DecidedAt { get; set; }
    /// <summary>Qarorni kim qabul qilgani (admin F.I.Sh.).</summary>
    public string DecidedBy { get; set; } = string.Empty;

    /// <summary>
    /// Buyurtma QAYERDAN kelgani: <c>"bot"</c> (Telegram, mijozning o'zi) | <c>"manual"</c>
    /// (markazda admin/kassir qo'lda sotgan). Qo'lda sotuvda <see cref="ChatId"/> = 0 bo'ladi —
    /// mijozga Telegram xabari YUBORILMAYDI (yuboriladigan chat yo'q).
    /// </summary>
    public string Source { get; set; } = "bot";
    /// <summary>KARTA to'lovida karta raqamining OXIRGI 4 raqami ("1234"). To'liq raqam
    /// HECH QACHON saqlanmaydi (<see cref="IntellectCRM.Application.Services.PaymentFields"/>
    /// bilan bir xil siyosat). Naqdda / botdan kelgan buyurtmada bo'sh.</summary>
    public string? CardLast4 { get; set; }
    /// <summary>KARTA to'lovi HAQIQATAN qilingan vaqt ("HH:mm"). Sana — <see cref="CreatedAt"/>.
    /// Moliyadagi <c>FinanceTransaction.PaidTime</c> bilan bir xil format.</summary>
    public string? PaidTime { get; set; }

    // -------------------------------------------------------------------------------------
    //  NASIYA (PaymentMethod = "credit") — kitob BERILDI, pul KEYIN olinadi
    //
    //  DIQQAT: nasiya sotuvda ham buyurtma odatdagidek TASDIQLANADI (Status="approved") va
    //  qoldiqdan ayiriladi — kitob mijozning qo'lida. Farqi faqat PULDA: pul olinmaguncha
    //  `PaidAt` bo'sh turadi va sotuv "qarz" bo'lib sanaladi. Kassir pulni olgach
    //  "Tasdiqlash" bosadi → `PaidAt`/`PaidBy`/`SettledMethod` to'ldiriladi va summa
    //  to'lovlarga (tushumga) qo'shiladi. Shu sababdan "to'landimi" savoli `Status` bilan
    //  EMAS, `BookSalesService.IsPaid` bilan javob beriladi.
    // -------------------------------------------------------------------------------------

    /// <summary>NASIYA: pulni qaytarish uchun va'da qilingan sana (ixtiyoriy). Bu sanadan
    /// keyin to'lanmagan nasiya "muddati o'tgan" bo'lib ajratiladi.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Pul HAQIQATAN olingan payt. Naqd/kartada — tasdiqlangan vaqt; nasiyada —
    /// kassir "To'landi" bosgan payt. Bo'sh = pul hali olinmagan (faqat nasiyada bo'ladi).</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Nasiya to'lovini kim qabul qilgani (admin/kassir F.I.Sh.).</summary>
    public string PaidBy { get; set; } = string.Empty;

    /// <summary>NASIYA qanday yopilgani: "cash" | "card". Nasiya bo'lmagan yoki hali
    /// to'lanmagan buyurtmada bo'sh. Karta bo'lsa <see cref="CardLast4"/> ham to'ldiriladi.</summary>
    public string? SettledMethod { get; set; }

    // -------------------------------------------------------------------------------------
    //  QAYTARISH (vozvrat) — mijoz kitobni qaytarib berdi
    //
    //  DIQQAT: qaytarish buyurtma HOLATINI o'zgartirmaydi (u "approved" bo'lib qoladi) —
    //  chunki qaytarish QISMAN ham bo'ladi (3 dona sotilib, 1 tasi qaytarilishi mumkin).
    //  Shu sababdan "qancha sotildi / qancha pul qoldi" savoliga `Status` emas,
    //  `BookSalesService.NetQty` / `NetTotal` javob beradi — barcha hisobotlar SOF
    //  (qaytarilgani ayirilgan) qiymat bilan ishlaydi.
    //
    //  Qaytarilgan dona OMBORGA QAYTADI (`BookStockMove`, Reason="return"), pul esa faqat
    //  ALLAQACHON OLINGAN bo'lsa qaytariladi: to'lanmagan nasiyada kitob qaytsa pul
    //  chiqmaydi — shunchaki qarz kamayadi.
    // -------------------------------------------------------------------------------------

    /// <summary>Shu buyurtmadan JAMI qaytarilgan dona (0 = qaytarilmagan). Bir necha marta
    /// qisman qaytarilsa qo'shilib boradi; <see cref="Qty"/> dan oshmaydi.</summary>
    public int ReturnedQty { get; set; }

    /// <summary>Oxirgi qaytarish payti (bo'sh = umuman qaytarilmagan).</summary>
    public DateTime? ReturnedAt { get; set; }

    /// <summary>Qaytarishni kim qabul qilgani (admin/kassir F.I.Sh.).</summary>
    public string ReturnedBy { get; set; } = string.Empty;

    /// <summary>Qaytarish sababi (kassir yozadi) — oxirgi qaytarishniki.</summary>
    public string ReturnReason { get; set; } = string.Empty;

    /// <summary>Mijozga HAQIQATAN qaytarilgan pul (jami). To'lanmagan nasiyada 0 bo'ladi —
    /// u yerda pul umuman olinmagan, faqat qarz kamayadi.</summary>
    public decimal RefundedAmount { get; set; }
}

/// <summary>
/// Botdagi buyurtma bergan chatning VAQTINCHALIK holati (bitta chatda bitta faol savdo sessiyasi).
/// <see cref="TestBotSession"/> bilan bir xil g'oya — bosqichma-bosqich savol-javob uchun.
/// </summary>
public class BookBotSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Telegram chat id (unikal — chatda bitta sessiya).</summary>
    public long ChatId { get; set; }
    /// <summary>Bosqich: "qty" (sonini kutamiz) | "pay" (to'lov turini kutamiz) |
    /// "receipt" (chek rasmini kutamiz).</summary>
    public string Step { get; set; } = "qty";
    public string BookId { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
    /// <summary>Tanlangan to'lov turi ("cash" | "card"), hali tanlanmagan bo'lsa bo'sh.</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/* =====================================================================================
 *  KARYERA (Intellect Career) — ishga qabul moduli
 *  Alohida Telegram bot (CAREER_BOT_TOKEN) + Mini App (`/vakansiya`, statik HTML/Bootstrap).
 *  Nomzod: Biz haqimizda / Vakansiyalar / Arizalarim. Admin: "Boshqaruv → Vakansiyalar".
 * ===================================================================================== */

/// <summary>
/// "Biz haqimizda" — karyera Mini App'ining birinchi bo'limi. Bitta qator (CenterMeta kabi
/// singleton): kimmiz, manzil, aloqa va ijtimoiy tarmoqlar. Admin "Boshqaruv → Vakansiyalar →
/// Biz haqimizda" bo'limidan to'ldiradi.
/// </summary>
public class CareerAbout
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Sahifa sarlavhasi (masalan "Intellect o'quv markazi").</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Bir-ikki jumlalik shior/qisqa tanishtiruv (sarlavha ostida).</summary>
    public string Tagline { get; set; } = string.Empty;
    /// <summary>Kimmiz — asosiy matn (bir necha xatboshi bo'lishi mumkin).</summary>
    public string About { get; set; } = string.Empty;
    /// <summary>Nega biz bilan ishlash kerak — imtiyozlar (har qatorda bittadan).</summary>
    public string Benefits { get; set; } = string.Empty;
    /// <summary>Logotip (`/uploads/...` yoki tashqi URL).</summary>
    public string LogoUrl { get; set; } = string.Empty;

    /* ---------- Manzil ---------- */
    public string Address { get; set; } = string.Empty;
    /// <summary>Mo'ljal ("Metro yonida", "3-qavat" va h.k.).</summary>
    public string Landmark { get; set; } = string.Empty;
    /// <summary>Xaritaga havola (Yandex/Google Maps) — "Xaritada ochish" tugmasi.</summary>
    public string MapUrl { get; set; } = string.Empty;
    /// <summary>Ish vaqti ("Du–Sh, 09:00–18:00").</summary>
    public string WorkTime { get; set; } = string.Empty;

    /* ---------- Aloqa ---------- */
    public string Phone { get; set; } = string.Empty;
    public string Phone2 { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /* ---------- Ijtimoiy tarmoqlar (bo'sh bo'lsa ko'rsatilmaydi) ---------- */
    public string Telegram { get; set; } = string.Empty;
    public string Instagram { get; set; } = string.Empty;
    public string Facebook { get; set; } = string.Empty;
    public string Youtube { get; set; } = string.Empty;
    public string Tiktok { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Vakansiya — bo'sh ish o'rni. <see cref="Status"/> "active" bo'lsa Mini App'dagi
/// "Vakansiyalar" bo'limida ko'rinadi; "archived" bo'lsa ko'rinmaydi, lekin unga tushgan
/// arizalar saqlanib qoladi (o'chirilmaydi — tarix buzilmasin).
/// </summary>
public class Vacancy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Lavozim nomi ("Ingliz tili o'qituvchisi").</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Bo'lim/yo'nalish ("O'quv bo'limi", "Marketing").</summary>
    public string Department { get; set; } = string.Empty;
    /// <summary>Bandlik turi: "full" (to'liq) | "part" (yarim) | "shift" (smenali) | "remote" (masofaviy).</summary>
    public string EmploymentType { get; set; } = "full";
    /// <summary>Ish joyi / filial ("Qo'qon, Markaziy filial").</summary>
    public string Location { get; set; } = string.Empty;

    /* ---------- Maosh ---------- */
    public decimal SalaryFrom { get; set; }
    public decimal SalaryTo { get; set; }
    /// <summary>Raqam o'rniga (yoki qo'shimcha) ko'rsatiladigan izoh — "kelishilgan holda".</summary>
    public string SalaryNote { get; set; } = string.Empty;

    /* ---------- Matnlar (har qatorda bitta band) ---------- */
    /// <summary>Qisqacha tavsif — ro'yxatda ham ko'rinadi.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Talablar.</summary>
    public string Requirements { get; set; } = string.Empty;
    /// <summary>Vazifalar.</summary>
    public string Responsibilities { get; set; } = string.Empty;
    /// <summary>Shart-sharoitlar.</summary>
    public string Conditions { get; set; } = string.Empty;

    /// <summary>Holat: "active" (faol — ilovada ko'rinadi) | "archived" (arxivlangan).</summary>
    public string Status { get; set; } = "active";
    /// <summary>Ariza qabul qilish oxirgi sanasi ("yyyy-MM-dd", ixtiyoriy). O'tib ketsa ilovada
    /// "muddati tugagan" deb ko'rsatiladi va yangi ariza qabul qilinmaydi.</summary>
    public string Deadline { get; set; } = string.Empty;
    /// <summary>Ro'yxatdagi tartibi (kichigi tepada).</summary>
    public int Order { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string ArchivedAt { get; set; } = string.Empty;
    public string ArchivedBy { get; set; } = string.Empty;
}

/// <summary>
/// Nomzod arizasi — Mini App'dagi forma orqali tushadi (F.I.Sh., telefon, tajriba, motivatsion
/// xat, CV fayli). Bosqichi <see cref="Status"/>da; har o'zgarish <see cref="JobApplicationEvent"/>
/// ga yoziladi va nomzodga karyera boti orqali xabar ketadi.
/// </summary>
public class JobApplication
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Ko'rsatiladigan ketma-ket raqam (#1, #2 ...).</summary>
    public int Number { get; set; }
    public string VacancyId { get; set; } = string.Empty;
    /// <summary>Vakansiya nomi nusxasi (vakansiya keyin o'zgarsa ham ariza o'qiladi).</summary>
    public string VacancyTitle { get; set; } = string.Empty;

    /* ---------- Telegram (karyera boti) ---------- */
    /// <summary>Ariza yuborgan chat — bosqich o'zgarganda shu yerga xabar ketadi.</summary>
    public long ChatId { get; set; }
    public string TgUsername { get; set; } = string.Empty;

    /* ---------- Nomzod ---------- */
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    /// <summary>Ish tajribasi (erkin matn).</summary>
    public string Experience { get; set; } = string.Empty;
    /// <summary>Motivatsion xat.</summary>
    public string Motivation { get; set; } = string.Empty;
    /// <summary>CV fayli (`/uploads/...`, faqat PDF).</summary>
    public string CvUrl { get; set; } = string.Empty;
    /// <summary>CV faylining asl nomi (adminga ko'rsatish uchun).</summary>
    public string CvName { get; set; } = string.Empty;

    /* ---------- Bosqich ---------- */
    /// <summary>Bosqich kaliti — <c>CareerStages</c> katalogidan:
    /// "new" | "review" | "interview" | "trial" | "hired" | "rejected".</summary>
    public string Status { get; set; } = "new";
    /// <summary>Oxirgi bosqich izohi — nomzod ilovada shuni ko'radi (suhbat vaqti, rad sababi).</summary>
    public string StatusNote { get; set; } = string.Empty;
    public string StatusChangedAt { get; set; } = string.Empty;
    public string StatusChangedBy { get; set; } = string.Empty;
    /// <summary>Faqat ADMIN ko'radigan ichki izoh (nomzodga ko'rinmaydi).</summary>
    public string AdminNote { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Ariza bosqichining har bir o'zgarishi (nomzod "Arizalarim"da tarix sifatida ko'radi).</summary>
public class JobApplicationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ApplicationId { get; set; } = string.Empty;
    /// <summary>Yangi bosqich kaliti.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Izoh (nomzodga ko'rinadi).</summary>
    public string Note { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Kim o'zgartirgani (admin F.I.Sh. yoki "Nomzod").</summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>Karyera botiga /start bosgan foydalanuvchi (statistika + xabar yuborish uchun).</summary>
public class CareerBotUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Telegram chat id (unikal).</summary>
    public long ChatId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Botga ulashgan telefon raqami (ixtiyoriy — forma uni oldindan to'ldiradi).</summary>
    public string Phone { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
}

/* =================================================================================================
 *  BOG'LANISH KERAK (follow-up navbati) — O'quvchilar bo'limi ostidagi modul.
 *
 *  Oqim: o'quvchi profilidagi "⋮" → "Bog'lanish kerak" → SABAB tanlanadi → o'quvchi NAVBATGA
 *  tushadi. Operator navbatdan bog'lanadi, "javobi nima dedi"ni yozadi va keyingi qadamni
 *  tanlaydi: hal bo'ldi / qayta qo'ng'iroq (sana bilan) / bog'lanib bo'lmadi.
 *
 *  Bosqich va natija KALITLARI — `ContactService` da (yagona katalog). Bu yerda ular faqat
 *  matn sifatida saqlanadi.
 * ============================================================================================== */

/// <summary>
/// BITTA "bog'lanish kerak" TALABI (case). Bir o'quvchida bir vaqtda faqat BITTA ochiq talab
/// bo'ladi — aks holda navbat bir xil odam bilan to'lib ketardi (<c>ContactService.OpenStatuses</c>).
/// </summary>
public class ContactRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Qaysi o'quvchi (<see cref="Student"/>.Id).</summary>
    public string StudentId { get; set; } = string.Empty;
    /// <summary>O'quvchi F.I.Sh — SNAPSHOT. Hisobotlar o'quvchi arxivlansa/nomi o'zgarsa ham
    /// buzilmasin (BookOrder bilan bir xil konvensiya).</summary>
    public string StudentName { get; set; } = string.Empty;

    /// <summary>Tanlangan sabab (<see cref="ActionReason"/>.Id, kategoriya "contact"). Bo'sh bo'lishi mumkin.</summary>
    public string ReasonId { get; set; } = string.Empty;
    /// <summary>Sabab matni — SNAPSHOT (sabablar katalogi keyin tahrirlansa tarix o'zgarmasin).</summary>
    public string ReasonLabel { get; set; } = string.Empty;
    /// <summary>Talab ochilganda yozilgan qo'shimcha izoh.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Holat: new | callback | done | failed (<c>ContactService.Statuses</c>).</summary>
    public string Status { get; set; } = ContactStatuses.New;

    /// <summary>QAYTA QO'NG'IROQ sanasi ("yyyy-MM-dd"). Faqat <c>Status=="callback"</c> da to'ladi;
    /// bugundan oldin bo'lsa — "muddati o'tgan" (navbatda qizil).</summary>
    public string DueDate { get; set; } = string.Empty;

    /// <summary>Nechta bog'lanish urinishi bo'lgan (<see cref="ContactAttempt"/> "contact" turi).
    /// Ro'yxatda ko'rsatish uchun denormalizatsiya — har qatorga alohida so'rov ketmasin.</summary>
    public int AttemptCount { get; set; }
    /// <summary>Oxirgi javob matni ("javobi nima dedi") — ro'yxatda ko'rinadi.</summary>
    public string LastResponse { get; set; } = string.Empty;
    /// <summary>Oxirgi amalni bajargan xodim F.I.Sh.</summary>
    public string LastActorName { get; set; } = string.Empty;
    /// <summary>Oxirgi amal vaqti (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string LastActionAt { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Talabni ochgan xodim F.I.Sh.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Yakunlangan vaqt (ISO). Bo'sh = hali ochiq.</summary>
    public string ClosedAt { get; set; } = string.Empty;
    /// <summary>Yakunlagan xodim F.I.Sh.</summary>
    public string ClosedBy { get; set; } = string.Empty;
}

/// <summary>
/// Talab bo'yicha BITTA hodisa: ochilishi, bog'lanish urinishi, bosqich o'zgarishi yoki izoh.
/// "Kim qaysi bosqichga oldi, natijasi qanday bo'ldi" savoliga AYNAN shu jadval javob beradi —
/// hisobotlar ham shundan hisoblanadi.
/// </summary>
public class ContactAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Hisobotlarni talabga JOIN qilmasdan yig'ish uchun takrorlangan (denormalizatsiya).</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Tur: created (talab ochildi) | contact (bog'lanildi) | note (izoh) | reopen (qayta ochildi).</summary>
    public string Type { get; set; } = ContactAttemptTypes.Contact;

    /// <summary>Bog'lanish NATIJASI: answered | no_answer | busy | wrong_number | other
    /// (<c>ContactService.Results</c>). Faqat <c>Type=="contact"</c> da to'ladi.</summary>
    public string Result { get; set; } = string.Empty;
    /// <summary>"Javobi nima dedi" — erkin matn. Modulning asosiy ma'lumoti.</summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Shu hodisadan KEYINGI holat (new | callback | done | failed) — "kim qaysi bosqichga oldi".</summary>
    public string NextStatus { get; set; } = string.Empty;
    /// <summary>Qayta qo'ng'iroq sanasi ("yyyy-MM-dd"), <c>NextStatus=="callback"</c> bo'lsa.</summary>
    public string DueDate { get; set; } = string.Empty;

    /// <summary>Bajargan xodim (AppUser.Id) — hisobotda xodimni ANIQ ajratish uchun.</summary>
    public string ActorId { get; set; } = string.Empty;
    /// <summary>Bajargan xodim F.I.Sh — SNAPSHOT (xodim o'chsa ham hisobot buzilmasin).</summary>
    public string ActorName { get; set; } = string.Empty;
    /// <summary>Vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Kun ("yyyy-MM-dd") — KUNLIK hisobot AYNAN shu ustun bo'yicha guruhlanadi
    /// (ISO vaqtdan `Substring` qilib guruhlash SQLda indeksdan foydalana olmasdi).</summary>
    public string Date { get; set; } = string.Empty;
}

/// <summary>"Bog'lanish kerak" talabining holat kalitlari (entity default'i uchun — yorliqlar
/// va tartib <c>ContactService.Statuses</c> da).</summary>
public static class ContactStatuses
{
    public const string New = "new";
    public const string Callback = "callback";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary><see cref="ContactAttempt.Type"/> kalitlari.</summary>
public static class ContactAttemptTypes
{
    public const string Created = "created";
    public const string Contact = "contact";
    public const string Note = "note";
    public const string Reopen = "reopen";
}

/// <summary>
/// "BOG'LANISH KERAK" hisobotining AI tahlili (Gemini) — yozilgan SABABLAR, javob matnlari va
/// natijalar bo'yicha xulosa.
///
/// <para>⚠️ Boshqa AI tahlillardan FARQI — u DAVRGA bog'langan: hisobot sahifasida operator
/// kun/oy/oraliq tanlaydi va tahlil AYNAN o'sha davr uchun yaratiladi. Shu sabab kalit
/// <see cref="Date"/> emas, (<see cref="FromDate"/>, <see cref="ToDate"/>) juftligi bo'ladi;
/// "kuniga bir marta" cheklovi esa AYNI davr uchun BUGUN yaratilgan yozuv bo'yicha ishlaydi
/// (ya'ni bir kunda har xil davrlarni tahlil qilish mumkin, bitta davrni ikki marta emas).</para>
/// </summary>
public class ContactAiAnalysis
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Tahlil qilingan davr boshi ("yyyy-MM-dd").</summary>
    public string FromDate { get; set; } = string.Empty;
    /// <summary>Tahlil qilingan davr oxiri ("yyyy-MM-dd").</summary>
    public string ToDate { get; set; } = string.Empty;
    /// <summary>Tahlil YARATILGAN kun ("yyyy-MM-dd", Toshkent) — kuniga bir marta cheklovi shu bo'yicha.</summary>
    public string Date { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = AppClock.Iso();
    /// <summary>Ishlatilgan Gemini modeli.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Qisqa xulosa — tarix ro'yxatida ko'rinadi.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Umumiy ball (0-100) — tarix/badge uchun.</summary>
    public int OverallScore { get; set; }
    /// <summary>To'liq strukturali natija (JSON): { ai, metrics }.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

/* =================================================================================================
 *  YUZ BILAN KIRISH (face login) — o'quvchi MOBIL ILOVASIGA kirishda shaxsni tasdiqlash.
 *
 *  MUAMMO: o'quvchilar login/parolni bir-biriga berib yuboradi (do'sti uning nomidan kiradi).
 *  YECHIM: YANGI QURILMADA birinchi kirishda selfi so'raladi va yuz solishtiriladi.
 *
 *  ⚠️ MODEL SERVERDA ISHLAMAYDI (server 1 GB RAM — `FACE-DETEKT-PLAN.md` §2, §6). Yuz modeli
 *  TELEFONDA ishlaydi va serverga faqat VEKTOR (512 yoki 128 ta float32) yuboriladi; server esa
 *  kosinus bilan solishtiradi (oddiy matematika — ML kutubxonasi kerak emas).
 *
 *  ⚠️ MAXFIYLIK (`FACE-DETEKT-PLAN.md` §5): yuz vektori — BIOMETRIK ma'lumot, ustiga voyaga
 *  yetmaganlarniki, va baza zaxirasi Telegram'ga yuboriladi. Shu sabab:
 *    • selfi FAYLLARI cheklangan muddat saqlanadi — `CenterMeta.LoginFaceKeepChecks` dan
 *      oshgan eski urinishlar (yozuvi ham, fayli ham) O'CHIRILADI (`FaceLoginService.CleanupAsync`);
 *    • auditga selfi MANZILI hech qachon yozilmaydi (`.claude/rules/audit.md` qoidasi);
 *    • VEKTORLAR BAZADA SHIFRLANGAN (AES-256-GCM, `FaceVault`). Kalit — FAQAT `.env`
 *      (`FACE_VECTOR_KEY`), bazada saqlanmaydi. Ya'ni baza dump'i O'ZI yetmaydi. Kalit
 *      sozlanmagan bo'lsa modul UMUMAN yoqilmaydi (shifrlanmagan saqlash varianti YO'Q);
 *    • SELFI FAYLLARI `uploads/face/` ostida — bu papka zaxira arxividan CHIQARILGAN
 *      (docker-compose `backup` xizmatida `--exclude`) va statik yo'l bilan ham berilmaydi
 *      (`PrivateFolderFileProvider`), faqat admin endpointi orqali.
 *
 *  ⚠️ TIRIKLIK (liveness): server har urinishdan oldin bir martalik `FaceChallenge` (nonce +
 *  tasodifiy harakatlar) beradi. Bu bosma surat/ekrandagi rasmni yopadi, lekin oldindan
 *  yozilgan VIDEO yoki o'zgartirilgan APK'ga qarshi KAFOLAT EMAS (`FaceLiveness` izohi).
 * ============================================================================================== */

/// <summary>
/// O'quvchining ETALON yuz vektori — kirishda kelgan selfi shu bilan solishtiriladi.
/// Har o'quvchida BITTA (StudentId unikal).
/// </summary>
public class StudentFaceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="Student"/>.Id — UNIKAL (bir o'quvchi — bitta etalon).</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Yuz vektori — <b>SHIFRLANGAN</b> blob (<c>FaceVault.Protect</c>: AES-256-GCM,
    /// kalit faqat `.env` da). `float[]` sifatida saqlanmaydi: Npgsql'da `real[]`, SQLite'da JSON
    /// bo'lib ketardi, `byte[]` esa ikkala provayderda ham bir xil `bytea`/`BLOB`.
    /// ⚠️ Kalit almashsa blob OCHILMAYDI — bu holat "etalon yo'q" deb qaraladi (istisno emas).</summary>
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    /// <summary>Vektor o'lchami (odatda 512 yoki 128) — `Vector.Length / 4` ga teng bo'lishi shart.</summary>
    public int Dim { get; set; }

    /// <summary>Vektorni YARATGAN model nomi/versiyasi. Vektor faqat O'ZINI yaratgan model bilan
    /// taqqoslanadi — model almashsa etalon YAROQSIZ (`CenterMeta.LoginFaceModelVersion`).</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Etalon qayerdan keldi: <c>photo</c> — o'quvchining profil rasmiga mos kelgan selfidan
    /// (avtomatik), <c>admin</c> — admin qo'lda tasdiqlagan urinishdan.</summary>
    public string Source { get; set; } = "photo";

    /// <summary>Etalon olingan selfi manzili (`/uploads/...`) — admin ko'rishi uchun. Eski
    /// urinishlar tozalanganda ham bu fayl O'CHIRILMAYDI (u etalonning "dalili").</summary>
    public string SampleUrl { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Kirishdagi HAR bir yuz tekshiruvi urinishi — admin "kim, qachon, qaysi qurilmadan urindi va
/// nima uchun rad etildi" savoliga shundan javob oladi.
/// </summary>
public class LoginFaceCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string StudentId { get; set; } = string.Empty;
    /// <summary>Kirayotgan akkaunt (<see cref="AppUser"/>.Id) — qurilma ishonchi shu bo'yicha beriladi.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Vaqt (ISO "yyyy-MM-ddTHH:mm:ss").</summary>
    public string CreatedAt { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    /// <summary>So'rov IP'si (Cloudflare ortida CF-Connecting-IP).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Selfi manzili (`/uploads/...`). ⚠️ Bu manzil AUDITGA yozilmaydi.</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Kosinus o'xshashligi (0..1). Solishtirishgacha yetmagan urinishda (sifat past,
    /// etalon yo'q) — <c>null</c>.</summary>
    public double? Score { get; set; }

    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>approved | rejected | pending (<c>FaceLoginService</c> konstantalari).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>O'zbekcha sabab — foydalanuvchiga ham, admin ro'yxatiga ham SHU matn ko'rsatiladi
    /// (matnlar yagona joyda: <c>FaceMatch</c>).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Klient hisoblagan sifat ko'rsatkichlari (JSON): sharpness/brightness/faceRatio/
    /// yaw/roll/eyesOpen/faces.</summary>
    public string Quality { get; set; } = string.Empty;

    /// <summary>Shu urinishdagi selfi vektori — <b>SHIFRLANGAN</b> (<c>FaceVault.Protect</c>,
    /// etalon bilan bir xil format). ⚠️ KERAK: <c>pending</c> urinishni admin tasdiqlaganda AYNAN
    /// shu vektor etalon bo'ladi — busiz "tasdiqlash" amalga oshmaydi.
    /// Eski urinishlar tozalanganda vektor ham yozuv bilan birga o'chadi.</summary>
    public byte[]? Vector { get; set; }
    public int Dim { get; set; }

    /// <summary>ILOVA HAQIQIYLIGI xulosasi (<c>AppAttestation.Code</c>):
    /// <c>ok</c> | <c>failed</c> | <c>unavailable</c> | <c>notConfigured</c>.
    /// Sozlama o'chiq bo'lsa ham YOZILADI — admin "qancha urinish o'zgartirilgan ilovadan
    /// keladi" ni ko'rib, majburiy qilishga qaror qila olsin.</summary>
    public string Attested { get; set; } = string.Empty;

    /// <summary>Attestation xulosasining qisqa sababi (masalan "paket nomi mos emas", "timeout").
    /// ⚠️ Maxfiy qiymat (token) yozilmaydi.</summary>
    public string AttestReason { get; set; } = string.Empty;
}

/// <summary>
/// BIR MARTALIK TIRIKLIK CHAQIRUVI (challenge) — server har selfi urinishidan OLDIN beradi:
/// tasodifiy <c>nonce</c> + TASODIFIY harakatlar ketma-ketligi (<c>FaceLiveness</c>).
///
/// <para><b>Nega kerak?</b> Nonce'siz o'zgartirilgan ilova bir marta ushlangan (yoki yasalgan)
/// so'rovni QAYTA-QAYTA yuboraverardi. Endi har urinish o'z nonce'i bilan bo'ladi va nonce
/// BIR MARTA ishlatiladi (<see cref="UsedAt"/>).</para>
///
/// <para>⚠️ Nonce urinish MUVAFFAQIYATSIZ bo'lsa ham ISHLATILGAN deb belgilanadi — aks holda
/// hujumchi bitta nonce bilan cheksiz urinardi.</para>
/// </summary>
public class FaceChallenge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Chaqiruvni olgan akkaunt (<see cref="AppUser"/>.Id). Boshqa foydalanuvchining
    /// nonce'i qabul QILINMAYDI.</summary>
    public string UserId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Tasodifiy satr (base64url, 32 bayt) — UNIKAL. Ilova uni Play Integrity tokeniga
    /// ham qo'yadi (<c>AppAttestation.Judge</c> solishtiradi).</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>So'ralgan harakatlar JSON massivi, masalan <c>["blink","turn_left"]</c>.
    /// TARTIB muhim — javob aynan shu tartibda kelishi shart.</summary>
    public string ActionsJson { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Muddat (ISO) — <c>FaceLiveness.ChallengeTtlSeconds</c> (90 s).</summary>
    public string ExpiresAt { get; set; } = string.Empty;
    /// <summary>Ishlatilgan vaqt (ISO). Bo'sh/null = hali ishlatilmagan.</summary>
    public string? UsedAt { get; set; }
}

/// <summary>
/// ISHONCHLI QURILMA — bir marta yuz bilan tasdiqlangan telefon. Keyingi kirishlarda selfi
/// so'ralmaydi. Telefon yo'qolsa admin bekor qiladi (<see cref="RevokedAt"/>) va o'sha qurilmada
/// yana yuz so'raladi.
/// </summary>
public class TrustedDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="AppUser"/>.Id (o'quvchi akkaunti).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Ilova generatsiya qiladigan barqaror qurilma identifikatori.</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    /// <summary>Bekor qilingan vaqt (ISO). Bo'sh/null = ishonchli.</summary>
    public string? RevokedAt { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════
//                          MARKETING — INSTAGRAM AI AGENTI
// ═══════════════════════════════════════════════════════════════════════════════════════════
// Modul Instagram'dagi izoh va DM'larga AI bilan javob beradi, qiziqqan odamni LID'ga aylantiradi.
// Konvensiya (butun loyihadagi kabi): `Id` — string GUID; sana/vaqt — ISO SATR (`AppClock.Iso()`),
// `DateTime` ustun EMAS; "yo'q" qiymat — `""` (nullable faqat ma'nosi bor joyda: `IgConversation.LeadId`).
// Modul o'chirilgan holatda (`CenterMeta.InstagramEnabled == false`) hech qanday tashqi so'rov ketmaydi.

/// <summary>
/// Ulangan Instagram akkaunt (Instagram Login orqali OAuth). Jadval bir nechta qatorga tayyor
/// (kelajakda filial), lekin amalda bir vaqtda bitta faol akkaunt yetarli.
/// </summary>
public class IgAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Instagram professional akkaunt id — webhook'da "biznikimi" tekshiruvi shu bilan
    /// qilinadi (o'zimiz yozgan izoh/xabarga javob berib cheksiz halqaga tushmaslik uchun).</summary>
    public string IgUserId { get; set; } = string.Empty;

    /// <summary>
    /// APP-SCOPED id (<c>me.id</c>) — <see cref="IgUserId"/> (<c>me.user_id</c>) dan BOSHQA raqam.
    ///
    /// <para>⚠️ Webhook'da <c>from.id</c> / <c>sender.id</c> <b>ba'zan</b> biri, <b>ba'zan</b>
    /// ikkinchisi bo'lib keladi. Faqat bittasini saqlash — botning O'Z izohini begona deb bilib,
    /// unga javob yozib CHEKSIZ HALQAGA tushishining aynan sababi
    /// (<c>.claude/rules/marketing-instagram.md</c> §4). Ikkalasi ham OAuth paytida bir so'rovda
    /// olinadi, shuning uchun saqlash tekin.</para>
    ///
    /// <para>Eski (bu ustun qo'shilishidan oldin ulangan) akkauntlarda BO'SH bo'ladi — himoya
    /// o'shanda ham ishlaydi (<c>IgUserId</c> + <c>entry.id</c> + <c>username</c>), lekin
    /// akkauntni qayta ulash uni to'ldiradi.</para>
    /// </summary>
    public string AppScopedUserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfilePictureUrl { get; set; } = string.Empty;

    /// <summary>Uzoq muddatli (60 kunlik) kirish tokeni.
    /// <para>⚠️ Bu — "kalitlar FAQAT .env" qoidasidan ATAYIN chekinish: token OAuth orqali ISH
    /// VAQTIDA olinadi va 45-kunda avtomatik yangilanadi, ya'ni uni `.env` ga yozib bo'lmaydi.
    /// `INSTAGRAM_APP_SECRET` va `INSTAGRAM_VERIFY_TOKEN` esa avvalgidek `.env` da qoladi.</para>
    /// <para>⚠️ Qiymat HECH QACHON DTO'ga, javobga, logga va auditga tushmaydi — tashqariga faqat
    /// "ulangan / muddati N kun qoldi" holati chiqadi.</para></summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Token muddati (ISO). Fon xizmati shu sanaga 15 kundan kam qolganda yangilaydi.</summary>
    public string TokenExpiresAt { get; set; } = string.Empty;
    /// <summary>Oxirgi marta qachon yangilangani (ISO) — diagnostika ekrani uchun.</summary>
    public string TokenRefreshedAt { get; set; } = string.Empty;

    /// <summary>Webhook obunasi (`subscribed_apps`) muvaffaqiyatli qilinganmi. False bo'lsa
    /// hodisalar umuman kelmaydi — Sozlamalar sahifasi buni qizil holat sifatida ko'rsatadi.</summary>
    public bool WebhookSubscribed { get; set; }

    /// <summary>Akkaunt uzilganda (`disconnect`) qator O'CHIRILMAYDI, faqat `false` qilinadi —
    /// suhbatlar tarixi va analitika saqlanib qolsin.</summary>
    public bool IsActive { get; set; } = true;

    public string ConnectedAt { get; set; } = string.Empty;
    /// <summary>Kim ulagani (xodim ismi) — audit yozuvi bilan bir xil ism.</summary>
    public string ConnectedBy { get; set; } = string.Empty;

    /// <summary>FAQ tugmalari (<see cref="IgIceBreaker"/>) Meta'ga oxirgi marta MUVAFFAQIYATLI
    /// yuborilgan vaqt (ISO). Bo'sh = hali sinxronlanmagan. Sinxronlash BEST-EFFORT: yiqilsa
    /// CRUD baribir o'tadi, sabab esa <see cref="FaqSyncError"/> da ko'rinadi — aks holda
    /// "tugma saqlandi, lekin Instagram'da chiqmayapti" holati sababsiz qolardi.</summary>
    public string FaqSyncedAt { get; set; } = string.Empty;

    /// <summary>FAQ sinxronlashning OXIRGI xatosi (o'zbekcha). Bo'sh = muammo yo'q.
    /// Muvaffaqiyatli sinxron uni tozalaydi; <see cref="FaqSyncedAt"/> esa xatoda O'CHMAYDI
    /// (oxirgi muvaffaqiyat vaqti diagnostika uchun qimmatli).</summary>
    public string FaqSyncError { get; set; } = string.Empty;
}

/// <summary>
/// Webhook'dan kelgan XOM hodisa — <b>durable navbat</b>.
/// <para><b>Nega jadval?</b> Meta webhook javobini ~5 soniyada kutadi va kechiksa hodisani qayta
/// yuboradi. AI + Graph API chaqiruvlari esa bundan uzoq davom etadi. Shuning uchun controller
/// xom JSON'ni SHU jadvalga yozib DARHOL 200 qaytaradi, haqiqiy ish esa fon xizmatida bajariladi.
/// Fire-and-forget'da (`Task.Run`) ilova qayta ishga tushsa hodisa yo'qolib ketardi.</para>
/// </summary>
public class IgWebhookEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Dedup kaliti — <b>UNIKAL indeks</b>. Meta bitta hodisani bir necha marta yuborishi
    /// mumkin (javob kechikkanda qayta urinadi); unikal indeks bo'lmasa mijoz bir xil savoliga
    /// bir necha marta javob olardi. Kalit deterministik: izohda `comment_id`, DM'da `mid`,
    /// ikkalasi ham yo'q bo'lsa sender+vaqt+matn hash'i.</summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>Meta yuborgan payload — o'zgartirilmagan holda. Qayta ishlash xato bersa hodisani
    /// shu yerdan qayta o'ynatish mumkin.</summary>
    public string RawJson { get; set; } = string.Empty;

    /// <summary>`pending` | `done` | `failed` | `skipped` (`IgConst.Ev*`). Indekslanadi —
    /// fon xizmati har bir sikl aynan `pending` qatorlarni tanlab oladi.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Nechta marta urinilgani. 3 ga yetganda `failed` — cheksiz sikl bo'lmasin.</summary>
    public int Attempts { get; set; }

    /// <summary>Oxirgi xato matni (o'zbekcha) — diagnostika ekranida ko'rinadi.</summary>
    public string Error { get; set; } = string.Empty;

    public string ReceivedAt { get; set; } = string.Empty;
    public string ProcessedAt { get; set; } = string.Empty;
}

/// <summary>
/// Bitta Instagram foydalanuvchisi bilan suhbat — <b>izoh ham, DM ham BIRGA</b>.
/// <para>Nega birga? Odam avval post ostiga "narxi qancha?" deb yozadi, keyin DM'ga o'tadi.
/// Ikkita alohida yozuv bo'lsa operator bir odamning ikki yarim suhbatini ko'rardi va AI
/// kontekstni yo'qotardi.</para>
/// </summary>
public class IgConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Suhbatdoshning Instagram id'si — indekslanadi (har kiruvchi hodisada suhbat
    /// aynan shu bo'yicha topiladi).</summary>
    public string IgUserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>`bot` = AI javob beradi · `operator` = pauza (odam yozyapti) · `closed` = yopilgan.</summary>
    public string Status { get; set; } = "bot";

    /// <summary>Operator pauzasi qachongacha (ISO). Bo'sh = pauza yo'q.
    /// <para><b>Nega muddat bilan?</b> Xodim suhbatga qo'lda kirsa (yoki biz yuborgan xabar
    /// "echo" bo'lib qaytsa) bot darhol jim bo'lishi kerak — aks holda mijoz odam va bot
    /// javoblarini aralash olardi. Ammo pauza abadiy qolsa suhbat jimgina "o'lik" bo'lib
    /// qolardi va hech kim sezmasdi, shuning uchun u O'ZI tugaydi.</para></summary>
    public string OperatorPausedUntil { get; set; } = string.Empty;

    /// <summary>Oxirgi KIRUVCHI xabar vaqti (ISO) — Instagram'ning <b>24 soatlik javob oynasi</b>
    /// aynan shundan hisoblanadi. Oyna yopiq bo'lsa DM yuborib bo'lmaydi (Meta rad etadi).</summary>
    public string LastInboundAt { get; set; } = string.Empty;
    public string LastOutboundAt { get; set; } = string.Empty;

    /// <summary>Ro'yxat uchun DENORMALIZATSIYA — inbox chizishda har suhbat uchun alohida
    /// "oxirgi xabar" so'rovi (N+1) qilinmasin.</summary>
    public string LastMessageText { get; set; } = string.Empty;
    public int MessageCount { get; set; }

    /// <summary>Operator hali ochib ko'rmagan.</summary>
    public bool Unread { get; set; } = true;

    /// <summary>ODAM ARALASHUVI KERAK: mijoz operatorni so'radi, AI ishlamadi yoki 24 soatlik
    /// oyna yopilib javob yubora olmadik. Inbox'da qizil chip bilan tepaga chiqadi.</summary>
    public bool NeedsOperator { get; set; }
    /// <summary>Nega operator kerakligi (o'zbekcha, qisqa) — operator ochmasdan sababini ko'rsin.</summary>
    public string NeedsOperatorReason { get; set; } = string.Empty;

    /// <summary>`uz-Cyrl` | `uz-Latn` | `ru` | `en` — mijoz qaysi tilda/yozuvda yozgan bo'lsa
    /// javob ham shunda bo'lishi uchun.</summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>Oxirgi aniqlangan niyat (`IgConst.Intents`).</summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>Qiziqish darajasi 0..100. <b>max(eski, yangi)</b> tarzida yangilanadi: odam bir
    /// marta "yozildim" desa, keyin "rahmat" deb yozgani uchun ball tushib ketmasin.</summary>
    public int LeadScore { get; set; }

    /// <summary>Bog'langan <see cref="Lead"/>.Id. <c>null</c> = hali lid yaratilmagan.
    /// Bu — modulda YAGONA nullable maydon: "lid yo'q" holati mazmunan mavjud va
    /// `""` bilan chalkashtirilmasligi kerak.</summary>
    public string? LeadId { get; set; }

    /// <summary>Suhbat REKLAMA izohidan boshlangan bo'lsa — o'sha e'lonning id'si. Bo'sh = organik.
    /// <para><b>Nega kerak?</b> ROI hisobotida reklama faqat forma lidini emas, izoh orqali kelgan
    /// lidni ham keltirgani ko'rinsin. Usiz reklama byudjeti "bekorga ketgan" bo'lib
    /// ko'rinardi.</para>
    /// <para>⚠️ Atributsiya <b>TAXMINIY</b>: Instagram Login yo'lidagi <c>comments</c> webhook'ida
    /// <c>ad_id</c> UMUMAN YO'Q (u faqat Facebook Login payloadida bor). Bog'lanish
    /// <c>media.id</c> ni <see cref="IgAdEntity.CreativeStoryId"/> bilan solishtirish orqali
    /// TIKLANADI: boostlangan organik postda ishlaydi, "dark post" va dinamik katalog
    /// reklamasida ishlamaydi. Shu sabab UI'da "taxminiy" deb belgilanishi SHART — aks holda
    /// bo'sh qiymat "reklamadan kelmagan" degan XATO xulosa berardi.</para></summary>
    public string AdId { get; set; } = string.Empty;

    /// <summary>Aniqlangan e'lonning kampaniyasi (<see cref="IgAdEntity.ParentId"/> zanjiri
    /// orqali). Denormalizatsiya: inbox ro'yxatida kampaniya nomini ko'rsatish uchun har suhbatda
    /// iyerarxiya bo'ylab yurish (N+1) kerak bo'lmasin. Bo'sh = aniqlanmagan.</summary>
    public string AdCampaignId { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Suhbatdagi bitta xabar (kirish yoki chiqish). AI kontekst tarixini shundan oladi.</summary>
public class IgMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="IgConversation"/>.Id — indekslanadi (suhbat lentasi shu bo'yicha o'qiladi).</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>`in` (mijozdan) | `out` (bizdan).</summary>
    public string Direction { get; set; } = "in";
    /// <summary>`comment` | `dm` | `private_reply` (`IgConst.Channel*`) — izohga ochiq javob,
    /// shaxsiy xabar yoki izohga yopiq javob.</summary>
    public string Channel { get; set; } = "dm";

    public string Text { get; set; } = string.Empty;
    /// <summary>Post id (izoh bo'lsa) — AI javobda post matnini (caption) hisobga oladi.</summary>
    public string MediaId { get; set; } = string.Empty;
    /// <summary>Izoh id — javob aynan shu izoh ostiga yoziladi.</summary>
    public string CommentId { get; set; } = string.Empty;
    /// <summary>Instagram tomonidagi xabar id (`mid`) — dedup va "echo" ni tanish uchun.</summary>
    public string IgMessageId { get; set; } = string.Empty;

    /// <summary>Kim yozgani: `"AI agent"` | xodim ismi | `@username`. Inbox lentasida ko'rinadi —
    /// operator qaysi javobni bot, qaysinisini odam yozganini adashtirmasin.</summary>
    public string ActorName { get; set; } = string.Empty;
    public bool IsAi { get; set; }
    public string AiIntent { get; set; } = string.Empty;
    public int AiScore { get; set; }

    /// <summary>Yuborishda xato bo'lsa — o'zbekcha matn. Xabar qatori BARIBIR saqlanadi:
    /// "javob ketmadi" ni operator ko'rishi kerak (jim yo'qolgan javob eng yomon holat).</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Bu xabar (odatda IZOH) qaysi reklama e'loni ostida yozilgani. Bo'sh = organik.
    /// <para>Suhbat darajasidagi <see cref="IgConversation.AdId"/> dan AYRI turadi ATAYIN:
    /// odam bir reklama ostida izoh yozib, keyin boshqasiga o'tishi mumkin, suhbat esa bitta
    /// bo'lib qoladi. Suhbatdagi qiymat — BIRINCHI teginish, xabardagi qiymat — o'sha izohning
    /// o'zi.</para>
    /// <para>⚠️ Atributsiya TAXMINIY (<see cref="IgConversation.AdId"/> izohiga qarang).</para></summary>
    public string AdId { get; set; } = string.Empty;

    /// <summary>Aniqlangan e'lonning kampaniyasi — ro'yxatda kampaniya nomini N+1 so'rovsiz
    /// ko'rsatish uchun denormalizatsiya. Bo'sh = aniqlanmagan.</summary>
    public string AdCampaignId { get; set; } = string.Empty;

    // ── E6.6 — JAVOB SIFATI JURNALI ──
    // Operator AI javobini tuzatib yuborsa — bu promptni yaxshilash uchun eng qimmatli ma'lumot:
    // "AI shunday dedi, odam esa shunday yozdi". Uch maydon FAQAT OPERATOR yozgan chiquvchi
    // xabarda to'ldiriladi (`IgQualityLog`), AI'ning o'z javobida bo'sh qoladi.

    /// <summary>Shu javobdan OLDIN AI taklif qilgan matn (bo'sh = taklif bo'lmagan).</summary>
    public string AiSuggestedText { get; set; } = string.Empty;

    /// <summary>Taklif qilingan javobning niyati (AI aniqlagan `intent`).
    /// <para>⚠️ Mavjud <see cref="AiIntent"/> ga YOZILMAYDI ATAYIN: analitika (`GET /analytics`)
    /// niyatlarni AYNAN o'sha ustun bo'yicha guruhlaydi va operator xabariga ham niyat yozilsa
    /// bitta suhbat ikki marta sanalib, mavjud hisobot buzilardi.</para></summary>
    public string AiSuggestedIntent { get; set; } = string.Empty;

    /// <summary>Operator taklifni O'ZGARTIRIBmi yubordi. `false` + <see cref="AiSuggestedText"/>
    /// to'la = taklif AYNAN qabul qilingan (bu ham qimmatli signal: "AI to'g'ri yozgan").</summary>
    public bool WasEdited { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Kalit so'z qoidasi — AI'dan OLDIN ishlaydi.
/// <para>Nega kerak? Ko'p savol bir xil ("narx", "manzil", "ish vaqti"). Ularga tayyor matn bilan
/// javob berish TEZ (AI kutilmaydi), ARZON (token sarflanmaydi) va ANIQ (AI o'ylab topmaydi).</para>
/// </summary>
public class IgAutoRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;

    /// <summary>Vergul bilan ajratilgan kalit so'zlar (masalan: `narx, narxi, qancha`).</summary>
    public string Keywords { get; set; } = string.Empty;
    /// <summary>`comment` | `dm` | `any` — qoida qaysi kanalda ishlaydi.</summary>
    public string Channel { get; set; } = "any";
    public string ReplyText { get; set; } = string.Empty;

    /// <summary>true — qoida ishlagach AI umuman chaqirilmaydi (mijoz ikkita javob olmasin).</summary>
    public bool StopAi { get; set; } = true;

    public bool IsActive { get; set; } = true;
    /// <summary>Tekshirish TARTIBI — birinchi mos kelgan qoida ishlaydi, shuning uchun aniqroq
    /// qoidalar yuqoriroq turadi.</summary>
    public int Order { get; set; }
    /// <summary>Nechta marta ishlagani — "qaysi qoida foydali" savoliga analitikada javob beradi.</summary>
    public int MatchCount { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// FAQ tugmasi (Instagram <b>ice breaker</b>) — Direct ochilganda mijozga ko'rinadigan
/// TAYYOR savol tugmasi.
/// <para><b>Nega kerak?</b> Ko'p mijoz nima so'rashni bilmay chiqib ketadi. Tayyor tugma
/// («Narxlar qancha?», «Manzil qayerda?») suhbatni boshlab beradi, javob esa saqlangan
/// matndan DARHOL yuboriladi — AI chaqirilmaydi (tez, arzon, aniq — <see cref="IgAutoRule"/>
/// bilan bir xil mulohaza, faqat mijoz yozmasdan bosadi).</para>
/// <para>⚠️ <b>Ko'pi bilan 4 ta</b> — bu Meta'ning ice breaker chegarasi (bitta locale'da
/// 4 tagacha savol), bizning tanlovimiz emas. 5-chisini saqlashga urinish 400 bilan rad
/// etiladi, Meta'ga esa faol tugmalardan faqat birinchi 4 tasi yuboriladi.</para>
/// <para>Tugma bosilganda webhook'ga <c>messaging_postbacks</c> hodisasi keladi, payload —
/// <c>FAQ:&lt;Id&gt;</c> (prefiks <c>IgConst.FaqPayloadPrefix</c>).</para>
/// </summary>
public class IgIceBreaker
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Tugmada ko'rinadigan savol. Meta chegarasi — 80 belgi.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>Tugma bosilganda yuboriladigan tayyor javob. DM chegarasi bilan bir o'lchovda
    /// (≤1000 — yuborishda baribir `TrimBytes` dan o'tadi).</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Tugmalar Direct'da shu tartibda ko'rinadi (kichigi yuqorida).</summary>
    public int Order { get; set; }

    /// <summary>O'chirilgan tugma Meta'ga yuborilmaydi va bosilsa (eski suhbatda qolgan bo'lsa)
    /// javob o'rniga oddiy qoida→AI oqimi ishlaydi.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Necha marta bosilgani — "qaysi savol mijozlarni qiziqtiradi" savoliga
    /// analitikada javob (<see cref="IgAutoRule.MatchCount"/> bilan bir xil maqsad).</summary>
    public int TapCount { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Bilim bazasi bo'lagi — <b>AI FAQAT shundan javob beradi</b>.
/// <para>Nega? Til modeli bo'sh joyni "o'ylab topish" bilan to'ldiradi: narx, chegirma va dars
/// jadvalini to'qib chiqarish esa markazning haqiqiy zarariga aylanadi. Prompt'da qat'iy qoida
/// bor — bu yerda yo'q ma'lumot so'ralsa AI operatorga o'tkazadi.</para>
/// </summary>
public class IgKnowledge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>Prompt'ga qo'shilish tartibi (muhimi yuqorida — prompt uzayib ketsa ham
    /// eng kerakli qism kesilmasin).</summary>
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Bo'lak QAYSI Word faylidan kelgani (asl fayl nomi). Qo'lda yozilgan bo'lakda BO'SH.
    ///
    /// <para><b>Nega kerak:</b> bilim bazasiga vaqti-vaqti bilan yangi hujjat yuklanadi va
    /// bir necha yuz bo'lak yig'iladi. Manba yozilmasa «narxlar hujjatining eski versiyasini
    /// olib tashlash» degan oddiy ish qo'lda, bo'lakma-bo'lak bajarilardi — amalda esa
    /// bajarilmasdi va bilim bazasida ESKI NARX qolib ketardi (AI eski narxni aytardi).
    /// Endi bitta amal: «shu fayldan kelganlarini o'chirish».</para>
    ///
    /// <para>⚠️ Faqat NOM saqlanadi — faylning O'ZI hech qayerga yozilmaydi (matn allaqachon
    /// <see cref="Content"/> da; nusxasini saqlash zaxira hajmini bekorga oshirardi).</para>
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    // ── E6.5 — RAG (semantik qidiruv) ──
    // Bilim bazasi o'sganda BUTUNLIGICHA promptga sig'maydi (`IgConst.KnowledgeLimit`) va oxiri
    // KESILADI — ya'ni aynan kerakli bo'lak tushib qolib, AI "bilmayman" deyishi mumkin edi.
    // Yechim: har bo'lakning ma'no vektori saqlanadi, savolga eng yaqin bir nechtasi tanlanadi.
    // ⚠️ Vektor bo'sh bo'lsa modul ESKI YO'L bilan ishlaydi (butun bilim bazasi) — RAG faqat
    // yaxshilash, majburiyat emas (`IgKnowledgeRag`).

    /// <summary>Ma'no vektori — JSON massiv (`[0.12,-0.03,...]`). Bo'sh = hali hisoblanmagan.
    /// <para>Nega JSON matn: loyihaga yangi kutubxona (`pgvector`) qo'shilmaydi, bilim bazasi esa
    /// o'nlab bo'lakdan iborat — kosinus oddiy C# hisobida yetarlicha tez.</para></summary>
    public string EmbeddingJson { get; set; } = string.Empty;

    /// <summary>Vektorni hisoblagan model nomi. Model almashsa vektor QAYTA hisoblanadi —
    /// har xil modelning vektorlari bir-biri bilan taqqoslanmaydi (o'lchami ham, ma'nosi ham
    /// boshqa fazoda).</summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>Vektor qachon hisoblangani (ISO). Diagnostika uchun: "nega bu bo'lak
    /// topilmayapti" savolida birinchi qaraladigan qiymat.</summary>
    public string EmbeddedAt { get; set; } = string.Empty;

    /// <summary>Vektor hisoblangan MATNNING hash'i (`IgKnowledgeRag.ContentHash`).
    /// <para>⚠️ <see cref="UpdatedAt"/> bilan solishtirish YETARLI EMAS: bilim bazasi bulk
    /// saqlanadi va faqat TARTIB o'zgarganda ham har bo'lakning `UpdatedAt`i yangilanadi —
    /// hash bo'lmasa har saqlashda BUTUN baza qaytadan embedding qilinardi (bekorga so'rov).</para></summary>
    public string EmbeddedHash { get; set; } = string.Empty;
}

/// <summary>
/// OAuth `state` — callback'ni tasdiqlash uchun BIR MARTALIK kalit (CSRF himoyasi).
/// <para>Nega baza? Callback'ni Meta boshqa so'rov sifatida yuboradi, ya'ni sessiya konteksti
/// yo'q. `state` bazada bo'lsa: (1) callback haqiqatan BIZ boshlagan oqimdanmi, (2) kim
/// boshlaganini bilamiz, (3) qayta ishlatib bo'lmaydi (<see cref="Used"/>).</para>
/// </summary>
public class IgOAuthState
{
    /// <summary>`state` qiymatining O'ZI (alohida ustun kerak emas — kalit shuning o'zi).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Oqimni boshlagan xodim ismi.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Muddat (ISO) — +15 daqiqa. Eskirgan `state` qabul qilinmaydi.</summary>
    public string ExpiresAt { get; set; } = string.Empty;
    /// <summary>Bir marta ishlatilgan — qayta ishlatishga urinish rad etiladi.</summary>
    public bool Used { get; set; }
}

// =================================================================================================
//  REKLAMA LIDLARI (Meta Lead Ads / "Instant Form")
// =================================================================================================
// Instagram/Facebook reklamasidagi FORMA to'ldirilganda lid CRM'ga avtomatik tushadi.
//
// ⚠️ Bu — modulning qolgan qismidan BOSHQA yo'l. Izoh va DM "Instagram API with Instagram Login"
// orqali keladi (`graph.instagram.com`), reklama lidi esa FACEBOOK PAGE obyektining `leadgen`
// webhook'i orqali (`graph.facebook.com`) — Meta'da bu ikkisi ayri mahsulot. Shu sababdan
// alohida token (Page Access Token) va alohida jadvallar kerak; nomlar esa modul bilan birga
// tursin uchun `Ig` prefiksida qoldirilgan.

/// <summary>
/// Reklama lidlari olinadigan FACEBOOK PAGE — Instagram akkaunt shunga bog'langan bo'ladi.
///
/// <para><b>Nega token bazada?</b> <see cref="IgAccount.AccessToken"/> bilan bir xil ATAYIN
/// chekinish: token ish vaqtida (admin qo'lda kiritganda yoki System User'dan olinganda)
/// paydo bo'ladi, ya'ni uni `.env` ga yozib bo'lmaydi. Qiymat HECH QACHON javobga, DTO'ga,
/// logga yoki auditga tushmaydi — tashqariga faqat "sozlangan / sozlanmagan" holati chiqadi.</para>
/// </summary>
public class IgAdPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Facebook Page id — webhook'dagi <c>entry.id</c> bilan solishtiriladi.</summary>
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;

    /// <summary>Page Access Token (`leads_retrieval` ruxsati bilan).
    /// <para>⚠️ System User tokeni MUDDATSIZ bo'ladi — tavsiya etilgani shu. Oddiy foydalanuvchi
    /// tokeni ~60 kunda o'ladi va lidlar JIMGINA kelmay qo'yadi, shuning uchun so'rov xatosi
    /// <see cref="LastError"/> ga yozilib, Sozlamalar sahifasida ko'rsatiladi.</para></summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Sahifa ilovaga `leadgen` maydoni bo'yicha obuna qilinganmi
    /// (<c>POST /{page-id}/subscribed_apps</c>). False bo'lsa hodisa umuman kelmaydi.</summary>
    public bool LeadgenSubscribed { get; set; }

    /// <summary>Uzilganda qator O'CHIRILMAYDI (kelgan lidlar tarixi saqlansin) — faqat
    /// <c>false</c> va token tozalanadi.</summary>
    public bool IsActive { get; set; } = true;

    public string ConnectedAt { get; set; } = string.Empty;
    public string ConnectedBy { get; set; } = string.Empty;

    /// <summary>Oxirgi lid qachon kelgani (ISO) — "ulangan, lekin lid kelmayapti" holatini
    /// Sozlamalar sahifasidan ko'rish uchun.</summary>
    public string LastLeadAt { get; set; } = string.Empty;

    /// <summary>Oxirgi xato (o'zbekcha) — masalan token muddati tugagani. Bo'sh = muammo yo'q.</summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// Reklama formasidan kelgan BITTA lid — Meta'dagi <c>leadgen_id</c> bo'yicha.
///
/// <para><b>Nega alohida jadval, faqat <see cref="Lead"/> yetmaydimi?</b> Uch sabab:
/// (1) <b>dedup</b> — <see cref="LeadgenId"/> unikal, ya'ni Meta hodisani qayta yuborsa ikkinchi
/// lid ochilmaydi (navbat yozuvlari 30 kunda tozalanadi, bu esa qoladi); (2) <b>analitika</b> —
/// "qaysi reklama/forma qancha lid berdi" savoliga `Lead` da javob yo'q; (3) <b>kanal tasnifi</b>
/// (<c>LeadOrigins</c>) — reklama lidini DM'dan kelgan liddan ajratish uchun ro'yxat kerak.</para>
///
/// <para>⚠️ Formada odatda FAQAT F.I.Sh. va telefon bo'ladi, lekin webhook payloadida reklama
/// identifikatorlari ham keladi — ular AYNAN shu yerda saqlanadi.</para>
/// </summary>
public class IgAdLead
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Meta bergan lid id — <b>UNIKAL indeks</b> (dedupning asosiy qavati).</summary>
    public string LeadgenId { get; set; } = string.Empty;

    public string PageId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    /// <summary>Forma nomi — lidning "qiziqqan yo'nalishi" sifatida ishlatiladi
    /// ("Yozgi IELTS intensiv"), chunki formaning o'zida kurs maydoni bo'lmasligi mumkin.</summary>
    public string FormName { get; set; } = string.Empty;

    public string AdId { get; set; } = string.Empty;
    public string AdName { get; set; } = string.Empty;
    public string AdsetId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string CampaignName { get; set; } = string.Empty;

    /// <summary><c>ig</c> | <c>fb</c> — reklama qaysi platformada ko'rsatilgan (Meta beradi).</summary>
    public string Platform { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Formaning BARCHA javoblari (xom JSON) — kelajakda forma maydoni qo'shilsa
    /// ma'lumot yo'qolmasin (ustun qo'shmasdan ham ko'rish mumkin).</summary>
    public string RawFieldsJson { get; set; } = string.Empty;

    /// <summary>CRM'dagi lid. Bo'sh bo'lishi MUMKIN — lid yaratishda xato bo'lsa yozuv baribir
    /// qoladi (<see cref="Error"/> bilan), ya'ni mijoz jimgina yo'qolmaydi.</summary>
    public string LeadId { get; set; } = string.Empty;
    /// <summary>Yangi lid ochildimi yoki mavjudiga qo'shildimi (takroriy murojaat).</summary>
    public bool IsNewLead { get; set; }

    /// <summary>Meta'dagi yaratilish vaqti (ISO) — hisobot AYNAN shu bo'yicha, qabul qilingan
    /// vaqt bo'yicha emas (navbat kechiksa kun chegarasi surilib ketardi).</summary>
    public string CreatedTime { get; set; } = string.Empty;
    public string ReceivedAt { get; set; } = string.Empty;

    /// <summary>Qayta ishlashdagi xato (o'zbekcha). Bo'sh = muvaffaqiyatli.</summary>
    public string Error { get; set; } = string.Empty;
}

// =================================================================================================
//  REKLAMA STATISTIKASI (Meta Ads Insights)
// =================================================================================================
// "Bu oyda reklamaga N so'm sarfladik, M ta lid keldi, bittasi K so'mga tushdi" degan savolga
// javob beradigan qatlam. Ads Manager lid SONINI biladi, lekin qaysi lid PUL TO'LAGANINI
// bilmaydi — CRM biladi, shuning uchun statistika mahalliy jadvallarda saqlanadi.
//
// ⚠️ Bu ham `graph.facebook.com` yo'li (reklama lidlari kabi), `graph.instagram.com` EMAS:
// Insights Ad Account obyektiga tegishli. Lekin token BOSHQA — reklama lidlari uchun Page
// tokeni (`leads_retrieval`), statistika uchun esa `ads_read` ruxsatli System User tokeni
// kerak. Page tokeni bilan Insights so'ralsa `OAuthException 190/200` chiqadi va sababini
// topish qiyin bo'ladi, shuning uchun token AYRI entity'da turadi.

/// <summary>
/// Statistika olinadigan Meta REKLAMA AKKAUNTI (Ad Account).
///
/// <para><b>Nega token bazada va UI'dan kiritiladi?</b> <see cref="IgAdPage"/> bilan AYNAN bir xil
/// naqsh: Business Manager'da System User yaratiladi, unga reklama akkaunti biriktiriladi va
/// <c>ads_read</c> ruxsati bilan MUDDATSIZ token generatsiya qilinadi. Bunday token `.env` ga
/// yozilmaydi (u ish vaqtida, admin qo'lida paydo bo'ladi) va OAuth oqimi ham kerak emas.
/// Qiymat HECH QACHON javobga, DTO'ga, logga yoki auditga tushmaydi — tashqariga faqat
/// "sozlangan / sozlanmagan" holati chiqadi.</para>
/// </summary>
public class IgAdAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Meta'dagi reklama akkaunti id — <b>"act_" PREFIKSI BILAN</b> saqlanadi
    /// (<c>act_1234567890</c>).
    /// <para>⚠️ Prefiks tashlab yuborilsa Graph so'rovi 404/`code 100` beradi, chunki so'rov yo'li
    /// aynan <c>/act_{id}/insights</c> ko'rinishida bo'ladi. Admin raqamni prefikssiz kiritishi
    /// juda ehtimolli, shuning uchun normalizatsiya SAQLASHDAN OLDIN qilinadi va bazada har doim
    /// prefiksli qiymat turadi — aks holda bir xil akkaunt ikki xil satr bo'lib, unikal indeks
    /// ham yordam bermasdi.</para></summary>
    public string AdAccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Valyuta ISO kodi (<c>USD</c> | <c>UZS</c>) — <c>GET /act_{id}?fields=currency</c>.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Minor unit uchun o'nlik xona soni: 2 = tiyin/sent, 0 = "zero-decimal" valyuta.
    /// <para>⚠️ <b>Bu qiymat Meta'dan KELMAYDI.</b> Ad Account tugunida <c>currency_offset</c>
    /// degan maydon YO'Q (u eskirgan Currency tugunida edi), so'ralsa butun so'rov `code 100`
    /// bilan rad etiladi. Offset bizning tomonda, valyuta kodidan sof funksiya bilan
    /// hisoblanadi; noma'lum kod uchun xavfsiz default — <b>2</b>. UZS ham 2.</para></summary>
    public int CurrencyOffset { get; set; } = 2;

    /// <summary>Akkauntning vaqt zonasi nomi (<c>Asia/Tashkent</c>).
    /// <para>⚠️ Kunlik statistikaning SANASI Meta tomonida AYNAN shu zonada kesiladi, bizning
    /// server zonasida emas. Sanani qayta hisoblashga urinish "bir kunlik siljish" xatosini
    /// keltirib chiqaradi (kecha sarflangan pul bugunga tushib qoladi), shuning uchun
    /// <see cref="IgAdInsight.StatDate"/> Meta bergan ko'rinishda YOZILADI, zona esa faqat
    /// ekranda tushuntirish uchun saqlanadi.</para></summary>
    public string TimezoneName { get; set; } = string.Empty;

    /// <summary>System User tokeni (<c>ads_read</c> ruxsati bilan).
    /// <para>⚠️ Javobga TUSHMAYDI — DTO'da faqat <c>tokenSet</c> bayrog'i qaytadi. Forma bo'sh
    /// yuborilsa mavjud token saqlanadi (akkaunt nomini tahrirlash uchun tokenni qayta yozish
    /// shart bo'lmasin).</para></summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Uzilganda qator O'CHIRILMAYDI (yig'ilgan statistika tarixi saqlansin) — faqat
    /// <c>false</c> va token tozalanadi.</summary>
    public bool IsActive { get; set; } = true;

    public string ConnectedAt { get; set; } = string.Empty;
    public string ConnectedBy { get; set; } = string.Empty;

    /// <summary>Oxirgi muvaffaqiyatli sinxronizatsiya (ISO). Bo'sh = hali umuman yuklanmagan
    /// (ya'ni birinchi ishga tushishda backfill qilinadi).</summary>
    public string LastSyncAt { get; set; } = string.Empty;

    /// <summary>Oxirgi xato (o'zbekcha) — token muddati tugagani, rate limit yoki ruxsat
    /// yetishmagani. Bo'sh = muammo yo'q. Sozlamalar sahifasida ko'rsatiladi: aks holda
    /// nosozlik "reklama ishlayapti, statistika yangilanmayapti" bo'lib ko'rinardi.</summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// Reklama iyerarxiyasining bitta tuguni: <b>kampaniya → reklama to'plami (adset) → e'lon (ad)</b>.
///
/// <para><b>Nega alohida jadval?</b> Insights faqat ID qaytaradi, NOM qaytarmaydi. Nomlarsiz
/// hisobot o'qib bo'lmaydigan raqamlar to'plamiga aylanadi, har hisobot uchun Meta'dan nom
/// so'rash esa rate limitni yeb qo'yardi. Shuning uchun iyerarxiya bir marta sinxronlanadi va
/// hisobot mahalliy JOIN bilan quriladi.</para>
///
/// <para>⚠️ Reklama Meta'da <b>o'chirilsa ham qator qoladi</b> — o'tgan oyning hisoboti
/// buzilmasligi kerak. "Hozir ishlayaptimi" savoliga <see cref="EffectiveStatus"/> javob
/// beradi.</para>
/// </summary>
public class IgAdEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Qaysi reklama akkauntiga tegishli (<c>act_...</c>).</summary>
    public string AdAccountId { get; set; } = string.Empty;

    /// <summary><c>campaign</c> | <c>adset</c> | <c>ad</c> — tugunning darajasi.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Meta'dagi id — <b>UNIKAL indeks</b> (qayta sinxronda upsert kaliti).
    /// <para>Uch daraja bitta jadvalda tursa ham to'qnashuv bo'lmaydi: Meta id'lari butun
    /// tizim bo'ylab yagona.</para></summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Ota tugun: adset → campaign, ad → adset. Kampaniyada bo'sh.
    /// <para>⚠️ Ataylab FOREIGN KEY EMAS: bolalar ota-onasidan oldin kelishi mumkin (Meta uchta
    /// alohida so'rovda beradi) va FK bo'lsa sinxronizatsiya tartibi buzilganda butun paket
    /// yiqilardi.</para></summary>
    public string ParentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Foydalanuvchi qo'ygan holat: <c>ACTIVE</c> | <c>PAUSED</c> | <c>DELETED</c>...</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>HAQIQIY holat — ota tugun pauzada bo'lsa yoki byudjet tugagan bo'lsa bu yerda
    /// ko'rinadi (<c>CAMPAIGN_PAUSED</c>, <c>ADSET_PAUSED</c>...). "Nega e'lon ACTIVE, lekin
    /// ko'rsatilmayapti" savoliga javob AYNAN shu ustunda.</summary>
    public string EffectiveStatus { get; set; } = string.Empty;

    /// <summary>Kampaniya maqsadi (<c>OUTCOME_LEADS</c>, <c>OUTCOME_ENGAGEMENT</c>...) — lid
    /// maqsadidagi kampaniyani boshqasidan ajratish uchun (CPL faqat lid kampaniyasida ma'noli).</summary>
    public string Objective { get; set; } = string.Empty;

    /// <summary>Kunlik byudjet — <b>MINOR UNIT</b> (tiyin/sent), butun son.
    /// <para>🔴 Meta'da ASSIMETRIYA bor: byudjet MINOR unit'da butun son bo'lib keladi
    /// (<c>5000</c> = 50.00 USD), <see cref="IgAdInsight.SpendMinor"/> manbasi bo'lgan
    /// <c>spend</c> esa MAJOR unit'da MATN bo'lib keladi (<c>"312.45"</c>). Ikkalasi bazada
    /// bir xil (minor) ko'rinishga keltiriladi — aks holda "sarf byudjetdan 100 barobar katta"
    /// kabi hisobotlar chiqardi.</para></summary>
    public long DailyBudgetMinor { get; set; }

    /// <summary>Umrbod byudjet — MINOR UNIT (yuqoridagi izohga qarang). Kunlik byudjet bilan
    /// odatda faqat bittasi to'ldirilgan bo'ladi.</summary>
    public long LifetimeBudgetMinor { get; set; }

    public string StartTime { get; set; } = string.Empty;
    /// <summary>Tugash vaqti (ISO). Bo'sh = tugash sanasi qo'yilmagan (uzluksiz).</summary>
    public string StopTime { get; set; } = string.Empty;

    /// <summary>Creative'ning <c>effective_object_story_id</c> — reklama ostidagi HAQIQIY
    /// media/post identifikatori.
    /// <para>Reklama izohlari atributsiyasi (E3) uchun kerak: izoh webhook'ida <c>ad_id</c>
    /// YO'Q, faqat <c>media.id</c> keladi. Shu ustun orqali "bu izoh qaysi reklamaga tegishli"
    /// savoliga javob topiladi.</para>
    /// <para>⚠️ Bu <b>TAXMINIY</b> atributsiya: boostlangan organik postda ishlaydi, "dark post"
    /// (chop etilmagan reklama) va dinamik katalog reklamasida ishlamaydi. Bo'sh bo'lishi
    /// normal.</para></summary>
    public string CreativeStoryId { get; set; } = string.Empty;

    /// <summary>Oxirgi marta qachon sinxronlangani (ISO).</summary>
    public string SyncedAt { get; set; } = string.Empty;
}

/// <summary>
/// BIR KUNLIK fakt: bitta tugun (kampaniya/adset/e'lon) uchun bitta sanadagi ko'rsatkichlar.
///
/// <para><b>Nega mahalliy nusxa?</b> (1) ROI hisoboti sarfni CRM'dagi to'lovlar bilan JOIN
/// qilishi kerak — buni Meta tomonida qilib bo'lmaydi; (2) Insights rate limiti qattiq, har
/// ekran ochilganda so'rov yuborib bo'lmaydi; (3) Meta ma'lumotni chegaralangan muddat saqlaydi,
/// biz esa yillik taqqoslash ko'rsatamiz.</para>
///
/// <para>⚠️ Qatorlar <b>UPSERT</b> qilinadi, faqat qo'shilmaydi: Meta oxirgi kunlarning
/// raqamlarini keyin ham tuzatadi (atributsiya kechikadi). Kalit —
/// <c>(Level, ExternalId, StatDate, Platform)</c> unikal indeksi; usiz har sinxronizatsiya
/// sarfni ikkilantirib yuborardi.</para>
/// </summary>
public class IgAdInsight
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string AdAccountId { get; set; } = string.Empty;

    /// <summary><c>campaign</c> | <c>adset</c> | <c>ad</c> — qaysi darajadagi fakt.
    /// <para>⚠️ Uch daraja bir jadvalda turadi, ya'ni hisobotda ular <b>QO'SHILMASLIGI</b> kerak:
    /// kampaniya qatori o'z e'lonlari yig'indisi bilan bir xil, birga sanalsa sarf ikki-uch
    /// barobar ko'rinardi. Har so'rov bitta darajani tanlaydi.</para></summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Tugunning Meta id'si — <see cref="IgAdEntity.ExternalId"/> bilan bog'lanadi
    /// (nom shu orqali topiladi).</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Sana <c>yyyy-MM-dd</c> — <b>reklama akkauntining vaqt zonasida</b>
    /// (<see cref="IgAdAccount.TimezoneName"/>).
    /// <para>⚠️ Meta bergan ko'rinishda YOZILADI, qayta hisoblanmaydi: konvertatsiya "bir kunlik
    /// siljish" xatosini keltirib chiqaradi va Ads Manager bilan raqamlar mos kelmay qoladi
    /// ("nega CRM'da kechagi sarf 0?").</para>
    /// <para>ISO vaqt EMAS, ataylab qisqa sana: guruhlash va oralik filtri AYNAN shu ustun
    /// bo'yicha ketadi va <c>Substring</c> bilan guruhlash indeksdan foydalana olmasdi.</para></summary>
    public string StatDate { get; set; } = string.Empty;

    /// <summary><c>instagram</c> | <c>facebook</c> | <c>all</c> — qaysi platformadagi kesim.
    /// <para><c>all</c> = kesimsiz (butun e'lon bo'yicha). Kesim bilan va kesimsiz qatorlar
    /// BIRGA saqlanadi (shuning uchun u unikal indeksning bir qismi), lekin hisobotda
    /// aralashtirib qo'shilmaydi — aks holda sarf ikkilanardi.</para></summary>
    public string Platform { get; set; } = "all";

    public long Impressions { get; set; }
    /// <summary>Erishilgan noyob odamlar soni.
    /// <para>⚠️ Reach kunlar bo'yicha <b>QO'SHILMAYDI</b> (bir odam ikki kun ko'rsa ikki marta
    /// sanaladi). Oylik reach kerak bo'lsa Meta'dan AYRI so'raladi; mahalliy yig'indi faqat
    /// "taxminiy" deb ko'rsatilishi mumkin.</para></summary>
    public long Reach { get; set; }
    /// <summary>Barcha bosishlar (like, izoh, profilga o'tish ham kiradi).</summary>
    public long Clicks { get; set; }
    /// <summary><c>inline_link_clicks</c> — faqat HAVOLA bosilgani. CTR va CPC hisoblarida
    /// odatda AYNAN shu ishlatiladi, <see cref="Clicks"/> emas.</summary>
    public long LinkClicks { get; set; }

    /// <summary>Sarf — <b>MINOR UNIT</b> (tiyin/sent).
    /// <para>🔴 Meta <c>spend</c> ni MAJOR unit'da MATN qilib beradi (<c>"312.45"</c>) —
    /// u <c>InvariantCulture</c> bilan o'qilib, <see cref="IgAdAccount.CurrencyOffset"/> ga
    /// ko'paytiriladi. O'zbek lokalida <c>decimal.Parse</c> nuqtani ajratuvchi deb o'qimasligi
    /// mumkin, ya'ni <c>"312.45"</c> → 31245 bo'lib ketardi.</para>
    /// <para>Butun sonda saqlanishining sababi: <c>double</c> da pul yig'indisi yaxlitlash
    /// xatosini to'playdi, hisobot esa tiyingacha to'g'ri bo'lishi kerak.</para></summary>
    public long SpendMinor { get; set; }

    /// <summary><c>onsite_conversion.lead_grouped</c> — Meta'ning INSTANT FORM lidlari
    /// (formani Meta o'zi ko'rsatgan). Reklama lidlari moduli aynan shularni oladi.</summary>
    public int LeadsOnsite { get; set; }

    /// <summary><c>offsite_conversion.fb_pixel_lead</c> — SAYTDAGI piksel lidi.
    /// <para>⚠️ <see cref="LeadsOnsite"/> bilan <b>QO'SHILMAYDI</b>: bitta odam ikkala hodisani
    /// ham keltirib chiqarishi mumkin (formani to'ldirdi, keyin saytga ham o'tdi) va yig'indi
    /// lidlar sonini shishirardi. Ular ayri ko'rsatiladi.</para></summary>
    public int LeadsPixel { get; set; }

    /// <summary><c>messaging_conversation_started_7d</c> — reklamadan keyin BOSHLANGAN
    /// yozishmalar (DM). "Xabar yuborish" maqsadidagi kampaniyaning asosiy natijasi.
    /// <para>⚠️ 7 kunlik atributsiya oynasi nom ichida — ya'ni bu son bir necha kun davomida
    /// ORQAGA qarab o'zgaradi, shuning uchun oxirgi kunlar qayta yuklanadi (upsert).</para></summary>
    public int MsgStarted { get; set; }

    /// <summary>Meta bergan XOM <c>actions</c> massivi (JSON).
    /// <para>Nega saqlanadi: kerakli hodisa turi keyinchalik o'zgaradi (Meta yangi
    /// <c>action_type</c> qo'shadi yoki eskisini o'chiradi), xom JSON bo'lsa ustun qo'shmasdan
    /// ham eski kunlarni qayta o'qib chiqish mumkin. Bo'sh massiv ham normal.</para></summary>
    public string ActionsJson { get; set; } = string.Empty;

    /// <summary>Meta qaytargan atributsiya sozlamasi (masalan <c>7d_click,1d_view</c>).
    /// <para>⚠️ Bu sozlama Meta tomonida O'ZGARISHI mumkin va o'zgargan zahoti eski raqamlar
    /// yangisi bilan taqqoslanmaydigan bo'lib qoladi. Saqlanmasa "nega konversiya birdan
    /// tushdi" savoliga hech qanday iz qolmasdi.</para></summary>
    public string AttributionSetting { get; set; } = string.Empty;

    /// <summary>Bu qator qachon olingani (ISO) — upsertda yangilanadi. "Ma'lumot qanchalik
    /// yangi" degan savol ekranda shu bo'yicha ko'rsatiladi.</summary>
    public string FetchedAt { get; set; } = string.Empty;
}

// =================================================================================================
//  KONTENT REJALASHTIRISH (Instagram Content Publishing)
// =================================================================================================
// ⚠️ Instagram'da NATIVE rejalashtirish YO'Q: `POST /{ig-user-id}/media` da
// `scheduled_publish_time` degan parametr mavjud emas, yaratilgan konteyner esa 24 soatdan
// keyin o'ladi. Demak navbat BIZDA: worker vaqt kelganini ko'radi → konteyner yaratadi →
// statusini so'raydi → `FINISHED` bo'lgach chop etadi. Konteynerni oldindan yaratib qo'yish
// TAQIQLANADI — u chop etishgacha eskirib qolishi mumkin.

/// <summary>
/// Rejalashtirilgan bitta post (feed rasm/video, Reels, Story yoki karusel).
///
/// <para>Navbat DB jadvalida — kesh yoki <c>Task.Run</c> emas: server qayta ishga tushganda
/// rejalashtirilgan post jimgina yo'qolib ketmasligi kerak.</para>
/// </summary>
public class IgScheduledPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><c>image</c> | <c>video</c> | <c>reels</c> | <c>story</c> | <c>carousel</c> —
    /// Meta'ga yuboriladigan konteyner turi va media talablari shundan kelib chiqadi
    /// (hajm, davomiylik, nisbat har turda BOSHQA).</summary>
    public string PostType { get; set; } = "image";

    /// <summary>Post matni (≤2200 belgi, ≤30 hashtag, ≤20 mention).
    /// <para>⚠️ Karuselda caption faqat OTA-ONA konteynerida ishlaydi — bolalarga berilgani
    /// jimgina e'tiborsiz qoldiriladi.</para></summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Media elementlari (JSON massiv): <c>[{url,type,coverUrl,thumbOffset,altText}]</c>.
    /// <para>Nega JSON, alohida jadval emas: element soni 1..10, ular faqat SHU post bilan birga
    /// o'qiladi va alohida qidirilmaydi — jadval qo'shish JOIN va migratsiya qo'shardi, foydasiz.</para>
    /// <para>🔴 <c>url</c> — <b>ochiq HTTPS</b> bo'lishi SHART: faylni Meta O'ZI yuklab oladi.
    /// Autentifikatsiya, IP cheklov yoki redirect ishlamaydi. Loyihadagi <c>/uploads</c> esa
    /// login ortida (`UploadsGuard`) — ya'ni oddiy upload manzilini bu yerga qo'yib bo'lmaydi.</para></summary>
    public string MediaJson { get; set; } = string.Empty;

    /// <summary>Qo'shimcha sozlamalar (JSON): <c>{shareToFeed,locationId,collaborators,audioName}</c>.
    /// <para>Ular tur bo'yicha har xil qo'llaniladi (masalan <c>shareToFeed</c> faqat Reels'da),
    /// ustun qilib ajratilsa jadvalda doim bo'sh turadigan maydonlar to'planardi.</para></summary>
    public string OptionsJson { get; set; } = string.Empty;

    /// <summary>Rejalashtirilgan vaqt (ISO) — <b>BIZNING navbat vaqti</b>, Meta'niki emas
    /// (§ yuqoridagi izoh). Worker shu vaqt kelganini ko'rib ishni boshlaydi.</summary>
    public string ScheduledAt { get; set; } = string.Empty;

    /// <summary><c>scheduled</c> | <c>processing</c> | <c>published</c> | <c>failed</c> |
    /// <c>cancelled</c>.
    /// <para><c>processing</c> ALOHIDA holat ekani muhim: konteyner yaratilgan, lekin hali chop
    /// etilmagan oraliq bor va u daqiqalar davom etadi. Usiz worker o'zi boshlagan ishni qayta
    /// boshlab, bitta post IKKI MARTA chop etilishi mumkin edi.</para></summary>
    public string Status { get; set; } = "scheduled";

    /// <summary>Meta yaratgan konteyner id (birinchi bosqich natijasi).
    /// <para>⚠️ Konteyner 24 soatdan keyin o'ladi — qayta urinishda ESKISINI ishlatib bo'lmaydi,
    /// yangisi yaratiladi.</para></summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>Konteynerning oxirgi holati: <c>IN_PROGRESS</c> | <c>FINISHED</c> |
    /// <c>ERROR</c> | <c>EXPIRED</c> | <c>PUBLISHED</c>. Faqat <c>FINISHED</c> da chop etiladi.</summary>
    public string ContainerStatus { get; set; } = string.Empty;

    /// <summary>Chop etilgandan keyingi media id.</summary>
    public string MediaId { get; set; } = string.Empty;
    /// <summary>Postning ochiq havolasi — admin natijani darhol ko'ra olsin.</summary>
    public string Permalink { get; set; } = string.Empty;

    /// <summary>Nechta marta urinilgani — cheksiz qayta urinishdan himoya (chegara oshsa
    /// <c>failed</c>). Chop etish limiti ham cheklangan resurs, uni bo'sh urinishlarga
    /// sarflab bo'lmaydi.</summary>
    public int Attempts { get; set; }

    /// <summary>Oxirgi xato (o'zbekcha). Post <c>failed</c> bo'lsa admin SABABINI ko'rishi kerak:
    /// media talabiga mos kelmadimi, limit tugadimi yoki token muddati o'tdimi.</summary>
    public string Error { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Haqiqatan chop etilgan vaqt (ISO) — rejalashtirilganidan farq qilishi mumkin
    /// (konteyner tayyorlanishi kutiladi), shuning uchun AYRI ustun.</summary>
    public string PublishedAt { get; set; } = string.Empty;
}

// =================================================================================================
//  CAPI — LID SIFATINI META'GA QAYTARISH (Conversions API)
// =================================================================================================
// Hozir Meta faqat "lid keldi" ni biladi. "Bu lid o'quvchi bo'ldi va pul to'ladi" ni bilmaydi.
// Shu ma'lumot qaytarilsa Meta HAQIQIY mijoz keltiradigan auditoriyaga optimallashadi.
//
// 🔴 MAXFIYLIK: hodisa navbati xom telefon/email SAQLAMAYDI — faqat hashlangan (SHA-256)
// ko'rinish. Xom PII faqat `Lead` jadvalida qoladi.

/// <summary>
/// Meta'ga yuboriladigan BITTA konversiya hodisasi (Conversions API navbati).
///
/// <para><b>Nega navbat jadvali?</b> Yuborish tashqi tarmoqqa bog'liq va xato bo'lishi mumkin;
/// jadval bo'lsa qayta urinish, dedup va "nima yuborildi" tarixi qoladi. To'g'ridan-to'g'ri
/// yuborish restartda hodisani yo'qotardi.</para>
/// </summary>
public class IgCapiEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>CRM'dagi <see cref="Lead"/>.Id — hodisa qaysi lid haqida.</summary>
    public string LeadId { get; set; } = string.Empty;

    /// <summary>Meta'ning lid id'si (<see cref="IgAdLead.LeadgenId"/>, 15–17 raqam).
    /// <para>⚠️ CAPI'ning "Conversion Leads" oqimi AYNAN shu id orqali ishlaydi — usiz Meta
    /// hodisani hech qanday reklamaga bog'lay olmaydi. Ya'ni faqat REKLAMA FORMASIDAN kelgan
    /// lidlar uchun hodisa yaratiladi; DM/izohdan kelgan lid bu navbatga tushmaydi.</para></summary>
    public string LeadgenId { get; set; } = string.Empty;

    /// <summary>Hodisa nomi — <b>ERKIN MATN</b>, kodga yozib qo'yilmaydi.
    /// <para>🔴 Meta hech qanday qat'iy satrni talab qilmaydi (qat'iy enum faqat Business
    /// Messaging CAPI'da). Shart bitta: Events Manager'da sozlangan bosqich nomi bilan AYNAN
    /// bir xil bo'lishi. Shuning uchun nomlar <see cref="CenterMeta.InstagramCapiStageQualified"/>
    /// va <see cref="CenterMeta.InstagramCapiStageWon"/> sozlamalaridan olinadi — markaz
    /// Events Manager'dagi nomni o'zgartirsa kod qayta yig'ilmaydi.</para></summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Dedup kaliti (<c>"{leadgenId}_{unix}"</c>) — <b>UNIKAL indeks</b>.
    /// <para>⚠️ Deterministik bo'lishi SHART: <c>Guid</c>/<c>Random</c> ishlatilsa qayta
    /// urinishda AYNI hodisa Meta'ga ikkinchi marta boshqa kalit bilan tushib, konversiya
    /// ikkilanardi. Meta tomonidagi dedup oynasi — <c>event_name</c> + <c>event_id</c> bo'yicha
    /// 48 soat; bizdagi unikal indeks esa uzoq muddatli qavat.</para></summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Hodisa vaqti (ISO).
    /// <para>⚠️ <b>7 kundan eski bo'lmasin</b> — Meta bunday hodisani rad etadi va rad javobi
    /// BUTUN paketga tegishli bo'ladi (bitta eski qator tufayli 1000 ta hodisa yo'qoladi).
    /// Shuning uchun eskirgan qator yuborilmaydi, <c>skipped</c> qilinadi.</para></summary>
    public string EventTime { get; set; } = string.Empty;

    /// <summary><c>pending</c> | <c>sent</c> | <c>failed</c> | <c>skipped</c>.
    /// <para><c>skipped</c> — yuborishga urinilmagan, lekin qayta ham urinilmaydi (muddati
    /// o'tgan yoki modul o'chiq bo'lgan hodisa). U <c>failed</c> dan ATAYIN ayrilgan: "xato"
    /// deb ko'rsatilsa admin muammo izlab vaqt sarflardi.</para></summary>
    public string Status { get; set; } = "pending";

    /// <summary>Yuborishga nechta urinilgani — cheksiz qayta urinishdan himoya.</summary>
    public int Attempts { get; set; }

    /// <summary>Oxirgi xato (o'zbekcha). Bo'sh = muammo yo'q.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Meta'ga ketgan tananing nusxasi (JSON) — diagnostika uchun.
    /// <para>🔴 <b>XOM PII YOZILMAYDI.</b> Telefon va email faqat normalizatsiya qilingan va
    /// SHA-256 bilan hashlangan holda tushadi. Data Protection Assessment aynan shuni
    /// tekshiradi; bundan tashqari bu ustunni ko'rgan har qanday xodim mijoz raqamini olib
    /// qolardi.</para>
    /// <para>⚠️ <c>test_event_code</c> ham bu yerga tushmasin — u faqat sinovda ishlatiladi va
    /// produksiyada so'rovdan butunlay olib tashlanadi.</para></summary>
    public string PayloadJson { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>Muvaffaqiyatli yuborilgan vaqt (ISO). Bo'sh = hali yuborilmagan.</summary>
    public string SentAt { get; set; } = string.Empty;
}

/// <summary>Landing sahifasidagi o'qituvchi kartochkasi va to'liq ma'lumotlari.</summary>
public class LandingTeacher
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
    public string ShortBio { get; set; } = string.Empty;
    public string FullBio { get; set; } = string.Empty;
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Landing sahifasidagi sertifikatlar va o'quvchilar muvaffaqiyati ko'rgazmasi.</summary>
public class LandingCertificate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>Bo'lim: "Xalqaro" | "Milliy" | "Oliygoh" (oliygohga kirish natijalari).
    /// ⚠️ Ommaviy sahifadagi filtr ANIQ belgilangan toifani ustun ko'radi: "Oliygoh" yozuv
    /// turi IELTS bo'lsa ham "Xalqaro" filtriga tushmaydi (bitta natija ikki joyda ko'rinmasin).</summary>
    public string Category { get; set; } = "Xalqaro";
    /// <summary>"IELTS" | "Multilevel" | "Milliy" | "SAT" | "Oliygoh" | "Boshqa".</summary>
    public string CertType { get; set; } = "IELTS";
    public string OverallScore { get; set; } = string.Empty;
    public string Listening { get; set; } = string.Empty;
    public string Reading { get; set; } = string.Empty;
    public string Writing { get; set; } = string.Empty;
    public string Speaking { get; set; } = string.Empty;
    public string ResultNote { get; set; } = string.Empty;
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Landing sahifasidagi ota-onalar va o'quvchilar fikrlari (Testimonials).</summary>
public class LandingTestimonial
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = "Ota-ona";
    public string AvatarUrl { get; set; } = string.Empty;
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Landing sahifasidagi ko'p beriladigan savollar va javoblar (FAQ).</summary>
public class LandingFaq
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
}
