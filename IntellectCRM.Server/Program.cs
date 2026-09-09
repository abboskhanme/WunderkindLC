using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using System.IO.Compression;
using System.Security.Claims;
using IntellectCRM.Domain;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Hubs;
using IntellectCRM.Application.Services;
using IntellectCRM.Infrastructure.Auth;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Server.Controllers;
using IntellectCRM.Server.Cti;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

// PostgreSQL (Npgsql): DateTime (Kind=Unspecified, AppClock — Toshkent local) ni `timestamp`
// (timezonesiz) sifatida saqlash — SQL Server'dagi xulqni saqlaydi. Aks holda Npgsql 6+ default
// `timestamptz` UTC talab qilib, har bir yozuvda istisno tashlaydi. Boshqa hech narsadan oldin o'rnatilishi shart.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ---------- .env ----------
// Kalitlar (token/parol/API kalit) YAGONA manbadan — .env / muhit o'zgaruvchilaridan o'qiladi
// (bazada saqlanmaydi — AppSecrets). Docker'da qiymatlar compose orqali muhit o'zgaruvchisi
// bo'lib keladi; konteynersiz ishga tushirilganda esa shu yerda .env fayli o'qiladi.
// Haqiqiy muhit o'zgaruvchisi HAR DOIM ustun (DotEnvFile faqat bo'shlarini to'ldiradi).
var dotEnv = DotEnvFile.Load(builder.Environment.ContentRootPath);
if (dotEnv.Count > 0) builder.Configuration.AddInMemoryCollection(dotEnv);
// Statik xizmatlar (JournalService, CenterAiAnalysisService, ...) ham kalitga muhtoj.
AppSecrets.Init(builder.Configuration);

var defaultConn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default sozlanmagan.");

// ---------- Xizmatlar ----------
// PostgreSQL (Npgsql). Connection string `ConnectionStrings:Default` orqali beriladi
// (dev: appsettings.json, prod: muhit o'zgaruvchisi). 1GB RAM serverga ham sig'adi.
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
    opt.UseNpgsql(defaultConn,
            npg =>
            {
                // Vaqtinchalik DB uzilishlarini avtomatik qayta urinish bilan chidaydi.
                npg.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
                // Ko'p kolleksiyali Include'larni alohida so'rovlarga ajratadi — kartezian portlashning oldini oladi.
                npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
        // Kesh invalidatsiya interceptor'i — har SaveChanges'dan keyin o'zgargan entity turlari
        // versiyasini oshiradi (DataCache), bog'liq keshni avtomatik eskirtiradi.
        .AddInterceptors(sp.GetRequiredService<CacheInvalidationInterceptor>()));

// Application qatlamidagi xizmatlar konkret AppDbContext o'rniga IAppDbContext'ga
// bog'lanadi — uni o'sha scoped AppDbContext instansiyasiga ulaymiz.
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// Kam o'zgaradigan ma'lumotlar (meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ReferenceCache>();
// "Ma'lumot o'zgarganda avtomatik yangilanadigan" versiyali kesh + uni oshiruvchi interceptor.
// Ikkalasi ham Singleton (versiyalar butun ilova bo'ylab yagona; interceptor holatsiz).
builder.Services.AddSingleton<DataCache>();
builder.Services.AddSingleton<CacheInvalidationInterceptor>();

// DataProtection kalitlarini DOIMIY volume'ga saqlaymiz. Aks holda kalitlar konteyner ichida
// (/root/.aspnet) turadi va HAR deploy'da yo'qoladi — natijada eski tokenlar/shifrlangan
// ma'lumotlar yaroqsiz bo'lib qoladi. /app/keys docker volume'iga ulangan (qarang docker-compose).
var keysDir = builder.Configuration["DataProtection:KeysPath"] ?? "/app/keys";
try { Directory.CreateDirectory(keysDir); } catch { /* dev'da yo'l bo'lmasligi mumkin — e'tiborsiz */ }
if (Directory.Exists(keysDir))
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("IntellectCRM");

// JWT sozlamalari. Imzo kaliti appsettings'da SAQLANMAYDI (repoga tushmasligi uchun) —
// uni `Jwt__Key` muhit o'zgaruvchisi yoki `dotnet user-secrets` orqali bering.
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        // Dev'da kalit berilmasa — vaqtinchalik tasodifiy kalit (server restartida tokenlar bekor bo'ladi).
        jwtOptions.Key = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        Console.WriteLine("[WARN] Jwt:Key berilmagan — DEV uchun vaqtinchalik tasodifiy kalit ishlatilmoqda. "
            + "Prod'da Jwt__Key muhit o'zgaruvchisini o'rnating.");
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key sozlanmagan yoki 32 belgidan qisqa. Uni `Jwt__Key` muhit o'zgaruvchisi "
            + "yoki user-secrets orqali bering (hech qachon appsettings.json'ga yozmang).");
    }
}
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
// Refresh token oqimi (chiqarish/rotatsiya/bekor qilish) — AppDbContext'ga bog'liq (scoped).
builder.Services.AddScoped<RefreshTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };

        // SignalR (WebSocket) token'ni Authorization header'da yubora olmaydi —
        // chat hub uchun tokenni query string'dan ("access_token") qabul qilamiz.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // DUAL-MODE (web cookie rejimi): Authorization sarlavhasi YO'Q bo'lsa (ya'ni
                // mobil Bearer emas) va `at` cookie bor bo'lsa — tokenni cookie'dan olamiz.
                // Bearer HAR DOIM ustun (mobil orqaga moslik buzilmaydi). SignalR WS handshake
                // cookie'ni avtomatik yuboradi, shuning uchun `/hubs` ham cookie bilan ishlaydi.
                if (string.IsNullOrEmpty(context.Token)
                    && string.IsNullOrEmpty(context.Request.Headers.Authorization.ToString())
                    && context.Request.Cookies.TryGetValue(IntellectCRM.Server.AuthCookies.AtCookie, out var atCookie)
                    && !string.IsNullOrEmpty(atCookie))
                    context.Token = atCookie;

                return Task.CompletedTask;
            },

            // Token bekor qilish (revocation): imzo/muddat to'g'ri bo'lsa ham, akkaunt holati
            // tekshiriladi — arxivlangan o'qituvchi/o'quvchi yoki o'chirilgan xodim/admin
            // eski tokeni bilan KIRA OLMAYDI. Parent (telefon orqali bog'lanadi) tekshirilmaydi.
            //
            // ⚠️ KESH (IMemoryCache, 60 soniya, kalit = rol + userId): ilgari bu tekshiruv HAR
            // avtorizatsiyalangan so'rovda DB'ga borardi. Endi natija (bloklanganmi + rol +
            // ruxsatlar) 60 soniyaga keshlanadi; kesh o'tib ketgach birinchi so'rov DB'dan
            // yangilaydi. QABUL QILINGAN SAVDO: bloklangan/arxivlangan foydalanuvchi, rol yoki
            // ruxsat o'zgarishi KO'PI BILAN 60 soniya kechikib amal qiladi — "qayta login shart
            // emas" siyosati saqlanadi, faqat bir daqiqagacha kechikish bilan. AddMemoryCache
            // yuqorida (ReferenceCache bilan birga) ro'yxatdan o'tgan.
            OnTokenValidated = async context =>
            {
                var p = context.Principal;
                var userId = p?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? p?.FindFirst("sub")?.Value;
                if (p is null || string.IsNullOrEmpty(userId)) return;

                var http = context.HttpContext;
                var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
                var ttl = TimeSpan.FromSeconds(60);

                bool blocked;
                if (p.IsInRole(Roles.Teacher))
                    // Arxivlangan YOKI "vaqtincha aktiv emas" qilingan o'qituvchi eski tokeni bilan
                    // ham kira olmaydi (o'quvchidagi LoginBlocked bilan bir xil mantiq).
                    blocked = await cache.GetOrCreateAsync("auth:teacher:" + userId, async e =>
                    {
                        e.AbsoluteExpirationRelativeToNow = ttl;
                        var db = http.RequestServices.GetRequiredService<AppDbContext>();
                        return !await db.Teachers.AsNoTracking()
                            .AnyAsync(t => t.UserId == userId && !t.IsArchived && !t.IsBlocked);
                    });
                else if (p.IsInRole(Roles.Student))
                    // Arxivlangan YOKI admin tomonidan login cheklangan o'quvchi eski tokeni bilan kira olmaydi.
                    blocked = await cache.GetOrCreateAsync("auth:student:" + userId, async e =>
                    {
                        e.AbsoluteExpirationRelativeToNow = ttl;
                        var db = http.RequestServices.GetRequiredService<AppDbContext>();
                        return !await db.Students.AsNoTracking()
                            .AnyAsync(s => s.UserId == userId && !s.IsArchived && !s.LoginBlocked);
                    });
                else if (p.IsInRole(Roles.Staff) || p.IsInRole(Roles.Admin) || p.IsInRole(Roles.SuperAdmin))
                {
                    // Butun User qatori emas, faqat kerakli maydonlar (rol + ruxsatlar)
                    // proyeksiya qilinadi — parol hashi va boshqa ustunlar xotira keshiga tushmaydi.
                    var u = await cache.GetOrCreateAsync("auth:user:" + userId, async e =>
                    {
                        e.AbsoluteExpirationRelativeToNow = ttl;
                        var db = http.RequestServices.GetRequiredService<AppDbContext>();
                        return await db.Users.AsNoTracking()
                            .Where(x => x.Id == userId)
                            .Select(x => new { x.Role, x.Permissions })
                            .FirstOrDefaultAsync();
                    });
                    blocked = u is null;
                    if (!blocked && p.Identity is ClaimsIdentity ident)
                    {
                        // ROL ham DB'dan olinadi (kesh orqali, 60 s). Sabab: "ikkinchi superadmin"
                        // tayinlansa (StaffController.SetRole) yoki qaytarilsa, tokendagi ESKI rol
                        // 12 soatgacha amal qilib turardi — ko'tarilgan odam qayta login qilmaguncha
                        // yangi huquqlarni olmasdi, tushirilgani esa eski huquqlar bilan ishlayverardi.
                        // Ruxsat claim'lari bilan bir xil siyosat: qayta login SHART EMAS.
                        if (!string.IsNullOrEmpty(u!.Role))
                        {
                            var tokenRole = ident.FindFirst(ident.RoleClaimType)?.Value;
                            if (!string.Equals(tokenRole, u.Role, StringComparison.Ordinal))
                            {
                                foreach (var c in ident.FindAll(ident.RoleClaimType).ToList())
                                    ident.RemoveClaim(c);
                                ident.AddClaim(new Claim(ident.RoleClaimType, u.Role));
                            }
                        }

                        // Xodim (staff) ruxsatlari DB'dan (kesh orqali) claim sifatida qo'shiladi —
                        // tokenga yozilmaydi, shuning uchun superadmin ruxsatni o'zgartirsa 60
                        // soniyagacha kechikish bilan amal qiladi (qayta login shart emas).
                        // AdminPerm atributi shu claim'larni tekshiradi.
                        // DIQQAT: shart TOKENdagi emas, DB'dagi rolga qaraydi (yuqorida sinxronlandi).
                        if (u.Role == Roles.Staff && u.Permissions is { Count: > 0 } perms)
                            foreach (var perm in perms)
                                ident.AddClaim(new Claim(AdminPermAttribute.ClaimType, perm));
                    }
                }
                else
                    blocked = false; // parent / boshqa — tegmaymiz

                if (blocked) context.Fail("Akkaunt arxivlangan yoki o'chirilgan");
            },
        };
    });
