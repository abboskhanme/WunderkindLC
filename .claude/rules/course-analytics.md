# Kurslar analitikasi qoidalari

"O'quv bo'limi → Kurslar analitikasi" (`/admin/subjects/analitika`, ruxsat `schedule.analytics`).
Migratsiya KERAK EMAS — modul mavjud jadvallardan (`StudentGroup` × `Group.CourseId`) hisoblanadi.

## 1. Eng muhim qoida — "ketdi" nima?

O'quvchi bir kursda bir necha guruhda bo'lishi mumkin: parallel (ikki guruh birga) yoki ketma-ket
(guruh almashtirish, darajani tugatib keyingisiga o'tish). **Har a'zolikni alohida sanasak, guruh
almashtirish "ketdi + keldi" bo'lib ko'rinardi** va hisobot qo'rqinchli, ammo yolg'on churn
ko'rsatardi.

Shuning uchun har `(o'quvchi, kurs)` juftligi uchun a'zoliklar **ORALIQLARGA** aylantiriladi va
ustma-ust/ketma-ket tushganlari birlashtiriladi (`CourseAnalytics.MergeIntervals`):

- **Keldi** = oraliq BOSHLANDI;
- **Ketdi** = oraliq TUGADI va oraliqni yopgan a'zolik `Status != "completed"`;
- **Tugatdi** = oraliq TUGADI va yopgan a'zolik `"completed"` (sertifikat bilan) — **churn EMAS**.

`MergeGapDays = 7`: ikki a'zolik orasidagi bir haftagacha tanaffus "chiqib ketish" hisoblanmaydi
(guruh almashtirish odatda o'sha kuni/ertasiga, lekin qo'lda qilinsa cho'zilishi mumkin).

⚠️ Parallel guruhda `Completed` bayrog'i **oraliqni YOPGAN** a'zolikdan olinadi, oxirgi qayta
ishlangandan emas — aks holda erta tugagan parallel a'zolik butun oraliqni "tugatgan" deb
belgilab qo'yardi.

## 2. "Oy oxirida faol" — SANALAR bo'yicha tiklanadi

`StudentGroup.Status` **joriy** holatni bildiradi: bugun muzlatilgan a'zolik o'tgan oyda faol
bo'lgan bo'lishi mumkin. Shuning uchun tarix `WasActiveAt` bilan sanalardan tiklanadi:

```
ActivatedAt <= sana  &&  (LeftAt bo'sh yoki > sana)  &&  (FrozenAt bo'sh yoki > sana)
```

⚠️ Yangi tarixiy ko'rsatkich qo'shsangiz — `Status` ga QARAMANG, shu funksiyadan foydalaning.

## 3. Takrorsizlik

Bir o'quvchi bir kursning ikki guruhida bo'lsa ham **bitta** sanaladi (kurs bo'yicha distinct
`StudentId`). Bu barcha sanoqlarga tegishli: joriy holat, oylik "keldi/ketdi", oy oxiridagi faollar.

**ISTISNO — pul:** `MonthlyRevenue` a'zoliklar bo'yicha yig'iladi, chunki ikki guruhda o'qiydigan
o'quvchi ikki marta to'laydi.

⚠️ **Arxivlangan O'QUVCHI joriy sanoqlarga KIRMAYDI** (faol/sinov/muzlatilgan/hozir va kesishuv,
2026-10-07): arxiv = o'quvchi ketdi, eski yozuvlarda esa a'zoligi hali tirik bo'lishi mumkin.
`MonthlyRevenue` TEGILMAYDI (a'zolik narxi — haqiqiy pul). Shu sabab keshning bog'liq turlariga
`Student` ham qo'shilgan (arxivlash sanoqni o'zgartiradi).

## 4. Kesishuv (kurslar birga olinishi)

FAQAT **faol** a'zoliklar (`Status == "active"`) bo'yicha — savol "kim haqiqatan bir nechta kursda
o'qiyapti". Sinovdagi va muzlatilganlar kirmaydi.

- taqsimot: 1 kurs → N o'quvchi, 2 kurs → M, ...
- juftliklar: kalit TARTIBLANGAN (`(A,B)` va `(B,A)` bitta qator) — aks holda jadvalda takror chiqardi.

## 5. Ma'lumot manbai va kesh

- **Arxivlangan guruhlar HAM yuklanadi** — ular orqali o'tgan o'quvchilar tarixi (keldi/ketdi)
  yo'qolmasin. "Guruhlar soni" esa faqat arxivlanmaganlardan sanaladi.
- `CourseId` bo'sh guruhlar analitikaga umuman kirmaydi (kurs kesimida ma'nosi yo'q).
- Natija `DataCache` da (`courses:analytics:{oylar}`), bog'liq turlar: `StudentGroup`, `Group`,
  `Subject`, `Teacher`, `Student`. A'zolik o'zgarsa kesh AVTOMATIK yangilanadi, TTL (10 daq) faqat zaxira.

## 6. Grafik ranglari — TEKSHIRILGAN

`#0284c7` (kelgan) + `#e11d48` (ketgan). Yashil/qizil **ATAYIN olinmadi**: deuteranopiyada ular
deyarli ajralmaydi (ΔE 2.7 — tekshiruvdan o'tmaydi). Faol o'quvchilar chizig'i — `#6366f1`
(yakka seriya, legend kerak emas).

Ikki o'lchov bitta grafikda **hech qachon ikki y-o'q bilan** ko'rsatilmaydi — "kelgan/ketgan" va
"faol o'quvchilar" alohida grafiklarda.
