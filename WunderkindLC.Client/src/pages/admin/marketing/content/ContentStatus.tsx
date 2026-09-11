/**
 * MARKETING → KONTENT → «HOLAT VA LIMIT».
 *
 * Sahifa savoli: <b>post joylashga tayyormizmi va nega chiqmayapti</b>. BATAFSIL ma'lumot
 * (kunlik limit, media talablari, savol-javob) ilgari navbat sahifasining tepasida turar va
 * har kuni ishlaydigan operatorning ekranidan joy olardi — endi u shu ALOHIDA sahifada:
 * sabab qidirilganda ochiladi, qolgan vaqtda navbatni to'smaydi.
 *
 * ⚠️ BIROQ ikkita JIDDIY nosozlik («akkaunt ulanmagan», «modul o'chiq») bu sahifada QOLMAYDI:
 * ular `ContentLayout` da, ya'ni BARCHA sub-sahifalar tepasida qizil banner bo'lib turadi.
 * Sabab: modul o'chiq bo'lsa operator Navbatda post yaratib, yashil xabar olib, postlar hech
 * qachon chiqmaganini bilmasdi — bu sahifaga esa kirishi shart emas edi.
 *
 * ⚠️ KUNLIK LIMIT ENDPOINTI HAR CHAQIRILGANDA META'GA SO'ROV YUBORADI. Shuning uchun u
 * avto-yangilanishga QO'SHILMAGAN: faqat sahifa ochilganda, sahifadagi «Yangilash» tugmasida
 * va layoutdagi «Yangilash» bosilganda (ikkalasi ham foydalanuvchining QO'LDAGI amali).
 *
 * 🔴 JAMI KVOTA NOMA'LUM BO'LSA EKRANDA AYNAN «noma'lum» YOZILADI. Taxminiy 50/100
 * KO'RSATILMAYDI: Meta hujjatlari o'zaro zid (qo'llanmada 100, namunada 50), noto'g'ri son esa
 * "yana 40 ta post joylasam bo'ladi" deb chalg'itardi.
 */
import { useCallback, useEffect, useState } from 'react'
import { Link, useOutletContext } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  getIgContentLimit,
  IG_LIMITS,
  type IgPostLimit,
} from '@/api/services/instagramContent'
import { Icon, MkCard, MkError, MkStat, MkStatusCard } from '../mk'
import type { ContentOutlet } from './ContentLayout'