builder.Services.AddAuthorization();

// Login endpoint uchun rate-limit — parol brute-force / credential-stuffing'ni sekinlashtiradi
// (IP bo'yicha daqiqada 10 urinish). Oshib ketsa 429 qaytadi.
// DIQQAT: Cloudflare tunnel ortida RemoteIpAddress BARCHA tashrifchilar uchun bitta (cloudflared
// konteyneri IP'si) — partitsiya shu bo'yicha bo'lsa limit hammaga UMUMIY bo'lib qoladi (bitta
// odam limitni tugatsa hamma 429 oladi). Shuning uchun haqiqiy IP CF-Connecting-IP (Cloudflare
// har doim qo'yadi) yoki X-Forwarded-For'dan olinadi; to'g'ridan ulanishda RemoteIpAddress.
static string ClientIp(HttpContext ctx)
{
    var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
    if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
    var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
    if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
    return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 429 javobiga O'ZBEKCHA JSON matn qo'shamiz. Sabab: rate-limiter sukut bo'yicha BO'SH tana
    // qaytaradi, landing formasi esa `data.message`ni o'qiydi va topolmagach umumiy "Xatolik yuz
    // berdi" ko'rsatardi — tashrifchi nima bo'lganini bilmay qayta-qayta bosaverardi (va limitni
    // yanada uzaytirardi). Endi u "bir daqiqadan keyin urinib ko'ring" deb aniq yozadi.
    // Retry-After — standart sarlavha; limiter oynaning qolgan vaqtini bersa o'shani ishlatamiz.
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Juda ko'p urinish. Iltimos, bir daqiqadan keyin qayta urinib ko'ring.\"}",
            token);
    };
    options.AddPolicy("login", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    // Bir martalik kod bilan login — parol brute-force bilan bir xil tahdid (endi kod, login'dan ko'ra
    // yuqori entropiyali, lekin baribir IP bo'yicha cheklanadi; kod hovuzi haqida signal bermaslik uchun
    // "login" bilan bir xil limit).
    options.AddPolicy("otp-verify", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    // Landing sahifasidagi "Bepul darsga yozilish" formasi — ochiq (auth'siz) endpoint,
    // spam/bot flood'ni sekinlashtiradi (IP bo'yicha daqiqada 5 ta so'rov).
    options.AddPolicy("public-lead", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Instagram webhook — ochiq (auth'siz) endpoint, lekin uni META chaqiradi. Limit ATAYIN SAXIY:
    // "public-lead" (daqiqada 5) bu yerda YARAMAYDI — faol akkauntda izoh/DM oqimi undan oson
    // oshadi, rad etilgan (429) hodisani esa Meta 36 soat qayta yuboradi va takrorlansa webhookni
    // BUTUNLAY o'chirib qo'yishi mumkin. Shu sabab chegara faqat qo'pol flood'ni ushlaydi.
    options.AddPolicy("instagram-webhook", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientIp(httpContext),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // GLOBAL (fallback) limiter — flood'ga qarshi himoya darvozasi (admission control). MUHIM: bir IP
    // ortida bir nechta xodim bo'lishi mumkin (markaz ofisi), shuning uchun AUTENTIFIKATSIYALANGAN
    // so'rov FOYDALANUVCHI bo'yicha (o'z bucketi — bir-birini bloklamaydi), anonim so'rov IP bo'yicha
    // partitsiyalanadi. Limit ATAYIN saxiy (daqiqada 600 ≈ 10/sek) — normal foydalanuvchi yetmaydi,
    // faqat qo'pol avtomatlashtirilgan flood 429 oladi. Endpoint-specific siyosatlar (login/public-lead)
    // buning ustiga qo'shimcha ishlaydi.
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var userId = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = !string.IsNullOrEmpty(userId) ? "u:" + userId : "ip:" + ClientIp(ctx);
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

// Real-time guruh chati (SignalR)
builder.Services.AddSignalR();
builder.Services.AddScoped<ChatService>();

// Oylik to'lovlarni avtomatik hisoblovchi fon xizmati
builder.Services.AddHostedService<IntellectCRM.Application.Services.TuitionAccrualService>();
builder.Services.AddHostedService<IntellectCRM.Application.Services.TurnstileLiveService>();
// Avtomatik to'lov eslatmasi (qarzdorlarga Telegram + push, 09:00 Toshkent).
builder.Services.AddHostedService<IntellectCRM.Application.Services.PaymentReminderService>();
// O'qituvchiga davomat kiritish eslatmasi (dars boshlanishidan N daqiqa keyin, push + Telegram).
builder.Services.AddHostedService<IntellectCRM.Application.Services.LessonAttendanceReminderService>();
// Erkin eslatma (admin belgilagan matn+auditoriya+jadval, push + Telegram).
builder.Services.AddHostedService<IntellectCRM.Application.Services.CustomReminderService>();
// Tug'ilgan kun avto-SMS (09:00 Toshkent; "birthday" hodisasiga andoza bo'lsa).
builder.Services.AddHostedService<IntellectCRM.Application.Services.BirthdaySmsService>();
// Sinov darsi eslatmasi (09:00 Toshkent; ertaga bo'ladigan sinovlar; "trial_reminder" andoza bo'lsa).
builder.Services.AddHostedService<IntellectCRM.Application.Services.TrialReminderService>();
// Kunlik avtomatik backup — markaz ma'lumotlarini JSON qilib Telegram orqali adminga (jadval CenterMeta'da).
builder.Services.AddHostedService<IntellectCRM.Application.Services.BackupSchedulerService>();
// "Topshiriqlar" (Kanban) kunlik eslatmasi — bugungi va kechikkan topshiriqlar mas'uliga botda.
builder.Services.AddHostedService<IntellectCRM.Application.Services.WorkTaskReminderService>();
// Kunlik markaz AI tahlili (ertalab ~8:00 Toshkent; Gemini kaliti + AiDailyAnalysisEnabled bo'lsa).
builder.Services.AddHostedService<IntellectCRM.Application.Services.CenterAiSchedulerService>();
// KPI: oy BOSHIDAGI surat (faol o'quvchilar soni, qarz qoldig'i) — 1-kuni yarim tundan keyin.
builder.Services.AddHostedService<IntellectCRM.Application.Services.Kpi.KpiSnapshotService>();

// Telegram bot (e'lon yuborish + ota-onalarni kontakt orqali ro'yxatga olish).
// Token appsettings "Telegram:BotToken" da; bo'sh bo'lsa bot ishga tushmaydi.
builder.Services.AddHttpClient();
// ⚠️ "telegram" NOMLI mijoz — `TelegramService` aynan shu nom bilan so'raydi. Ilgari bu nom
// ro'yxatdan O'TMAGAN edi: nomlanmagan konfiguratsiya = default `HttpClient.Timeout` = 100 SEKUND.
// Oqibati: lid kartasi bir necha chatda ketma-ket yangilanadi va Telegram javob bermay qolsa
// `PATCH /leads/{id}` javobi N × 100 sekundgacha osilib qolardi (3 chat ≈ 5 daqiqa).
// 10 sekund — Telegram odatda 1 sekunddan tez javob beradi; undan uzoq kutish CRM endpointini
// bloklashdan boshqa hech narsa bermaydi (karta keyingi o'zgarishda baribir yangilanadi).
// Long polling (`getUpdates`, 30 s kutadi) bu timeout'ga TUSHMAYDI — u o'z mijozini sozlaydi.
builder.Services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(10));
// ⚠️ "eskiz" NOMLI mijoz — `EskizService` shu nom bilan so'raydi. AYNAN SHU XATO telegram'da
// bo'lgan va bu yerda TAKRORLANGAN edi: nom ro'yxatdan o'tmagani uchun default 100 SEKUND
// timeout ishlardi.
// Haqiqiy hodisa (2026-09-07, 16:51-16:56 Toshkent): Eskiz API'si ~5 daqiqa javob bermay
// qoldi. Har "SMS yuborish" bosilishi 100 sekund osilib turdi (`TaskCanceledException:
// ... HttpClient.Timeout of 100 seconds elapsing`), operator 7 marta bosdi — ya'ni admin
// oynasi 12 daqiqa qotib turdi. Eskiz o'zi 0.5-1.5 sekundda javob beradi.
// 25 sekund — sekin tarmoqqa ham yetarli, lekin osilishni foydalanuvchi kutadigan chegarada
// ushlab qoladi.
builder.Services.AddHttpClient("eskiz", c => c.Timeout = TimeSpan.FromSeconds(25));
builder.Services.AddSingleton<TelegramService>();
// Onlayn test (bot orqali ishlanadigan) oqimi — TelegramBotService shu servisga yo'naltiradi.
builder.Services.AddSingleton<OnlineTestBotService>();
// Kitob sotuvi (bot orqali buyurtma) oqimi — TelegramBotService shu servisga yo'naltiradi.
builder.Services.AddSingleton<BookShopBotService>();
builder.Services.AddHostedService<TelegramBotService>();
// KARYERA (Intellect Career) — ALOHIDA bot (.env: CAREER_BOT_TOKEN) + `/vakansiya` Mini App.
// Token bo'sh bo'lsa bot jim kutadi, qolgan hamma narsa odatdagidek ishlaydi.
builder.Services.AddSingleton<CareerTelegramService>();
builder.Services.AddSingleton<CareerService>();
builder.Services.AddHostedService<CareerBotService>();
// MARKETING — INSTAGRAM AI AGENTI.
// `InstagramApi` — Graph API mijozi: typed HttpClient (`AddHttpClient<T>`), ya'ni ulanishlar
// pulda boshqariladi va socket tugab qolmaydi (`CameraGateway` bilan bir xil naqsh).
builder.Services.AddHttpClient<IntellectCRM.Application.Services.InstagramApi>();
// Oqim (webhook hodisasi → qoida/AI → javob → lid) — har hodisa uchun O'ZI scope ochadi,
// shuning uchun Singleton (fon xizmati uni bir marta oladi va sikl bo'yi ishlatadi).
builder.Services.AddSingleton<IntellectCRM.Application.Services.InstagramPipeline>();
// FAQ tugmalarini (ice breakers) Meta'ga sinxronlash — YAGONA manba (§22). Uni FAQ CRUD,
// modul yoqilishi (SaveSettings) va akkaunt ulash (connect-token + OAuth callback) chaqiradi;
// callback boshqa controllerda bo'lgani uchun mantiq shu servisda birlashtirilgan. Scoped:
// `IAppDbContext`ga bog'liq (so'rov bilan bir xil DbContext instansiyasi).
builder.Services.AddScoped<IntellectCRM.Application.Services.InstagramFaqSync>();
// Navbatni qayta ishlovchi + tokenni 45-kunda yangilovchi + eski hodisalarni tozalovchi fon xizmati.
// ⚠️ `CenterMeta.InstagramEnabled == false` bo'lsa u HECH QANDAY tashqi so'rov qilmaydi.
builder.Services.AddHostedService<IntellectCRM.Application.Services.InstagramWorkerService>();
// MARKETING — REKLAMA LIDLARI (Meta Lead Ads).
// ⚠️ ALOHIDA mijoz: bu `graph.facebook.com` va PAGE tokeni bilan ishlaydi (izoh/DM esa
// `graph.instagram.com` va Instagram Login tokeni bilan) — ikkisini bitta sinfga qo'shish
// tokenlarni chalkashtirishga olib kelardi. marketing-instagram.md §16.
builder.Services.AddHttpClient<IntellectCRM.Application.Services.MetaAdsApi>();
// Hodisani qayta ishlaydi (Graph → IgAdLead → CRM Lead → Telegram). Scoped: `IAppDbContext`ga
// bog'liq va uni pipeline O'Z scope'idan oladi.
builder.Services.AddScoped<IntellectCRM.Application.Services.MetaLeadgenService>();
// MARKETING — REKLAMA STATISTIKASI (Ads Insights), CAPI va KONTENT JOYLASH.
// ⚠️ Uchala mijoz ham AYRI typed HttpClient: har birining O'Z hosti va O'Z tokeni bor
// (Insights/CAPI — `graph.facebook.com` + System User/dataset tokeni, Publish —
// `graph.instagram.com` + Instagram Login tokeni). Tokenlarni chalkashtirish
// "OAuthException 190" bo'lib chiqadi va sababini topish juda qiyin.
builder.Services.AddHttpClient<IntellectCRM.Application.Services.MetaInsightsApi>();
builder.Services.AddScoped<IntellectCRM.Application.Services.MetaInsightsService>();
builder.Services.AddHttpClient<IntellectCRM.Application.Services.MetaCapiApi>();
builder.Services.AddScoped<IntellectCRM.Application.Services.MetaCapiService>();
builder.Services.AddHttpClient<IntellectCRM.Application.Services.InstagramPublishApi>();
builder.Services.AddScoped<IntellectCRM.Application.Services.InstagramPublishService>();
// BILIM BAZASI VEKTORLARI (RAG). `AddHttpClient` KERAK EMAS — `GeminiService` kabi statik
// HttpClient ishlatadi. Fon xizmati uni har 60 soniyada chaqiradi (`EmbedPendingAsync`).
builder.Services.AddScoped<IntellectCRM.Application.Services.IgEmbeddingService>();
// Yetim media fayllarini tozalash (`uploads/marketing-public/`). Fon xizmati kuniga bir marta
// chaqiradi; post o'chirilganda/tahrirlanganda controller ham shu servisdan foydalanadi.
builder.Services.AddScoped<IntellectCRM.Application.Services.MarketingMediaCleanup>();

// FCM (Firebase push) — service account CenterMeta'da; token keshi uchun singleton.
builder.Services.AddSingleton<FcmService>();
// Eskiz.uz SMS — login/parol CenterMeta'da; token keshi uchun singleton.
builder.Services.AddSingleton<EskizService>();
// YAGONA avto-xabar dispatcheri (SMS+Push+Telegram) — Eskiz/Fcm/Telegram singletonlariga tayanadi.
builder.Services.AddSingleton<AutoMessageService>();
// MoiZvonki (bulutli telefoniya — operator telefoni orqali) — YAGONA telefoniya provayderi.
// REST mijoz + webhook avto-obuna. Sozlanmagan bo'lsa Call Center moduli o'chiq (CallsController.Provider).
builder.Services.AddSingleton<MoiZvonkiService>();
builder.Services.AddHostedService<MoiZvonkiSetupService>();
// Qo'ng'iroqlar TARIXI sinxroni (calls.list) — webhook'siz ham tarix to'ladi; singleton
// sifatida ham ro'yxatda (controller qo'lda "Yangilash"da SyncOnceAsync'ni chaqiradi).
builder.Services.AddSingleton<MoiZvonkiCallSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MoiZvonkiCallSyncService>());

// CTI (Local Call) — Android agent WebSocket ulanishlarini boshqaruvchi (jonli onlayn + dial buyruq).
builder.Services.AddSingleton<CtiConnectionManager>();
// Local SMS — agent telefonining SIM-kartasidan (send_sms) — Eskiz'ga muqobil provider sifatida
// MessagesController/AutoMessageService/CtiController tomonidan ishlatiladi.
builder.Services.AddSingleton<CtiSmsService>();

// OMMAVIY SMS NAVBATI — ko'p oluvchili partiya so'rov ichida emas, FONDA yuboriladi (Cloudflare
// javobni 100 s kutadi, 100 ta SMS esa bir necha daqiqa oladi). Singleton + fon ishchisi bir xil
// nusxa: controller unga navbatga qo'yadi, ishchi esa bittalab yuboradi.
builder.Services.AddSingleton<SmsQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SmsQueueService>());

// O'zgarishlar tarixi (audit) — joriy foydalanuvchini aniqlash uchun HttpContext kerak
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IntellectCRM.Application.Services.AuditService>();
// Bog'lanish navbatiga qo'shish — admin va O'QITUVCHI controllerlari uchun yagona manba.
builder.Services.AddScoped<IntellectCRM.Application.Services.ContactQueueService>();
// Yuz bilan kirish (o'quvchi mobil ilovasi) — login qarori, tekshirish, ishonchli qurilma.
// Auth, o'quvchi va admin controllerlari AYNAN shuni chaqiradi (qoida bitta joyda).
builder.Services.AddScoped<IntellectCRM.Application.Services.FaceLoginService>();
// Biometrik vektorlar seyfi — kalit FAQAT `.env` da (FACE_VECTOR_KEY). Holatsiz → Singleton.
// ⚠️ Kalit yo'q bo'lsa modul o'chiq bo'ladi (pastda startupda ogohlantirish logi yoziladi).
builder.Services.AddSingleton(IntellectCRM.Application.Services.FaceVault.FromSecrets());
// Ilova haqiqiyligi (Google Play Integrity) — OAuth tokenini keshlaydi → Singleton.
builder.Services.AddSingleton<IntellectCRM.Application.Services.AppAttestation>();

// Shartnoma andozasini (Word) to'ldirish xizmati
builder.Services.AddScoped<IntellectCRM.Application.Services.ContractService>();

// Sertifikat tizimi (HTML yaratish, hash, tekshirish)
builder.Services.AddScoped<IntellectCRM.Application.Services.CertificateService>();

// TEST SERTIFIKATI — Word andoza to'ldirish + LibreOffice orqali PDF ga o'girish.
// Konvertor holatsiz (stateless) va o'zi navbatlaydi — Singleton yetarli.
builder.Services.AddSingleton<IntellectCRM.Application.Services.DocxToPdfConverter>();
builder.Services.AddScoped<IntellectCRM.Application.Services.TestCertificateService>();
// Generatsiya FON ishi + uning holati. Holat xotirada va so'rovlar oralig'ida yashashi kerak —
// shuning uchun Singleton (o'zi kerakli joyda scope ochadi).
builder.Services.AddSingleton<IntellectCRM.Application.Services.TestCertificateJobs>();

// `/uploads` darvozasi — token tekshiruvi, ochiq fayllar keshi va cookie (batafsil: UploadsGuard).
// Singleton: keshlar so'rovlar oralig'ida yashashi kerak.
builder.Services.AddSingleton<IntellectCRM.Server.UploadsGuard>();

// Turniket/FaceID integratsiyasi — o'qituvchilar davomatini avtomatik yuklash
builder.Services.AddScoped<IntellectCRM.Application.Services.TurnstileService>();

// Xona vaqt konflikti aniqlash (guruh yaratish/tahrirlashda warning)
builder.Services.AddScoped<IntellectCRM.Application.Services.RoomConflictService>();

// Xona bandlik va samaradorlik metrikalari
builder.Services.AddScoped<IntellectCRM.Application.Services.RoomUtilizationService>();
builder.Services.AddScoped<IntellectCRM.Application.Services.ScheduleService>();

// Kamera (videokuzatuv) media-shlyuzi (MediaMTX) bilan ishlash
builder.Services.AddHttpClient<IntellectCRM.Application.Services.CameraGateway>();
// NVR arxivi — o'z HttpClient'ini ichida yaratadi (Digest auth uchun NetworkCredential kerak,
// turniketdagi naqsh bilan bir xil), shuning uchun oddiy singleton.
builder.Services.AddSingleton<IntellectCRM.Application.Services.NvrArchiveService>();

// Javoblarni siqish (Brotli + Gzip). Level.Fastest — TTFB ga ortiqcha CPU yuk qo'ymaydi.
// Eslatma: Cloudflare orqasida bo'lsa, CF chetda allaqachon siqadi — bu origin uchun foydali.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// OutputCache — faqat OCHIQ (auth talab qilmaydigan) endpointlar uchun ([OutputCache] qo'yilganlar).
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("public-tenant", b => b.Expire(TimeSpan.FromSeconds(30)));
});

