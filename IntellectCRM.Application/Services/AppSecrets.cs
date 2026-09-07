using Microsoft.Extensions.Configuration;

namespace IntellectCRM.Application.Services;

/// <summary>
/// TIZIM KALITLARI (maxfiy qiymatlar) — YAGONA MANBA: <c>.env</c> / muhit o'zgaruvchilari.
///
/// <para><b>QOIDA:</b> API kaliti, token, parol va service-account JSON kabi maxfiy qiymatlar
/// BAZADA SAQLANMAYDI va UI'dan kiritilmaydi. Ilgari ular <c>CenterMeta</c> ustunlarida turardi —
/// bu ustunlar <c>RemoveSecretsFromDb</c> migratsiyasida O'CHIRILDI (baza dump'i/backup'i, audit
/// va SQL kirishi orqali kalit sizib chiqmasin). Yangi kalit qo'shilganda ham shu yerga qo'shiladi,
/// <c>CenterMeta</c>ga EMAS.</para>
///
/// <para>Har bir kalit IKKI xil nom bilan o'qiladi:
/// <list type="bullet">
///   <item><c>Telegram:BotToken</c> — docker-compose ichida <c>Telegram__BotToken</c> ko'rinishida
///     uzatiladi (prod'dagi asosiy yo'l);</item>
///   <item><c>TELEGRAM_BOT_TOKEN</c> — <c>.env</c> faylidagi xom nom (mahalliy <c>dotnet run</c>
///     yoki <c>env_file</c> bilan ishlaganda — <see cref="DotEnvFile"/> yuklaydi).</item>
/// </list>
/// Ikkalasi ham bo'lsa — birinchisi (aniq sozlangan) ustun.</para>
///
/// <para>Statik: <see cref="JournalService"/>, <see cref="CenterAiAnalysisService"/> kabi STATIK
/// xizmatlar ham kalitga muhtoj (AppClock bilan bir xil naqsh). <see cref="Init"/> Program.cs'da
/// bir marta chaqiriladi.</para>
/// </summary>
public static class AppSecrets
{
    private static IConfiguration? _config;

    /// <summary>Program.cs'da (boshqa hamma narsadan oldin) bir marta chaqiriladi.</summary>
    public static void Init(IConfiguration config) => _config = config;

    /// <summary>Konfiguratsiyadan qiymat: avval "Bo'lim:Kalit", so'ng xom .env nomi (ENV_NAME).</summary>
    private static string Read(string sectionKey, string envName)
    {
        var v = _config?[sectionKey];
        if (string.IsNullOrWhiteSpace(v)) v = _config?[envName];
        return (v ?? "").Trim();
    }

    /* ---------- Telegram bot ---------- */

    /// <summary>BotFather tokeni. Bo'sh — bot ishlamaydi (ilova baribir ishlaydi).</summary>
    public static string TelegramBotToken => Read("Telegram:BotToken", EnvKeys.TelegramBotToken);

    /* ---------- Karyera boti (Intellect Career — ALOHIDA bot) ---------- */

    /// <summary>Ishga qabul (vakansiya) botining BotFather tokeni. Asosiy botdan MUSTAQIL:
    /// bo'sh bo'lsa faqat karyera boti ishlamaydi, qolgan hamma narsa odatdagidek yuradi.</summary>
    public static string CareerBotToken => Read("Career:BotToken", EnvKeys.CareerBotToken);

    /* ---------- Push (Firebase / FCM) ---------- */

    /// <summary>Firebase service account JSON (to'liq, bir qatorda) — serverdan push yuborish uchun.
    /// Ichida <c>private_key</c> bor: eng maxfiy qiymatlardan biri.</summary>
    public static string FcmServiceAccountJson => Read("Fcm:ServiceAccountJson", EnvKeys.FcmServiceAccountJson);

    /* ---------- AI (Google Gemini) ---------- */

    public static string GeminiApiKey => Read("Gemini:ApiKey", EnvKeys.GeminiApiKey);

    /* ---------- Speaking / transkripsiya (Azure Cognitive Services) ---------- */

    public static string AzureSpeechKey => Read("Azure:SpeechKey", EnvKeys.AzureSpeechKey);
    /// <summary>Resurs hududi (maxfiy emas, lekin kalit bilan birga sozlanadi — yarmi UI'da,
    /// yarmi .env'da bo'lib qolmasin).</summary>
    public static string AzureSpeechRegion => Read("Azure:SpeechRegion", EnvKeys.AzureSpeechRegion);

    /* ---------- SMS (Eskiz.uz) ---------- */

    /// <summary>Eskiz kabinet login (email) — parol bilan birga hisob ma'lumoti.</summary>
    public static string EskizEmail => Read("Eskiz:Email", EnvKeys.EskizEmail);
    public static string EskizPassword => Read("Eskiz:Password", EnvKeys.EskizPassword);