export function ContentStatus() {
  const { can } = usePerm()
  const canEdit = can('marketing.content', 'edit')
  /**
   * ⚠️ Diagnostika LAYOUTDAN olinadi, bu sahifa uni O'ZI so'ramaydi.
   * Sabab: layout `GET /content/status` ni sub-nav sanoqlari va tepadagi ogohlantirishlar
   * uchun baribir o'qiydi — ilgari sahifa ochilganda AYNAN bir xil so'rov ikki marta ketardi.
   * «Yangilash» bosilganda ham layout uni o'zi qayta o'qiydi.
   */
  const { reloadKey, diag, diagError, refreshCounts } = useOutletContext<ContentOutlet>()

  const [limit, setLimit] = useState<IgPostLimit | null>(null)
  const [limitLoading, setLimitLoading] = useState(true)
  const [limitError, setLimitError] = useState('')

  /** ⚠️ Bu chaqiruv Meta'ga chiqadi — faqat QO'LDA (yuqoridagi izohga qarang). */
  const loadLimit = useCallback(async () => {
    setLimitLoading(true)
    setLimitError('')
    try {
      setLimit(await getIgContentLimit())
    } catch (e) {
      setLimitError(apiErrorMessage(e, "Kunlik limitni o'qib bo'lmadi"))
    } finally {
      setLimitLoading(false)
    }
  }, [])

  useEffect(() => { void loadLimit() }, [loadLimit, reloadKey])

  return (
    <div className="fade-up">
      <LimitCard limit={limit} loading={limitLoading} error={limitError} onRefresh={() => void loadLimit()} />

      {/* ⚠️ Diagnostika xatosi AYNAN shu sahifada ko'rsatiladi (layout uni jim o'tkazadi):
          bu sahifaning butun mazmuni shu, jim yutilsa foydalanuvchi bo'sh ekran ko'rib
          "nega hech narsa yo'q" deb qolardi. */}
      {diagError && <MkError text={diagError} onRetry={refreshCounts} />}

      {diag && (
        <>
          <MkCard
            title="Chop etishga tayyormi"
            sub="Postlar umuman chiqishi uchun uchala qator ham joyida bo‘lishi kerak"
          >
            <div className="mk-status-grid">
              <MkStatusCard
                label="Instagram akkaunti"
                ok={diag.accountConnected}
                value={diag.accountConnected ? 'Ulangan' : 'Ulanmagan'}
                hint={diag.accountConnected
                  ? 'Postlar shu akkauntga joylanadi.'
                  : 'Post joylash uchun Marketing → Sozlamalar bo‘limida akkauntni ulang.'}
              />

              <MkStatusCard
                label="Chop etish moduli"
                ok={diag.enabled}
                value={diag.enabled ? 'Yoqilgan' : 'O‘chiq'}
                hint={diag.enabled
                  ? 'Navbatdagi postlar vaqti kelganda avtomatik joylanadi.'
                  : 'Reja saqlanadi, lekin HECH QANDAY post joylanmaydi. Sozlamalardan «Instagram’ga post joylash» ni yoqing.'}
              />

              {/* ⚠️ `scopeGranted === null` — "noma'lum": berilgan OAuth ruxsatlari saqlanmaydi.
                  Yolg'on "ha" dan ko'ra ochiq "noma'lum" yaxshi, shuning uchun bu holat XATO
                  emas, OGOHLANTIRISH rangida (`warn`) chiziladi. */}
              <MkStatusCard
                label="Chop etish ruxsati (scope)"
                ok={diag.scopeGranted === true}
                warn={diag.scopeGranted === null}
                value={diag.scopeGranted === true ? 'Berilgan' : diag.scopeGranted === false ? 'Berilmagan' : 'Noma’lum'}
                hint={`Post joylash uchun ${diag.publishScope} ruxsati kerak va u faqat QAYTA ULANISH orqali beriladi. Postlar «Xato» bo‘lib qolayotgan bo‘lsa — Sozlamalardagi «Qayta ulash» ni bosing va Instagram so‘ragan ruxsatlarni tasdiqlang.`}
              />
            </div>

            <div style={{ marginTop: 14 }}>
              <Link className="btn btn-outline btn-sm" to="/admin/marketing/settings">
                <Icon name="settings" /> Marketing sozlamalari
              </Link>
            </div>
          </MkCard>

          <div className="mk-kpi">
            <MkStat label="Rejalashtirilgan" value={diag.scheduled} tone="primary" icon="clock" />
            <MkStat label="Joylanmoqda" value={diag.processing} tone="warning" icon="refresh" />
            <MkStat label="Xato" value={diag.failed} tone="danger" icon="warn" />
            <MkStat label="Shu haftada joylandi" value={diag.publishedThisWeek} tone="success" icon="check" />
          </div>
        </>
      )}

      <MediaRequirements />
      <WhyNotPublished canEdit={canEdit} />
    </div>
  )
}

/* ═══════════════════════════════════════ KUNLIK LIMIT ═══════════════════════════════════════ */

/**
 * Meta'ning kunlik chop etish kvotasi.
 *
 * 🔴 Jami kvota noma'lum bo'lsa (`unknown` yoki `total === 0`) ekranda AYNAN "noma'lum"
 * yoziladi va PROGRESS CHIZIG'I umuman chizilmaydi — to'lgan/bo'sh nisbatini bilmasdan
 * chiziq chizish "yarmi to'ldi" degan yolg'on taassurot berardi.
 */
