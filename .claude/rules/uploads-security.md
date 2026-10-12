# `/uploads` xavfsizlik modeli

Yuklangan barcha fayllar bitta TEKIS papkada: `ContentRoot/uploads`. U docker volume
(`uploads:/app/uploads`) va tungi zaxiraga kiradigan **yagona** papka — shuning uchun fayllarni
undan chiqarish (ko'chirish) zaxiradan tushib qolish degani.

## `/uploads` DARVOZASI — login talab qiladi (`UploadsGuard`)

`/uploads` ilgari autentifikatsiyasiz berilardi. Endi `UploadsGuard` (Server) middleware'i uni
darvozalaydi: **token yo'q → 404** (403 emas — faylning mavjudligini ham tasdiqlamaymiz).

**Nega cookie?** Loyihada JWT Bearer ishlatiladi, brauzer esa `<img src="/uploads/...">` ga
`Authorization` sarlavhasini **yubormaydi**. Shuning uchun `Path=/uploads` ga cheklangan
`up_at` cookie'si qo'yiladi — brauzer uni rasm so'rovlarida o'zi yuboradi va **frontend kodiga
tegish kerak bo'lmadi**. (Loyihadagi mavjud yondashuv bilan bir xil: `/ws` va SignalR ham sarlavha
yubora olmagani uchun tokenni boshqa yo'ldan oladi.)

**Cookie qanday paydo bo'ladi?** Alohida qadam YO'Q: `UseAuthentication` dan keyingi middleware
har qanday avtorizatsiyalangan API so'rovida cookie'ni qo'yadi/tiklaydi. Ya'ni **mavjud
sessiyalar qayta login qilmasdan** ishlayveradi.

Cookie sozlamalari va ular NEGA shunday:
- `HttpOnly` — JS o'qiy olmaydi (XSS bilan o'g'irlanmasin);
- `Path=/uploads` — API so'rovlariga umuman yuborilmaydi;
- HTTPS'da `SameSite=None; Secure` — Telegram Mini App SPA'ni `web.telegram.org` ichida
  IFRAME'da ochadi, u yerdan kelgan rasm so'rovlari "cross-site" hisoblanadi va `Lax` cookie
  **yuborilmasdi**. Dev (http) da `Lax`.

**Token tekshiruvi qo'lda:** statik fayllar pipeline'da `UseAuthentication` dan OLDIN turadi, ya'ni
guard ichida `HttpContext.User` hali bo'sh. Shuning uchun token (sarlavha yoki cookie) qo'lda
tekshiriladi va natija keshlanadi (bitta sahifada o'nlab rasm bo'lishi mumkin).

**OCHIQ qoladigan narsalar — LOGIN'SIZ ko'riladigan sahifalarning rasmlari.** Printsip BITTA:
"ochiq" deb faqat markaz **O'ZI ommaviy ko'rsatayotgan** fayl hisoblanadi. Ro'yxat bazadan olinadi,
1 daqiqa keshlanadi (`UploadsGuard.PublicNamesAsync`), qaror esa `UploadAccessRules` (Application)
da — testlangan.

| Manba | Nima uchun ochiq | Filtr |
|---|---|---|
| `CenterMeta.LogoUrl`, `CareerAbout.LogoUrl` | Logotip login sahifasida, PWA manifestida va ochiq vakansiya sahifasida kerak — foydalanuvchi hali kirmagan | — (hammasi) |
| `LandingTeacher.PhotoUrl` | Landing OMMAVIY sahifa (`GET /api/public/landing-data`, `[AllowAnonymous]`) — o'qituvchi surati mehmonga ko'rsatiladi | **faqat `IsActive`** |
| `LandingCertificate.ImageUrl` | O'sha landing: natija/sertifikat rasmi | **faqat `IsActive`** |
| `LandingTestimonial.AvatarUrl` | O'sha landing: "Ota-onalar va o'quvchilar fikri" bo'limidagi avatar | **faqat `IsActive`** |

⚠️ **NEGA landing rasmlari ochilishi SHART edi:** admin CMS orqali rasmni yuklaydi, manzil
`/uploads/<guid>.png` bo'lib bazaga tushadi. Admin o'zi (login qilgan, `up_at` cookie'si bor) rasmni
KO'RADI, tashqaridagi mehmon esa 404 olib **sinuq rasm** ko'rardi. Ya'ni nosozlik "lokalda ishlaydi,
serverda ishlamaydi" bo'lib ko'rinardi — aslida farq lokal/server emas, **login qilgan/qilmagan** edi.

⚠️ **YANGI LANDING BO'LIMI QO'SHSANGIZ — bu jadvalni ham to'ldiring.** "Fikrlar" (testimonials)
CMS tabi aynan shu sababdan yarim ishlagan: admin fikr qo'shardi, `landing-data` uni qaytarardi,
lekin avatar ro'yxatda yo'q edi — ya'ni bo'lim chizilganda mehmon sinuq rasm ko'rardi. Qoida:
**landing markup'iga `/uploads/...` manzili chiqadigan har bir yangi maydon shu ro'yxatga
qo'shilishi SHART**, aks holda nosozlik faqat login qilmagan mehmonda ko'rinadi (ishlab
chiquvchida esa ishlaydi).

⚠️ **NEGA faqat `IsActive`:** admin sertifikatni yoki o'qituvchini saytdan olib tashlasa, fayl ham
darhol (kesh muddatidan keyin, ko'pi bilan 1 daqiqada) YOPILADI. "Bir marta ommaviy bo'lgan fayl
abadiy ommaviy" qoidasi bu yerda ishlamaydi — filtr `landing-data` endpointidagi filtr bilan
AYNAN bir xil.

⚠️ **CHEGARA — `UploadAccessRules.MaxPublicNames` (2000).** Ro'yxat xotirada turadi va har
`/uploads` so'rovida ko'riladi, ya'ni cheksiz o'sa olmaydi. Chegaradan oshgani **jimgina
tashlanmaydi** — `PublicNamesFrom` nechtasi kirmaganini qaytaradi, guard esa ogohlantirish logi
yozadi ("nega bu sertifikat saytda ko'rinmayapti" savoli javobsiz qolmasin). Guard manzillarni
**LOGOTIPDAN boshlab** yig'adi, shuning uchun chegara faqat landing rasmlarini qirqadi —
login sahifasi hech qachon buzilmaydi.

**Favqulodda o'chirish:** `Uploads:RequireAuth=false` — kodni qayta yig'masdan eski xatti-harakatga
qaytaradi (startupda ogohlantirish logi yoziladi). Rad etilgan har so'rov logga yoziladi
(`/uploads rad etildi: <yo'l> (UA: ...)`) — kutilmagan mijoz shu yerdan ko'rinadi.

⚠️ **MOBIL ILOVALAR:** Flutter ilovalari bu repoda EMAS. Agar ular rasmlarni `Authorization`
sarlavhasisiz yuklasa, ular uchun rasm ko'rinmay qoladi. Guard sarlavhani ham qabul qiladi, ya'ni
ilova HTTP mijoziga token qo'shsa yetadi. Deploydan keyin log'da `/uploads rad etildi` qatorlarini
kuzating.

**Eski qoida hamon kuchda** (endpoint yozganda):

> Manzilni bergan joy = faylni ko'rishga ruxsat bergan joy. Endi u login talab qiladi, lekin
> tizimdagi ISTALGAN foydalanuvchi manzilni bilsa ochadi — ya'ni "kim ko'rishi mumkin" savoli
> baribir muhim.

## Hozirgi himoya qatlamlari

| Qatlam | Nima beradi |
|---|---|
| Fayl nomi `Guid.NewGuid():N` (`UploadGuard.SafeName`) | 128 bit tasodifiylik — taxmin qilib topib bo'lmaydi. Asl nom (`ali-passport.pdf`) hech qachon saqlanmaydi |
| Katalog ro'yxati o'chiq | `UseDirectoryBrowser` yo'q; fayl nomlarini ro'yxatlaydigan endpoint ham yo'q |
| `Referrer-Policy: no-referrer` | Manzil "Referer" orqali tashqi saytga sizib chiqmaydi |
| `Cache-Control: private` | Cloudflare/proxy umumiy keshida saqlanmaydi |
| `PrivateFolderFileProvider` | `uploads/certificates` va `uploads/face` STATIK yo'l bilan berilmaydi (qarang: `tests.md`) |

⚠️ **So'rov loglari YO'Q:** `appsettings.json` da `"Microsoft.AspNetCore": "Warning"` — ya'ni
`/uploads` ga kim murojaat qilgani **yozilmaydi**. Demak "sizdimi?" degan savolga o'tmishga qarab
javob berib bo'lmaydi. Buni yoqish — alohida ish.

## Kim qaysi fayl manzilini oladi (darvozalar)

### Sertifikatlar — API orqali, fayl statik yo'ldan BERILMAYDI
- Eski HTML sertifikatlar (`CertificatesController`) va yangi test sertifikatlari
  (`TestResultsController` / `TeacherPortalController`) — fayl faqat avtorizatsiyalangan
  endpointdan, diskdan o'qib beriladi.
- ⚠️ `CertificatesController` da ilgari admin marshrutlarida **yalang `[Authorize]`** turardi —
  ya'ni istalgan o'quvchi/ota-ona begona o'quvchining sertifikat faylini yuklab olardi, sertifikat
  yaratardi va shablonlarni o'chira olardi. Endi har bir admin amalida
  `[AdminPerm("students.list", ReadRequiresPerm = true)]`. Controllerda ochiq/o'quvchi/admin marshrutlari
  aralash bo'lgani uchun atribut **metod darajasida** qo'yilgan.

### Telegram xabar biriktirmalari va yutuq rasmlari — `uploads/messaging/` (2026-10-12)
- Fayl statik yo'ldan **berilmaydi** (`PrivateFolderFileProvider`, `Uploads:PublicCertificates` ga BOG'LIQ EMAS —
  har doim yopiq). O'qiydiganlar: outbox ishchisi (Telegram'ga OQIM bilan) va ruxsatli endpointlar —
  `GET api/admin/messaging/attachments/{id}/content` (`messages.broadcast`, o'qish ham), `GET api/admin/achievements/{id}/photo`
  (`students.list`), kabinet `GET api/student/achievements/{id}/photo` (egalik `TargetAsync`). Javob `Cache-Control: private, no-store`.
- Saqlangan manzil (`MessageAttachment.FileUrl`) HECH QACHON javobga chiqmaydi; diskka yo'l faqat
  `MessageAttachmentStore.PhysicalPath` (naqsh `/uploads/messaging/<32 hex>.<ruxsat etilgan kengaytma>`) orqali.
- Yuklash: kengaytma + Content-Type + sehrli baytlar uchalasi mos bo'lishi shart; nom GUID (`messaging.md` §0.5).
- Papka `uploads/` ichida — tungi arxivga o'z-o'zidan kiradi; 60 kundan eski, Telegram'ga yuklangan va ishlatilmayotgan
  fayl o'chiriladi (yutuq rasmi — hech qachon).

### Yuz bilan kirish selfilari — `uploads/face/`
- Fayl statik yo'ldan **berilmaydi** (`PrivateFolderFileProvider`), faqat
  `GET /api/admin/face/checks/{id}/image` va `GET /api/admin/face/profile/{studentId}/image`
  (`[AdminPerm("students.face", ReadRequiresPerm = true)]`). DTO'lardagi `imageUrl`/`sampleUrl`
  aynan shu API yo'lini qaytaradi — `/uploads/...` manzili **hech qachon** javobga tushmaydi.
- ⚠️ `Uploads:PublicCertificates=true` favqulodda kaliti bu papkaga **TA'SIR QILMAYDI**: u faqat
  sertifikatlarni ochadi. Biometrik suratni "vaqtincha ochib qo'yish" varianti yo'q.
- Papka kunlik zaxira arxividan ham chiqarilgan (`docker-compose` → `tar --exclude`), sabab
  `DEPLOY.md` §6.1 da.

### Xodim (staff) uchun O'QISH darvozasi
`AdminPermAttribute` da GET/HEAD/OPTIONS xodimga **ataylab ochiq** — bo'limlararo o'qish buzilmasin
(masalan Moliya → o'quvchilar ro'yxati). Lekin nozik HUJJAT qaytaradigan bo'limlarda bu xavfli, shu
sabab ikkita vosita bor:

1. **`[AdminPerm("bolim", ReadRequiresPerm = true)]`** — o'qish uchun ham bo'lim ruxsati talab
   qilinadi. Yoqilgan joylar (u yerda bo'limlararo o'qish kerak emasligi tekshirilgan):
   - `ContractsController` — shartnoma `PdfUrl`/`DocxUrl`
   - `CareerController` — nomzod `CvUrl`
   - `AiCheckController` — o'quvchi ovoz yozuvi `AudioUrl`
2. **Javobni tozalash** — bo'limlararo o'qish HAQIQATAN kerak bo'lgan joyda
   (`StudentsController`: moliya/qabul o'quvchilar ro'yxatini o'qishi kerak). U yerda
   `RedactDocs` `students.list` ruxsati yo'q xodimga `BirthCertificateUrl` (surat) va
   `ParentPassportUrl` (passport skani) manzillarini **bermaydi**; ism/telefon/balans qoladi.
   UI'da surat o'rniga bosh harflar chiqadi — ish buzilmaydi.
   DIQQAT: tozalanadigan endpointlar `AsNoTracking` bo'lishi SHART — aks holda bo'shatilgan
   maydonlar tasodifan bazaga yozilib ketishi mumkin.

Ruxsat qoidasining O'ZI — `Application/Services/PermissionRules.cs` (sof funksiyalar, testlangan:
`PermissionRulesTests`). `AdminPermAttribute` faqat rol tekshiruvini qo'shadi. Nom bo'yicha
adashish yo'q: `"students-arxiv"` ruxsati `"students"` ni ochmaydi.

## Yangi endpoint yozganda tekshiring

- Javobda `/uploads/...` manzili bormi? Bo'lsa — uni ko'rishi mumkin bo'lgan HAR BIR rol shu faylni
  abadiy saqlab qolishga haqlimi?
- Agar yo'q bo'lsa: `ReadRequiresPerm` yoqing, yoki manzilni javobdan tozalang, yoki faylni
  avtorizatsiyalangan download endpoint orqali bering (manzil o'rniga).
- `[AllowAnonymous]` sinf darajasidagi `[Authorize]` va `[AdminPerm]` ni **ham** bekor qiladi.
  `/api/admin/...` yo'lida u hech qachon turmasligi kerak (ilgari `GradingController` da shunday
  xato bor edi — studentId bilan baholash statistikasi login'siz ochiq edi).

## BAZADAGI MANZIL → DISKDAGI FAYL (o'qish, yuborish, o'chirish) — `UploadUrlRules`

Klientdan kelgan manzil (`TestResult.PdfUrl`, `JobApplication.CvUrl`, `Book.CoverUrl` ...) keyin
server tomonidan FAYL sifatida o'qilsa (botga yuborilsa) yoki O'CHIRILSA — ikki qatlam SHART:

1. **Yozishda** — server O'ZI yaratgan nom naqshi: `IsServerPdf` (`/uploads/<32 hex>.pdf`),
   `IsCareerCv` (`/uploads/cv-<32 hex>.pdf`), `IsTestCertificateFile`. Aks holda 400.
2. **O'qish/o'chirishda** — yo'l `UploadUrlRules.ResolveFlat` (faqat `uploads/` ning O'ZI; pastki papka,
   `..`, absolut yo'l → `null`) yoki `StudentPurge.ResolveUploadPath` / `FaceStorage.ResolvePath` orqali.
   HECH QACHON `Path.Combine(ContentRootPath, url.TrimStart('/'))` emas.

⚠️ 2026-10-08 (KRITIK; 2026-10 dan onlayn test boti butunlay olib tashlangan): onlayn test boti `PdfUrl` ni ilova ildiziga nisbatan tekshiruvsiz o'qirdi —
o'qituvchi `"/../.env"` yozib serverning maxfiy faylini botdan olardi. Kitob muqovasi (`CoverUrl`)
ham xuddi shu yo'l bilan o'qilardi. O'chirishda esa: nomzod CV manzili va guruhdagi test PDF'i
BEGONA faylga (shartnoma, dars PDF'i) ishora qilib, uni diskdan o'chirtira olardi — endi faqat naqshga
mos VA boshqa joyda ishlatilmagan fayl o'chiriladi (`career.md` §6, `tests.md`).

**O'chirishdan oldin "hali kimdir ishlatyaptimi"** — `UploadReferences.InUseAsync` (o'quvchi hujjatlari,
o'qituvchi/foydalanuvchi surati, shartnoma va andozalari, test PDF'i va sertifikat andozasi, dars
materiallari, kitob muqovasi/chek, CV, taklif rasmlari, AI tekshiruv audiosi, landing rasmlari,
logotiplar). `SaveChanges` dan KEYIN chaqiriladi (o'chirilgan yozuv hisobga olinmasin) yoki
`exceptStudentId` bilan. Yangi entity'da `/uploads` manzili saqlansa — shu ro'yxatga ham qo'shing.

| Qayerda o'chiriladi | Naqsh | Ishlatilish tekshiruvi |
|---|---|---|
| Shartnoma (almashtirish, o'chirish) — `ContractsController` | `IsServerPdf` / `IsServerUpload(.docx)`; PDF biriktirishda ham `IsServerPdf` (400) | `InUseAsync`, saqlangandan keyin |
| O'quvchini butunlay o'chirish — `StudentPurge` (surat, passport) | `IsServerUpload` (umumiy yuklash nomi) | boshqa o'quvchi + `InUseAsync` |
| Guruh o'chirish — test PDF'i, sertifikat | `IsServerPdf`, `IsTestCertificateFile` | boshqa test / shartnoma / dars materiali |
| Ariza o'chirish — CV | `IsCareerCv` | boshqa ariza |

Naqshga mos KELMAGAN eski manzil o'chirilmaydi — ortiqcha fayl qolgani begona hujjatni o'chirib
yuborishdan xavfsizroq.

## Qatlamlar (kim nimani ko'radi)

1. **Tashqaridagi begona** — `/uploads` dan faqat LOGOTIP va landing sahifasining FAOL rasmlarini
   (o'qituvchi surati, sertifikat) oladi, boshqa hech narsani (404).
2. **Tizimga kirgan foydalanuvchi** — manzilni bilsa faylni oladi. Shuning uchun manzilni
   javobga qo'shishdan oldin "bu rol buni ko'rishi kerakmi" savoli baribir muhim (yuqoridagi
   darvozalar shu uchun).
3. **`uploads/certificates`** — hech kimga statik yo'ldan berilmaydi (hatto adminga ham), faqat
   avtorizatsiyalangan download endpointlari orqali.

## OCHIQ MEDIA — `uploads/marketing-public/` (Instagram kontent moduli)

Bu **yagona istisno**: `/uploads` ostidagi bitta papka **login TALAB QILMAYDI**. Qoidaning
o'zi buzilmadi — istisno alohida papka, alohida marshrut va alohida qulf bilan chegaralangan.

### Nega ochiq bo'lishi SHART edi

Instagram (Meta) postni joylashda media faylni **O'ZI yuklab oladi**: manzil **ochiq HTTPS**
bo'lishi kerak — autentifikatsiya, IP cheklov va redirect **ishlamaydi**
(`KENGAYTIRISH-PROMPT.md` §5.6). `UploadsGuard` ortidagi fayl Meta uchun 404 bo'lgani uchun
har post `2207052` («Media yuklab bo'lmadi») bilan yiqilardi — ya'ni **busiz butun kontent
moduli ishlamaydi**. Landing rasmlaridagi bilan bir xil mantiq: markaz bu faylni **O'ZI
ommaga chiqarmoqchi**, farqi shundaki ko'rsatuvchi bizning saytimiz emas, Instagram.

### Nima TUSHADI / nima TUSHMAYDI

| | |
|---|---|
| **Tushadi** | FAQAT rejalashtirilgan post uchun yuklangan JPEG rasm yoki MP4/MOV video |
| **Tushmaydi** | hujjat, shartnoma, sertifikat, selfi, o'quvchi surati, CV, ovoz yozuvi — **hech narsa**. Boshqa modullar bu papkaga yozmaydi |

Yozadigan yagona joy — `POST /api/admin/instagram/content/media`
(`InstagramController.Media.cs`, ruxsat `marketing.content`). O'chirish —
`DELETE .../content/media`.

### Qanday chegaralangan (qatlamlar)

| Qatlam | Nima beradi |
|---|---|
| Statik marshrut **shu jismoniy papkaga ildizlangan** (`Program.cs`) | `uploads/` ning qolgani, `uploads/certificates` va `uploads/face` bu provayder uchun **umuman mavjud emas** |
| Marshrut **`UploadsGuard` dan OLDIN**, lekin darvoza joyida | fayl topilsa shu yerda yakunlanadi; topilmasa so'rov darvozaga tushib 404 bo'ladi |
| `ServeUnknownFileTypes = false` + **yopiq MIME xaritasi** (`.jpg/.jpeg/.mp4/.mov`) | papkaga tasodifan tushgan `.html`/`.svg` **berilmaydi** — aks holda bu bizning domenimizda saqlangan XSS bo'lardi |
| Fayl nomi `{Guid:N}{kengaytma}` | 128 bit tasodifiylik; asl nom saqlanmaydi (`UploadGuard.SafeName` naqshi) |
| Yuklashda **uchta mustaqil tekshiruv** | kengaytma · `Content-Type` · fayl boshidagi **sehrli baytlar**. Uchalasi mos kelishi SHART — faqat kengaytmaga ishonish `.jpg` nomli HTML qo'yish yo'lini ochib berardi |
| Hajm chegarasi | `IgPublishConst.MaxImageBytes` / `MaxReelsBytes` — Meta'niki bilan bitta joydan |
| O'chirishda `MarketingPublicMedia.SafeStoredName` | "nima ruxsat etilgan" naqshi (**32 hex + ruxsat etilgan kengaytma**): `..`, absolut yo'l, teskari chiziq, papka ajratkichi va begona papka manzili o'z-o'zidan rad etiladi. Ustiga `Path.GetFullPath` bilan qayta tekshiruv |
| Sarlavhalar | `Cache-Control: public` (**ataylab** — Meta/CDN keshlashi mumkin), `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` |
| Audit | yozuv bor, lekin **fayl manzili/nomi YOZILMAYDI** (§1: manzil ochiq — tarixni ko'rgan har kim faylni abadiy olib qolardi). `EntityId` — o'zgarmas `content-media` |

⚠️ **ZAXIRA:** papka `uploads/` ichida, ya'ni tungi arxivga **o'z-o'zidan kiradi** — alohida ish
kerak emas (`uploads/face` dan farqli: u ATAYIN `--exclude` qilingan).

⚠️ **Qabul qilingan xavf:** manzilni bilgan har kim (Meta ham, boshqa ham) faylni oladi va uni
`Cache-Control: public` sababli oraliqdagi kesh saqlab qolishi mumkin. Bu **ataylab**: fayl
baribir Instagram'da ommaga chiqadi. Shu sababdan bu papkaga **ommaviy bo'lmasligi mumkin
bo'lgan hech narsa** qo'yilmaydi.

⚠️ **Qabul qilingan xavf (symlink):** `PhysicalFileProvider` symlink'ni kuzatadi. Papkaga
symlink qo'yish uchun serverda shell kerak (u holda baribir hamma narsa ochiq), yozish oqimimiz
esa faqat GUID nomli oddiy fayl yaratadi — shuning uchun qo'shimcha chora ko'rilmadi.

### Yangi OCHIQ marshrut qo'shishdan oldin

1. **Fayl haqiqatan OMMAVIYmi?** "Login qilgan hamma ko'rishi mumkin" ≠ "internetdagi hamma
   ko'rishi mumkin". Javob "yo'q" bo'lsa — imzolangan vaqtinchalik URL yoki avtorizatsiyalangan
   download endpointi (Variant B).
2. **Marshrut ildizi ANIQ bitta papkami?** Hech qachon `uploads/` ildizini ochmang.
3. **MIME ro'yxati yopiqmi?** `ServeUnknownFileTypes` yoqilgan ochiq marshrut = XSS yuzasi.
4. **Fayl nomini KIM belgilaydi?** Faqat server (GUID). Foydalanuvchi nomi hech qachon.
5. **`MarketingPublicMediaTests` ni yangilang** — u darvozasiz `UseStaticFiles` bloklari
   AYNAN BITTA ekanini qulflaydi va ikkinchisi qo'shilsa qizaradi. Bu **ataylab**: yangi ochiq
   marshrut jimgina paydo bo'lmasin, qaror ko'rib chiqilsin.
