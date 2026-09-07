# Kameralar (videokuzatuv) qoidalari

"Boshqaruv → Kameralar" (`/admin/boshqaruv/cameras`, ruxsat `cameras`) +
"Sozlamalar → Kamera integratsiya" (`/admin/settings/cameras`, ruxsat `settings.cameras`).
Migratsiya: `AddCameraRecordToggle` (`Camera.RecordEnabled`, `CenterMeta.CameraRecordEnabled`).

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

**Xulosa (kelishuv):** yozuv — LOKAL va QISQA muddatli (yoki umuman o'chiq); Drive esa
**qo'lda qirqilgan KLIP** uchun ("hodisa" 5 daqiqalik MP4 ≈ 75–150 MB — Drive'ga aynan mos).

⚠️ Agar kelajakda 24/7 **off-site** arxiv haqiqatan kerak bo'lsa — u Drive emas, **S3-mos
obyekt ombori** (Cloudflare R2 va h.k.) bo'ladi; loyihada `rclone` + `RCLONE_REMOTE`
plumbing'i allaqachon bor (`backup` servisi). Bu ALOHIDA ish sifatida ko'rib chiqilsin —
bu qoidaga tayanadi, uni almashtirmaydi.

## 6. Sozlama .env dan ham

`Camera__Enabled` / `Camera__RecordEnabled` (compose → `CAMERA_ENABLED` /
`CAMERA_RECORD_ENABLED`). Bo'sh bo'lsa bazadagi qiymat tegilmaydi (env-wins naqshi,
`Program.cs`). Odatda UI'dan boshqariladi.

## 7. Yangi ko'rsatkich/amal qo'shsangiz

- Yozuvga tayanadigan yangi endpoint (playback, eksport, AI tahlil) yozsangiz — avval
  `CameraRules.ShouldRecord` ni tekshiring va **sababni OCHIQ ayting**. Aks holda shlyuz
  "topilmadi" deb qaytaradi va foydalanuvchi buni NOSOZLIK deb o'ylaydi (sozlama emas).
- `EnsureAsync` ni `record` bayrog'isiz chaqirmang — bosh kalitni chaqiruvchi hisoblaydi
  (shlyuz bazani bilmaydi). Yagona yo'l: `CamerasController.SyncAsync`.
- Yozuv fayli manzilini (`/recordings/...`) javobga **chiqarmang** — u faqat shlyuz ichida.
  Fayl har doim avtorizatsiyalangan `clip` endpointi orqali beriladi
  (`.claude/rules/uploads-security.md` dagi bilan bir xil printsip).