builder.Services.AddControllers();

// HSTS — brauzer domenni 1 yil davomida FAQAT https deb eslab qoladi (subdomenlar ham).
// `Preload` ATAYIN qo'shilmadi: preload ro'yxatiga tushish qaytarib bo'lmas majburiyat
// (barcha subdomenlar abadiy https bo'lishi shart) — bu qaror alohida ko'rib chiqiladi.
// Amalda faqat prod'da qo'llanadi (`app.UseHsts()` ham `!IsDevelopment()` ostida).
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

var app = builder.Build();

// ---------- Bazani yaratish va seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Migration startup retry — baza init bo'lgunga qadar kutadi (1GB server sekin bo'lishi mumkin)
    var maxRetries = 10;
    var secretsChecked = false;   // eski kalitlar bir marta tekshiriladi (qarang LegacySecretRescue)
    for (var attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            // Kalitlar bazadan .env ga ko'chirilmoqda: RemoveSecretsFromDb migratsiyasi ustunlarni
            // O'CHIRADI, shuning uchun undan OLDIN eski qiymatlarni logga chiqaramiz (admin nusxa
            // olib .env ga joylashi uchun). Ustunlar allaqachon o'chgan bo'lsa — jim o'tadi.
            // Baza tayyor bo'lmasa false qaytadi va keyingi urinishda takrorlanadi (logga BIR marta).
            if (!secretsChecked) secretsChecked = LegacySecretRescue.ReportPendingSecrets(db.Database, app.Logger);
            db.Database.Migrate();
            app.Logger.LogInformation("[migration] muvaffaqiyatli (urinish {Attempt}/{Max})", attempt, maxRetries);
            break;
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            app.Logger.LogWarning(ex,
                "[migration] urinish {Attempt}/{Max} muvaffaqiyatsiz — {Seconds}s kutishdan keyin qayta",
                attempt, maxRetries, attempt * 3);
            await Task.Delay(TimeSpan.FromSeconds(attempt * 3));
        }
    }

    try
    {
        await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""MapIframeUrl"" text DEFAULT '';");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "[fix] CenterMeta MapIframeUrl ustuni qo'shish o'tkazib yuborildi");
    }

    // SELF-HEAL: eski "support" rolli akkauntlar role="teacher" bo'lishi kerak (support sahifasi
    // teacher portalida IsSupport bo'yicha ko'rinadi). "support" rol bilan ular /teacher portaliga
    // kira olmasdi — bir martalik idempotent tuzatish.
    try
    {
        var movedSupport = await db.Users.Where(u => u.Role == "support")
            .ExecuteUpdateAsync(up => up.SetProperty(u => u.Role, Roles.Teacher));
        if (movedSupport > 0)
            app.Logger.LogInformation("[fix] {N} ta 'support' rolli akkaunt 'teacher'ga ko'chirildi", movedSupport);
    }
    catch { }

    // CenterMeta ustunlarini idempotent qo'shish (ADD COLUMN IF NOT EXISTS) — eski o'rnatishlarda
    // ustun allaqachon bor, ya'ni bu blok FAQAT yangi/bo'sh bazaga ta'sir qiladi.
    //
    // ⚠️ DEFAULT qiymatlar ATAYIN BO'SH (''). Ilgari bu yerda 'https://youtube.com',
    // 'info@intellect.uz', '+998 (90) 344-44-34' kabi "namuna" qiymatlar turardi va natijada YANGI
    // o'rnatishda bazada markazga tegishli BO'LMAGAN kontaktlar paydo bo'lardi: sahifada YouTube
    // kanali "bor" bo'lib ko'rinardi, admin esa uni bo'shata olmasdi. Endi "kiritilmagan" holati
    // BO'SH satr bilan ifodalanadi; ommaviy sahifada ko'rsatiladigan zaxira matn faqat O'QISH
    // paytida (LandingCmsController / GET /api/public/landing-data) qo'llanadi.
    //
    // ⚠️ Bu yerda MapIframeUrl'ni "bo'sh bo'lsa OpenStreetMap havolasi bilan to'ldirish" UPDATE'i
    // bor edi — OLIB TASHLANDI va QAYTA QO'SHILMASIN. U har startupda ishlagani uchun admin CMS'dan
    // xarita havolasini o'chirsa, server qayta ishga tushgach havola O'ZI qaytib kelardi va xaritani
    // umuman o'chirib bo'lmasdi. Bo'sh qiymat — adminning ATAYIN qilgan tanlovi.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""MapIframeUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""TelegramUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""InstagramUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""YoutubeUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""FacebookUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterEmail"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""AppStoreUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""PlayMarketUrl"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""ContactPhone"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterAddress"" text DEFAULT '';
            ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""WorkingHours"" text DEFAULT '';
        ");
    }
    catch (Exception ex)
    {
        // Ilgari bu yerda jim `catch { }` turardi: DDL bajarilmasa (masalan baza foydalanuvchisida
        // ALTER huquqi yo'q) hech kim bilmasdi va keyinroq "column does not exist" xatolari chiqib,
        // sababi noma'lum qolardi. Endi kamida ogohlantirish yoziladi (yuqoridagi MapIframeUrl
        // bloki bilan bir xil uslub) — startup baribir to'xtatilmaydi.
        app.Logger.LogWarning(ex, "[fix] CenterMeta ustunlarini qo'shish o'tkazib yuborildi");
    }

    // Birinchi ishga tushish: hech qanday foydalanuvchi bo'lmasa, standart SUPER ADMIN yaratamiz —
    // aks holda tizimga kira oladigan hech kim bo'lmaydi. Login/parol muhit o'zgaruvchisidan keladi
    // (Seed__OwnerLogin / Seed__OwnerPassword). Parol berilmasa — tasodifiy generatsiya qilinadi va
    // loglarga yoziladi. Diqqat: `Email` maydoni aslida USERNAME (login), email emas.
    if (!db.Users.Any())
    {
        var login = app.Configuration["Seed:OwnerLogin"];
        if (string.IsNullOrWhiteSpace(login)) login = "admin";
        var pwd = app.Configuration["Seed:OwnerPassword"];
        // Parol .env'da berilmaganda — tasodifiy generatsiya (faqat shu holatda logga yoziladi,
        // aks holda kira oladigan hech kim bo'lmaydi). Berilgan parol HECH QACHON logga yozilmaydi.
        var generated = string.IsNullOrWhiteSpace(pwd);
        if (generated) pwd = AccountFactory.GeneratePassword(10);
        var owner = new AppUser
        {
            FullName = "Super Admin",
            Role = Roles.SuperAdmin,
            Email = login,
            PasswordHash = PasswordHasher.Hash(pwd),
            InitialPassword = pwd,   // birinchi login'gacha ko'rinadi (AuthController tozalaydi)
        };
        db.Users.Add(owner);
        db.SaveChanges();
        if (generated)
            app.Logger.LogWarning(
                "[seed] Super admin yaratildi — login: '{Login}', parol: '{Password}' "
                + "(OWNER_PASSWORD berilmagani uchun generatsiya qilindi — ko'chirib oling va kirgach o'zgartiring).",
                login, pwd);
        else
            app.Logger.LogInformation(
                "[seed] Super admin yaratildi — login: '{Login}' (parol .env'dagi OWNER_PASSWORD — logga yozilmadi).",
                login);
    }

    // Amal sabablari (muzlatish/o'chirish/sinovga qaytarish/lid/guruh) — standart sabablar bilan
    // to'ldiriladi (admin keyin Sabablar bo'limida o'zgartiradi). PER-KATEGORIYA idempotent: jadval
    // prod'da allaqachon to'ldirilgan bo'lsa ham, faqat hozir BO'SH kategoriyalar seed qilinadi
    // (yangi kategoriyalar restartda qo'shiladi, mavjudlari takrorlanmaydi).
    {
        var defaults = new (string Cat, string[] Labels)[]
        {
            ("freeze", new[] { "Sog'liq sababli", "Ta'til / sayohat", "Moliyaviy qiyinchilik", "Vaqtincha tanaffus" }),
            ("return_trial", new[] { "Qayta sinov so'radi", "To'lov muammosi", "Darajani qayta tekshirish" }),
            ("remove_active", new[] { "Boshqa markazga o'tdi", "Ko'chib ketdi", "Darslardan voz kechdi", "Moliyaviy sabab" }),
            ("remove_trial", new[] { "Sinovdan keyin qoldirmadi", "Qiziqmadi", "Narx mos kelmadi" }),
            ("remove_frozen", new[] { "Uzoq vaqt qaytmadi", "Boshqa markazga o'tdi", "Voz kechdi" }),
            ("lead_delete", new[] { "Aloqaga chiqmadi", "Qiziqmadi", "Noto'g'ri raqam", "Takror / spam" }),
            ("group_delete", new[] { "O'quvchi yetarli emas", "O'qituvchi ketdi", "Kurs tugadi", "Guruhlar birlashtirildi" }),
            ("student_delete", new[] { "Boshqa markazga o'tdi", "Ko'chib ketdi", "Xato kiritilgan", "Voz kechdi" }),
            ("teacher_delete", new[] { "Ishdan bo'shadi", "Boshqa joyga o'tdi", "Xato kiritilgan" }),
            ("staff_delete", new[] { "Ishdan bo'shadi", "Xato kiritilgan" }),
            ("finance_delete", new[] { "Xato kiritilgan", "Takroriy to'lov", "Bekor qilindi", "Qaytarib berildi" }),
        };
        var existingCats = db.ActionReasons.Select(r => r.Category).Distinct().ToHashSet();
        var seeded = false;
        foreach (var (cat, labels) in defaults)
        {
            if (existingCats.Contains(cat)) continue;
            for (var i = 0; i < labels.Length; i++)
                db.ActionReasons.Add(new ActionReason { Category = cat, Label = labels[i], Order = i });
            seeded = true;
        }
        if (seeded)
        {
            db.SaveChanges();
            app.Logger.LogInformation("[seed] Standart amal sabablari yaratildi");
        }
    }

    // Lid bosqichlari (kanban ustunlari) — standart voronka bilan to'ldiriladi. IDEMPOTENT: faqat
    // jadval BO'SH bo'lsa seed qilinadi (admin keyin nomlash/o'chirish/tartiblashni o'zgartiradi).
    // Bu MUHIM: bosqich yo'q paytda daraja testidan tushgan lid Stage="" bilan yaratilib, kanbanda
    // ko'rinmay qolardi (hech qaysi ustunga tushmaydi). Bosqich doim mavjud bo'lsa — to'g'ri tushadi.
    if (!db.LeadStages.Any())
    {
        var stages = new (string Title, string Color)[]
        {
            ("Yangi", "blue"),
            ("Bog'lanildi", "cyan"),
            ("Sinov darsi", "amber"),
            ("O'ylanmoqda", "violet"),
            ("Aylantirildi", "emerald"),
        };
        for (var i = 0; i < stages.Length; i++)
            db.LeadStages.Add(new LeadStage { Title = stages[i].Title, Color = stages[i].Color, Order = i });
        db.SaveChanges();
        app.Logger.LogInformation("[seed] Standart lid bosqichlari yaratildi");
    }

    // SMS andozalari SEED QILINMAYDI — "Xabar matnlari" kutubxonasi faqat FOYDALANUVCHI yaratgan
    // matnlarni (qo'lda "Yangi matn" + "Xabar yaratish" avto qoidalari) ko'rsatadi. Bo'sh boshlanadi.
    // (Eski ReminderRule/SmsTemplate.IsAuto ko'chirish seed'i ham olib tashlangan.)

    // "Bog'lanish kerak" TAXTASINING TIZIM USTUNLARI — har bazaviy holat uchun bittadan.
