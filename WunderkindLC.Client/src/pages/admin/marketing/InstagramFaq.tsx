import { useCallback, useEffect, useMemo, useState } from 'react'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'
import {
  createIgFaq, deleteIgFaq, getIgFaq, syncIgFaq, updateIgFaq,
  type IgFaq, type IgFaqList, type IgFaqPayload, type IgFaqSyncResult,
} from '@/api/services/instagram'
import {
  Icon, MarketingPage, MkCard, MkDialog, MkEmpty, MkError, MkLoading, MkNotice, MkSheet, MkStat,
} from './mk'

/** Meta cheklovi: savol matni 80 belgidan oshsa API rad etadi. */
const QUESTION_MAX = 80
/** Javob matni limiti (bazadagi ustun bilan bir xil). */
const ANSWER_MAX = 1000
/** Instagram DM bitta xabarga ~1000 belgi beradi, lekin 485 dan uzun matnni
 *  mijoz ekranida ikkiga bo'lib ko'rsatishi mumkin — bu XATO emas, ogohlantirish. */
const ANSWER_SPLIT_WARN = 485

const EMPTY: IgFaqPayload = { question: '', answer: '', isActive: true }

/**
 * FAQ TUGMALARI (Instagram ice breakers) — mijoz Direct'ni BIRINCHI marta ochganda
 * ko'rinadigan tayyor savollar. Bosilsa savol mijoz nomidan ketadi va bot javob beradi.
 *
 * ⚠️ Amal va sinxron — IKKI ALOHIDA natija: yozuv bazaga tushgan bo'lsa ham Meta'ga
 * yetkazish yiqilgan bo'lishi mumkin (`sync.ok=false`). Ikkisi FARQLAB ko'rsatiladi,
 * aks holda operator «saqlandi» deb o'ylab, Instagram'da eski savollar qolganini sezmasdi.
 */