function LimitCard({
  limit, loading, error, onRefresh,
}: {
  limit: IgPostLimit | null
  loading: boolean
  error: string
  onRefresh: () => void
}) {
  const unknown = !limit || limit.unknown || limit.total <= 0
  const pct = !unknown && limit ? Math.min(100, Math.round((limit.usage / limit.total) * 100)) : 0

  return (
    <MkCard
      title="Kunlik chop etish limiti"
      sub="Instagram sutkada nechta post qabul qilishini o‘zi belgilaydi"
      actions={
        <button className="btn btn-ghost btn-sm" onClick={onRefresh} disabled={loading}>
          <Icon name="refresh" /> {loading ? 'So‘ralmoqda…' : 'Yangilash'}
        </button>
      }
    >
      {error && <MkError text={error} onRetry={onRefresh} />}

      {!error && (
        <>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, flexWrap: 'wrap' }}>
            <div style={{ fontSize: 34, fontWeight: 800, lineHeight: 1 }}>{limit ? limit.usage : '—'}</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-2)' }}>
              / {unknown ? 'noma’lum' : limit?.total}
            </div>
            {limit?.text && <span className="match-pill">{limit.text}</span>}
          </div>

          {!unknown && (
            <div className="progress-track" style={{ marginTop: 12 }}>
              {/* Rang `course-analytics.md` bilan bir xil: yashil/qizil juftlik ISHLATILMAYDI. */}
              <div className="progress-fill" style={{ width: `${pct}%`, background: '#0284c7' }} />
            </div>
          )}

          <div className="field-hint" style={{ marginTop: 10 }}>
            {unknown
              ? 'Instagram jami kvotani bermadi — bu son noma’lum. Taxminiy raqam ataylab ko‘rsatilmaydi: Meta hujjatlarida 50 va 100 deb zid yozilgan, noto‘g‘ri son esa chalg‘itadi. Limit to‘lsa post «Rejalashtirilgan» bo‘lib qoladi va kvota bo‘shashi bilan avtomatik joylanadi.'
              : 'Karusel — 1 ta post hisoblanadi. Limit to‘lsa post «Rejalashtirilgan» bo‘lib qoladi va kvota bo‘shashi bilan avtomatik joylanadi.'}
          </div>
          <div className="field-hint">
            ⚠️ Bu son Instagram’dan so‘raladi va o‘zi yangilanmaydi — kerak bo‘lganda «Yangilash» ni bosing.
          </div>
        </>
      )}

      {limit?.error && <div className="field-hint" style={{ color: 'var(--danger)' }}>{limit.error}</div>}
    </MkCard>
  )
}

/* ═══════════════════════════════════════ MEDIA TALABLARI ═══════════════════════════════════════ */

/**
 * 🔴 §5.5 — media talablari OCHIQ yoziladi.
 * Sabab: bu qoidalar buzilsa Meta xatoni faqat konteyner tayyorlangandan keyin qaytaradi,
 * ya'ni post o'z vaqtida chiqmasdi va nima uchunligi kech ma'lum bo'lardi.
 *
 * ⚠️ Raqamlar QO'LDA yozilmaydi — hammasi `IG_LIMITS` dan (u backend `IgPublishConst` bilan
 * AYNAN bir xil bo'lishi shart). Aks holda chegara backendda o'zgarganda ma'lumotnoma
 * jimgina yolg'on gapirib turardi.
 */
function MediaRequirements() {
  const feedRatio = `nisbat ${IG_LIMITS.feedRatio.min}–${IG_LIMITS.feedRatio.max}, kenglik ${IG_LIMITS.feedWidth.min}–${IG_LIMITS.feedWidth.max} px`
  const reelsDuration = `${IG_LIMITS.reelsSeconds.min}–${IG_LIMITS.reelsSeconds.max} s`

  const rows: { type: string; format: string; size: string; ratio: string; duration: string }[] = [
    {
      type: 'Rasm',
      format: 'JPEG (.jpg / .jpeg)',
      size: `≤ ${IG_LIMITS.imageMb} MB`,
      ratio: feedRatio,
      duration: '—',
    },
    {
      type: 'Video',
      format: 'MP4 / MOV',
      size: `≤ ${IG_LIMITS.reelsMb} MB`,
      ratio: '9:16 (masalan 1080×1920)',
      duration: reelsDuration,
    },
    {
      type: 'Reels',
      format: 'MP4 / MOV',
      size: `≤ ${IG_LIMITS.reelsMb} MB`,
      ratio: '9:16 (masalan 1080×1920)',
      duration: reelsDuration,
    },
    {
      type: 'Story',
      format: 'JPEG yoki MP4 / MOV',
      size: `rasm ≤ ${IG_LIMITS.imageMb} MB · video ≤ ${IG_LIMITS.storyVideoMb} MB`,
      ratio: '9:16',
      duration: `video ${IG_LIMITS.storyVideoSeconds.min}–${IG_LIMITS.storyVideoSeconds.max} s`,
    },
    {
      type: `Karusel (${IG_LIMITS.carouselItems.min}–${IG_LIMITS.carouselItems.max} ta element)`,
      format: 'JPEG va/yoki MP4 / MOV',
      size: `rasm ≤ ${IG_LIMITS.imageMb} MB · video ≤ ${IG_LIMITS.reelsMb} MB`,
      ratio: 'birinchi element bo‘yicha — qolganlari shunga qirqiladi',
      duration: reelsDuration,
    },
  ]

  return (
    <MkCard
      title="Media talablari"
      sub="Bu chegaralar Instagram’niki — CRM ularni saqlashdayoq tekshiradi, xato joylash paytida emas"
      pad={false}
    >
      <div className="mk-scroll-x">
        <table className="mk-table">
          <thead>
            <tr>
              <th>Tur</th>
              <th>Format</th>
              <th>Maksimal hajm</th>
              <th>Nisbat / o‘lcham</th>
              <th>Davomiylik</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.type}>
                <td style={{ fontWeight: 700 }}>{r.type}</td>
                <td>{r.format}</td>
                <td>{r.size}</td>
                <td>{r.ratio}</td>
                <td>{r.duration}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="field-hint" style={{ padding: '10px 14px 14px' }}>
        ⚠️ O‘lcham maydonlarida <b>0 = noma’lum</b>: bunday qiymat tekshirilmaydi. Taxminiy son
        yozmang — noto‘g‘ri qiymat to‘g‘ri media’ni bekorga rad etadi. Karusel elementlariga
        alohida matn yozilmaydi (Instagram uni ko‘rsatmaydi).
      </div>
    </MkCard>
  )
}