    /* ---------- Turniket / FaceID qurilmasi ---------- */

    public static string TurnstileUsername => Read("Turnstile:Username", EnvKeys.TurnstileUsername);
    public static string TurnstilePassword => Read("Turnstile:Password", EnvKeys.TurnstilePassword);

    /* ---------- NVR (videoregistrator) arxivi ---------- */
    // Turniket bilan bir xil naqsh: manzil/port bazada (UI'dan), login/parol FAQAT .env da.
    public static string NvrUsername => Read("Nvr:Username", EnvKeys.NvrUsername);
    public static string NvrPassword => Read("Nvr:Password", EnvKeys.NvrPassword);

    /* ---------- YUZ BILAN KIRISH: vektorlarni shifrlash kaliti ---------- */

    /// <summary>Yuz vektorlarini bazada shifrlash kaliti — <b>base64, AYNAN 32 bayt</b>
    /// (<c>openssl rand -base64 32</c>). <see cref="FaceVault"/> ishlatadi.
    /// <para>⚠️ Bo'sh bo'lsa yuz bilan kirish moduli UMUMAN yoqilmaydi — biometrik vektorni
    /// jimgina ochiq saqlash varianti YO'Q. Yo'qolsa/almashsa: eski etalonlar ochilmaydi va
    /// o'quvchilar keyingi kirishda profil rasmi orqali qayta ro'yxatdan o'tadi (ma'lumot
    /// yo'qolmaydi, faqat qayta ro'yxat).</para></summary>
    public static string FaceVectorKey => Read("Face:VectorKey", EnvKeys.FaceVectorKey);

    /* ---------- ILOVA HAQIQIYLIGI (Google Play Integrity) ---------- */

    /// <summary>Play Integrity API uchun Google Cloud service account JSON (bir qatorda).
    /// <para>Bo'sh bo'lsa <see cref="FcmServiceAccountJson"/> ZAXIRA sifatida ishlatiladi
    /// (<c>AppAttestation.ServiceAccountJson</c>) — Firebase loyihasi ayni paytda GCP loyihasi,
    /// lekin o'sha loyihada <b>Play Integrity API yoqilgan</b> va Play Console'dagi ilova unga
    /// <b>bog'langan</b> bo'lishi shart.</para></summary>
    public static string PlayIntegrityServiceAccountJson =>
        Read("PlayIntegrity:ServiceAccountJson", EnvKeys.PlayIntegritySaJson);

    /// <summary>Android ilovaning paket nomi (masalan <c>uz.intellectcrm.student</c>) —
    /// Play Integrity so'rovi va javobdagi <c>packageName</c> tekshiruvi uchun. MAXFIY EMAS,
    /// lekin kalit bilan birga sozlansin (yarmi UI'da, yarmi `.env` da bo'lib qolmasin).</summary>
    public static string PlayIntegrityPackage => Read("PlayIntegrity:Package", EnvKeys.PlayIntegrityPackage);

    /* ---------- MARKETING: INSTAGRAM AI AGENTI ---------- */

    /// <summary>Meta ilovasining <b>App Secret</b>i — ikki vazifasi bor:
    /// (1) webhook <c>X-Hub-Signature-256</c> imzosini tekshirish, (2) OAuth'da kodni tokenga
    /// almashtirish.
    /// <para>⚠️ Bo'sh bo'lsa imzo tekshiruvi <b>FAIL-CLOSED</b> ishlaydi: webhook so'rovlari
    /// rad etiladi (403). Bu ataylab — sozlanmagan kalit "himoyasiz ochiq endpoint"ga
    /// aylanmasligi kerak.</para></summary>
    public static string InstagramAppSecret => Read("Instagram:AppSecret", EnvKeys.InstagramAppSecret);

    /// <summary>Webhook GET tasdig'i (verify) uchun O'ZIMIZ o'ylab topadigan token — Meta
    /// Dashboard'dagi "Verify token" bilan AYNAN bir xil bo'lishi shart. Maxfiy: bilgan odam
    /// bizning manzilni o'z ilovasiga webhook qilib ulay olardi.</summary>
    public static string InstagramVerifyToken => Read("Instagram:VerifyToken", EnvKeys.InstagramVerifyToken);

    /* ---------- MARKETING: REKLAMA LIDLARI (Meta Lead Ads) ---------- */

