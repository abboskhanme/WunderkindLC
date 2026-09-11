import { useCallback, useEffect, useMemo, useState } from 'react'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  createIgRule, deleteIgRule, getIgRules, updateIgRule,
  type IgRule, type IgRuleChannel, type IgRulePayload,
} from '@/api/services/instagram'
import {
  Icon, MarketingPage, MkCard, MkDialog, MkEmpty, MkError, MkLoading, MkSheet, MkStat,
} from './mk'

/** Qoida qaysi kanalda ishlashi. */
const CHANNELS: { key: IgRuleChannel; label: string }[] = [
  { key: 'any', label: 'Ikkalasi' },
  { key: 'comment', label: 'Izoh' },
  { key: 'dm', label: 'Shaxsiy xabar' },
]

const channelLabel = (c: IgRuleChannel) => CHANNELS.find((x) => x.key === c)?.label ?? c

const EMPTY: IgRulePayload = {
  title: '',
  keywords: '',
  channel: 'any',
  replyText: '',
  stopAi: true,
  isActive: true,
  order: 0,
}

/**
 * JAVOB QOIDALARI — kalit so'z → tayyor javob.
 *
 * Qoidalar AI'dan OLDIN tekshiriladi: mos kelsa javob darhol (tez va arzon) yuboriladi.
 * «AI'ni to'xtatish» yoqilgan bo'lsa qoida ishlagach AI umuman chaqirilmaydi.
 * Tartib (`order`) muhim — birinchi mos kelgan qoida ishlaydi.
 */