// IDEMPOTENT: yo'q bo'lgani qo'shiladi (jadval bo'sh bo'lishi shart emas), shuning uchun
// keyinchalik yangi holat qo'shilsa ham ustuni o'z-o'zidan paydo bo'ladi.
// ⚠️ MUHIM: ustun HOLATNI almashtirmaydi — StageId bo'sh talab o'z holatining TIZIM ustunida
// ko'rinadi (tizim ustunining Id'si = holat kaliti), ya'ni eski qatorlarni to'ldirish shart emas.
{
    var existing = db.ContactStages.Select(x => x.Id).ToHashSet();
    var added = 0;
    for (var i = 0; i < ContactService.SystemStages.Count; i++)
    {
        var def = ContactService.SystemStages[i];
        if (existing.Contains(def.Key)) continue;
        db.ContactStages.Add(new ContactStage
        {
            Id = def.Key,
            Title = def.Title,
            Color = def.Color,
            BaseStatus = def.Key,
            Order = i,
            IsSystem = true,
        });
        added++;
    }
    if (added > 0)
    {
        db.SaveChanges();
        app.Logger.LogInformation("[seed] Bog'lanish taxtasining {Count} ta tizim ustuni yaratildi", added);
    }
}

// KPI: rol QOIDALARI (Excel konstantalari) va kunlik CHEKLIST shablonlari.
// IDEMPOTENT — mavjud versiya/shablon HECH QACHON qayta yozilmaydi, faqat yo'q bo'lgani
// qo'shiladi (rahbarning «Qoidalar» sahifasidagi tahriri restartda yo'qolmasin).
// TRY-CATCH: jadval yo'q bo'lsa (migratsiya qo'llanmagan) logga yozib o'tadi.
try
{
    var (kpiRules, kpiTemplates, kpiItems) =
        await IntellectCRM.Application.Services.Kpi.KpiSeedService.EnsureAsync(db, "Tizim");
    if (kpiRules + kpiTemplates + kpiItems > 0)
        app.Logger.LogInformation(
            "[seed] KPI: {Rules} qoidalar to'plami, {Templates} cheklist shabloni, {Items} band qo'shildi",
            kpiRules, kpiTemplates, kpiItems);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex,
        "[seed] KPI seed failed (migratsiya qo'llanmagan bo'lsa normal) — keyingi restartda qayta urinadi");
}

