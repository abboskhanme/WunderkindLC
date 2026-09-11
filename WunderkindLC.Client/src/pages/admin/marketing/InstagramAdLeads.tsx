import { useCallback, useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  getIgAdLeads, retryIgAdLead,
  type IgAdLead, type IgAdLeadList, type IgBreakdown,
} from '@/api/services/instagram'
import { Icon, MarketingPage, MkCard, MkEmpty, MkError, MkLoading, MkStat } from './mk'

/**
 * REKLAMA LIDLARI — Instagram/Facebook target reklamasidagi forma (Instant Form) orqali
 * kelgan murojaatlar.
 *
 * Sahifaning asosiy vazifasi ikkita savolga javob berish:
 * (1) <b>lid CRM'ga tushdimi</b> — tushmagani qizil bo'lib ko'rinadi va «Qayta olish» tugmasi
 *     bilan tuzatiladi (odatiy sabab: lid kelganda token hali kiritilmagan edi);
 * (2) <b>qaysi forma / kampaniya qancha lid berdi</b> — marketolog pulini qayerga sarflashni
 *     shundan biladi.
 *
 * ⚠️ Jamlanma va kesimlar SERVERDA, butun topilma bo'yicha hisoblanadi — ro'yxat sahifalangani
 * uchun uni qatorlardan qo'shib chiqarish noto'g'ri son berardi.
 *
 * ⚠️ KO'RINISH: sahifa to'liq kenglikda ochiladi. Ikki kesim kartochkasi `mk-cols2` grid'ida —
 * qat'iy `1fr 1fr` ATAYIN olib tashlandi: u tor ekranda ikkala kartani ham siqib qo'yardi.
 */