export function InstagramFaq() {
  const { can } = usePerm()
  const canCreate = can('marketing.rules', 'create')
  const canEdit = can('marketing.rules', 'edit')
  const canDelete = can('marketing.rules', 'delete')

  const [data, setData] = useState<IgFaqList | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [modal, setModal] = useState<IgFaq | 'new' | null>(null)
  /** O'chirish tasdig'i — `window.confirm` o'rniga bo'lim uslubidagi kichik oyna. */
  const [toDelete, setToDelete] = useState<IgFaq | null>(null)
  const [removing, setRemoving] = useState(false)
  const [syncing, setSyncing] = useState(false)
  /** CRUD/sinxron natijasi haqidagi yopiladigan xabar (amal natijasidan AYRI). */
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger' | 'info'; text: string } | null>(null)
  /** Tartib almashtirilayotgan qator — tugmalar bloklanadi (ikki PUT ketma-ket). */
  const [moving, setMoving] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgFaq()
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e, "FAQ tugmalarini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  /** Tartib bo'yicha; teng bo'lsa yaratilish vaqti — ro'yxat sakramasin. */
  const items = useMemo(
    () => [...(data?.items ?? [])].sort((a, b) => a.order - b.order || a.createdAt.localeCompare(b.createdAt)),
    [data],
  )
  const maxItems = data?.maxItems ?? 4
  const full = items.length >= maxItems

  const stats = useMemo(() => ({
    total: items.length,
    active: items.filter((f) => f.isActive).length,
    taps: items.reduce((s, f) => s + f.tapCount, 0),
  }), [items])

  /** CRUD muvaffaqiyatli, lekin sinxron yiqilgan holatning YAGONA xabar shakli. */
  const noteSaved = (sync: IgFaqSyncResult, done: string) => {
    setNotice(sync.ok
      ? { tone: 'success', text: `${done} va Meta bilan sinxronlandi.` }
      : { tone: 'danger', text: `${done}, lekin Meta bilan sinxronlashmadi: ${sync.message} Keyinroq «Meta bilan sinxronlash» tugmasini bosing.` })
  }

  const remove = async (f: IgFaq) => {
    setError('')
    setRemoving(true)
    try {
      const { sync } = await deleteIgFaq(f.id)
      setToDelete(null)
      noteSaved(sync, "Savol o'chirildi")
      load()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
      setToDelete(null)
    } finally {
      setRemoving(false)
    }
  }

  /**
   * Yuqoriga/pastga — qo'shni bilan `order` almashtiriladi (ikki PUT).
   * Drag-and-drop ATAYIN yo'q: ro'yxat ko'pi bilan 4 qator, tugma soddaroq va aniqroq.
   */
  const move = async (idx: number, dir: -1 | 1) => {
    const a = items[idx]
    const b = items[idx + dir]
    if (!a || !b || moving) return
    setError('')
    setMoving(true)
    try {
      // Ikkala order teng bo'lsa (eski/qo'lda kiritilgan ma'lumot) oddiy almashtirish
      // hech narsani o'zgartirmasdi — u holda siljish yo'nalishi bo'yicha ±1 beriladi.
      const [oa, ob] = a.order === b.order ? [a.order + dir, a.order] : [b.order, a.order]
      const r1 = await updateIgFaq(a.id, { question: a.question, answer: a.answer, isActive: a.isActive, order: oa })
      const r2 = await updateIgFaq(b.id, { question: b.question, answer: b.answer, isActive: b.isActive, order: ob })
      const bad = [r1.sync, r2.sync].find((s) => !s.ok)
      if (bad) noteSaved(bad, "Tartib o'zgartirildi")
      load()
    } catch (e) {
      setError(apiErrorMessage(e, "Tartibni o'zgartirib bo'lmadi"))
    } finally {
      setMoving(false)
    }
  }

  const runSync = async () => {
    setError('')
    setSyncing(true)
    try {
      const sync = await syncIgFaq()
      setNotice(sync.ok
        ? { tone: 'success', text: sync.message || 'Meta bilan sinxronlandi.' }
        : { tone: 'danger', text: `Sinxronlashmadi: ${sync.message}` })
      load()
    } catch (e) {
      setError(apiErrorMessage(e, 'Sinxronlab bo\'lmadi'))
    } finally {
      setSyncing(false)
    }
  }

  return (
    <MarketingPage
      title="FAQ tugmalari"
      sub="Direct birinchi ochilganda ko'rinadigan tayyor savollar (ice breakers)."
      actions={canCreate && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {full && <span className="field-hint">Meta ko'pi bilan {maxItems} ta savolga ruxsat beradi.</span>}
          <button
            className="btn btn-primary"
            disabled={full}
            title={full ? `Meta ko'pi bilan ${maxItems} ta savolga ruxsat beradi` : undefined}
            onClick={() => setModal('new')}
          >
            <Icon name="plus" /> Yangi savol
          </button>
        </div>
      )}
    >
      <div className="fade-up">
        {error && <div style={{ marginBottom: 14 }}><MkError text={error} onRetry={load} /></div>}
        {notice && (
          <div style={{ marginBottom: 14 }}>
            <MkNotice tone={notice.tone} text={notice.text} onClose={() => setNotice(null)} />
          </div>
        )}

        {loading && <MkLoading />}

        {!loading && data && (
          <>
            {items.length > 0 && (
              <div className="mk-kpi" style={{ marginBottom: 18 }}>
                <MkStat label="Jami savol" value={stats.total} icon="msg" tone="primary" hint={`Ko'pi bilan ${maxItems} ta`} />
                <MkStat label="Faol" value={stats.active} icon="check" tone="success" hint="Faqat shular Instagram'ga chiqadi" />
                <MkStat label="Jami bosilish" value={stats.taps.toLocaleString()} icon="trendUp" tone="muted" hint="Mijozlar tugmani necha marta bosgan" />
              </div>
            )}

            {/* Sinxron holati DOIM ko'rinadi (bo'sh ro'yxatda ham): «Instagram'da nima
                turibdi» savoliga javob — sahifaning asosiy ishonch manbai. */}
            <MkCard>
              <div className="row-between">
                <div style={{ minWidth: 0 }}>
                  <div className="opt-name">Meta bilan sinxron</div>
                  <div className="opt-desc">
                    {data.syncedAt
                      ? `Oxirgi muvaffaqiyatli sinxron: ${formatDateTime(data.syncedAt)}`
                      : "Hali sinxronlanmagan — savollar Instagram'da hali ko'rinmaydi."}
                  </div>
                  {data.syncError && (
                    <div
                      role="alert"
                      style={{ marginTop: 6, display: 'flex', alignItems: 'center', gap: 6, color: 'var(--danger)', fontSize: 13, fontWeight: 600 }}
                    >
                      <Icon name="warn" style={{ width: 15, height: 15, flexShrink: 0 }} />
                      <span>{data.syncError}</span>
                    </div>
                  )}
                </div>
                {canEdit && (
                  <button className="btn btn-ghost" onClick={runSync} disabled={syncing}>
                    <Icon name="refresh" /> {syncing ? 'Sinxronlanmoqda…' : 'Meta bilan sinxronlash'}
                  </button>
                )}
              </div>
            </MkCard>

            {items.length === 0 && (
              <div style={{ marginTop: 18 }}>
                <MkEmpty
                  text="FAQ tugmasi yo'q"
                  hint="FAQ tugmalari — mijoz profilingizga birinchi marta Direct ochganda xabar maydoni ustida ko'rinadigan tayyor savollar. Mijoz tugmani bossa savol o'z nomidan yuboriladi va siz kiritgan javob darhol qaytadi — «Narxlar qanday?», «Manzilingiz qayerda?» kabi savollar bilan suhbat bir bosishda boshlanadi."
                />
              </div>
            )}

            {items.length > 0 && (
              <div style={{ marginTop: 18 }}>
                {/* Jadval — Javob qoidalari bilan bir xil naqsh: tartib, holat va
                    bosilishlar yonma-yon solishtiriladi. */}
                <MkCard pad={false}>
                  <div className="mk-scroll-x">
                    <table className="mk-table">
                      <thead>
                        <tr>
                          <th style={{ width: 110 }}>Tartib</th>
                          <th>Savol</th>
                          <th>Javob</th>
                          <th>Holat</th>
                          <th className="mk-num">Bosilish</th>
                          <th style={{ width: 90 }} />
                        </tr>
                      </thead>
                      <tbody>
                        {items.map((f, i) => (
                          <tr key={f.id}>
                            <td>
                              <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                                <span className="mk-num" style={{ minWidth: 18 }}>{i + 1}</span>
                                {canEdit && (
                                  <>
                                    <button
                                      className="icon-btn" title="Yuqoriga" aria-label="Yuqoriga"
                                      style={{ width: 26, height: 26 }}
                                      disabled={i === 0 || moving}
                                      onClick={() => move(i, -1)}
                                    >
                                      <Icon name="chevUp" style={{ width: 14, height: 14 }} />
                                    </button>
                                    <button
                                      className="icon-btn" title="Pastga" aria-label="Pastga"
                                      style={{ width: 26, height: 26 }}
                                      disabled={i === items.length - 1 || moving}
                                      onClick={() => move(i, 1)}
                                    >
                                      <Icon name="chevDown" style={{ width: 14, height: 14 }} />
                                    </button>
                                  </>
                                )}
                              </div>
                            </td>
                            <td style={{ fontWeight: 700 }}>{f.question}</td>
                            <td style={{ maxWidth: 520, color: 'var(--text-2)' }}>
                              <div style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {f.answer}
                              </div>
                            </td>
                            <td>
                              {f.isActive
                                ? <span className="badge badge-success"><span className="badge-dot" /> Faol</span>
                                : <span className="badge" style={{ background: 'var(--surface-2)', color: 'var(--text-3)' }}>O'chiq</span>}
                            </td>
                            <td className="mk-num">{f.tapCount.toLocaleString()}</td>
                            <td>
                              <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                                {canEdit && (
                                  <button className="icon-btn" title="Tahrirlash" style={{ width: 32, height: 32 }} onClick={() => setModal(f)}>
                                    <Icon name="edit" style={{ width: 15, height: 15 }} />
                                  </button>
                                )}
                                {canDelete && (
                                  <button className="icon-btn" title="O'chirish" style={{ width: 32, height: 32, color: 'var(--danger)' }} onClick={() => setToDelete(f)}>
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
              </div>
            )}
          </>
        )}

        {modal && (
          <FaqSheet
            faq={modal === 'new' ? null : modal}
            onClose={() => setModal(null)}
            onSaved={(sync, isNew) => {
              setModal(null)
              noteSaved(sync, isNew ? "Savol qo'shildi" : 'Savol saqlandi')
              load()
            }}
          />
        )}

        {toDelete && (
          <MkDialog
            title="Savolni o'chirish"
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
              «<b>{toDelete.question}</b>» savoli o'chirilsinmi?
            </div>
            <div className="field-hint" style={{ marginTop: 8 }}>
              O'chirilgach tugma Instagram Direct'dan ham yo'qoladi (Meta bilan sinxronlanadi).
            </div>
          </MkDialog>
        )}
      </div>
    </MarketingPage>
  )
}

/**
 * Savol yaratish/tahrirlash — to'liq ekranli oyna (`MkSheet`, Javob qoidalari uslubi).
 * Chapda mijoz ko'radigan SAVOL, o'ngda bot qaytaradigan JAVOB — ikkisi mustaqil matn.
 */
function FaqSheet({
  faq, onClose, onSaved,
}: {
  faq: IgFaq | null
  onClose: () => void
  onSaved: (sync: IgFaqSyncResult, isNew: boolean) => void
}) {
  /**
   * Boshlang'ich qiymat AYRI saqlanadi — `dirty` shu bilan solishtiriladi (RuleSheet
   * bilan bir xil sabab: asl nusxa oyna ochilgandan keyin o'zgarmasligi kerak).
   */
  const [initial] = useState<IgFaqPayload>(() => (
    faq
      ? { question: faq.question, answer: faq.answer, isActive: faq.isActive }
      : EMPTY
  ))
  const [form, setForm] = useState<IgFaqPayload>(initial)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const patch = (p: Partial<IgFaqPayload>) => setForm((f) => ({ ...f, ...p }))

  /** Forma o'zgardimi — MkSheet Esc/✕ da tasdiq so'rashi uchun (maydonma-maydon). */
  const dirty = (Object.keys(initial) as (keyof IgFaqPayload)[]).some((k) => form[k] !== initial[k])

  const qLen = form.question.length
  const aLen = form.answer.length
  const qOver = qLen > QUESTION_MAX
  const aOver = aLen > ANSWER_MAX

  const save = async () => {
    if (!form.question.trim() || !form.answer.trim()) {
      setError("Savol va javob matni to'ldirilishi shart.")
      return
    }
    if (qOver) {
      setError(`Savol ${QUESTION_MAX} belgidan oshmasin (hozir ${qLen} ta).`)
      return
    }
    if (aOver) {
      setError(`Javob ${ANSWER_MAX} belgidan oshmasin (hozir ${aLen} ta).`)
      return
    }
    setSaving(true)
    setError('')
    try {
      // Tahrirda `order` SAQLANADI — yuborilmasa server uni o'zgartirmasligi
      // kontraktda kafolatlanmagan, ochiq yuborilgani aniqroq.
      const res = faq
        ? await updateIgFaq(faq.id, { ...form, order: faq.order })
        : await createIgFaq(form)
      onSaved(res.sync, !faq)
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <MkSheet
      title={faq ? 'Savolni tahrirlash' : 'Yangi savol'}
      sub="Mijoz tugmani bossa savol o'z nomidan yuboriladi va bot shu javobni qaytaradi."
      icon="msg"
      onClose={onClose}
      dirty={dirty}
      footer={(
        <>
          {/* Xato tugmalar YONIDA — `mk-sheet-foot` qotib turadi, tanadagi xato esa
              skroll ortida ko'rinmay qolardi (RuleSheet dagi bilan bir xil sabab). */}
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
        {/* ── CHAP: mijoz KO'RADIGAN savol ── */}
        <MkCard title="Savol" sub="Direct ochilganda tugma sifatida ko'rinadi">
          <div className="field">
            <label className="field-label">Savol matni</label>
            <input
              className="input" value={form.question}
              onChange={(e) => patch({ question: e.target.value })}
              placeholder="Masalan: Narxlar qanday?"
            />
            {/* Hisoblagich limitdan oshganda QIZARADI — saqlash ham bloklanadi. */}
            <div className="field-hint" style={qOver ? { color: 'var(--danger)', fontWeight: 700 } : undefined}>
              {qLen}/{QUESTION_MAX} belgi{qOver ? ' — limitdan oshdi, Meta qabul qilmaydi' : ''}
            </div>
          </div>

          {/* Jonli ko'rinish — savol Instagram'dagi kabi tugma shaklida. Bo'sh
              bo'lsa chizilmaydi (bo'sh pufak faqat joy egallardi). */}
          {form.question.trim() && (
            <div className="field">
              <label className="field-label">Mijoz nimani ko'radi</label>
              <div
                style={{
                  display: 'inline-block',
                  background: 'var(--surface-2)',
                  border: '1px solid var(--border)',
                  borderRadius: 999,
                  padding: '9px 16px',
                  fontSize: 13.5,
                  fontWeight: 600,
                  color: 'var(--text-1)',
                  maxWidth: '100%',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                }}
              >
                {form.question}
              </div>
            </div>
          )}

          <div className="row-between">
            <div>
              <div className="opt-name">Faol</div>
              <div className="opt-desc">O'chirilgan savol saqlanadi, lekin Instagram'ga chiqmaydi.</div>
            </div>
            <div className={'switch ' + (form.isActive ? 'on' : '')} onClick={() => patch({ isActive: !form.isActive })} />
          </div>
        </MkCard>

        {/* ── O'NG: bot QAYTARADIGAN javob ── */}
        <MkCard title="Javob" sub="Tugma bosilganda avtomatik yuboriladigan matn">
          <div className="field">
            <label className="field-label">Javob matni</label>
            <textarea
              className="textarea" value={form.answer}
              onChange={(e) => patch({ answer: e.target.value })}
              placeholder="Mijozga yuboriladigan javob…"
              style={{ minHeight: 170 }}
            />
            <div className="field-hint" style={aOver ? { color: 'var(--danger)', fontWeight: 700 } : undefined}>
              {aLen}/{ANSWER_MAX} belgi{aOver ? ' — limitdan oshdi' : ''}
            </div>
            {/* 485+ — BLOKLAMAYDIGAN ogohlantirish: xabar ketadi, lekin ikkiga
                bo'linib ko'rinishi mumkin. Limit xatosi bilan aralashmasin. */}
            {!aOver && aLen > ANSWER_SPLIT_WARN && (
              <div
                style={{
                  marginTop: 6, display: 'flex', alignItems: 'center', gap: 6,
                  color: 'var(--warning)', fontSize: 12.5, fontWeight: 600,
                }}
              >
                <Icon name="warn" style={{ width: 14, height: 14, flexShrink: 0 }} />
                <span>Instagram DM'da {ANSWER_SPLIT_WARN} belgidan uzun xabar ikkiga bo'linishi mumkin.</span>
              </div>
            )}
          </div>

          {form.answer.trim() && (
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
                {form.answer}
              </div>
            </div>
          )}
        </MkCard>
      </div>
    </MkSheet>
  )
}