// YETIM LIDLARNI TUZATISH: bosqichi mavjud bo'lmagan (eski bo'sh "" yoki o'chirilgan ustunga
    // tegishli) lidlar kanbanda ko'rinmaydi — ularni birinchi (Order) bosqichga ko'chiramiz.
    // Har restartda arzon ishlaydi (faqat yetim bo'lsa yozadi) → prod'dagi mavjud yetimlarni tuzatadi.
    {
        var validStageIds = db.LeadStages.Select(s => s.Id).ToList();
        var firstStageId = db.LeadStages.OrderBy(s => s.Order).Select(s => s.Id).FirstOrDefault();
        if (firstStageId is not null)
        {
            var orphans = db.Leads.Where(l => !validStageIds.Contains(l.Stage)).ToList();
            if (orphans.Count > 0)
            {
                foreach (var l in orphans) l.Stage = firstStageId;
                db.SaveChanges();
                app.Logger.LogInformation(
                    "[repair] {Count} ta yetim lid (bosqichsiz) birinchi bosqichga ko'chirildi", orphans.Count);
            }
        }
    }

    // Xodim roli shablonlari — yangi xodim qo'shishda tanlash uchun standart rol/ruxsatlar.
    // PER-KOD IDEMPOTENT: yo'q shablon YARATILADI; mavjudiga seed'dagi YETISHMAGAN ruxsatlar
    // QO'SHILADI (union — shablonlar UI'dan tahrirlanmaydi, ular tizim-boshqaruvidagi ro'yxat;
    // yangi bo'lim ruxsati (masalan "calls") chiqsa, mavjud bazada ham avtomatik yangilanadi).
    // TRY-CATCH: jadval yo'q bo'lsa (migration qo'llanmagan) logga yozib o'tadi.
    try
    {
        var templates = new (string Code, string Name, string Desc, string[] Perms)[]
        {
            ("call_operator", "Qo'ng'iroq operatori",
                "Qo'ng'iroq qabul qiladi (Call Center), lidlarni yaratadi va boshqaradi",
                new[] { "calls", "leads", "messages" }),
            ("cashier", "Kassir",
                "To'lovlarni kiritadi va boshqaradi, o'quvchi ma'lumotlarini ko'radi",
                // "kassa" — pul qabul qilish ish o'rni (o'quvchini topib to'lov kiritish). Faqat
                // shu ruxsat berilsa ham kassir ishlay oladi (o'quvchi tahriri/moliya hisobotisiz).
                new[] { "kassa", "students", "finance", "messages" }),
            ("administrator", "Administrator",
                "Asosiy boshqaruv — guruhlar, o'quvchilar, o'qituvchilar, o'quv bo'limi",
                new[] { "leads", "students", "teachers", "classes", "schedule", "messages", "app" }),
        };
        var existingTemplates = db.StaffRoleTemplates.ToList();
        var created = 0;
        var patched = 0;
        foreach (var (code, name, desc, perms) in templates)
        {
            var tpl = existingTemplates.FirstOrDefault(t => t.Code == code);
            if (tpl is null)
            {
                db.StaffRoleTemplates.Add(new StaffRoleTemplate
                {
                    Code = code,
                    Name = name,
                    Description = desc,
                    DefaultPermissions = new List<string>(perms),
                });
                created++;
                continue;
            }
            // Mavjud shablonga faqat YANGI ruxsatlar qo'shiladi (nom/izoh tegilmaydi).
            var missing = perms.Where(p => !tpl.DefaultPermissions.Contains(p)).ToList();
            if (missing.Count > 0)
            {
                tpl.DefaultPermissions.AddRange(missing);
                patched++;
            }
        }
        if (created + patched > 0)
        {
            db.SaveChanges();
            app.Logger.LogInformation(
                "[seed] Xodim roli shablonlari sinxronlandi: {Created} yangi, {Patched} ruxsati to'ldirilgan",
                created, patched);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "[seed] StaffRoleTemplates seed failed (migration qo'llanmagan bo'lsa normal) — keyingi restartda qayta urinyadi");
    }

    // ---------- Integratsiya sozlamalarini .env (config) dan CenterMeta'ga qo'llash ----------
    // ENV-WINS: .env'da berilgan integratsiya qiymati DB'dagidan USTUN turadi (har deploy'da qo'llanadi) —
    // admin .env'da boshqaradi, UI'da "Saqlash" bosish shart emas. .env'da BO'SH qoldirilgan integratsiya
    // esa UI'dan boshqariladi (DB'da saqlanadi, deploy buzmaydi). Bo'sh qiymatlar e'tiborsiz qoldiriladi.
    //
    // DIQQAT: bu yerda FAQAT maxfiy BO'LMAGAN sozlamalar bor. Kalitlar (bot tokeni, FCM service
    // account, Gemini/Azure kaliti, Eskiz login/paroli, turniket login/paroli) DB'ga UMUMAN
    // yozilmaydi — ular to'g'ridan-to'g'ri .env dan o'qiladi (AppSecrets).
    try
    {
        var cfg = app.Configuration;
        var meta = db.CenterMeta.FirstOrDefault();
        var existed = meta is not null;
        meta ??= new CenterMeta();
        var changed = false;
        void S(string key, Action<string> set)
        { var v = cfg[key]; if (!string.IsNullOrWhiteSpace(v)) { set(v.Trim()); changed = true; } }
        void B(string key, Action<bool> set)
        { var v = cfg[key]; if (!string.IsNullOrWhiteSpace(v) && bool.TryParse(v.Trim(), out var b)) { set(b); changed = true; } }
        void I(string key, Action<int> set)
        { var v = cfg[key]; if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v.Trim(), out var n)) { set(n); changed = true; } }

        // Telegram bot (TOKEN bu yerda YO'Q — .env dan o'qiladi)
        S("Telegram:BotUsername", v => meta.TelegramBotUsername = v.TrimStart('@'));
        S("Telegram:BotName", v => meta.TelegramBotName = v);
        S("Telegram:Channel", v => meta.TelegramChannel = v);
        S("Telegram:AdminChatId", v => meta.TelegramAdminChatId = v);
        // Eskiz SMS: jo'natuvchi nomi (From) ATAYIN bu yerda YO'Q — u UI'dan boshqariladigan
        // yagona Eskiz sozlamasi. .env'dagi ESKIZ_FROM faqat DB'da qiymat bo'lmaganda zaxira
        // sifatida ishlatiladi (EskizService.SenderOf); aks holda har deployda admin tanlagan
        // tasdiqlangan nikname "4546" ga qaytib ketardi.
        // Turniket / FaceID (login/parol .env da)
        B("Turnstile:Enabled", v => meta.TurnstileEnabled = v);
        S("Turnstile:Vendor", v => meta.TurnstileVendor = v);
        S("Turnstile:Host", v => meta.TurnstileHost = v);
        I("Turnstile:Port", v => meta.TurnstilePort = v);
        // Kamera
        B("Camera:Enabled", v => meta.CameraEnabled = v);
        // 24/7 diskka yozib borish (jonli kuzatuvdan ALOHIDA) — default O'CHIQ
        B("Camera:RecordEnabled", v => meta.CameraRecordEnabled = v);
        // NVR arxivi (login/parol .env da: NVR_USERNAME / NVR_PASSWORD)
        B("Nvr:Enabled", v => meta.NvrEnabled = v);
        S("Nvr:Host", v => meta.NvrHost = v);
        I("Nvr:RtspPort", v => meta.NvrRtspPort = v);
        I("Nvr:IsapiPort", v => meta.NvrIsapiPort = v);
        S("Nvr:Vendor", v => meta.NvrVendor = v.ToLowerInvariant());
        // Kunlik AI tahlil
        B("AiAnalysis:Enabled", v => meta.AiDailyAnalysisEnabled = v);
        I("AiAnalysis:Hour", v => meta.AiDailyAnalysisHour = v);
        // Telegram backup
        B("Backup:TelegramEnabled", v => meta.TelegramBackupEnabled = v);
        I("Backup:Hour", v => meta.BackupScheduleHour = v);
        I("Backup:Minute", v => meta.BackupScheduleMinute = v);

        if (changed)
        {
            if (!existed) db.CenterMeta.Add(meta);
            db.SaveChanges();
            app.Logger.LogInformation("[env] Integratsiya sozlamalari .env dan qo'llandi (env-wins)");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "[env] Integratsiya sozlamalarini .env dan qo'llashda xatolik (migratsiya qo'llanmagan bo'lsa normal)");
    }

    // Telegram bot username/nomi — xotiraga (token .env dan o'qiladi, DB'da saqlanmaydi).
    // DIQQAT: env-wins blokidan KEYIN — aks holda .env dagi TELEGRAM_BOT_USERNAME endigina DB'ga
    // yozilgan bo'lsa ham, xizmat keyingi restartgacha bo'sh nom bilan qolardi (t.me havolasi buzilardi).
    try { scope.ServiceProvider.GetRequiredService<TelegramService>().Load(db); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "[telegram] bot nomini yuklab bo'lmadi"); }
}

