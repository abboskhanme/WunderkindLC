# «Aktiv muzlatish» qoidalari

Yangi o'quv yiliga o'tishda qilinadigan muzlatish. Migratsiya: `AddStudentGroupYearFreeze`
(`StudentGroup.YearFreeze`, bool).

## 1. ENG MUHIM — bu KOSMETIK belgi

`Status` baribir **`"frozen"`** bo'lib qoladi. Yangi status qiymati **KIRITILMAGAN**.

Sabab: `"frozen"` ni o'nlab joy tekshiradi — oylik hisobi (`MembershipLifecycle.BillableInMonth`,
`TuitionService`), jurnal va davomat oynasi (`StudentJournalBuilder`, `GroupSnapshotBuilder`),
baholash (`GradingService` — muzlatilganlar baholanmaydi), maosh (`SalaryLedger`), kurs
analitikasi (`CourseAnalytics`), SMS auditoriyasi, to'lov eslatmalari. Yangi status qiymati
shularning HAMMASINI jimgina buzardi.

Shuning uchun farq faqat **qo'shimcha bayroqda**: `YearFreeze = true`.

⚠️ **Hisob-kitobga, o'quvchining aktiv/aktiv emasligiga va boshqa hech qanday mantiqqa
TA'SIR QILMAYDI.** Oddiy muzlatish bilan aynan bir xil ishlaydi — `FreezeCoreAsync` bitta,
`MembershipBilling.SettleFreezeAsync` o'sha-o'sha. Yangi tekshiruv (`if (YearFreeze) ...`)
mantiqqa **qo'shilmasin**: qo'shilsa, bu qoida buziladi.

## 2. Nima uchun kerak

Savol bitta: **"yangi o'quv yiliga nechta o'quvchi bilan o'tyapmiz"**. Yoz oxirida
o'quvchilar ommaviy muzlatiladi; ular orasida "tashlab ketgan" ham, "sentyabrda qaytadigan"
ham bor. Ikkalasi ham `frozen` bo'lgani uchun ro'yxatda ajratib bo'lmasdi.

Endi yangi yil uchun muzlatilganlar o'quvchilar ro'yxatida alohida belgi va alohida holat
filtri bilan sanaladi.

## 3. RUXSAT — qo'yish superadmin, olib tashlash odatdagidek

| Amal | Kim |
|---|---|
| Aktiv muzlatish (bayroqni QO'YISH) | **faqat superadmin** |
| Aktivlashtirish / sinovga qaytarish (bayroqni olib tashlash) | admin ham, superadmin ham, `classes:create` bo'lgan xodim ham |

⚠️ Tekshiruv **hard rol** bilan: `User.IsInRole(Roles.SuperAdmin)`. `[AdminPerm]` bu yerda
YARAMAYDI — u `admin` ni ham o'tkazib yuboradi (`AdminPermAttribute.cs`, "to'liq huquqli
rollar — cheklovsiz"). Frontendda ham shu sabab `can()` emas, `user?.role === 'superadmin'`.

⚠️ **Yangi ruxsat kaliti YO'Q** — `adminPermissions` katalogi va `PermissionCatalogTests`
tegilmaydi. Amal delegatsiya qilinmaydi (`retentionBonus` dagidek "superadmin yoki ruxsat
berilgan xodim" emas): foydalanuvchi aniq "faqat superadmin" dedi.

⚠️ Ruxsatsiz so'rov **403** bilan rad etiladi, JIMGINA oddiy muzlatishga TUSHIRILMAYDI.
Sabab: "Aktiv muzlatish" tugmasi bosilib, natijada oddiy muzlatish bo'lib qolsa — hisobot
yolg'on chiqardi (aynan shu ajratish uchun qilingan modul).

## 4. Bayroq QAYERDA qo'yiladi va QAYERDA tozalanadi

**Qo'yiladi** — faqat `FreezeCoreAsync(..., yearFreeze: true)` orqali:
`POST {id}/members/{studentId}/freeze` va `POST .../bulk-freeze` (guruh ichida ham,
guruhsiz — barcha guruhlar bo'yicha ham), so'rov tanasida `yearFreeze: true`.
**Yangi marshrut YO'Q** — mavjud endpointlarga bitta maydon qo'shilgan.

**Tozalanadi** — `FrozenAt` bo'shatilgan HAR joyda: aktivlashtirish (`ActivateCoreAsync`),
sinovga qaytarish (`ReturnToTrial`), guruhga qayta qo'shish (`AddMember`) va guruh
almashtirishning MAQSAD guruh tomoni (`TransferMember`, `toSg.FrozenAt = ""` yonida — aks
holda o'tgan yilgi belgi FAOL a'zolikda yolg'on turib qolardi).

**Qo'yilmaydi** — `TransferMember` ning muzlatish tomoni, `Close` (guruhni yopish),
`CompleteAndTransfer` (sertifikat bilan tugatish): ular boshqa amallar, o'quv yiliga
o'tish emas.

⚠️ **ALLAQACHON muzlatilgan a'zolikni «aktiv muzlatish»ga AYLANTIRIB BO'LMAYDI.**
`FreezeCoreAsync` `Status == "frozen"` da darhol qaytadi, ommaviy amalda esa
`MembershipBulk.IsEligible` faqat `active`/`trial` ni o'tkazadi (qayta muzlatish qisman
to'lovni IKKI marta yozardi). Kerak bo'lsa: avval aktivlashtirish, keyin aktiv muzlatish.
Bu ATAYIN: yil oxirigacha allaqachon ketib qolganlar "yangi yilga o'tayotganlar" emas.

## 5. Ko'rinishi

`Student.MemberState` yangi qiymat: **`"yearFrozen"`**.
Ustunlik: **`active > trial > yearFrozen > frozen > ""`** (bir o'quvchi bir necha guruhda
turlicha bo'lsa). `StudentGroupState.YearFreeze` — guruh kesimida.

⚠️ `Status` "frozen" bo'lib qolgani uchun **eski mantiq buzilmaydi**: o'quvchilar ro'yxatidagi
"guruh nomlari" ustuni muzlatilganlarni baribir yashiradi, `Active` baribir false.

Rang — **indigo**: sky (oddiy muzlatish) va violet (sinov) dan ajralib tursin.
Yorliqlar: amal «Aktiv muzlatish», belgi «Aktiv muzlatilgan».

Sabab katalogi — mavjud **`freeze`** kategoriyasi (yangi kategoriya qo'shilmagan).

## 6. Audit

Yozuv oddiy muzlatishnikidek `Membership` turida (`EntityId = "{groupId}:{studentId}"`),
lekin matni boshqacha: **«Aktiv muzlatildi (yangi o'quv yili) …»**. Shu sababdan
"O'zgarishlar tarixi"da ikki xil muzlatishni matn bo'yicha ajratish mumkin.

⚠️ `GroupSnapshotBuilder` sabablarni audit matnidan `"Muzlatildi"` prefiksi bo'yicha ajratib
oladi — yangi matn qo'shilganda o'sha joyni ham tekshiring.

## 7. Yangi ko'rsatkich qo'shsangiz

- Pul, davomat, baho, maosh yoki analitikada `YearFreeze` ni **TEKSHIRMANG** — u yerda u
  yo'q (§1). Faqat ro'yxat/ko'rinish qatlamida ishlatiladi.
- Yangi muzlatish yo'li qo'shsangiz — bayroqni tozalash kerakmi, yo'qmi degan savolga javob
  bering (§4 jadvali).
