import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, ShieldCheck } from 'lucide-react'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'

/**
 * Ommaviy (autentifikatsiyasiz) maxfiylik siyosati sahifasi — `/privacy`.
 *
 * Google Play / App Store ilova sahifasidagi "Privacy Policy" URL'i shu sahifaga ishora qiladi
 * (masalan https://lc.wunderkindedu.uz/privacy). Play'ning "Data deletion" havolasi esa
 * `/privacy#delete` ga.
 *
 * ⚠️ MATN ILOVANING HAQIQIY KODIGA ASOSLANGAN — umumiy shablon EMAS. Play sharhlovchisi
 * e'lon qilingan ma'lumotlarni ilova ruxsatlari bilan solishtiradi va mos kelmasa rad etadi.
 * Ilovada ruxsat/SDK/endpoint o'zgarsa — SHU SAHIFA ham yangilanishi SHART (va Play Console
 * dagi "Data safety" formasi ham). Manba: `Wunderkind-Student-app-new` (o'quvchi/ota-ona ilovasi):
 *   • ruxsatlar: INTERNET, POST_NOTIFICATIONS, ACCESS_FINE_LOCATION, ACCESS_COARSE_LOCATION;
 *   • joylashuv: FAQAT "Uy joylashuvi" ekranida, foydalanuvchi tugmani bosganda, bir martalik
 *     (fon rejimi YO'Q, ACCESS_BACKGROUND_LOCATION YO'Q);
 *   • kamera/galereya: FAQAT taklif/shikoyatga rasm biriktirish;
 *   • mikrofon/ovoz yozish: YO'Q; analytics/crashlytics/reklama SDK: YO'Q;
 *   • Firebase: faqat Core + Cloud Messaging (push).
 */