/* ═══════════════════════════════════════ SAVOL-JAVOB ═══════════════════════════════════════ */

/**
 * «Nega post chiqmayapti» — eng ko'p uchraydigan uchta sabab.
 *
 * ⚠️ Bularning hammasi haqiqiy nosozliklardan olingan: ular ekranda TURMASA foydalanuvchi
 * sababni Instagram tomondan qidirib, topa olmasdi (masalan `/uploads/…` manzili admin
 * brauzerida OCHILADI — u login qilgan; Meta uchun esa o'sha manzil 404).
 */
function WhyNotPublished({ canEdit }: { canEdit: boolean }) {
  return (
    <MkCard title="Nega post chiqmayapti" sub="Eng ko‘p uchraydigan uchta sabab">
      <div className="mk-cols2">
        <div>
          <div style={{ fontWeight: 700, fontSize: 13.5, marginBottom: 4 }}>
            1. Media manzili ochiq HTTPS emas
          </div>
          <div className="field-hint" style={{ margin: 0 }}>
            Instagram faylni <b>o‘zi yuklab oladi</b>, ya'ni manzil login, IP cheklov va yo‘naltirishsiz
            ochilishi shart. CRM’ning oddiy <code>/uploads/…</code> manzillari login ortida turadi va
            Meta uchun <b>404</b> bo‘ladi — post <code>2207052</code> xatosi bilan yiqiladi. Muharrirdagi
            «Fayl yuklash» tugmasi faylni maxsus <b>ochiq</b> papkaga qo‘yadi va manzilni o‘zi yozadi.
          </div>
        </div>

        <div>
          <div style={{ fontWeight: 700, fontSize: 13.5, marginBottom: 4 }}>
            2. Kunlik limit to‘lgan
          </div>
          <div className="field-hint" style={{ margin: 0 }}>
            Bu holatda post <b>yo‘qolmaydi</b> va «Xato» ham bo‘lmaydi: u «Rejalashtirilgan» bo‘lib
            navbatda qoladi va kvota bo‘shashi bilan <b>avtomatik</b> joylanadi. Urinishlar soni ham
            oshmaydi — hech narsa qilish shart emas, kutish yetarli.
          </div>
        </div>

        <div>
          <div style={{ fontWeight: 700, fontSize: 13.5, marginBottom: 4 }}>
            3. Joylangan postni o‘zgartirmoqchisiz
          </div>
          <div className="field-hint" style={{ margin: 0 }}>
            Instagram API’si joylangan postni tahrirlashni ham, o‘chirishni ham qo‘llab-quvvatlamaydi.
            Matnni ham, rasmni ham faqat <b>Instagram ilovasidan</b> o‘zgartirish mumkin. CRM’dagi
            o‘chirish esa <b>faqat CRM yozuvini</b> o‘chiradi.
          </div>
        </div>

        <div>
          <div style={{ fontWeight: 700, fontSize: 13.5, marginBottom: 4 }}>
            Yana ham chiqmasa
          </div>
          <div className="field-hint" style={{ margin: 0 }}>
            Yuqoridagi «Chop etishga tayyormi» qatorlarini tekshiring: akkaunt ulanganmi, modul
            yoqilganmi va chop etish ruxsati berilganmi. Ruxsat <b>faqat qayta ulanish</b> orqali
            beriladi. Har postning o‘z sababi «Joylanganlar → Xato» kesimida yozilgan.
            {!canEdit && ' Sozlamalarni o‘zgartirish uchun tahrirlash ruxsati kerak.'}
          </div>
        </div>
      </div>
    </MkCard>
  )
}
