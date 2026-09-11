import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, Trash2 } from 'lucide-react'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'

/**
 * MA'LUMOTNI O'CHIRISH — ommaviy (autentifikatsiyasiz) sahifa, `/data-deletion`.
 *
 * <p><b>Nima uchun bor:</b> Meta App sozlamalarida <b>«Data Deletion Instructions URL»</b>
 * majburiy maydon — Instagram moduli (Marketing bo'limi) shusiz sozlanmaydi. Google Play ham
 * shunga o'xshash havolani so'raydi. Manzil AYNAN shu bo'lishi kerak:
 * <c>https://&lt;domen&gt;/data-deletion</c> (`.claude/rules/marketing-instagram.md` §14).</p>
 *
 * <p>⚠️ Sahifa <b>hech qanday CRM ma'lumotini KO'RSATMAYDI</b> va login talab qilmaydi —
 * u faqat "qanday qilib o'chirishni so'rash mumkin" degan yo'riqnoma. Bu yerga forma, qidiruv
 * yoki hisob ma'lumoti QO'SHILMASIN: ochiq manzilda begona odam boshqaning ma'lumotini
 * so'rab olishi mumkin bo'lardi.</p>
 *
 * <p>Batafsil siyosat — `/privacy` (aloqador bo'limlar: «Instagram orqali murojaat qilganlar»
 * va «Hisob va ma'lumotlarni o'chirish»).</p>
 */
export function DataDeletionPage() {
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '', email: '' })

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  const centerName = brand.name || 'Wunderkind'

  return (
    <div className="min-h-screen bg-slate-50 px-4 py-10">
      <div className="mx-auto w-full max-w-3xl">
        <div className="mb-8 flex flex-col items-center gap-4 text-center">
          {brand.logoUrl ? (
            <img src={brand.logoUrl} alt="Logo" className="h-14 w-14 rounded-2xl object-contain" />
          ) : (
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-600 text-white shadow-lg">
              <Trash2 className="h-7 w-7" />
            </div>
          )}
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-slate-800">
              Ma'lumotlarni o'chirish
            </h1>
            <p className="mt-1 text-sm text-slate-400">{centerName}</p>
          </div>
        </div>

        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
          <div className="space-y-6 text-[15px] leading-relaxed text-slate-700">
            <p className="rounded-xl bg-slate-50 px-4 py-3 text-sm">
              <b>Qisqacha:</b> bizga murojaat qiling — ma'lumotlaringiz <b>30 kun ichida</b>
              {' '}o'chiriladi. Instagram orqali yozgan bo'lsangiz, o'sha yerning o'zida
              «ma'lumotlarimni o'chiring» deb yozishingiz kifoya.
            </p>

            <Block title="1. Instagram orqali yozgan bo'lsangiz">
              <p>
                Markazning Instagram sahifasiga izoh yozgan yoki Direct orqali murojaat qilgan
                bo'lsangiz, biz saqlagan yagona narsa — <b>foydalanuvchi nomingiz (@username),
                Instagram bergan ichki identifikatoringiz va yozishmangiz matni</b>.
              </p>
              <p className="mt-2">O'chirishning ikki yo'li bor:</p>
              <ul className="mt-2 list-disc space-y-1 pl-5">
                <li>
                  Direct'da <b>«ma'lumotlarimni o'chiring»</b> deb yozing — murojaat operatorga
                  tushadi;
                </li>
                <li>
                  yoki quyidagi aloqa ma'lumotlari orqali murojaat qiling va Instagram
                  foydalanuvchi nomingizni ko'rsating.
                </li>
              </ul>
              <p className="mt-2 text-sm text-slate-500">
                Instagram'dagi <b>o'z izohingizni</b> istalgan payt o'zingiz o'chira olasiz — u
                holda izoh Instagram'dan yo'qoladi, bizdagi nusxasi esa yuqoridagi murojaat bilan
                o'chiriladi.
              </p>
            </Block>

            <Block title="2. Mobil ilova yoki CRM foydalanuvchisi bo'lsangiz">
              <p>
                Hisobingizni va u bilan bog'liq ma'lumotlarni (profil, surat, joylashuv, qurilma
                tokenlari, yozishmalar) o'chirish uchun murojaatda <b>ism-familiya va login</b>
                {' '}ingizni ko'rsating.
              </p>
            </Block>

            <Block title="3. Nima o'chiriladi va nima saqlanadi">
              <ul className="list-disc space-y-1 pl-5">
                <li>
                  <b>O'chiriladi:</b> hisob (login/parol), profil surati, joylashuv, qurilma
                  tokenlari, yozishmalar, Instagram suhbatlari va murojaatlar.
                </li>
                <li>
                  <b>Saqlanib qolishi mumkin:</b> moliyaviy hujjatlar (to'lov yozuvlari) va
                  o'quvchilikni tasdiqlovchi yozuvlar — qonun hujjatlari talab qilgan muddatda.
                </li>
              </ul>
            </Block>

            <Block title="4. Muddat">
              <p>
                Murojaat <b>30 kun ichida</b> ko'rib chiqiladi va bajarilgani haqida sizga xabar
                beriladi.
              </p>
            </Block>

            <Block title="5. Biz bilan bog'lanish">
              <ul className="list-disc space-y-1 pl-5">
                {brand.email && (
                  <li>
                    E-pochta: <b>{brand.email}</b>
                  </li>
                )}
                {brand.phone && (
                  <li>
                    Telefon: <b>{brand.phone}</b>
                  </li>
                )}
                <li>Instagram: markaz sahifasiga Direct orqali</li>
                {!brand.email && !brand.phone && (
                  <li>
                    O'quv markazi ma'muriyatiga murojaat qiling (aloqa ma'lumotlari markaz
                    sahifasida).
                  </li>
                )}
              </ul>
            </Block>
          </div>

          <div className="mt-8 border-t border-slate-100 pt-5 text-sm">
            <Link
              to="/privacy"
              className="inline-flex items-center gap-1.5 font-medium text-brand-600 hover:underline"
            >
              <ArrowLeft className="h-4 w-4" /> Maxfiylik siyosati
            </Link>
          </div>
        </div>
      </div>
    </div>
  )
}

function Block({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="mb-2 text-base font-semibold text-slate-800">{title}</h2>
      <div className="space-y-1">{children}</div>
    </section>
  )
}