// ---------- Pipeline ----------

// Cloudflare Tunnel / reverse-proxy orqasida: haqiqiy mijoz IP'si (X-Forwarded-For) va
// HTTPS sxemasi (X-Forwarded-Proto) tiklanadi. Busiz login rate-limit hamma uchun bitta
// IP'ga (tunnel) tushib qoladi va HTTPS-redirect tsikli yuzaga kelishi mumkin.
// FAQAT prod'da yoqamiz (dev'da Vite proxy bu sarlavhalarni yubormaydi).
// MUHIM: konteyner portini internetga OCHMANG — unga faqat cloudflared kirsin.
if (!app.Environment.IsDevelopment())
{
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    fwd.KnownNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

// Javoblarni siqish — pipeline boshida (statik fayllar va API javoblari ham siqilsin).
app.UseResponseCompression();

// Xavfsizlik sarlavhalari — barcha javoblarga (statik fayllar va /uploads ham). MIME-sniffing,
// clickjacking va (prod'da) saqlangan XSS'ga qarshi himoya.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    // /uploads fayllari (dars PDF va h.k.) SPA ichida iframe'da ko'rsatiladi —
    // faqat O'Z domenimizdan frame'lashga ruxsat, boshqa hamma javob DENY.
    var isUpload = context.Request.Path.StartsWithSegments("/uploads");
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    // Brauzer imkoniyatlarini cheklash. To'lov va USB — UMUMAN o'chiq (bu ilovaga kerak emas).
    // Kamera (PhotoDialog), mikrofon (ovoz yozish/Call Center) va joylashuv (o'quvchi GPS) esa
    // O'Z domenimizda ATAYIN ishlaydi — shuning uchun 'self', begona/iframe'dagi manbaga esa yo'q.
    headers["Permissions-Policy"] =
        "camera=(self), microphone=(self), geolocation=(self), payment=(), usb=()";
    // CSP faqat prod'da — dev'da SPA Vite serverida alohida beriladi.
    if (!app.Environment.IsDevelopment() && isUpload)
    {
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Content-Security-Policy"] = "frame-ancestors 'self'";
    }
    else if (!app.Environment.IsDevelopment())
    {
        // CRM SPA — Telegram Mini App sifatida ham ochiladi: Telegram Web (web.telegram.org)
        // uni <iframe> ichida yuklaydi — avvalgi "frame-ancestors 'none'" buni bloklab, mijozda
        // "Oops, failed to load ..." xatosini chiqargan. Shu sabab Telegram domenlariga ham
        // frame'lashga ruxsat berilgan. X-Frame-Options ATAYLAB QO'YILMAYDI — u bir nechta domenni
        // qo'llamaydi (faqat DENY/SAMEORIGIN), zamonaviy brauzerlar baribir CSP frame-ancestors'ni
        // ustun qo'yadi; XFO: DENY qoldirilsa ba'zi mijozlarda CSP'ga qaramay bloklashi mumkin edi.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "img-src 'self' data: blob: https:; " +
            // blob: — Call Center yozuv pleyeri (audio auth bilan blob qilib ochiladi) va
            // shunga o'xshash media; busiz prod'da <audio src="blob:..."> JIM bloklanadi.
            "media-src 'self' blob:; " +
            // fonts.googleapis.com — landing/sertifikatlar sahifalari Google Fonts'ni
            // <link rel="stylesheet"> bilan yuklaydi (SPA emas, statik marketing sahifalari).
            // Busiz prod'da shrift STYLESHEET'i bloklanadi va sahifa zaxira shriftda chiqadi.
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            // gstatic — FCM web SW (firebase-messaging-sw.js) `firebasejs`'ni importScripts qiladi.
            // telegram.org — bot Menu Button orqali Web App sifatida ochilganda kerak bo'ladigan SDK
            // (`/js/telegram-web-app.js`). Ikkalasi ham ANIQ yo'l bilan toraytirilgan — host darajasidagi
            // ruxsat kerak emas, aks holda o'sha domendagi boshqa har qanday skript ham yuklanardi.
            "script-src 'self' https://www.gstatic.com/firebasejs/ https://telegram.org/js/; " +
            "worker-src 'self'; " +
            // googleapis/gstatic — FCM web token olish (getToken) so'rovlari. Yalang `ws: wss:` ATAYIN
            // OLIB TASHLANDI — u istalgan hostga WebSocket ochilishiga yo'l qo'yib, token oqib ketish
            // kanali bo'lardi. Bizning WS/SignalR same-origin, CSP3'da 'self' same-origin wss'ni qamraydi.
            "connect-src 'self' https://*.googleapis.com https://*.gstatic.com https://fcm.googleapis.com; " +
            // fonts.gstatic.com — Google Fonts'ning .woff2 fayllari aynan shu hostdan keladi
            // (stylesheet googleapis'da, shriftning O'ZI gstatic'da — ikkalasi ham kerak).
            "font-src 'self' data: https://fonts.gstatic.com; " +
            // frame-src ILGARI YO'Q EDI -> default-src 'self' ga tushardi va landing'dagi
            // XARITA <iframe> (landing.js `renderMap`) PRODDA JIM BLOKLANARDI (dev'da CSP
            // umuman yo'q, shuning uchun "lokalda ishlaydi, serverda yo'q" ko'rinishini berardi).
            // Ro'yxat — markaz Sozlamalaridagi "Xarita" maydoniga qo'yilishi mumkin bo'lgan
            // provayderlar: Yandex (map-widget), Google Maps embed, OpenStreetMap.
            // ATAYIN oq ro'yxat ('self' + shu uchtasi): CMS maydoniga tashqi HTML yozilsa ham
            // ixtiyoriy sayt iframe'da ochilib ketmasin.
            "frame-src 'self' https://yandex.uz https://*.yandex.uz https://yandex.ru https://*.yandex.ru " +
                "https://www.google.com https://maps.google.com https://*.google.com " +
                "https://www.openstreetmap.org https://*.openstreetmap.org; " +
            "frame-ancestors 'self' https://web.telegram.org https://*.web.telegram.org; " +
            // object-src/base-uri — plagin va <base> orqali inyeksiyani yopadi. form-action 'self' —
            // forma ma'lumotini begona hostga POST qilib bo'lmaydi. upgrade-insecure-requests — qolgan
            // har qanday http:// so'rov avtomatik https'ga ko'chiriladi (aralash kontent bo'lmasin).
            "object-src 'none'; base-uri 'self'; form-action 'self'; upgrade-insecure-requests";
    }
    else
    {
        headers["X-Frame-Options"] = "DENY";
    }
    await next();
});

if (!app.Environment.IsDevelopment())
    app.UseHsts();

var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsDir);

// ---------------------------------------------------------------------------------------------
// MAXFIY PAPKALAR — statik yo'l bilan BERILMAYDI.
//
// `/uploads` ochiq statik papka: manzilni bilgan har kim login'siz oladi. Sertifikat esa shaxsiy
// ma'lumot (F.I.Sh, ball, foiz, o'quvchining SURATI). API tomonda egalik tekshiriladi, lekin
// faylning o'zi shu tekshiruvni chetlab o'tib olinardi — manzil bir marta oshkor bo'lsa (brauzer
// tarixi, ulashilgan havola) u abadiy ishlayverardi, o'qituvchi guruhdan chiqarilsa ham.
//
// Fayllar KO'CHIRILMAYDI: `uploads` — docker volume va tungi zaxiraga kiradigan yagona papka.
// Ular joyida qoladi, faqat statik tarzda berilmaydi. Yuklab olish avvalgidek ishlaydi — u
// avtorizatsiyalangan endpointlar orqali, fayl diskdan o'qib beriladi:
//   • test sertifikati  — `test-results/certificates/{id}/download` va ZIP (OwnsGroup/AdminPerm)
//   • eski HTML sertifikat — `students/{id}/certificates/{id}/download`, `student/certificates/...`
// Mijozga to'g'ridan-to'g'ri fayl manzili BERILMAYDI (`DownloadUrl` har doim API manzili).
//
// Ikkala statik middleware ham yopiladi, chunki ikki sertifikat tizimi ikki xil joyga yozadi:
//   • yangi test sertifikatlari → ContentRoot/uploads/certificates (TestCertificateService)
//   • eski HTML sertifikatlar   → wwwroot/uploads/certificates     (CertificateService)
//
// `Uploads:PublicCertificates=true` — favqulodda qaytarish kaliti (kutilmaganda biror mijoz
// faylni to'g'ridan-to'g'ri olayotgani aniqlansa, kodni qayta yig'masdan yoqib turish uchun).
var webRootForPrivate = app.Environment.WebRootPath ?? app.Environment.ContentRootPath;
// ⚠️ `uploads/face` — YUZ BILAN KIRISH selfilari (biometrika, ustiga voyaga yetmaganlarniki).
// Sertifikatlar bilan bir xil siyosat: statik yo'l bilan UMUMAN berilmaydi, faqat
// avtorizatsiyalangan admin endpointi orqali (`/api/admin/face/checks/{id}/image`). Bundan
// tashqari bu papka kunlik zaxira arxividan ham chiqarilgan (docker-compose `backup` → `--exclude`).
var certificateFolders = new[]
{
    Path.Combine(uploadsDir, "certificates"),
    Path.Combine(webRootForPrivate, "uploads", "certificates"),
};
// ⚠️ Yuz selfilari HAR DOIM yopiq: `Uploads:PublicCertificates` favqulodda kaliti FAQAT
// sertifikatlarga tegishli — biometrik suratlarni "vaqtincha ochib qo'yish" varianti YO'Q.
var faceFolders = new[]
{
    Path.Combine(uploadsDir, IntellectCRM.Application.Services.FaceStorage.FolderName),
    Path.Combine(webRootForPrivate, "uploads", IntellectCRM.Application.Services.FaceStorage.FolderName),
};
var privateFilesLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PrivateFiles");
var certificatesArePublic = app.Configuration.GetValue("Uploads:PublicCertificates", false);
if (certificatesArePublic)
    privateFilesLogger.LogWarning(
        "DIQQAT: Uploads:PublicCertificates=true — sertifikat fayllari statik yo'l bilan OCHIQ berilmoqda.");

