# Kameralar (videokuzatuv) qoidalari

"Boshqaruv → Kameralar" (`/admin/boshqaruv/cameras`, ruxsat `cameras`) +
"Sozlamalar → Kamera integratsiya" (`/admin/settings/cameras`, ruxsat `settings.cameras`).
Migratsiya: `AddCameraRecordToggle` (`Camera.RecordEnabled`, `CenterMeta.CameraRecordEnabled`).

> **Qisqacha:** arxiv (orqaga qaytarish) uchun **afzal yo'l — NVR** (§8). U allaqachon 24/7
> yozib turibdi, ya'ni bizda disk ham, 24/7 trafik ham kerak emas. Bizning shlyuz yozuvimiz
> (§2–§5) endi **zaxira yo'l**: NVR bo'lmagan kameralar uchun.

## 1. ENG MUHIM — JONLI KUZATUV va YOZUV ikki BOSHQA narsa

Bir vaqtlar modul bitta edi: kamera qo'shilsa — u avtomatik **24/7 yozib borilardi**. Buni
o'chirishning yo'li yo'q edi (kamerani butunlay o'chirishdan boshqa). Endi ikkiga ajratilgan:

| | Jonli kuzatuv | 24/7 yozuv |
|---|---|---|
| Nima beradi | Kamerani hozir ko'rish (HLS) | Orqaga qaytarish + bo'lakni qirqib olish |
| Narxi | ~0 (faqat kimdir qaraganda) | **disk + markazning kanali 24/7** |
| Kaliti | `CenterMeta.CameraEnabled` | `CenterMeta.CameraRecordEnabled` (**default O'CHIQ**) |

⚠️ **Yozuv o'chirilganda kamera baribir jonli ko'rinaveradi.** Bu ikkisini aralashtirish eng
ko'p uchraydigan tushunmovchilik — UI matnlarida ham har safar shu aytiladi.

## 2. Yozuv QACHON bo'ladi — `CameraRules.ShouldRecord`

```
bosh kalit (CenterMeta.CameraRecordEnabled)
  && kamera faol (Camera.IsActive)
  && shu kameraga yozuv ruxsat etilgan (Camera.RecordEnabled)
```

Ikki daraja ATAYIN:

- **Bosh kalit** — "umuman yozamizmi". O'chirilsa hamma kamera birdan yozuvdan chiqadi
  (har birini qo'lda tahrirlash kerak emas).
- **Kamera bayrog'i** — ISTISNO uchun: kirish eshigi yozilsin, sinf xonasi yozilmasin.

⚠️ **Bosh kalit o'zgarganda BARCHA kameralar shlyuzga QAYTA yuboriladi**
(`SettingsController.SaveCameras`). Aks holda baza o'zgarib, MediaMTX eski holatda qolardi —
ya'ni "yozuvni o'chirdim, lekin disk baribir to'lyapti". Kalit shu yerda qo'llanadi, kamera
keyingi marta tahrirlanganda emas.

⚠️ Migratsiyada `Cameras.RecordEnabled` mavjud qatorlar uchun **`true`** bilan to'ldirilgan
(entity default'i ham `true`). Sabab: bu bayroq "shu kamerani ISTISNO qil" degani. `false`
bilan to'ldirilsa admin bosh kalitni yoqib, "yoqdim — baribir yozilmayapti" holatiga tushardi.

⚠️ Bosh kalit o'qilmasa (`CenterMeta` qatori yo'q) — **yozuv O'CHIQ** (fail-closed).
"Sozlanmagan" holat jimgina 24/7 yozuvni yoqib yubormasin.

⚠️ **NVR HAMMASIDAN USTUN.** Kameraning `NvrChannel > 0` bo'lsa (va NVR sozlangan bo'lsa) u
bosh kalit yoqilgan bo'lsa ham **shlyuzda yozilmaydi** — `CameraRules.ShouldRecordWithNvr`.
Sabab §8 da. Kod har doim shu funksiyani chaqiradi, `ShouldRecord` ni to'g'ridan-to'g'ri emas.

## 3. `sourceOnDemand` — bu optimizatsiya EMAS

`CameraRules.SourceOnDemand(record) => !record`:

| Yozuv | `sourceOnDemand` | Nima bo'ladi |
|---|---|---|
| BOR | `false` | Shlyuz kameraga **doim** ulanib turadi — uzluksiz yozuv uchun shart |
| YO'Q | `true` | Faqat kimdir jonli ochganda ulanadi |

⚠️ Kameralar markazda (Qo'qon), shlyuz esa serverda — ya'ni har RTSP oqimi **markazning
yuklash (upload) kanalidan** o'tadi. `sourceOnDemand = false` da bu kanal har kamera uchun
24/7 band bo'ladi, hech kim qaramasa ham. Yozuv o'chirilganda buni saqlab qolishning ma'nosi
yo'q — shuning uchun ikkalasi BIRGA hal qilinadi, alohida sozlama qilinmagan.

## 4. NEGA yozuv default O'CHIQ — hisob

Taxminiy, 1080p (2MP) H.264 kamera uchun:

| Oqim sifati | Bir kamera / kun | 8 kamera / kun | 8 kamera × 7 kun |
|---|---|---|---|
| 2 Mbit/s | ~21,6 GB | ~173 GB | **~1,2 TB** |
| 4 Mbit/s (Hikvision default) | ~43 GB | ~346 GB | **~2,4 TB** |

Ya'ni yozuv — modulning eng qimmat qismi va u **jimgina** ishlab turardi. Endi u ongli qaror:
kalit, ogohlantirish matni va "nechta kamera yozilmoqda" sanog'i bilan.

⚠️ Yozuv **faqat yoqilgandan keyingi** vaqt uchun bo'ladi. Orqaga ishlamaydi — UI'da shu
aytiladi, aks holda "yoqdim, lekin kechagi yozuv yo'q" savoli chiqardi.

## 5. Yozuv QAYERDA saqlanadi — SERVER diski

`cam-recordings` docker volume, MediaMTX `recordPath`:
`/recordings/%path/%Y-%m-%d_%H-%M-%S-%f`. Eskirganini shlyuz O'ZI o'chiradi
(`recordDeleteAfter` = `Camera.RetentionDays × 24h`, 0 kun = cheksiz).

⚠️ **Bu volume tungi zaxiraga KIRMAYDI** (`backup` servisi faqat `uploads` va
`cti-recordings` ni oladi) — ATAYIN: hajmi bilan zaxira arxivini yo'q qilardi.

### 5.1. Google Drive'ga yozib olish — NEGA to'g'ridan-to'g'ri BO'LMAYDI

Savol ko'tarilgan edi: "serverga emas, Drive'ga yozilsin". Tekshirildi — **24/7 yozuvni
Drive'ga ko'chirish yechim emas**:

1. **MediaMTX faqat LOKAL faylga yozadi** — bulut backend'i yo'q. Drive'ni `rclone mount`
   bilan ulash mumkin, lekin u ham (`--vfs-cache-mode writes`) avval **lokal diskka** yozib,
   fayl yopilganda yuklaydi. Ya'ni lokal disk baribir kerak, ustiga tarmoq uzilishi = yozuvda
   teshik.
2. **Playback UMUMAN ishlamaydi.** `GET {id}/clip` MediaMTX playback serveriga boradi, u esa
   segment fayllarini **diskdan** o'qiydi. Drive'dagi faylni u ko'rmaydi — "istalgan vaqtdan
   30 soniya" olish uchun butun segmentni avval yuklab olish kerak bo'lardi.
3. **Hajm Drive tarifiga sig'maydi.** §4 dagi hisob: 8 kamera ≈ 5 TB/oy o'sish. Business
   Standard = 2 TB, Business Plus = 5 TB — ya'ni tarif bir necha haftada to'ladi. Ustiga
   Drive'da **kuniga 750 GB yuklash** chegarasi bor.
4. **Drive — hujjat ombori, videoarxiv emas**: vaqt bo'yicha indeks yo'q, tezkor qisman
   o'qish yo'q.

**Xulosa (kelishuv):** 24/7 arxiv **NVR'da qoladi** (§8) — Drive'ga ham, bizning serverga ham
ko'chirilmaydi. Drive esa **qo'lda qirqilgan KLIP** uchun ("hodisa" 5 daqiqalik MP4 ≈ 75–150 MB
— Drive'ga aynan mos).

⚠️ Agar kelajakda 24/7 **off-site** arxiv haqiqatan kerak bo'lsa — u Drive emas, **S3-mos
obyekt ombori** (Cloudflare R2 va h.k.) bo'ladi; loyihada `rclone` + `RCLONE_REMOTE`
plumbing'i allaqachon bor (`backup` servisi). Bu ALOHIDA ish sifatida ko'rib chiqilsin —
bu qoidaga tayanadi, uni almashtirmaydi.

## 8. NVR ARXIVI — asosiy yo'l

Markazda **Hikvision NVR** bor va u allaqachon 24/7 o'z disklariga yozib turibdi. Shuning
uchun biz **hech narsa yozmaymiz** — faqat SO'RALGAN bo'lakni undan olib beramiz.

| | Biz yozganda | NVR'dan olganda |
|---|---|---|
| Disk (bizda) | 20–43 GB/kun × kamera | **~0** (vaqtinchalik oqim) |
| Markaz kanali | 24/7 band | faqat kimdir arxiv so'raganda |
| Arxiv chuqurligi | disk sig'gancha | **NVR disklari** (odatda 30–60 kun) |
| Internet uzilsa | yozuvda teshik | NVR lokal yozadi — teshik yo'q |

### Sozlash

1. **Sozlamalar → Kamera integratsiya** → «NVR arxivi»: manzil, RTSP porti (554), ISAPI porti (80).
2. **`.env`**: `NVR_USERNAME` / `NVR_PASSWORD`. ⚠️ Login/parol **bazada saqlanmaydi** —
   turniketdagi bilan bir xil qoida (`AppSecrets`). Javobda faqat "berilganmi" bayrog'i qaytadi.
3. Har kameraga **NVR kanali** (Kameralar → Tahrirlash). **0 = kamera NVR'da yo'q.**
4. Kamera kartasidagi **«Yozuvni tekshirish»** — ISAPI qidiruvi, aloqani sinash uchun.

### Arxiv manbasi — `CameraRules.ArchiveSource`

```
nvrEnabled && cam.NvrChannel > 0   ->  "nvr"    (NVR arxivi)
ShouldRecord(...)                  ->  "local"  (bizning shlyuz yozuvimiz)
aks holda                          ->  "none"   (arxiv yo'q, faqat jonli)
```

⚠️ **Klientda qayta hisoblanmaydi** — server `CameraDto.ArchiveSource` da tayyor beradi.
Ikki joyda hisoblansak, biri o'zgarganda ikkinchisi jimgina eskirib qolardi.

⚠️ **Faol bo'lmagan kamera ham NVR'dan o'qiladi.** "Faol emas" = biz jonli ko'rsatmaymiz;
NVR'dagi ESKI arxiv esa o'z joyida turibdi. Aks holda kamerani o'chirish bilan butun tarix
yo'qolgandek bo'lardi.

### Texnik jihatlar (`HikvisionNvr` — sof, testlangan)

- **RTSP playback manzili:** `rtsp://.../Streaming/tracks/{kanal}01?starttime=…&endtime=…`
  (`{kanal}01` = asosiy oqim, `{kanal}02` = sub).
- ⚠️ **VAQT UTC'DA.** Foydalanuvchi ekranda MARKAZ vaqtini (UTC+5) tanlaydi. Konversiya
  qilinmasa arxiv **5 soat surilgan** holda kelardi — va bu "topilmadi" emas, "boshqa
  vaqtning yozuvi" bo'lgani uchun darhol sezilmasdi. `AppClock.ToUtc` ishlatiladi.
- ⚠️ **Login/parol manzil ICHIDA** (Hikvision boshqa yo'lni qabul qilmaydi) — shuning uchun
  log va xato matnlarida **`HikvisionNvr.Redact` MAJBURIY**. Xato xabari NVR manzilini o'z
  ichiga oladi (diagnostika uchun kerak), ya'ni tozalanmasa parol ekranga chiqib ketardi.
- **ffmpeg remux:** `-c copy` (qayta kodlash YO'Q — 1 GB RAM serverni ushlab qolardi),
  `-movflags frag_keyframe+empty_moov` (oddiy MP4 da indeks fayl OXIRIDA yoziladi, ya'ni
  javobni boshlashdan oldin butun klipni yig'ish kerak bo'lardi), `-rtsp_transport tcp`
  (UDP'da uzoq masofada video to'kilardi).
- ⚠️ ffmpeg **Dockerfile'da** (prod va dev) — busiz NVR rejimida "Yozuvni ko'rish" ishlamaydi.
- ⚠️ ffmpeg jarayoni maxsus `FfmpegStream` bilan o'raladi: klient ulanishni uzganda jarayon
  **o'ldiriladi**. Aks holda yetim ffmpeg'lar to'planib, har biri RTSP oqimini tortib turardi.
- **ISAPI qidiruv** (`ContentMgmt/search`): `searchID` har so'rovda YANGI GUID — bir xil id
  "oldingi qidiruvning keyingi sahifasi" deb talqin qilinadi va ikkinchi so'rov bo'sh qaytarardi.
  Javob **lokal nom** bo'yicha o'qiladi (namespace firmware'ga qarab farq qiladi).

### ⚠️ Diagnostika 200 bilan qaytadi

`GET {id}/nvr-search` xato holatida ham **200** qaytaradi, ichida `ok=false` va SABAB.
Sozlash aynan shu matn bilan qilinadi (manzil? port? login? kanal?) — 502 bilan qaytarilsa
klientda "server xatosi" bo'lib ko'rinib, sabab yo'qolardi.

### Hali SINALMAGAN (haqiqiy qurilmada)

⚠️ Kod real NVR'da **tekshirilmagan** — manzil formati va ISAPI javob sxemasi firmware'ga
qarab farq qilishi mumkin. Shuning uchun «Yozuvni tekshirish» tugmasi va SABAB matnlari
qo'shilgan: sozlash o'sha yerdan ko'rinadi. Birinchi ulanishda tekshiring:
manzil serverdan ochiladimi · port · login/parol · kanal raqami · vaqt mos keladimi
(5 soat surilgan bo'lsa — mintaqa muammosi).

## 6. Sozlama .env dan ham

`Camera__Enabled` / `Camera__RecordEnabled` (compose → `CAMERA_ENABLED` /
`CAMERA_RECORD_ENABLED`) va `Nvr__Enabled` / `Nvr__Host` / `Nvr__RtspPort` / `Nvr__IsapiPort` /
`Nvr__Vendor`. Bo'sh bo'lsa bazadagi qiymat tegilmaydi (env-wins naqshi, `Program.cs`).
Odatda UI'dan boshqariladi.

⚠️ **`NVR_USERNAME` / `NVR_PASSWORD` — FAQAT `.env` da**, bazaga hech qachon yozilmaydi
(`AppSecrets`, turniketdagi naqsh). `EnvKeysWiringTests` ikkalasi compose'da ham,
`.env.example` da ham borligini qulflaydi.

## 7. Yangi ko'rsatkich/amal qo'shsangiz

- Yozuvga tayanadigan yangi endpoint (playback, eksport, AI tahlil) yozsangiz — avval
  `CameraRules.ShouldRecord` ni tekshiring va **sababni OCHIQ ayting**. Aks holda shlyuz
  "topilmadi" deb qaytaradi va foydalanuvchi buni NOSOZLIK deb o'ylaydi (sozlama emas).
- `EnsureAsync` ni `record` bayrog'isiz chaqirmang — bosh kalitni chaqiruvchi hisoblaydi
  (shlyuz bazani bilmaydi). Yagona yo'l: `CamerasController.SyncAsync`.
- **NVR manzilini (login/parol bilan) javobga yoki logga CHIQARMANG** — `HikvisionNvr.Redact`.
- Yozuv fayli manzilini (`/recordings/...`) javobga **chiqarmang** — u faqat shlyuz ichida.
  Fayl har doim avtorizatsiyalangan `clip` endpointi orqali beriladi
  (`.claude/rules/uploads-security.md` dagi bilan bir xil printsip).
