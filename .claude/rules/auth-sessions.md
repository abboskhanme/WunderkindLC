---
description: Kirish va sessiyalar — refresh token, parol tiklash/almashtirishda sessiyalarni tugatish, parol darvozasi, o'quvchi parollari seyfi, «oxirgi kirish», yuz bilan kirish qamrovi.
paths:
  - "WunderkindLC.Infrastructure/Auth/**"
  - "WunderkindLC.Application/Services/Accounts/**"
  - "WunderkindLC.Server/Controllers/GroupCredentialsController.cs"
  - "WunderkindLC.Server/Controllers/StudentsController.Credentials.cs"
  - "WunderkindLC.Server/Controllers/AuthController.cs"
  - "WunderkindLC.Server/Controllers/AdminFaceController.cs"
  - "WunderkindLC.Server/Controllers/StudentFaceController.cs"
  - "WunderkindLC.Application/Services/FaceLoginService.cs"
---

# Kirish va sessiyalar qoidalari

Migratsiya KERAK EMAS — hammasi mavjud `RefreshTokens` / `Users` jadvallarida. Istisno — o'quvchi
parollari seyfi (§6, `StudentCredentials` jadvali, CRM Phase 6 migratsiyasi `CrmAccounts`).

## 1. Sessiya = refresh token NASLI (`FamilyId`)

