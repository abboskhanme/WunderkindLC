/**
 * MARKETING → KONTENT bo'limining O'ROVCHISI (layout).
 *
 * Modul ilgari bitta sahifa + katta modal edi; endi u uchta sub-sahifaga bo'lingan
 * (Navbat · Joylanganlar · Holat va limit), post muharriri esa ALOHIDA to'liq sahifa
 * (`/admin/marketing/kontent/yangi`, `/post/:id`) — u layoutdan TASHQARIDA, chunki uzun
 * forma yonida sub-nav va sarlavha faqat joyni egallardi.
 *
 * ⚠️ Sub-sahifalar sidebar nav'da YO'Q — ular shu yerdagi `MkSubnav` orqali ochiladi.
 * Sabab: bular bitta bo'limning ichki tablari, sidebar esa bo'limlar ro'yxati.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, Outlet } from 'react-router-dom'
import { getIgContentStatus, type IgContentStatus } from '@/api/services/instagramContent'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import { Icon, MarketingPage, MkSubnav, type MkSubnavItem } from '../mk'

/**
 * Layout bolalarga uzatadigan kontekst.
 *
 * ⚠️ Layout bolalarning MA'LUMOTINI bilmaydi (navbat oy bo'yicha, joylanganlar boshqa filtr
 * bilan, holat esa umuman boshqa endpointdan o'qiydi). Shuning uchun «Yangilash» tugmasi
 * to'g'ridan-to'g'ri qayta yuklamaydi — u faqat SIGNAL beradi, bola esa uni o'z yuklash
 * effektining bog'liqligiga qo'yadi.
 */
export interface ContentOutlet {
  /** Har «Yangilash» bosilganda ortadi — bola shuni `useEffect` bog'liqligiga qo'ysin. */
  reloadKey: number
  /** Sub-nav sanoqlarini qayta o'qiydi (post joylangandan keyin bola chaqiradi). */
  refreshCounts: () => void
  /**
   * `GET /content/status` natijasi — layout uni sub-nav sanoqlari VA yuqoridagi ogohlantirishlar
   * uchun baribir o'qiydi.
   *
   * ⚠️ Kontekstga ATAYIN chiqarildi: «Holat va limit» sahifasi ilgari AYNAN shu endpointni
   * ikkinchi marta o'zi so'rardi — bir sahifa ochilganda ikkita bir xil so'rov ketardi.
   */
  diag: IgContentStatus | null
  /**
   * Diagnostikani o'qishdagi xato matni (bo'sh — xato yo'q).
   *
   * ⚠️ Layout uni O'ZI CHIZMAYDI (har sahifa tepasida takrorlanadigan qizil chiziq bo'lardi) —
   * matn faqat «Holat va limit» sahifasiga uzatiladi, u yerda bu ma'lumot sahifaning MAZMUNI.
   */
  diagError: string
}

export function ContentLayout() {
  const { can } = usePerm()
  const canEdit = can('marketing.content', 'edit')

  const [diag, setDiag] = useState<IgContentStatus | null>(null)
  const [diagError, setDiagError] = useState('')
  const [reloadKey, setReloadKey] = useState(0)

  /**
   * Sub-nav sanoqlari.
   *
   * ⚠️ `GET /content/status` FAQAT bazadan o'qiydi (Meta'ga chiqmaydi) — shuning uchun uni
   * layoutda, ya'ni HAR sub-sahifada chaqirish xavfsiz. Kunlik limit endpointi esa har
   * chaqirilganda Meta'ga so'rov yuboradi va u ATAYIN faqat "Holat va limit" sahifasida.
   *
   * ⚠️ Xato bu yerda CHIZILMAYDI (har sahifa tepasida takrorlanadigan qizil chiziq bo'lardi),
   * lekin JIM ham yutilmaydi: matn kontekstga beriladi va «Holat va limit» sahifasi uni
   * ko'rsatadi — o'sha sahifaning butun mazmuni shu.
   */
  const refreshCounts = useCallback(async () => {
    try {
      setDiag(await getIgContentStatus())
      setDiagError('')
    } catch (e) {
      setDiag(null)
      setDiagError(apiErrorMessage(e, "Holatni o'qib bo'lmadi"))
    }
  }, [])

  useEffect(() => { void refreshCounts() }, [refreshCounts])

  /** «Yangilash» — sanoqlarni ham, ochiq turgan bolaning ma'lumotini ham yangilaydi. */
  const reload = useCallback(() => {
    setReloadKey((k) => k + 1)
    void refreshCounts()
  }, [refreshCounts])

  const queueCount = (diag?.scheduled ?? 0) + (diag?.processing ?? 0)
  const failedCount = diag?.failed ?? 0

  const items: MkSubnavItem[] = [
    { to: '/admin/marketing/kontent', label: 'Navbat', icon: 'clock', end: true, count: queueCount },
    { to: '/admin/marketing/kontent/joylangan', label: 'Joylanganlar', icon: 'grid' },
    { to: '/admin/marketing/kontent/holat', label: 'Holat va limit', icon: 'gauge', count: failedCount },
  ]

  /**
   * ⚠️ Kontekst `useMemo` bilan BARQAROR saqlanadi: bola `refreshCounts` ni `useEffect`
   * bog'liqligiga qo'ysa, har renderdagi yangi funksiya cheksiz sikl hosil qilardi.
   */
  const context = useMemo<ContentOutlet>(
    () => ({ reloadKey, refreshCounts: () => { void refreshCounts() }, diag, diagError }),
    [reloadKey, refreshCounts, diag, diagError],
  )

  return (
    <MarketingPage
      title="Kontent"
      sub="Instagram postlarini rejalashtirish va joylash — rasm, video, Reels, Story, karusel"
      actions={
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <button className="btn btn-ghost btn-sm" onClick={reload}>
            <Icon name="refresh" /> Yangilash
          </button>
          {canEdit && (
            <Link className="btn btn-primary btn-sm" to="/admin/marketing/kontent/yangi">
              <Icon name="plus" /> Yangi post
            </Link>
          )}
        </div>
      }
      subnav={<MkSubnav items={items} />}
    >
      {/* 🔴 Diagnostika ogohlantirishlari — `<Outlet />` USTIDA, ya'ni BARCHA sub-sahifalarda.
          Sabab pastdagi `ContentAlerts` izohida. */}
      <ContentAlerts diag={diag} />

      <Outlet context={context} />
    </MarketingPage>
  )
}