export function InstagramRules() {
  const { can } = usePerm()
  const canCreate = can('marketing.rules', 'create')
  const canEdit = can('marketing.rules', 'edit')
  const canDelete = can('marketing.rules', 'delete')

  const [rules, setRules] = useState<IgRule[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [modal, setModal] = useState<IgRule | 'new' | null>(null)
  /** O'chirish tasdig'i — `window.confirm` o'rniga bo'lim uslubidagi kichik oyna. */
  const [toDelete, setToDelete] = useState<IgRule | null>(null)
  const [removing, setRemoving] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgRules()
      .then(setRules)
      .catch((e) => setError(apiErrorMessage(e, "Qoidalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  const remove = async (r: IgRule) => {
    setError('')
    setRemoving(true)
    try {
      await deleteIgRule(r.id)
      setToDelete(null)
      load()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
      setToDelete(null)
    } finally {
      setRemoving(false)
    }
  }

  /**
   * Ro'yxat ustidagi ko'rsatkichlar — "qoidalar umuman ishlayaptimi" degan savolga
   * jadvalni o'qimasdan javob beradi. Sanoq mijoz tomonda: ro'yxat baribir to'liq
   * yuklanadi, ya'ni qo'shimcha so'rov KERAK EMAS.
   */
  const stats = useMemo(() => ({
    total: rules.length,
    active: rules.filter((r) => r.isActive).length,
    stopAi: rules.filter((r) => r.stopAi).length,
    matches: rules.reduce((s, r) => s + r.matchCount, 0),
  }), [rules])

  return (
    <MarketingPage
      title="Javob qoidalari"
      sub="Kalit so'z → tayyor javob. AI'dan oldin ishlaydi."
      actions={canCreate && (
        <button className="btn btn-primary" onClick={() => setModal('new')}>
          <Icon name="plus" /> Yangi qoida
        </button>
      )}
    >
      <div className="fade-up">
        {error && <div style={{ marginBottom: 14 }}><MkError text={error} onRetry={load} /></div>}

        {loading && <MkLoading />}

        {!loading && rules.length === 0 && (
          <MkEmpty
            text="Qoida yo'q"
            hint="Eng ko'p beriladigan savollar (narx, manzil, ish vaqti) uchun qoida qo'shsangiz, javob bir zumda va aniq bo'ladi."
          />
        )}

        {!loading && rules.length > 0 && (
          <>
            <div className="mk-kpi" style={{ marginBottom: 18 }}>
              <MkStat label="Jami qoida" value={stats.total} icon="rules" tone="primary" />
              <MkStat label="Faol" value={stats.active} icon="check" tone="success" hint="Faqat shular ishlaydi" />
              <MkStat label="AI'ni to'xtatadi" value={stats.stopAi} icon="zap" tone="warning" hint="Javobdan keyin AI chaqirilmaydi" />
              <MkStat label="Jami moslik" value={stats.matches.toLocaleString()} icon="trendUp" tone="muted" hint="Qoidalar necha marta ishlagan" />
            </div>

            {/* Jadval ATAYIN qoldi: ustunlar (tartib · kanal · AI · moslik) yonma-yon
                solishtiriladi, kartochkalarda esa bu taqqoslash yo'qolardi. Sahifa
                to'liq kenglikda bo'lgani uchun jadval endi qisqarmaydi. */}
            <MkCard pad={false}>
              <div className="mk-scroll-x">
                <table className="mk-table">
                  <thead>
                    <tr>
                      <th style={{ width: 60 }}>Tartib</th>
                      <th>Sarlavha</th>
                      <th>Kalit so'zlar</th>
                      <th>Kanal</th>
                      <th>Javob</th>
                      <th>AI</th>
                      <th>Holat</th>
                      <th className="mk-num">Moslik</th>
                      <th style={{ width: 90 }} />
                    </tr>
                  </thead>
                  <tbody>
                    {rules.map((r) => (
                      <tr key={r.id}>
                        <td className="mk-num">{r.order}</td>
                        <td style={{ fontWeight: 700 }}>{r.title}</td>
                        <td>
                          <div className="kw-wrap">
                            {r.keywords.split(',').map((k) => k.trim()).filter(Boolean).map((k) => (
                              <span key={k} className="chip-kw">{k}</span>
                            ))}
                          </div>
                        </td>
                        <td>{channelLabel(r.channel)}</td>
                        <td style={{ maxWidth: 520, color: 'var(--text-2)' }}>
                          <div style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {r.replyText}
                          </div>
                        </td>
                        <td>
                          {r.stopAi
                            ? <span className="badge" style={{ background: 'var(--surface-2)', color: 'var(--text-3)' }}>To'xtatiladi</span>
                            : <span className="badge badge-ai"><Icon name="sparkle" style={{ width: 11, height: 11 }} /> Davom etadi</span>}
                        </td>
                        <td>
                          {r.isActive
                            ? <span className="badge badge-success"><span className="badge-dot" /> Faol</span>
                            : <span className="badge" style={{ background: 'var(--surface-2)', color: 'var(--text-3)' }}>O'chiq</span>}
                        </td>
                        <td className="mk-num">{r.matchCount.toLocaleString()}</td>
                        <td>
                          <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                            {canEdit && (
                              <button className="icon-btn" title="Tahrirlash" style={{ width: 32, height: 32 }} onClick={() => setModal(r)}>
                                <Icon name="edit" style={{ width: 15, height: 15 }} />
                              </button>
                            )}
                            {canDelete && (
                              <button className="icon-btn" title="O'chirish" style={{ width: 32, height: 32, color: 'var(--danger)' }} onClick={() => setToDelete(r)}>
                                <Icon name="trash" style={{ width: 15, height: 15 }} />
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </MkCard>
          </>
        )}

        {modal && (
          <RuleSheet
            rule={modal === 'new' ? null : modal}
            nextOrder={rules.length ? Math.max(...rules.map((r) => r.order)) + 1 : 1}
            onClose={() => setModal(null)}
            onSaved={() => { setModal(null); load() }}
          />
        )}

        {toDelete && (
          <MkDialog
            title="Qoidani o'chirish"
            tone="danger"
            onClose={() => setToDelete(null)}
            footer={(
              <>
                <button className="btn btn-ghost" onClick={() => setToDelete(null)}>Bekor qilish</button>
                <button className="btn btn-danger" onClick={() => remove(toDelete)} disabled={removing}>
                  <Icon name="trash" /> {removing ? "O'chirilmoqda…" : "O'chirish"}
                </button>
              </>
            )}
          >
            <div>
              «<b>{toDelete.title}</b>» qoidasi o'chirilsinmi?
            </div>
            <div className="field-hint" style={{ marginTop: 8 }}>
              Qoida o'chirilgach shu kalit so'zlarga tayyor javob yuborilmaydi — savol AI'ga o'tadi.
            </div>
          </MkDialog>
        )}
      </div>
    </MarketingPage>
  )
}

/**
 * Qoida yaratish/tahrirlash — TO'LIQ EKRANLI oyna (`MkSheet`).
 *
 * ⚠️ Forma ATAYIN ikki ustunga bo'lingan, chunki qoidada ikkita mustaqil savol bor:
 * «QACHON ishlaydi» (kalit so'z, kanal, tartib) va «NIMA QILADI» (javob matni,
 * AI'ni to'xtatish, faollik). Ilgari ular bitta tor ustunda ketma-ket turib,
 * foydalanuvchi javob matnini yozayotib kalit so'zlarni ko'rmasdi.
 */
function RuleSheet({
  rule, nextOrder, onClose, onSaved,
}: {
  rule: IgRule | null
  nextOrder: number
  onClose: () => void
  onSaved: () => void
}) {
  /**
   * BOSHLANG'ICH qiymat AYRI saqlanadi — `dirty` ("forma o'zgardimi") aynan shunga
   * solishtirib aniqlanadi. Bir marta hisoblanadi (`useState` initsializatori): oyna
   * ochilgandan keyin "asl nusxa" o'zgarmasligi kerak, aks holda har chizilishida
   * yangilanib, hech qachon "o'zgardi" bo'lmasdi.
   */
  const [initial] = useState<IgRulePayload>(() => (
    rule
      ? {
        title: rule.title,
        keywords: rule.keywords,
        channel: rule.channel,
        replyText: rule.replyText,
        stopAi: rule.stopAi,
        isActive: rule.isActive,
        order: rule.order,
      }
      : { ...EMPTY, order: nextOrder }
  ))
  const [form, setForm] = useState<IgRulePayload>(initial)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const patch = (p: Partial<IgRulePayload>) => setForm((f) => ({ ...f, ...p }))

  /**
   * Forma o'zgardimi. `MkSheet` shu bayroqqa qarab Esc va ✕ bosilganda TASDIQ so'raydi:
   * ilgari yarim to'ldirilgan qoida bitta tasodifiy Esc bilan izsiz yo'qolardi.
   *
   * ⚠️ Maydonma-maydon solishtiriladi (`JSON.stringify` emas): kalitlar tartibi
   * `{ ...f, ...p }` dan keyin o'zgarib qolsa satrlar teng bo'lmay, forma tegilmagan
   * holda ham "o'zgargan" ko'rinardi.
   */
  const dirty = (Object.keys(initial) as (keyof IgRulePayload)[]).some((k) => form[k] !== initial[k])

  /** Kalit so'zlar chiplari — vergul bilan ajratilgani ANIQ ko'rinsin. */
  const keywordChips = form.keywords.split(',').map((k) => k.trim()).filter(Boolean)

  const save = async () => {
    if (!form.title.trim() || !form.replyText.trim()) {
      setError("Sarlavha va javob matni to'ldirilishi shart.")
      return
    }
    setSaving(true)
    setError('')
    try {
      if (rule) await updateIgRule(rule.id, form)
      else await createIgRule(form)
      onSaved()
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <MkSheet
      title={rule ? 'Qoidani tahrirlash' : 'Yangi qoida'}
      sub="Mijoz kalit so'zlardan birini yozsa — shu javob darhol yuboriladi."
      icon="rules"
      onClose={onClose}
      dirty={dirty}
      footer={(
        <>
          {/* 🔴 XATO AYNAN SHU YERDA — tugmalar yonida.
              `mk-sheet-body` skrollanadi, `mk-sheet-foot` esa qotib turadi. Xato tananing
              TEPASIDA chizilganda (ilgari shunday edi) pastga tushib "Saqlash" bosgan odam
              uni UMUMAN ko'rmasdi: u faqat oyna yopilmayotganini sezardi va sababini
              bilmasdi. Oyoqdagi xat esa bosilgan tugmadan bir qarichda turadi.
              `marginRight: auto` — oyoq `justify-content: flex-end`, ya'ni matn chapga
              itariladi va tugmalarni surib yubormaydi. */}
          {error && (
            <div
              role="alert"
              style={{
                marginRight: 'auto', display: 'flex', alignItems: 'center', gap: 8,
                color: 'var(--danger)', fontSize: 13, fontWeight: 700, minWidth: 0,
              }}
            >
              <Icon name="warn" style={{ width: 16, height: 16, flexShrink: 0 }} />
              <span>{error}</span>
            </div>
          )}
          <button className="btn btn-ghost" onClick={onClose}>Bekor qilish</button>
          <button className="btn btn-primary" onClick={save} disabled={saving}>
            <Icon name="check" /> {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </>
      )}
    >

      <div className="mk-cols2">
        {/* ── CHAP: QACHON ishlaydi ── */}
        <MkCard title="Qachon ishlaydi" sub="Qoida qanday xabarga va qaysi navbatda mos keladi">
          <div className="field">
            <label className="field-label">Sarlavha</label>
            <input
              className="input" value={form.title}
              onChange={(e) => patch({ title: e.target.value })}
              placeholder="Masalan: Narx so'rovlari"
            />
            <div className="field-hint">Faqat ichki nom — mijozga ko'rinmaydi.</div>
          </div>

          <div className="field">
            <label className="field-label">Kalit so'zlar</label>
            <input
              className="input" value={form.keywords}
              onChange={(e) => patch({ keywords: e.target.value })}
              placeholder="narx, qancha, narxi, price, цена"
            />
            <div className="field-hint">Vergul bilan ajrating. Mijoz shu so'zlardan birini yozsa qoida ishlaydi.</div>
            {keywordChips.length > 0 && (
              <div className="kw-wrap" style={{ marginTop: 8 }}>
                {keywordChips.map((k, i) => <span key={`${k}-${i}`} className="chip-kw">{k}</span>)}
              </div>
            )}
          </div>

          <div className="field">
            <label className="field-label">Kanal</label>
            <div className="seg" style={{ width: 'fit-content' }}>
              {CHANNELS.map((c) => (
                <button
                  key={c.key}
                  className={form.channel === c.key ? 'active' : ''}
                  onClick={() => patch({ channel: c.key })}
                >{c.label}</button>
              ))}
            </div>
            <div className="field-hint">Qoida faqat tanlangan kanaldagi xabarlarga tegishli.</div>
          </div>

          <div className="field">
            <label className="field-label">Tartib</label>
            <input
              className="input" type="number" min={0} value={form.order}
              onChange={(e) => patch({ order: Number(e.target.value) || 0 })}
              style={{ maxWidth: 140 }}
            />
            <div className="field-hint">Kichik raqam oldin tekshiriladi — birinchi mos kelgan qoida ishlaydi.</div>
          </div>
        </MkCard>

        {/* ── O'NG: NIMA QILADI ── */}
        <MkCard title="Nima qiladi" sub="Mijozga yuboriladigan javob va qoidadan keyingi xatti-harakat">
          <div className="field">
            <label className="field-label">Javob matni</label>
            <textarea
              className="textarea" value={form.replyText}
              onChange={(e) => patch({ replyText: e.target.value })}
              placeholder="Mijozga yuboriladigan javob…"
              style={{ minHeight: 170 }}
            />
          </div>

          {/* Jonli ko'rinish — matn mijozning ekranida qanday chiqishini ko'rsatadi.
              Bo'sh bo'lsa CHIZILMAYDI: bo'sh pufak faqat joyni egallardi. */}
          {form.replyText.trim() && (
            <div className="field">
              <label className="field-label">Mijoz nimani ko'radi</label>
              <div
                style={{
                  background: 'var(--surface-2)',
                  border: '1px solid var(--border)',
                  borderRadius: 14,
                  borderTopLeftRadius: 4,
                  padding: '11px 14px',
                  fontSize: 13.5,
                  lineHeight: 1.55,
                  color: 'var(--text-1)',
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                }}
              >
                {form.replyText}
              </div>
              <div className="field-hint">{form.replyText.length} belgi</div>
            </div>
          )}

          <div className="row-between">
            <div>
              <div className="opt-name">AI'ni to'xtatish</div>
              <div className="opt-desc">
                Yoqilgan bo'lsa qoida javob bergach AI umuman chaqirilmaydi (tez va arzon).
                O'chirilsa AI javobni to'ldiradi.
              </div>
            </div>
            <div className={'switch ' + (form.stopAi ? 'on' : '')} onClick={() => patch({ stopAi: !form.stopAi })} />
          </div>

          <div className="row-between">
            <div>
              <div className="opt-name">Faol</div>
              <div className="opt-desc">O'chirilgan qoida saqlanadi, lekin ishlamaydi.</div>
            </div>
            <div className={'switch ' + (form.isActive ? 'on' : '')} onClick={() => patch({ isActive: !form.isActive })} />
          </div>
        </MkCard>
      </div>
    </MkSheet>
  )
}