- Access token (JWT) — 60 daqiqa (`JwtOptions.AccessMinutes`), bazada YO'Q.
- Refresh token — 30 kun, **sirpanuvchi** (har refresh'da rotatsiya, nasl o'sha-o'sha).
- Access tokenda sessiya claim'i bor: **`sfam`** = refresh nasli (`JwtTokenService.SessionClaimType`).
  U login, OTP, yuz tasdig'i va har refresh'da yoziladi. Mobil ilova refresh tokenni body'da
  saqlaydi (cookie yo'q) — "hozirgi sessiya"ni faqat shu claim orqali bilamiz.

## 2. SESSIYALARNI TUGATISH — qachon SHART

`RefreshTokenService.RevokeAllForUserAsync(db, userId, keepFamilyId)` — foydalanuvchining
barcha faol refresh tokenlarini bekor qiladi. **SaveChanges QILMAYDI** — parol o'zgarishi bilan
BITTA tranzaksiyada saqlanadi.

| Amal | Joy | Joriy sessiya |
|---|---|---|
| Admin parolni qayta yaratdi | `Students/Teachers/StaffController.ResetPassword` | — (hammasi) |
| Admin formada yangi parol o'rnatdi | `StudentsController.Update`, `TeachersController.Update`, `StaffController.Update` | — |
| Arxivdan QAYTARISH (yangi parol bilan ham, parolsiz ham) | `StudentsController.Restore`, `TeachersController.Restore` | — (hammasi) |
| O'z parolini almashtirdi | `AuthController.UpdateAccount`, `StudentPortalController.ChangePassword` | **SAQLANADI** |
| Ishonchli qurilma bekor qilindi | `AdminFaceController.RevokeDevice` | — (hammasi) |

⚠️ Nega: busiz yo'qolgan telefon yoki parolni bilib olgan odam parol almashtirilgandan keyin ham
30 kunlik sirpanuvchi refresh bilan CHEKSIZ kirib yuraverardi — parol tiklash bezak edi.
Arxivdan qaytarishda ham xuddi shu: arxivdan OLDINGI sessiya qayta tirilmasin (eski arxivlash
sessiyalarni bekor qilmagan bo'lishi mumkin, shuning uchun parolsiz qaytarishda ham).

⚠️ O'z parolini almashtirganda joriy sessiya `AuthController.EndOtherSessionsAsync` bilan
aniqlanadi: avval web `rt` cookie'si (`Path=/api/auth`, ya'ni faqat `/api/auth/*` da keladi),
bo'lmasa `sfam` claim'i. Aniqlanmasa — HAMMASI bekor (xavfsiz tomon: qayta login).

⚠️ Access token bekor qilinmaydi — u ko'pi bilan 60 daqiqa yashaydi, keyin refresh rad etiladi.

⚠️ **AI ulanishlari (read-only MCP, `McpGrant`) ham shu funksiyada bekor qilinadi — HAR DOIM**, o'z parolini
almashtirganda ham (joriy veb-sessiya saqlanadi, AI ulanishi emas). MCP tokeni har so'rovda grant holatini
tekshiradi, ya'ni ulanish keyingi so'rovdayoq to'xtaydi. `docs/SPEC-mcp.md` §4.

⚠️ Refresh token QAYSI QURILMAGA berilganini saqlamaydi — shuning uchun qurilma bekor qilinsa
o'quvchining hamma sessiyasi tugaydi (boshqa ishonchli telefonda bir marta qayta kiradi, selfi
so'ralmaydi).

**Yangi parol yo'li qo'shsangiz** (`SetInitialPassword` / `SetOwnPassword` chaqiradigan joy) —
`RevokeAllForUserAsync` ni ham chaqiring.

## 3. PAROL DARVOZASI — ko'rish = tiklash = o'rnatish

Yangi parol QAYTARILADI (yoki admin o'zi yozadi) — ya'ni bu akkauntga KIRISH. Shuning uchun
`Credentials`, eksport, `ResetPassword`, formadagi "yangi parol" va **arxivdan qaytarishdagi
`NewPassword`** — BITTA darvoza:
`AdminPermAttribute.HasFullAccess(User, "students.list" | "teachers.list" | "staff")`.

| Yo'l | Darvoza |
|---|---|
| `Students/TeachersController.Credentials`, eksport, **tanlanganlar eksportidagi «Parol» ustuni** | `HasFullAccess` |
| Guruh «Login/parollar» (`GroupCredentialsController`: ro'yxat, `ensure`, Excel) | `HasFullAccess` (+ sinf `[AdminPerm("students.list", ReadRequiresPerm = true)]`) |
| `Students/Teachers/StaffController.ResetPassword` | `HasFullAccess` |
| `Students/Teachers/StaffController.Update` (formadagi yangi parol) | `HasFullAccess` |
| `StudentsController.Restore`, `TeachersController.Restore` — **`NewPassword` berilsa** | `HasFullAccess`, HECH NARSA o'zgartirilishidan OLDIN (2026-10-08) |

- ⚠️ `Restore` ilgari faqat sinf darajasidagi `…list:create` (POST) bilan ochilardi: parolni KO'RA
  olmaydigan xodim arxivdagi akkauntni O'ZI tanlagan parol bilan qaytarib, uning nomidan kirardi.
  Parolsiz qaytarish — avvalgidek sinf ruxsati bilan.

- Ilgari `ResetPassword` faqat `…list:create` bilan ochilardi — parolni KO'RA olmaydigan xodim
  uni qayta yaratib, o'quvchi/o'qituvchi nomidan kirardi.
- Rad etilsa 403 + sabab matni (UI ko'rsatadi).
- `HasFullAccess` — `PermissionRules.HasFullSection`: yalang bo'lim (`students`) sahifa
  kalitini (`students.list`) ham ochadi (`permissions.md` §8).

Tiklashda `SetInitialPassword` (hash + `InitialPassword` + seyf, §6) — aks holda «Login/parol», Excel
eksport va botdagi login xabari ESKI, yaroqsiz parolni ko'rsatardi. Parolning O'ZI auditga
yozilmaydi — faqat "qayta yaratildi" fakti (o'quvchi: `StudentDiscount` turi, login amallari bilan).

### 3.1. `InitialPassword` — HAR muvaffaqiyatli kirishda tozalanadi

Ochiq parol faqat "egasi hali kirmagan" davrda kerak («Login/parol», eksport, botdagi login xabari).
Foydalanuvchi akkauntga KIRGACH u bazada saqlanmaydi (`user.InitialPassword = null`):

| Kirish yo'li | Joy |
|---|---|
| Parol | `AuthController.Login` |
| Bot kodi (OTP) — Mini App («📱 Panel») ham shu yo'l bilan kiradi | `AuthController.OtpLogin` |
| Yuz tasdig'i (to'liq sessiya) | `StudentFaceController.IssueSessionAsync` (zaxira — odatda oldingi parol/OTP login'da tozalangan) |

⚠️ Ilgari faqat PAROL bilan kirishda tozalanardi: bot kodi bilan kiradigan o'quvchining ochiq paroli
(tiklashlardan keyin ham) bazada, eksportda va zaxirada abadiy qolardi. **Refresh — kirish EMAS**,
u tozalamaydi. Tozalangandan keyin botdagi login xabari "Parolingizni avval olgansiz" deydi;
kerak bo'lsa admin parolni qayta yaratadi. Yangi kirish yo'li qo'shsangiz — shu qatorni ham qo'shing.

## 4. «Oxirgi kirish» (`AppUser.LastLoginAt`)

Login/OTP'da va **har refresh'da** yoziladi (`RefreshTokenService.StampLastSeen`, ko'pi bilan
5 daqiqada bir marta). Ilova bir marta login qilib 30 kun refresh bilan ishlaydi — faqat
login'da yozilsa "oxirgi kirish" bir oy eskirib turardi. Format: `yyyy-MM-ddTHH:mm:ss`, markaz vaqti.

## 5. YUZ BILAN KIRISH — FAQAT MOBIL ILOVA (web va Mini App ISTISNO)

`FaceLoginService.DecideAsync`: `deviceId` yo'q → yuz so'ralmaydi. Web va Telegram Mini App
`deviceId` yubormaydi, ya'ni ular **ATAYIN** yuz tekshiruvidan tashqarida.

Sabab: oqim telefon uchun qurilgan — vektor TELEFONDA hisoblanadi, tiriklik (harakatlar) ilovada
o'lchanadi; web klientda selfi oqimi umuman yo'q (brauzerda model ishlamaydi — `FACE-DETEKT-PLAN.md`
§2, §7.4). Web'da talab qilinsa, «📱 Panel» (Mini App, `teacher-portal.md` §5) va sayt orqali
HECH BIR o'quvchi kira olmasdi.

⚠️ **Qabul qilingan oqibat:** parolni bilgan odam web orqali yuz tekshiruvisiz kiradi. Yuz
tekshiruvi "ilovani kim ishlatyapti"ni tasdiqlaydi, akkauntni butunlay yopmaydi.

Yopish kerak bo'lsa (ALOHIDA ish, qaror egasiniki): yoki web uchun selfi oqimi (brauzer modeli
— yangi bog'liqlik), yoki sozlama "yuz yoqilganda o'quvchi faqat ilovadan kiradi" — login
`deviceId`siz kelsa token BERILMAYDI va o'zbekcha sabab ("ilova orqali kiring") qaytadi.

## 6. O'QUVCHI PAROLLARI SEYFI — login/parol DOIM ko'rinadi (CRM Phase 6, 2026-10-12)

Egasining qarori: "o'quvchi login + paroli administratorlarga doim ko'rinsin; guruh bo'yicha eksport".
Ilgari ochiq parol faqat `InitialPassword` da — o'quvchi BIRINCHI marta kirguncha — turardi (§3.1), keyin
faqat "Yangi parol" qolardi. SPEC: `docs/SPEC-crm-upgrade.md` §7 P6.1.

### 6.1. Saqlash — `StudentCredentials` jadvali, AES-256-GCM

- `StudentCredential { UserId (PK), PasswordCipher, Source, UpdatedAt }` — har o'quvchi akkauntiga bitta qator.
  `Source`: `create` · `reset` · `admin_set` · `self_change` · `backfill`.
- Shifrlash — `CredentialVault` (`FaceVault` nusxasi): kalit FAQAT `.env` → `CREDENTIAL_VAULT_KEY`
  (base64, 32 bayt, `openssl rand -base64 32`) → compose `Credentials__VaultKey`. Bazada ham, zaxirada ham
  YO'Q — baza dump'i o'zi parolni ochmaydi. DataProtection kalitlari (`dpkeys` volume) ATAYIN ishlatilmadi:
  u volume zaxiraga kirmaydi.
- ⚠️ **Kalit yo'q/buzuq → HECH NARSA saqlanmaydi** (fail-closed, ochiq matnda hech qachon). Startupda
  ogohlantirish logi, profil kartasi va guruh oynasida «Parollarni saqlash o'chiq: .env da
  CREDENTIAL_VAULT_KEY yo'q» (superadminga; boshqalarga umumiy matn). Bu holatda faqat hali ishlatilmagan
  `InitialPassword` ko'rinadi (eskicha).
- ⚠️ **Kalit yo'qolsa/almashsa** — saqlangan parollar ochilmaydi (`Unprotect` → `null`, istisno YO'Q):
  "Parol saqlanmagan" bo'lib ko'rinadi, loginlar ishlayveradi; guruh oynasidan qayta yaratiladi.
- **FAQAT o'quvchi** (`role = student`). O'qituvchi, xodim, admin va OTA-ONA (`parent`) paroli saqlanmaydi
  (SPEC §10 #10).

### 6.2. Yozish — "unutib bo'lmaydigan" yo'l: `StudentCredentialInterceptor`

Parol FAQAT `AccountExtensions` orqali yoziladi (`SetInitialPassword(plain, source)`, `SetOwnPassword`,
`BlockLogin`; `AccountFactory.CreateAccountFor` birinchisini `create` bilan chaqiradi). Ular hash'dan tashqari
`AppUser.PendingPassword` ni (`[NotMapped]`, `[JsonIgnore]`) qo'yadi, interceptor esa AYNAN o'sha
`SaveChanges` da (bitta tranzaksiya) seyf qatorini yozadi:

| Hodisa | Seyf |
|---|---|
| `set` + kalit bor | qator yoziladi/yangilanadi |
| `set` + kalit YO'Q | eski qator O'CHIRILADI (aks holda kalit qaytganda NOTO'G'RI parol ko'rinardi) |
| `clear` (`BlockLogin` — arxiv) | qator o'chiriladi |
| o'quvchi akkaunti o'chirildi | qator o'chiriladi (`StudentPurge` ham aniq o'chiradi) |
| o'quvchi emas | hech narsa |

Qaror — `StudentCredentials.Decide` (sof, testlangan). Interceptor `CrmAccountsSetup.AddCrmAccounts` da DI
`IInterceptor` sifatida ro'yxatdan o'tadi (Program.cs tegilmaydi). Testlar kontekstni qo'lda quradi —
`TestDb.Sqlite(new StudentCredentialInterceptor(vault))`.

⚠️ **`PasswordHash` ga to'g'ridan-to'g'ri YOZMANG** — interceptor buni ko'rmaydi va seyfdagi parol jimgina
eskiradi. `StudentCredentialLeakTests.No_direct_password_hash_writes_outside_AccountExtensions` qulflaydi.
Yangi parol yo'li qo'shsangiz — `AccountExtensions` ni chaqiring (va §2 bo'yicha sessiyalarni tugating).

Manba (source) chaqiruv joyida: akkaunt yaratish (o'quvchi formasi, import, liddan aylantirish, `Credentials`
GET ning avto-yaratishi) → `create`; har "Yangi parol" va guruh `ensure` → `reset`; formadagi yangi parol va
`Restore` dagi `NewPassword` → `admin_set` (standart); kabinetdagi o'z almashtirishi → `self_change`.

`InitialPassword` xatti-harakati O'ZGARMADI (§3.1: kirishda tozalanadi, botdagi login xabari).

### 6.3. Ko'rsatish — `StudentCredentials.Reveal`

1. Seyf qatori ochildi → o'sha parol (`known`, manba, vaqt);
2. aks holda hali ishlatilmagan `InitialPassword` (manba `initial`);
3. aks holda `known: false` — «Parol saqlanmagan — «Yangi parol» bilan yarating».

⚠️ `InitialPassword` bor bo'lsa u DOIM joriy parol (u hash bilan birga yoziladi va har kirish/o'z
almashtirishda tozalanadi) — undan farq qiladigan seyf qatori eskirgan (faqat kalit o'chiq paytdagi tiklashdan
keyin) va yutqazadi.

**ESKI o'quvchilar:** seyfdan oldin kirib bo'lgan o'quvchi paroli hash'dan TIKLANMAYDI — `known: false`.
Startupdagi bir martalik `StudentCredentialBackfill` (`SeedMarker` `student-credential-backfill-2026-10`)
faqat hali ishlatilmagan `InitialPassword` larni seyfga ko'chiradi; kalit bo'lmasa belgi qo'yilmaydi va keyingi
startupda qayta uriniladi. Qolganlari — guruh oynasidagi bir tugma bilan.

### 6.4. Qayerda ko'rinadi (darvoza — §3, BITTA)

| Joy | Endpoint |
|---|---|
| O'quvchi profili, chap ustun → «Kirish ma'lumotlari» (login + nusxa, parol yashirin 👁 + nusxa, manba/vaqt, «Yangi parol») | `GET students/{id}/credentials` (+ `known`, `source`, `updatedAt`, `vaultConfigured`) |
| Guruh → «O'quvchilar» → «Login/parollar» oynasi | `GET classes/{groupId}/credentials` — tirik, arxivlanmagan a'zolar; HECH NARSA yaratmaydi |
| ↳ «Paroli noma'lumlarga yangi parol yaratish (n)» (tasdiq: «Ularning eski parollari ishlamay qoladi») | `POST …/credentials/ensure` `{ onlyUnknown: true }` — akkaunti yo'qqa akkaunt, `known: false` larga yangi parol (`reset`) + sessiyalar tugaydi; MA'LUM parol tegilmaydi |
| ↳ «Excel yuklab olish» — «№, F.I.SH., Login, Parol, Telegram, Holat», noma'lum → «(saqlanmagan)» | `GET …/credentials/export` → `login_parollar_{guruh}_{sana}.xlsx`, audit `Group` «Login/parollar eksport qilindi: …» (parolsiz) |
| Barcha o'quvchilar eksporti / tanlanganlar eksporti | `GET students/export`, `POST students/export-selected` (Parol ustuni endi `HasFullAccess` ga — ilgari faqat superadmin) |

- Klient nusxasi: `lib/studentCreds.ts` → `canStudentCreds` (admin/superadmin yoki yalang `students` /
  `students.list`). **O'qituvchi HECH QACHON ko'rmaydi** (SPEC §10 #5) — o'qituvchi portali seyfga tegmaydi (test).
- Parol qaytaradigan HAR javob — `Cache-Control: no-store`.
- ⚠️ Parol HECH QACHON ro'yxat endpointlari javobiga (o'quvchilar ro'yxati, guruh a'zolari) qo'shilmaydi.
- Kabinetdagi «Parolni o'zgartirish» ekranida (faqat o'quvchiga): «Eslatma: markaz administratorlari yangi
  parolingizni ko'ra oladi».

### 6.5. Hech qachon TUSHMAYDI

- **Kunlik JSON zaxira** — `BackupService.ExcludedTables` da `studentCredentials` (+ `SecretScrub` nom bo'yicha
  `…Password…` va `byte[]` ni baribir olib tashlaydi). `pg_dump` da shifrlangan holda bor — kalitsiz o'qilmaydi.
- **Read-only MCP** — ustun nomida `Password` bor (`McpReadOnlyGrants.IsSensitiveColumn`) va jadval hech bir
  vosita `Reads` ida yo'q. ⚠️ Ustun NOMINI o'zgartirmang (`PasswordCipher`) — himoya nom bo'yicha.
- **Audit / log** — faqat "qayta yaratildi / eksport qilindi" fakti; `PendingPasswordChange.ToString()` parolni
  chiqarmaydi; arxiv suratlari va API javoblari `PendingPassword` ni serializatsiya qilmaydi (`[JsonIgnore]`).