// YUZ BILAN KIRISH: shifrlash kaliti yo'q bo'lsa modul UMUMAN ishlamaydi (biometrik vektorni
// ochiq saqlash varianti yo'q — `FaceVault`). Admin sozlamada "yoqilgan" deb turib, nima uchun
// selfi so'ralmayotganini tushunmay qolmasin.
if (!IntellectCRM.Application.Services.AppSecrets.FaceVectorKeyConfigured)
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FaceLogin").LogWarning(
        "DIQQAT: FACE_VECTOR_KEY sozlanmagan (yoki 32 baytlik base64 emas) — YUZ BILAN KIRISH "
        + "moduli O'CHIQ. Kalit yaratish: openssl rand -base64 32");

// ---------------------------------------------------------------------------------------------
// OCHIQ MEDIA — `uploads/marketing-public/` (Instagram kontent moduli, §5.6 Variant A).
//
// NEGA OCHIQ: Instagram postni joylashda media faylni O'ZI yuklab oladi — manzil ochiq HTTPS
// bo'lishi SHART (auth, IP cheklov va redirect ishlamaydi). `/uploads` esa `UploadsGuard`
// ortida, ya'ni login talab qiladi va har post `2207052` («Media yuklab bo'lmadi») bilan
// yiqilardi. Busiz butun kontent moduli ishlamaydi.
//
// 🔴 BU MARSHRUTDAN BOSHQA HECH QANDAY FAYL CHIQMAYDI. Uch qatlam:
//   1) FileProvider AYNAN shu jismoniy papkaga ildizlangan — `uploads/` ning qolgan qismi,
//      `uploads/certificates` va `uploads/face` bu provayder uchun UMUMAN mavjud emas
//      (`..` bilan yuqoriga chiqish ham `PhysicalFileProvider` da rad etiladi);
//   2) `ServeUnknownFileTypes = false` + YOPIQ MIME xaritasi (JPEG/MP4/MOV) — papkaga
//      tasodifan tushgan boshqa fayl (masalan `.html`) berilmaydi. Bu papka BIZNING
//      domenimizda, ya'ni u yerdan HTML chiqishi saqlangan XSS bo'lardi;
//   3) fayl nomi faqat `{Guid:N}{kengaytma}` (yuklash endpointi, `MarketingPublicMedia`).
//
// MARSHRUT DARVOZADAN OLDIN turadi: statik middleware faylni topsa so'rovni SHU YERDA
// yakunlaydi, ya'ni `UploadsGuard` ga umuman yetib bormaydi. Fayl topilmasa so'rov pastga
// tushadi va darvoza uni odatdagidek 404 qiladi.
//
// ZAXIRA: papka `uploads/` ichida — tungi arxivga o'z-o'zidan kiradi, alohida ish kerak emas
// (`uploads/face` dan farqli — u ATAYIN `--exclude` qilingan).
var marketingPublicDir = Path.Combine(
    uploadsDir, IntellectCRM.Application.Services.MarketingPublicMedia.FolderName);
Directory.CreateDirectory(marketingPublicDir);

var marketingPublicTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(
    new Dictionary<string, string>(
        IntellectCRM.Application.Services.MarketingPublicMedia.ContentTypes,
        StringComparer.OrdinalIgnoreCase));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(marketingPublicDir),
    RequestPath = IntellectCRM.Application.Services.MarketingPublicMedia.RequestPath,
    ContentTypeProvider = marketingPublicTypes,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        var h = ctx.Context.Response.Headers;
        // ATAYIN `public`: Meta faylni yuklab oladi va CDN'da keshlashi mumkin — bu papka
        // ommaviy (qolgan `/uploads` esa `private`, chunki u yerda maxfiy hujjatlar bor).
        h.CacheControl = "public,max-age=86400";
        // Sarlavhalar yuqoridagi umumiy middleware'da ham qo'yiladi; bu yerda TAKROR
        // qo'yilishi ataylab — marshrut ochiq bo'lgani uchun u boshqa qatlamga bog'liq
        // qolmasin (MIME-sniffing va manzilning "Referer" orqali sizib chiqishi).
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
    },
});

// ---------------------------------------------------------------------------------------------
// `/uploads` DARVOZASI — yuklangan fayllar login talab qiladi (batafsil: UploadsGuard).
// DIQQAT: statik fayllar pipeline'da `UseAuthentication` dan OLDIN turadi, shuning uchun bu yerda
// `HttpContext.User` hali bo'sh — token QO'LDA tekshiriladi (xuddi `/ws` dagidek).
// Guard IKKALA statik middleware'dan ham oldin: wwwroot ham `/uploads/...` yo'lini berishi mumkin.
var uploadsGuard = app.Services.GetRequiredService<IntellectCRM.Server.UploadsGuard>();
if (!uploadsGuard.Enabled)
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("UploadsGuard")
        .LogWarning("DIQQAT: Uploads:RequireAuth=false — /uploads fayllari login'siz OCHIQ berilmoqda.");

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/uploads")) { await next(); return; }
    if (await uploadsGuard.IsAllowedAsync(context)) { await next(); return; }

    uploadsGuard.LogDenied(context);
    // 403 emas, 404: faylning MAVJUDLIGINI ham tasdiqlamaymiz.
    context.Response.StatusCode = StatusCodes.Status404NotFound;
});

IFileProvider Guarded(IFileProvider innerProvider) =>
    new IntellectCRM.Application.Services.PrivateFolderFileProvider(
        innerProvider, privateFilesLogger,
        certificatesArePublic ? faceFolders : [.. certificateFolders, .. faceFolders]);

// ── LANDING FAYLLARI UCHUN QAT'IY "KESHLAMA" SIYOSATI ──────────────────────────────────────────
// Vite assetlari kontent-hash bilan chiqadi (`/assets/index-XXXX.js`) — nomi har build'da o'zgargani
// uchun ular eskirmaydi. Landing esa DOIMIY nomli fayllardan iborat: manzil hech qachon o'zgarmaydi,
// ya'ni brauzer yoki ORALIQDAGI kesh (Cloudflare, korporativ proksi, mobil brauzer) bir marta saqlab
// qo'ysa — eski nusxa abadiy qolib ketishi mumkin. Aynan shu sababdan 2026-08-05 da tuzatilgan xato
// prodda 13 kun ko'rinib turdi va lid formasi validatsiyada to'xtab qolardi.
//
// `no-cache` = "keshla, lekin har safar qayta tasdiqla" — odatda YETARLI, lekin oraliqdagi CDN/proksi
// buni turlicha talqin qilishi mumkin. Shuning uchun landing HTML/JS uchun `no-store` (umuman
// saqlama) qo'yiladi, ustiga eski HTTP/1.0 proksilar tushunadigan `Pragma`/`Expires` ham.
//
// ⚠️ FAQAT quyidagi 4 fayl uchun — butun saytga EMAS: rasm, shrift va `/assets/` keshlanishi KERAK,
// aks holda har ochilishda qaytadan yuklanib trafik va sahifa ochilish vaqti oshib ketardi.
string[] landingNoStorePaths =
[
    "/landing.html", "/landing.js", "/sertifikatlar.html", "/sertifikatlar.js",
];

// Sarlavhalar BITTA joyda: statik fayl pipeline'i ham, `/landing` va `MapFallback` marshrutlari ham
// aynan shuni chaqiradi — aks holda ikkovi ayri ketib, biri keshlanadigan bo'lib qolardi.
static void ApplyLandingNoStore(HttpResponse res)
{
    res.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    res.Headers.Pragma = "no-cache";
    res.Headers.Expires = "0";
}

bool IsLandingNoStorePath(string path) =>
    landingNoStorePaths.Contains(path, StringComparer.OrdinalIgnoreCase);

// DIQQAT: UseDefaultFiles ATAYLAB ishlatilmaydi — `/` ni o'zimiz SPA fallback'da beramiz.
// SPA statik fayllari: Vite assetlari kontent-hash bilan (immutable, 1 yil); index.html — no-cache.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = Guarded(app.Environment.WebRootFileProvider),
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var path = ctx.Context.Request.Path.Value ?? "";
        // Faqat Vite kontent-hashli assetlar (/assets/...) abadiy keshlanadi (nomi har build'da
        // o'zgaradi). Qolganlari — html, favicon (nomi o'zgarmaydi) — no-cache, aks holda
        // yangilanishlar brauzer/Cloudflare keshida ko'rinmay qoladi.
        if (path.Contains("/assets/", StringComparison.OrdinalIgnoreCase))
            headers.CacheControl = "public,max-age=31536000,immutable";
        // Landing HTML/JS — doimiy nomli, shuning uchun umuman saqlanmasin (yuqoridagi izoh).
        else if (IsLandingNoStorePath(path))
            ApplyLandingNoStore(ctx.Context.Response);
        else
            headers.CacheControl = "no-cache";
    },
});