export function PrivacyPolicyPage() {
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '', email: '' })

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  const centerName = brand.name || 'Wunderkind'
  const updated = '03.08.2026'

  /** Aloqa qatori — email va telefon (Sozlamalar → Markaz ma'lumotlaridan keladi). */
  const contact = (
    <>
      {brand.email && (
        <>
          e-pochta: <b>{brand.email}</b>
          {brand.phone ? ', ' : ''}
        </>
      )}
      {brand.phone && (
        <>
          telefon: <b>{brand.phone}</b>
        </>
      )}
      {!brand.email && !brand.phone && <b>o'quv markazi ma'muriyati</b>}
    </>
  )

  return (
    <div className="min-h-screen bg-slate-50 px-4 py-10">
      <div className="mx-auto w-full max-w-3xl">
        {/* Sarlavha */}
        <div className="mb-8 flex flex-col items-center gap-4 text-center">
          {brand.logoUrl ? (
            <img src={brand.logoUrl} alt="Logo" className="h-14 w-14 rounded-2xl object-contain" />
          ) : (
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-600 text-white shadow-lg">
              <ShieldCheck className="h-7 w-7" />
            </div>
          )}
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-slate-800">Maxfiylik siyosati</h1>
            <p className="mt-1 text-sm text-slate-400">
              {centerName} · oxirgi yangilanish: {updated}
            </p>
          </div>
        </div>

        {/* Matn */}
        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
          <div className="space-y-6 text-[15px] leading-relaxed text-slate-700">
            <p>
              Ushbu maxfiylik siyosati <b>{centerName}</b> o'quv markazining mobil ilovalari
              («Wunderkind Student» — o'quvchi va ota-ona uchun, «Wunderkind Teacher» — o'qituvchi
              uchun) hamda veb-tizimi (bundan buyon birgalikda — «Ilova») foydalanuvchilarining
              ma'lumotlari qanday to'planishi, ishlatilishi va himoyalanishini tushuntiradi.
            </p>
            <p className="rounded-xl bg-slate-50 px-4 py-3 text-sm">
              <b>Qisqacha:</b> Ilova faqat o'quv jarayonini yuritish uchun zarur ma'lumotlarni
              ishlatadi. Biz ma'lumotlaringizni <b>sotmaymiz</b>, reklama uchun ishlatmaymiz va
              reklama tarmoqlariga bermaymiz. Ilovada reklama, analitika (analytics) yoki
              foydalanuvchini kuzatuvchi (tracking) SDK'lar <b>yo'q</b>.
            </p>

            <Section title="1. Kim ma'lumotlarni nazorat qiladi">
              <p>
                Ma'lumotlar nazoratchisi — <b>{centerName}</b> o'quv markazi. Hisob (login/parol)
                foydalanuvchining o'zi tomonidan yaratilmaydi: uni o'quv markazi ma'muriyati
                yaratadi va foydalanuvchiga topshiradi. Aloqa: {contact}.
              </p>
            </Section>

            <Section title="2. Biz to'playdigan ma'lumotlar">
              <p>Ilova quyidagi ma'lumotlarni ishlatadi:</p>
              <ul className="mt-2 list-disc space-y-2 pl-5">
                <li>
                  <b>Hisob va shaxsiy ma'lumotlar:</b> login va parol, ism-familiya, tug'ilgan sana,
                  jinsi, o'quvchi va ota-onaning telefon raqami, guruh/sinf, qabul sanasi, profil
                  surati (agar yuklangan bo'lsa). Bu ma'lumotlarni <b>o'quv markazi kiritadi</b>;
                  ilovada siz ularni asosan ko'rasiz.
                </li>
                <li>
                  <b>O'quv ma'lumotlari:</b> davomat, baholar, uy vazifasi va o'quv dasturi
                  topshiriqlari, testlar va ularning javoblari, dars jadvali, sertifikatlar, reyting.
                </li>
                <li>
                  <b>To'lov ma'lumotlari:</b> oylik hisob, to'langan summalar va balans —{' '}
                  <b>faqat ko'rish uchun</b>. Ilova to'lovni qabul qilmaydi va bank kartasi
                  ma'lumotlarini so'ramaydi hamda saqlamaydi.
                </li>
                <li>
                  <b>Siz yozgan matnlar:</b> markaz bilan yozishma (chat), taklif va shikoyatlar,
                  AI yordamida tekshiriladigan yozma ishlar.
                </li>
                <li>
                  <b>Joylashuv (ixtiyoriy):</b> qarang — 3-bo'lim.
                </li>
                <li>
                  <b>Rasm (ixtiyoriy):</b> taklif yoki shikoyatga biriktirish uchun kamera yoki
                  galereyadan tanlangan bitta rasm. Boshqa maqsadda kamera/galereyaga murojaat
                  qilinmaydi.
                </li>
                <li>
                  <b>Bildirishnoma uchun texnik ma'lumot:</b> push-xabar yuborish uchun qurilma
                  tokeni (Firebase Cloud Messaging), platforma nomi («android»/«ios») va
                  operatsion tizim versiyasi. Telefon modeli, IMEI, reklama identifikatori yoki
                  boshqa qurilma identifikatorlari <b>to'planmaydi</b>.
                </li>
              </ul>
            </Section>

            <Section title="3. Joylashuv (GPS) — nima uchun va qanday">
              <ul className="list-disc space-y-2 pl-5">
                <li>
                  <b>Maqsad:</b> o'quvchining <b>uy manzilini xaritada bir marta belgilash</b>. Bu
                  o'quv markaziga o'quvchilarning yashash hududini bilish (masalan yo'l xarajati,
                  qatnov va xavfsizlik masalalari) uchun kerak.
                </li>
                <li>
                  <b>Qachon olinadi:</b> faqat siz ilovadagi «Uy joylashuvi» bo'limiga kirib,
                  joylashuvni aniqlash tugmasini bosganingizda. Joylashuv{' '}
                  <b>fon rejimida (background) kuzatilmaydi</b>, ilova yopiq turganda olinmaydi va
                  harakatingiz kuzatilmaydi.
                </li>
                <li>
                  <b>Nima yuboriladi:</b> faqat kenglik (latitude), uzunlik (longitude) va manzil
                  matni. Tezlik, balandlik yoki harakat tarixi yuborilmaydi.
                </li>
                <li>
                  <b>Kim ko'radi:</b> faqat o'quv markazi ma'muriyati (admin panelidagi xarita).
                </li>
                <li>
                  <b>Bekor qilish:</b> ruxsatni istalgan vaqtda telefon sozlamalaridan
                  (Sozlamalar → Ilovalar → Wunderkind Student → Ruxsatlar) bekor qilishingiz mumkin.
                  Ilovaning qolgan barcha bo'limlari joylashuvsiz ham to'liq ishlaydi.
                </li>
              </ul>
            </Section>

            <Section title="4. Ma'lumotlardan foydalanish maqsadi">
              <ul className="list-disc space-y-1 pl-5">
                <li>O'quv jarayonini yuritish: davomat, baho, topshiriq va to'lov hisobi;</li>
                <li>Bildirishnoma yuborish (dars, baho, to'lov eslatmasi, e'lonlar);</li>
                <li>Foydalanuvchini autentifikatsiya qilish va hisob xavfsizligini ta'minlash;</li>
                <li>Murojaat va shikoyatlarga javob berish;</li>
                <li>Ilova ishidagi nosozliklarni bartaraf etish.</li>
              </ul>
              <p className="mt-2">
                Ma'lumotlar <b>reklama, profillashtirish yoki sotish</b> uchun ishlatilmaydi.
              </p>
            </Section>

            <Section title="5. Ma'lumotlar kimga uzatiladi">
              <p>
                Ma'lumotlaringizni sotmaymiz. Ular faqat Ilova ishlashi uchun zarur bo'lgan
                xizmat ko'rsatuvchilarga uzatiladi:
              </p>
              <ul className="mt-2 list-disc space-y-2 pl-5">
                <li>
                  <b>Google Firebase Cloud Messaging</b> — push-bildirishnomalarni yetkazish uchun
                  (qurilma tokeni va xabar matni). Firebase Analytics, Crashlytics yoki reklama
                  xizmatlari ishlatilmaydi.
                </li>
                <li>
                  <b>OpenStreetMap</b> — «Uy joylashuvi» xaritasining tasvirlari shu xizmatdan
                  yuklanadi. Bunda ularning serveriga qurilmangizning IP manzili va ko'rilayotgan
                  xarita hududi ma'lum bo'ladi.
                </li>
                <li>
                  <b>SMS provayderi</b> — hisob va to'lovga oid SMS xabarlar uchun (telefon raqami
                  va xabar matni).
                </li>
                <li>
                  <b>Telegram</b> — agar siz markaz botiga o'z ixtiyoringiz bilan ulangan bo'lsangiz,
                  xabarnomalarni yuborish uchun.
                </li>
                <li>
                  <b>Google Gemini</b> — «AI tekshiruv» bo'limidan foydalansangiz, siz yozgan
                  matn tahlil uchun uzatiladi. Bu bo'limdan foydalanish ixtiyoriy.
                </li>
              </ul>
              <p className="mt-2">
                Bundan tashqari, ma'lumotlar faqat qonun talab qilgan hollarda tegishli davlat
                organlariga taqdim etilishi mumkin.
              </p>
            </Section>

            <Section title="6. Saqlash muddati va himoya">
              <p>
                Ma'lumotlar o'quv markazining himoyalangan serverida saqlanadi; ulanish HTTPS orqali
                shifrlanadi, kirish rol asosida cheklanadi va zaxira nusxalari olinadi. Ma'lumotlar
                siz o'quv markazining o'quvchisi (yoki xodimi) bo'lgan davrda va undan keyin qonun
                talab qilgan muddatda saqlanadi. Qurilmangizda esa faqat kirish tokeni, hisob nomi
                va ilova mavzusi (tungi/kunduzgi) saqlanadi — ilovadan chiqqaningizda ular
                o'chiriladi.
              </p>
            </Section>

            <Section title="7. Voyaga yetmaganlar ma'lumotlari">
              <p>
                Ilova o'quv markazi tomonidan boshqariladi va bolalarga mustaqil ro'yxatdan o'tish
                imkonini bermaydi. Voyaga yetmagan o'quvchining ma'lumotlari ota-onasi yoki qonuniy
                vakili roziligi asosida markaz tomonidan kiritiladi va faqat o'quv maqsadlarida
                ishlatiladi. Ilovada reklama va bolalar uchun nomaqbul kontent yo'q.
              </p>
            </Section>

            <Section title="8. Sizning huquqlaringiz">
              <p>
                Siz o'zingiz (yoki farzandingiz) haqidagi ma'lumotlarni ko'rish, noto'g'risini
                tuzatish, ulardan nusxa olish yoki o'chirishni so'rash huquqiga egasiz. Buning
                uchun {contact} orqali murojaat qiling.
              </p>
            </Section>

            <Section title="9. Hisob va ma'lumotlarni o'chirish" id="delete">
              <p>
                Hisobingizni va u bilan bog'liq ma'lumotlarni o'chirishni so'rash uchun{' '}
                {contact} orqali murojaat qiling va murojaatda ism-familiya hamda ilovadagi
                loginingizni ko'rsating.
              </p>
              <ul className="mt-2 list-disc space-y-1 pl-5">
                <li>Murojaat <b>30 kun ichida</b> ko'rib chiqiladi.</li>
                <li>
                  <b>O'chiriladi:</b> hisob (login/parol), profil surati, joylashuv, qurilma
                  tokenlari, yozishmalar va murojaatlar.
                </li>
                <li>
                  <b>Saqlanib qolishi mumkin:</b> moliyaviy hujjatlar (to'lov yozuvlari) va
                  o'quvchilikni tasdiqlovchi yozuvlar — qonun hujjatlari talab qilgan muddatda.
                </li>
                <li>
                  Faqat <b>joylashuvni</b> o'chirish uchun ilovadagi «Uy joylashuvi» bo'limiga
                  murojaat qiling yoki markazga xabar bering; qurilma <b>ruxsatini</b> esa o'zingiz
                  telefon sozlamalaridan bekor qilishingiz mumkin.
                </li>
              </ul>
            </Section>

            {/* ⚠️ META (Instagram) TALABI. Modul markazning Instagram akkauntiga kelgan izoh va
                shaxsiy xabarlarga AI bilan javob beradi — ya'ni Instagram foydalanuvchisining
                ma'lumoti qayta ishlanadi. Meta App sozlamalaridagi «Privacy Policy URL» aynan shu
                sahifaga ishora qiladi va sharhlovchi bu bo'limni izlaydi. Modul o'zgarsa
                (yangi maydon yig'ilsa) — shu ro'yxat ham yangilanishi SHART.
                Batafsil: `.claude/rules/marketing-instagram.md` §14. */}
            <Section title="10. Instagram orqali murojaat qilganlar" id="instagram">
              <p>
                Markazning Instagram sahifasiga <b>izoh</b> yozsangiz yoki <b>shaxsiy xabar</b>
                {' '}(Direct) yuborsangiz, murojaatingizga javob berish uchun quyidagilar qayta
                ishlanadi:
              </p>
              <ul className="mt-2 list-disc space-y-1 pl-5">
                <li>
                  <b>Yig'iladi:</b> Instagram foydalanuvchi nomingiz (@username), Instagram bergan
                  ichki identifikatoringiz, xabar/izoh matni va yuborilgan vaqti. Agar o'zingiz
                  yozsangiz — ism va telefon raqamingiz (markazga murojaat sifatida saqlanadi).
                </li>
                <li>
                  <b>Yig'ilmaydi:</b> Instagram parolingiz, do'stlar ro'yxatingiz, boshqa
                  yozishmalaringiz, sahifangizdagi postlar va shaxsiy ma'lumotlaringiz. Markaz
                  Instagram akkauntingizga kira olmaydi.
                </li>
                <li>
                  <b>Nima uchun:</b> savolingizga javob berish, kurslar haqida ma'lumot berish va
                  siz so'ragan bo'lsangiz — qayta bog'lanish.
                </li>
                <li>
                  <b>Kim ko'radi:</b> faqat markazning mas'ul xodimlari. Javob matnini tayyorlash
                  uchun xabaringiz Google'ning Gemini xizmatiga yuboriladi; unga ismingiz va
                  telefoningiz <b>berilmaydi</b>.
                </li>
                <li>
                  <b>Javobning bir qismi avtomatik</b> (AI yordamchisi) — bu suhbatning birinchi
                  xabarida ochiq aytiladi. Istalgan payt «operator» deb yozsangiz jonli xodimga
                  ulanadi.
                </li>
                <li>
                  <b>O'chirish:</b> Instagram orqali yig'ilgan ma'lumotni o'chirish uchun quyidagi
                  bo'limga qarang yoki Direct'da «ma'lumotlarimni o'chiring» deb yozing.
                </li>
              </ul>
            </Section>

            <Section title="11. Siyosatdagi o'zgarishlar">
              <p>
                Ushbu siyosat vaqti-vaqti bilan yangilanishi mumkin. Muhim o'zgarishlar Ilova orqali
                e'lon qilinadi. Yuqoridagi «oxirgi yangilanish» sanasi joriy versiyani bildiradi.
              </p>
            </Section>

            <Section title="12. Biz bilan bog'lanish">
              <p>
                Maxfiylik bo'yicha savol yoki so'rovlaringiz bo'lsa, <b>{centerName}</b> o'quv
                markaziga murojaat qiling — {contact}.
              </p>
            </Section>
          </div>
        </div>

        {/* Orqaga */}
        <div className="mt-6 text-center">
          <Link
            to="/login"
            className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
          >
            <ArrowLeft className="h-4 w-4" /> Kirish sahifasiga qaytish
          </Link>
        </div>
      </div>
    </div>
  )
}

function Section({
  title,
  id,
  children,
}: {
  title: string
  /** Bo'limga to'g'ridan-to'g'ri havola uchun (masalan Play "Data deletion URL" → /privacy#delete). */
  id?: string
  children: React.ReactNode
}) {
  return (
    <section id={id} className="scroll-mt-6">
      <h2 className="mb-2 text-base font-semibold text-slate-800">{title}</h2>
      {children}
    </section>
  )
}