/* ═══════════════════════════════════════ DIAGNOSTIKA OGOHLANTIRISHLARI ═══════════════════════════════════════ */

/**
 * «Nega post chiqmayapti» savolining eng ko'p uchraydigan sabablari — OPERATOR EKRANIDA.
 *
 * 🔴 Bu bloklar ATAYIN layoutda, ya'ni Navbat sahifasida ham ko'rinadi. Ilgari ular faqat
 * «Holat va limit» sahifasida, xotirjam kartochka ko'rinishida edi — natijada modul o'chiq
 * bo'lganda operator Navbatda post yaratar, yashil «Post navbatga qo'shildi» xabarini olar va
 * postlar HECH QACHON chiqmasdi. Sub-nav'dagi yagona sanoq esa `failed`, modul o'chiq bo'lganda
 * u ham 0 (post umuman urinilmaydi) — ya'ni ekranda birorta belgi qolmasdi.
 *
 * ⚠️ SCOPE ogohlantirishi (sariq) faqat akkaunt ULANGAN va modul YOQILGAN bo'lganda chiziladi.
 * Sabab: modul o'chiq bo'lsa post umuman urinilmaydi — o'sha paytdagi "scope noma'lum" ikkinchi
 * darajali gap bo'lib, birinchi (haqiqiy) sababni shovqin bilan ko'mib qo'yardi. Modul yoqilgach
 * u darhol qaytadi. Sub-nav chipiga belgi qo'yish varianti tanlanmadi: `MkSubnavItem` faqat
 * SON qabul qiladi va "noma'lum" holatni son bilan ifodalab bo'lmasdi.
 */
function ContentAlerts({ diag }: { diag: IgContentStatus | null }) {
  if (!diag) return null

  const showScope = diag.accountConnected && diag.enabled && diag.scopeGranted !== true

  return (
    <>
      {!diag.accountConnected && (
        <div className="mk-alert mk-alert-danger">
          <Icon name="unlink" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
          <div>
            <div className="mk-alert-title">Instagram akkaunti ulanmagan</div>
            <div style={{ fontSize: 12.5 }}>
              Post joylash uchun Marketing → Sozlamalar bo‘limida akkauntni ulang.
            </div>
            <SettingsLink />
          </div>
        </div>
      )}

      {diag.accountConnected && !diag.enabled && (
        <div className="mk-alert mk-alert-danger">
          <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
          <div>
            <div className="mk-alert-title">Chop etish moduli o‘chiq</div>
            <div style={{ fontSize: 12.5 }}>
              Reja saqlanadi, lekin <b>hech qanday post joylanmaydi</b>. Marketing → Sozlamalar bo‘limidan
              «Instagram’ga post joylash» ni yoqing.
            </div>
            <SettingsLink />
          </div>
        </div>
      )}

      {showScope && (
        <div className="mk-alert">
          <Icon name="link" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
          <div>
            <div className="mk-alert-title">Chop etish ruxsati (scope) noma’lum</div>
            <div style={{ fontSize: 12.5 }}>
              Post joylash uchun <code>{diag.publishScope}</code> ruxsati kerak va u <b>qayta ulanish</b>
              orqali beriladi. Agar postlar «Xato» bo‘lib qolayotgan bo‘lsa — Sozlamalardagi «Qayta ulash»
              ni bosing va Instagram so‘ragan ruxsatlarni tasdiqlang.
            </div>
            <SettingsLink />
          </div>
        </div>
      )}
    </>
  )
}

/** Ogohlantirishdagi yagona amal — sozlamalarga o'tish (matnda aytilgan joyni QIDIRISH shart emas). */
function SettingsLink() {
  return (
    <Link className="btn btn-outline btn-sm" to="/admin/marketing/settings" style={{ marginTop: 8 }}>
      <Icon name="settings" /> Marketing sozlamalari
    </Link>
  )
}