// Yuklangan materiallar (/uploads) — alohida papkadan. Kesh PRIVATE: maxfiy hujjatlar (passport/tug'ilganlik
// guvohnomasi/shartnoma skanlari) Cloudflare/proxy/umumiy keshda SAQLANMASIN — faqat brauzerning o'z keshi
// (URL tasodifiy GUID + no-referrer bilan birga).
// ⚠️ Bu yerdagi QOLGAN fayllar (o'quvchi surati, kitob muqovasi, dars PDF) hamon ochiq — ular
// `<img>`/`<iframe>` da kerak va brauzer ularga `Authorization` sarlavhasini yubora olmaydi.
// Ularni yopish alohida ish: login'da `/uploads` ga cookie qo'yib, shu papkani cookie/token bilan
// darvozalash (mobil ilovalar ham yangilanishi kerak).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = Guarded(new PhysicalFileProvider(uploadsDir)),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "private,max-age=3600";
        // Yuklangan fayl javobiga skript o'ldiruvchi CSP: `default-src 'none'` + `sandbox` —
        // agar biror HTML/SVG shu papkadan berilib qolsa ham u JS ishga tushira olmaydi (bizning
        // domenda saqlangan XSS bo'lmasin). `frame-ancestors 'self'` SAQLANADI — dars PDF'lari SPA
        // iframe'ida ochiladi. (Ochiq `marketing-public` media ALOHIDA marshrutda — bunga tegmaydi.)
        ctx.Context.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; sandbox; frame-ancestors 'self'";
    },
});

// Swagger ATAYLAB o'chirilgan (global) — butun API yuzasini ochib qo'ymaslik uchun
// `/api/swagger` UI/JSON endpointlari berilmaydi.

// CTI (Local Call) — Android agent WebSocket. UseWebSockets HTTPS-redirect'dan OLDIN: upgrade so'rovi
// redirect qilinmasin. /ws terminal branch — SPA fallback'ga tushmaydi. Auth QO'LDA (query ?token=)
// tekshiriladi (SignalR access_token uslubiga o'xshash), chunki brauzer/ilova WS'da header yubora olmaydi.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.Map("/ws", wsApp => wsApp.Run(async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var token = ctx.Request.Query["token"].ToString();
    var (valid, agentId) = ValidateAgentToken(token, jwtOptions);
    if (!valid)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var manager = ctx.RequestServices.GetRequiredService<CtiConnectionManager>();
    var scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>();
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CtiWebSocket");
    var lifetime = ctx.RequestServices.GetRequiredService<IHostApplicationLifetime>();

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await CtiWebSocketHandler.HandleAsync(socket, agentId!, manager, scopeFactory, logger, lifetime.ApplicationStopping);
}));

// CTI WS token tekshiruvi: imzo/muddat/issuer/audience + rol == ctiagent. (agentId, valid) qaytaradi.
static (bool Valid, string? AgentId) ValidateAgentToken(string token, JwtOptions o)
{
    if (string.IsNullOrWhiteSpace(token)) return (false, null);
    try
    {
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = o.Issuer,
            ValidAudience = o.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)),
        }, out _);
        if (!principal.IsInRole(Roles.CtiAgent)) return (false, null);
        var agentId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return string.IsNullOrEmpty(agentId) ? (false, null) : (true, agentId);
    }
    catch { return (false, null); }
}

app.UseHttpsRedirection();

app.UseAuthentication();

// ---------------------------------------------------------------------------------------------
// YUZ TASDIG'I DARVOZASI — CHEKLANGAN token (scope=face) bilan kelgan so'rovlar.
//
// Login yuz tasdig'ini talab qilganda o'quvchiga TO'LIQ token BERILMAYDI (AuthController):
// qaytadigan JWT'da `scope=face` claim'i bo'ladi va u FAQAT selfi yuborish oqimiga yetadi.
// Bu darvoza bo'lmasa butun funksiya bezakka aylanardi — ilova selfi ekranini ko'rsatib
// turarkan, o'sha token bilan jurnal/baho/chat endpointlariga bemalol borib kelaverardi.
//
// Ruxsat ro'yxati SOF FUNKSIYADA (`FaceScopeGate.IsAllowed`, testlangan) — bu yerda faqat
// chaqiriladi. Javob 401 + `faceRequired:true`: ilova buni ko'rib selfi ekranini ochadi.
// DIQQAT: `UseAuthorization` dan OLDIN va cookie beruvchi middleware'dan ham OLDIN turadi
// (cheklangan sessiyaga `/uploads` cookie'si berilmasin).
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && context.User.HasClaim(FaceScopeGate.ClaimType, FaceScopeGate.FaceScope)
        && !FaceScopeGate.IsAllowed(context.Request.Path.Value, context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            faceRequired = true,
            message = FaceScopeGate.BlockedMessage,
        });
        return;
    }
    await next();
});

// `/uploads` cookie'sini TIKLASH — avtorizatsiyalangan har qanday so'rovdan keyin.
// Alohida "login" qadami yo'q: SPA baribir doim API chaqiradi, ya'ni mavjud sessiyalar ham
// qayta login qilmasdan rasmlarni ko'raveradi. Cookie faqat `Path=/uploads` ga tegishli.
// ⚠️ Cheklangan (scope=face) sessiyaga cookie BERILMAYDI — u yuqoridagi darvozadan o'tolmaydi,
// lekin ruxsat etilgan uchta yo'l ham cookie olmasligi kerak (aks holda selfi ekranidagi
// foydalanuvchi butun `/uploads` papkasini ocha olardi).
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && !context.User.HasClaim(FaceScopeGate.ClaimType, FaceScopeGate.FaceScope))
        uploadsGuard.IssueCookie(context);
    await next();
});

app.UseAuthorization();

// CSRF himoyasi (double-submit) — auth'dan KEYIN (User to'ldirilgan bo'lsin), controllerlardan
// OLDIN. Faqat COOKIE bilan autentifikatsiya qilingan UNSAFE so'rovlarni tekshiradi; Bearer
// bilan kelgan mobil/web so'rovlari ISTISNO (batafsil: CsrfMiddleware).
app.UseMiddleware<IntellectCRM.Server.CsrfMiddleware>();

app.UseRateLimiter();
// OutputCache middleware — tayyor turadi, lekin [OutputCache] faqat ochiq endpointlarga qo'yiladi
// (multi-tenant xavfsizligi uchun; pastdagi izohga qarang). Auth'dan keyin turishi shart.
app.UseOutputCache();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<LiveHub>("/hubs/live");

// API "tirikligi": https://<domen>/api ochilganda SPA HTML emas, JSON qaytaradi.
app.MapGet("/api", () => Results.Ok(new
{
    name = "IntellectCRM API",
    status = "ok",
    environment = app.Environment.EnvironmentName,
    timeUtc = DateTime.UtcNow,
}));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// Noma'lum /api/* yo'llari — SPA HTML emas, 404 JSON qaytsin (mobil/klient uchun toza).
app.MapFallback("/api/{**slug}", () => Results.NotFound(new { message = "API endpoint topilmadi" }));

// SPA fallback: barcha hostlar (intellectschool.uz, crm.intellectschool.uz, ...) → React SPA
// (index.html). SPA'ga kirilganda React `RootRedirect` autentifikatsiyasiz foydalanuvchini `/login`ga yuboradi.
// ISTISNO: apex domen (Tenancy:RootDomain va www.<RootDomain>) uchun — agar statik `landing.html` mavjud
// bo'lsa — o'sha qaytariladi (marketing sahifasi, CRM emas). `/landing` yo'li istalgan hostda ham
// (masalan crm.* ustida ham) preview/tekshirish uchun to'g'ridan-to'g'ri landing.html'ga yo'naltiradi.
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var rootDomain = (builder.Configuration["Tenancy:RootDomain"] ?? "").Trim().ToLowerInvariant();

// KARYERA MINI APP — `/vakansiya` (va `/vakansiya/...`) statik `vakansiya.html` sahifasini beradi.
// SPA (React) EMAS: karyera boti Telegram Mini App sifatida ochadigan yengil HTML/CSS/Bootstrap
// sahifa. Fallback'dan OLDIN turadi, aks holda index.html qaytardi.
app.MapGet("/vakansiya/{**rest}", ServeCareerAppAsync);
app.MapGet("/vakansiya", ServeCareerAppAsync);

async Task ServeCareerAppAsync(HttpContext ctx)
{
    var file = Path.Combine(webRoot, "vakansiya.html");
    if (!File.Exists(file)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    // no-cache: Telegram ichidagi WebView eski nusxani ushlab qolmasin.
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.SendFileAsync(file);
}

app.MapGet("/landing", async ctx =>
{
    var landingFile = Path.Combine(webRoot, "landing.html");
    if (!File.Exists(landingFile)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    // no-store: telefon brauzerlari va Cloudflare yangilangan sahifani darhol olsin (sahifa kichik,
    // kesh shart emas). Statik `/landing.html` bilan BIR XIL siyosat — bir yo'ldan eski, boshqasidan
    // yangi nusxa kelib chalkashmasin.
    ApplyLandingNoStore(ctx.Response);
    await ctx.Response.SendFileAsync(landingFile);
});

app.MapFallback(async ctx =>
{
    var host = ctx.Request.Host.Host.ToLowerInvariant(); // portsiz
    var isApexHost = rootDomain.Length > 0 && (host == rootDomain || host == $"www.{rootDomain}");
    var landingFile = Path.Combine(webRoot, "landing.html");

    // Maxfiylik siyosati (/privacy) — apex domenda ham landing.html EMAS, React SPA sahifasi
    // ko'rsatilsin (Google Play talab qiladigan ochiq sahifa istalgan hostda ishlashi uchun).
    var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
    var isPrivacy = path.StartsWith("/privacy");
    var isSertifikatlar = path.StartsWith("/sertifikatlar");

    if (isSertifikatlar)
    {
        var certsFile = Path.Combine(webRoot, "sertifikatlar.html");
        if (File.Exists(certsFile))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ApplyLandingNoStore(ctx.Response);
            await ctx.Response.SendFileAsync(certsFile);
            return;
        }
    }

    if (isApexHost && !isPrivacy && File.Exists(landingFile))
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        // no-store: apex domenda landing aynan shu yo'ldan keladi — statik fayl bilan bir xil siyosat.
        ApplyLandingNoStore(ctx.Response);
        await ctx.Response.SendFileAsync(landingFile);
        return;
    }

    var file = Path.Combine(webRoot, "index.html");
    if (!File.Exists(file)) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.SendFileAsync(file);
});

app.Run();