export function InstagramAdLeads() {
  const { can } = usePerm()
  const canEdit = can('marketing.settings', 'edit')

  const [data, setData] = useState<IgAdLeadList | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')

  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [q, setQ] = useState('')
  const [status, setStatus] = useState<'all' | 'ok' | 'failed'>('all')
  const [page, setPage] = useState(1)

  /**
   * `?campaign=` — «Reklama statistikasi» jadvalidagi «Lidlarni ko'rish →» havolasidan keladi.
   * ⚠️ Havola manzilda QOLADI (state'ga ko'chirilmaydi): foydalanuvchi sahifani yangilasa yoki
   * havolani nusxa qilib bersa filtr saqlanib qolsin. Tozalash — «Kampaniya filtri» chipidagi
   * «×» tugmasi orqali.
   */
  const [params, setParams] = useSearchParams()
  const campaign = params.get('campaign') ?? ''

  const clearCampaign = () => {
    params.delete('campaign')
    setParams(params, { replace: true })
    setPage(1)
  }

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgAdLeads({ from, to, q, status, campaign, page })
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e, "Reklama lidlarini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [from, to, q, status, campaign, page])

  useEffect(load, [load])

  /** Filtr o'zgarsa birinchi sahifaga qaytamiz — aks holda bo'sh sahifada qolib ketilardi. */
  const patchFilter = (fn: () => void) => { fn(); setPage(1) }

  const retry = async (id: string) => {
    setBusy(id)
    setError('')
    try {
      await retryIgAdLead(id)
      load()
    } catch (e) {
      setError(apiErrorMessage(e, "Qayta olib bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <MarketingPage
      title="Reklama lidlari"
      sub="Target reklamadagi forma orqali kelgan murojaatlar (Meta Lead Ads)"
      actions={<button className="btn btn-ghost btn-sm" onClick={load}><Icon name="refresh" /> Yangilash</button>}
    >
      <div className="fade-up">
        {/* Filtrlar */}
        <MkCard>
          <div style={{ display: 'flex', gap: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Boshlanishi</label>
              <input className="input" type="date" value={from} onChange={(e) => patchFilter(() => setFrom(e.target.value))} />
            </div>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Tugashi</label>
              <input className="input" type="date" value={to} onChange={(e) => patchFilter(() => setTo(e.target.value))} />
            </div>
            <div style={{ flex: 1, minWidth: 200 }}>
              <label className="field-label">Qidiruv</label>
              <input
                className="input" value={q} placeholder="ism, telefon, forma yoki kampaniya"
                onChange={(e) => patchFilter(() => setQ(e.target.value))}
              />
            </div>
            <div className="seg">
              {([['all', 'Hammasi'], ['ok', 'CRM’da'], ['failed', 'Xato']] as const).map(([k, l]) => (
                <button key={k} className={status === k ? 'active' : ''} onClick={() => patchFilter(() => setStatus(k))}>
                  {l}
                </button>
              ))}
            </div>
          </div>
        </MkCard>

        {campaign && (
          <div className="mk-alert" style={{ marginBottom: 16 }}>
            <Icon name="filter" style={{ width: 18, height: 18, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              Faqat bitta kampaniyaning lidlari ko'rsatilyapti (reklama statistikasidan o'tildi).
            </div>
            <button className="btn btn-ghost btn-sm" onClick={clearCampaign}>
              <Icon name="close" /> Filtrni olib tashlash
            </button>
          </div>
        )}

        {loading && <MkLoading />}
        {!loading && error && <MkError text={error} onRetry={load} />}

        {!loading && data && (
          <>
            {error && <div style={{ marginBottom: 16 }}><MkError text={error} /></div>}

            <div className="mk-kpi" style={{ marginBottom: 22 }}>
              {([
                { label: 'Kelgan lidlar', value: data.totals.total, tone: 'primary', icon: 'inbox' },
                { label: 'CRM’ga tushgan', value: data.totals.withLead, tone: 'success', icon: 'check' },
                { label: 'Yangi mijoz', value: data.totals.newLeads, tone: 'muted', icon: 'user' },
                /* ⚠️ Xato bilan qolgan lid ALOHIDA ajratib turadi — "reklamaga pul ketdi,
                   lid qani?" savoli ekranga chiqmasdan qolmasin. */
                { label: 'Xato bilan qolgan', value: data.totals.failed, tone: 'danger', icon: 'warn' },
              ] as const).map((s) => (
                <MkStat key={s.label} label={s.label} value={s.value.toLocaleString()} tone={s.tone} icon={s.icon} />
              ))}
            </div>

            <div className="mk-cols2" style={{ marginBottom: 18 }}>
              <Breakdown
                title="Forma bo'yicha"
                sub="Qaysi taklif ko'proq lid berdi"
                rows={data.byForm}
              />
              <Breakdown
                title="Kampaniya bo'yicha"
                sub="Reklama byudjeti qayerda ishlayapti"
                rows={data.byCampaign}
              />
            </div>

            <MkCard title="Lidlar" sub="Eng yangisi tepada — Meta bergan vaqt bo'yicha">
              {data.items.length === 0
                ? (
                  <MkEmpty
                    text="Lid yo'q"
                    hint="Reklama endi ishga tushgan bo'lsa birinchi lidni kuting. Kelmayotgan bo'lsa — Sozlamalar bo'limidagi «Reklama lidlari» kartasida obuna va token holatini tekshiring."
                  />
                )
                : data.items.map((l) => (
                  <AdLeadRow key={l.id} lead={l} canEdit={canEdit} busy={busy === l.id} onRetry={() => retry(l.id)} />
                ))}

              {pages > 1 && (
                <div style={{ display: 'flex', justifyContent: 'center', gap: 10, marginTop: 16, alignItems: 'center' }}>
                  <button className="btn btn-outline btn-sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                    Oldingi
                  </button>
                  <span className="feed-time">{page} / {pages}</span>
                  <button className="btn btn-outline btn-sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                    Keyingi
                  </button>
                </div>
              )}
            </MkCard>
          </>
        )}
      </div>
    </MarketingPage>
  )
}

/**
 * Bitta lid qatori.
 *
 * ⚠️ Xato bo'lgan qator JIMGINA bo'sh ko'rinmaydi — sababi ochiq yoziladi va yonida «Qayta olish»
 * tugmasi turadi. Aks holda "reklamaga pul ketdi, lid qani?" degan savol javobsiz qolardi.
 */
function AdLeadRow({
  lead, canEdit, busy, onRetry,
}: {
  lead: IgAdLead
  canEdit: boolean
  busy: boolean
  onRetry: () => void
}) {
  const failed = lead.leadId === ''
  return (
    <div className="feed-item" style={{ alignItems: 'flex-start' }}>
      <div
        className="rule-num"
        style={failed
          ? { background: 'var(--danger-soft)', color: 'var(--danger)' }
          : { background: 'var(--primary-soft)', color: 'var(--primary)' }}
      >
        <Icon name={failed ? 'warn' : 'user'} style={{ width: 14, height: 14 }} />
      </div>

      <div className="feed-body" style={{ minWidth: 0 }}>
        <div style={{ fontWeight: 700, fontSize: 13.5 }}>
          {lead.fullName || "Ism ko'rsatilmagan"}
          {lead.isNewLead && <span className="badge badge-success" style={{ marginLeft: 8 }}>Yangi</span>}
          {!lead.isNewLead && !failed && (
            <span className="badge" style={{ marginLeft: 8 }}>Takroriy murojaat</span>
          )}
        </div>

        <div className="page-sub" style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 2 }}>
          {lead.phone
            ? <a href={`tel:${lead.phone}`}>{lead.phone}</a>
            : <span>telefon yo'q</span>}
          {lead.formName && <span>· {lead.formName}</span>}
          {lead.campaignName && <span>· {lead.campaignName}</span>}
          {lead.platform && <span>· {lead.platform === 'ig' ? 'Instagram' : lead.platform === 'fb' ? 'Facebook' : lead.platform}</span>}
        </div>

        {failed && lead.error && (
          <div style={{ marginTop: 8, color: 'var(--danger)', fontSize: 12.5 }}>{lead.error}</div>
        )}
      </div>

      <div style={{ textAlign: 'right', flexShrink: 0, display: 'flex', flexDirection: 'column', gap: 6, alignItems: 'flex-end' }}>
        <div className="feed-time">{(lead.createdTime || '').replace('T', ' ').slice(0, 16)}</div>
        {failed
          ? canEdit && (
            <button className="btn btn-outline btn-sm" onClick={onRetry} disabled={busy}>
              <Icon name="refresh" /> {busy ? 'Olinmoqda…' : 'Qayta olish'}
            </button>
          )
          : (
            <Link className="btn btn-ghost btn-sm" to="/admin/leads">
              <Icon name="arrowRight" /> Lidlarda
            </Link>
          )}
      </div>
    </div>
  )
}

/** Kesim kartochkasi: forma/kampaniya nomi → lidlar soni va ulush chizig'i. */
function Breakdown({ title, sub, rows }: { title: string; sub: string; rows: IgBreakdown[] }) {
  const total = rows.reduce((s, r) => s + r.count, 0)
  return (
    <MkCard title={title} sub={sub}>
      {rows.length === 0
        ? <MkEmpty text="Ma'lumot yo'q" />
        : rows.map((r) => {
          const pct = total > 0 ? Math.round((r.count / total) * 100) : 0
          return (
            <div className="metric-row" key={r.key} style={{ gap: 10 }}>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 600 }}>{r.key || '—'}</div>
                <div className="progress-track" style={{ marginTop: 6 }}>
                  {/* Rang `course-analytics.md` qoidasiga muvofiq — yashil/qizil juftlik ISHLATILMAYDI. */}
                  <div className="progress-fill" style={{ width: `${pct}%`, background: '#0284c7' }} />
                </div>
              </div>
              <div style={{ textAlign: 'right', minWidth: 62 }}>
                <div className="mk-num">{r.count.toLocaleString()}</div>
                <div className="feed-time">{pct}%</div>
              </div>
            </div>
          )
        })}
    </MkCard>
  )
}