    /// <summary>
    /// Reklama lidlari webhook'ini imzolagan Meta ilovasining App Secret'i.
    ///
    /// <para><b>Nega alohida kalit?</b> Izoh/DM "Instagram API with Instagram Login" mahsulotidan
    /// keladi, reklama lidi esa FACEBOOK PAGE obyektining <c>leadgen</c> webhook'idan. Ikkalasi
    /// BITTA Meta ilovasida bo'lishi ham mumkin, AYRI ilovalarda ham. Shuning uchun kalit
    /// alohida, lekin <b>bo'sh bo'lsa Instagram kalitiga qaytadi</b> — bitta ilova ishlatilganda
    /// admin hech narsa qo'shmaydi.</para>
    ///
    /// <para>⚠️ Fallback FAIL-OPEN EMAS: ikkalasi ham bo'sh bo'lsa natija baribir bo'sh satr va
    /// <see cref="InstagramSignature.Verify"/> <c>false</c> qaytaradi (so'rov rad etiladi).</para>
    /// </summary>
    public static string MetaAppSecret
    {
        get
        {
            var own = Read("Meta:AppSecret", EnvKeys.MetaAppSecret);
            return own.Length > 0 ? own : InstagramAppSecret;
        }
    }

    /// <summary>Reklama lidlari webhook'ining verify tokeni. Bo'sh bo'lsa Instagram tokeniga
    /// qaytadi (bitta ilova / bitta callback URL holati).</summary>
    public static string MetaVerifyToken
    {
        get
        {
            var own = Read("Meta:VerifyToken", EnvKeys.MetaVerifyToken);
            return own.Length > 0 ? own : InstagramVerifyToken;
        }
    }

    /* ---------- Holat (Sozlamalar sahifasi uchun) ---------- */

    public static bool TelegramConfigured => TelegramBotToken.Length > 0;
    public static bool CareerBotConfigured => CareerBotToken.Length > 0;
    public static bool GeminiConfigured => GeminiApiKey.Length > 0;
    public static bool AzureSpeechConfigured => AzureSpeechKey.Length > 0 && AzureSpeechRegion.Length > 0;
    public static bool EskizConfigured => EskizEmail.Length > 0 && EskizPassword.Length > 0;
    public static bool TurnstileCredentialsConfigured => TurnstileUsername.Length > 0 && TurnstilePassword.Length > 0;
    /// <summary>NVR login/paroli berilganmi — arxivni olib kelish shusiz mumkin emas.</summary>
    public static bool NvrCredentialsConfigured => NvrUsername.Length > 0 && NvrPassword.Length > 0;
    /// <summary>Yuz vektorlari kaliti bor VA yaroqli (32 bayt base64) — modul shu bilan yoqiladi.</summary>
    public static bool FaceVectorKeyConfigured => FaceVault.IsValidKey(FaceVectorKey);
    /// <summary>Instagram moduli uchun ikkala `.env` kaliti ham berilganmi. Ikkalasi HAM shart:
    /// verify tokensiz Meta webhookni ro'yxatga ololmaydi, app secretsiz esa kelgan hodisa
    /// imzosini tekshirib bo'lmaydi (va u rad etiladi).</summary>
    public static bool InstagramConfigured => InstagramAppSecret.Length > 0 && InstagramVerifyToken.Length > 0;

    /// <summary>
    /// <c>.env</c> o'zgaruvchilari nomlari — Sozlamalar sahifasida adminga "qaysi qatorni qo'shish
    /// kerak" deb ko'rsatiladi va <see cref="DotEnvFile"/>/hujjatlar bilan bitta joyda turadi.
    /// </summary>
    public static class EnvKeys
    {
        public const string TelegramBotToken = "TELEGRAM_BOT_TOKEN";
        public const string CareerBotToken = "CAREER_BOT_TOKEN";
        public const string FcmServiceAccountJson = "FCM_SERVICE_ACCOUNT_JSON";
        public const string GeminiApiKey = "GEMINI_API_KEY";
        public const string AzureSpeechKey = "AZURE_SPEECH_KEY";
        public const string AzureSpeechRegion = "AZURE_SPEECH_REGION";
        public const string EskizEmail = "ESKIZ_EMAIL";
        public const string EskizPassword = "ESKIZ_PASSWORD";
        public const string TurnstileUsername = "TURNSTILE_USERNAME";
        public const string TurnstilePassword = "TURNSTILE_PASSWORD";
        public const string NvrUsername = "NVR_USERNAME";
        public const string NvrPassword = "NVR_PASSWORD";
        public const string FaceVectorKey = "FACE_VECTOR_KEY";
        public const string PlayIntegritySaJson = "PLAY_INTEGRITY_SA_JSON";
        public const string PlayIntegrityPackage = "PLAY_INTEGRITY_PACKAGE";
        public const string InstagramAppSecret = "INSTAGRAM_APP_SECRET";
        public const string InstagramVerifyToken = "INSTAGRAM_VERIFY_TOKEN";
        public const string MetaAppSecret = "META_APP_SECRET";
        public const string MetaVerifyToken = "META_VERIFY_TOKEN";
    }
}
