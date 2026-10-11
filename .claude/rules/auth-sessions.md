---
description: Kirish va sessiyalar — refresh token, parol tiklash/almashtirishda sessiyalarni tugatish, parol darvozasi, «oxirgi kirish», yuz bilan kirish qamrovi.
paths:
  - "WunderkindLC.Infrastructure/Auth/**"
  - "WunderkindLC.Server/Controllers/AuthController.cs"
  - "WunderkindLC.Server/Controllers/AdminFaceController.cs"
  - "WunderkindLC.Server/Controllers/StudentFaceController.cs"
  - "WunderkindLC.Application/Services/FaceLoginService.cs"
---

# Kirish va sessiyalar qoidalari

Migratsiya KERAK EMAS — hammasi mavjud `RefreshTokens` / `Users` jadvallarida.

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
| `Students/TeachersController.Credentials`, eksport | `HasFullAccess` |
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

Tiklashda `SetInitialPassword` (hash + `InitialPassword`) — aks holda «Login/parol», Excel
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
