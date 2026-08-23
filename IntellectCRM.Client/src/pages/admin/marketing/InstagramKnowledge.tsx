import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  deleteIgKnowledgeSource, getIgKnowledge, getIgKnowledgeStatus, importIgKnowledgeDocx,
  saveIgKnowledge, type IgKnowledge, type IgKnowledgeStatus,
} from '@/api/services/instagram'
import { Icon, MarketingPage, MkEmpty, MkError, MkLoading, MkNotice, MkStat } from './mk'

/**
 * BILIM BAZASI — AI javoblarining YAGONA manbasi.
 *
 * ⚠️ AI faqat shu yerda yozilgan ma'lumot asosida javob beradi: narxni, chegirmani yoki
 * jadvalni O'YLAB TOPMAYDI. Bu yer bo'sh bo'lsa agent hech qanday mazmunli javob bera olmaydi.
 *
 * Barcha bo'laklar BITTA «Saqlash» tugmasi bilan yuboriladi (bulk `PUT /knowledge`) —
 * qatorlarni erkin qo'shib/o'chirib, tartibini o'zgartirib, so'ng bir marta saqlaysiz.
 *
 * ⚠️ WORD YUKLASH bundan MUSTAQIL: u serverda darhol saqlanadi va yangilangan ro'yxatni
 * qaytaradi. Sabab — yuklangan bo'laklarni «saqlanmagan o'zgarish» holatida ushlab turish
 * xavfli edi: admin sahifani yopsa 40 betlik hujjat jimgina yo'qolardi.
 */
export function InstagramKnowledge() {
  const { can } = usePerm()
  const canEdit = can('marketing.knowledge', 'edit')

  const [items, setItems] = useState<IgKnowledge[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState('')
  const [dirty, setDirty] = useState(false)

  // ── Word yuklash ──
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)
  const [status, setStatus] = useState<IgKnowledgeStatus | null>(null)

  /**
   * Holat (vektorlar tayyorligi + yuklangan hujjatlar) ALOHIDA so'rov bilan olinadi.
   * ⚠️ Xatosi ekranga chiqarilmaydi: bu YORDAMCHI ma'lumot va uning nosozligi tufayli
   * bilim bazasining o'zi qizil banner ostida qolib ketmasligi kerak.
   */
  const loadStatus = useCallback(() => {
    getIgKnowledgeStatus().then(setStatus).catch(() => setStatus(null))
  }, [])

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgKnowledge()
      .then((xs) => { setItems(xs); setDirty(false) })
      .catch((e) => setError(apiErrorMessage(e, "Bilim bazasini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
    loadStatus()
  }, [loadStatus])

  useEffect(load, [load])

  /**
   * Word hujjatini yuklash. Natija — YANGILANGAN to'liq ro'yxat (klient qayta `GET` qilmaydi).
   *
   * ⚠️ Saqlanmagan o'zgarishlar bo'lsa yuklash TO'XTATILADI: server javobidagi ro'yxat
   * ekrandagini butunlay almashtiradi, ya'ni admin qo'lda yozgan matni jimgina yo'qolardi.
   */
  const upload = async (file: File) => {
    if (dirty) {
      setError("Avval o'zgarishlarni saqlang — Word yuklash ro'yxatni serverdagi holat bilan almashtiradi.")
      return
    }
    setUploading(true)
    setError('')
    setSaved('')
    try {
      const res = await importIgKnowledgeDocx(file)
      setItems(res.items)
      setDirty(false)
      // ⚠️ Chegaradan oshib olinmagan bo'laklar JIM QOLMAYDI — aks holda admin hujjat
      // to'liq yuklandi deb o'ylab, yarmi yo'q holatda qolardi.
      setSaved(
        `«${res.fileName}» yuklandi — ${res.added} ta bo'lak qo'shildi.`
        + (res.skipped > 0 ? ` ⚠️ ${res.skipped} ta bo'lak chegaradan oshgani uchun olinmadi.` : ''),
      )
      loadStatus()
    } catch (e) {
      setError(apiErrorMessage(e, "Hujjatni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  /** Bitta hujjatdan kelgan barcha bo'laklarni o'chirish (eski versiyani almashtirish). */
  const removeSource = async (fileName: string, chunks: number) => {
    if (!window.confirm(`«${fileName}» hujjatidan kelgan ${chunks} ta bo'lak o'chirilsinmi?`)) return
    setError('')
    setSaved('')
    try {
      const res = await deleteIgKnowledgeSource(fileName)
      setItems(res.items)
      setDirty(false)
      setSaved(`«${fileName}» olib tashlandi — ${res.skipped} ta bo'lak o'chirildi.`)
      loadStatus()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    }
  }

  const patch = (i: number, p: Partial<IgKnowledge>) => {
    setItems((xs) => xs.map((x, k) => (k === i ? { ...x, ...p } : x)))
    setDirty(true)
    setSaved('')
  }

  const add = () => {
    setItems((xs) => [...xs, { title: '', content: '', order: xs.length + 1, isActive: true }])
    setDirty(true)
    setSaved('')
  }

  const remove = (i: number) => {
    if (!window.confirm("Bo'lak o'chirilsinmi? (Saqlaganingizdan keyin yo'qoladi)")) return
    setItems((xs) => xs.filter((_, k) => k !== i))
    setDirty(true)
    setSaved('')
  }

  /** Tartibni almashtirish — `order` maydonlari qayta raqamlanadi. */
  const move = (i: number, dir: -1 | 1) => {
    const j = i + dir
    if (j < 0 || j >= items.length) return
    const next = [...items]
    const tmp = next[i]
    next[i] = next[j]
    next[j] = tmp
    setItems(next.map((x, k) => ({ ...x, order: k + 1 })))
    setDirty(true)
    setSaved('')
  }

  const save = async () => {
    const empty = items.findIndex((x) => !x.title.trim() || !x.content.trim())
    if (empty >= 0) {
      setError(`${empty + 1}-bo'lakning sarlavhasi yoki matni bo'sh — to'ldiring yoki o'chiring.`)
      return
    }
    setSaving(true)
    setError('')
    setSaved('')
    try {
      const next = await saveIgKnowledge(items.map((x, k) => ({ ...x, order: k + 1 })))
      setItems(next)
      setDirty(false)
      setSaved(`Saqlandi — ${next.length} ta bo'lak.`)
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  /**
   * «AI qancha ma'lumot ko'radi» ko'rsatkichlari — hammasi MIJOZ tomonda sanaladi
   * (ro'yxat baribir to'liq yuklangan, qo'shimcha so'rov kerak emas).
   * Belgilar soni muhim: promptga tushadigan matn hajmi shu.
   */
  const stats = useMemo(() => ({
    total: items.length,
    active: items.filter((x) => x.isActive).length,
    off: items.filter((x) => !x.isActive).length,
    chars: items.reduce((s, x) => s + x.content.length, 0),
  }), [items])

  return (
    <MarketingPage
      title="Bilim bazasi"
      sub="AI shu ma'lumot asosida javob beradi"
      actions={canEdit && (
        <div style={{ display: 'flex', gap: 8 }}>
          {/* Fayl tanlash maydoni yashirin — tugma ko'rinishi qolgan amallar bilan bir xil bo'lsin.
              `value` har tanlovdan keyin tozalanadi: aks holda AYNI faylni qayta yuklash
              (hujjat tuzatilgandan keyin) `onChange` ni umuman ishga tushirmasdi. */}
          <input
            ref={fileRef}
            type="file"
            accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            style={{ display: 'none' }}
            onChange={(e) => {
              const f = e.target.files?.[0]
              e.target.value = ''
              if (f) void upload(f)
            }}
          />
          <button
            className="btn btn-outline"
            onClick={() => fileRef.current?.click()}
            disabled={uploading}
            title="Word hujjatidan bo'laklar QO'SHILADI (mavjudlari o'chmaydi)"
          >
            <Icon name="upload" /> {uploading ? 'Yuklanmoqda…' : 'Word yuklash'}
          </button>
          <button className="btn btn-outline" onClick={add}><Icon name="plus" /> Bo'lak qo'shish</button>
          <button className="btn btn-primary" onClick={save} disabled={saving || !dirty}>
            <Icon name="check" /> {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      )}
    >
      <div className="fade-up">
        <div className="mk-alert">
          <Icon name="warn" style={{ width: 20, height: 20, flexShrink: 0 }} />
          <div style={{ flex: 1 }}>
            <div className="mk-alert-title">AI FAQAT shu ma'lumot asosida javob beradi</div>
            <div>
              Narx, jadval, chegirma va shartlarni AI o'ylab topmaydi — bu yerda yozilmagan
              savolga u «aniq ma'lumot uchun murojaat qiling» deb javob beradi. Ma'lumot
              o'zgarsa (masalan narx) shu yerni yangilash SHART.
            </div>
          </div>
        </div>

        {error && <div style={{ marginBottom: 14 }}><MkError text={error} /></div>}
        {saved && !error && (
          <div style={{ marginBottom: 14 }}>
            <MkNotice text={saved} tone="success" onClose={() => setSaved('')} />
          </div>
        )}

        {loading && <MkLoading />}

        {!loading && items.length === 0 && (
          <MkEmpty
            text="Bilim bazasi bo'sh"
            hint="Kurslar, narxlar, manzil, ish vaqti — har mavzu uchun alohida bo'lak qo'shing yoki tayyor Word hujjatini yuklang."
          />
        )}

        {!loading && items.length > 0 && (
          <div className="mk-kpi" style={{ marginBottom: 18 }}>
            <MkStat label="Jami bo'lak" value={stats.total} icon="book" tone="primary" />
            <MkStat label="Faol" value={stats.active} icon="check" tone="success" hint="Faqat shular promptga tushadi" />
            <MkStat label="O'chiq" value={stats.off} icon="close" tone="muted" hint="Saqlanadi, lekin ishlatilmaydi" />
            <MkStat label="Matn hajmi" value={stats.chars.toLocaleString()} icon="text" tone="muted" hint="Belgilar soni" />
          </div>
        )}

        {/* ── AI QIDIRUVI (RAG) HOLATI ──
            Katta hujjat yuklangandan keyin vektorlar fon xizmatida bir necha daqiqada
            hisoblanadi. Busiz admin «AI hujjatni ko'rmayapti» deb o'ylab, hujjatni
            qayta-qayta yuklardi — jarayon ekranda ochiq ko'rinishi kerak. */}
        {!loading && status && status.active > 0 && !status.ragReady && status.geminiConfigured && (
          <div style={{ marginBottom: 14 }}>
            <MkNotice
              tone="info"
              text={
                `AI qidiruvi tayyorlanmoqda: ${status.embedded} / ${status.active} bo'lak. `
                + "Shu vaqt ichida ham AI javob beradi — faqat kerakli bo'lakni tanlash aniqligi pastroq. "
                + "Jarayon fonda o'zi tugaydi (odatda bir necha daqiqa)."
              }
            />
          </div>
        )}

        {/* ── YUKLANGAN HUJJATLAR ──
            Manba yozilmasa «narxlar hujjatining eski versiyasini olib tashlash» degan oddiy
            ish qo'lda, bo'lakma-bo'lak bajarilardi — amalda esa bajarilmasdi va bilim
            bazasida ESKI NARX qolib ketardi. */}
        {!loading && status && status.sources.length > 0 && (
          <div className="card card-pad" style={{ marginBottom: 18 }}>
            <div className="section-head">
              <div className="section-title">Yuklangan hujjatlar</div>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
              {status.sources.map((src) => (
                <div
                  key={src.fileName}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 10,
                    border: '1px solid var(--border)', borderRadius: 10, padding: '8px 12px',
                  }}
                >
                  <Icon name="book" style={{ width: 16, height: 16, opacity: 0.7 }} />
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontWeight: 600 }}>{src.fileName}</div>
                    <div className="field-hint" style={{ margin: 0 }}>
                      {src.chunks} ta bo'lak · {src.chars.toLocaleString()} belgi
                    </div>
                  </div>
                  {canEdit && (
                    <button
                      className="icon-btn"
                      title="Shu hujjatdan kelgan barcha bo'laklarni o'chirish"
                      style={{ width: 32, height: 32, color: 'var(--danger)' }}
                      onClick={() => void removeSource(src.fileName, src.chunks)}
                    >
                      <Icon name="trash" style={{ width: 15, height: 15 }} />
                    </button>
                  )}
                </div>
              ))}
            </div>
            <div className="field-hint">
              Hujjat yangilansa: avval eskisini o'chiring, so'ng yangi faylni yuklang —
              aks holda bilim bazasida ikkala versiya ham qoladi va AI eski narxni aytishi mumkin.
            </div>
          </div>
        )}

        {/* Bo'laklar ikki ustunda: matn maydonlari uzun, bitta ustunda ular keng
            ekranda cho'zilib, ro'yxat esa cheksiz pastga ketardi. Tartib row-major —
            ya'ni o'qilish ketma-ketligi `order` bilan bir xil. */}
        {!loading && items.length > 0 && (
          <div className="mk-cols2">
            {items.map((it, i) => (
              <div className="mk-kb-item" key={it.id ?? `yangi-${i}`} style={{ marginBottom: 0 }}>
                <div className="mk-kb-head">
                  <span className="rule-num">{i + 1}</span>
                  <input
                    className="input"
                    style={{ flex: 1, minWidth: 0 }}
                    value={it.title}
                    disabled={!canEdit}
                    onChange={(e) => patch(i, { title: e.target.value })}
                    placeholder="Bo'lak sarlavhasi — masalan: Kurslar va narxlar"
                  />
                  {canEdit && (
                    <>
                      {/* ⚠️ Yorliqlar "Yuqoriga/Pastga" EMAS: ikki ustunli grid'da
                          qo'shni bo'lak yonma-yon turadi, gap esa TARTIB haqida. */}
                      <button className="icon-btn" title="Tartibda oldinga" style={{ width: 34, height: 34 }} onClick={() => move(i, -1)} disabled={i === 0}>
                        <Icon name="chevUp" style={{ width: 16, height: 16 }} />
                      </button>
                      <button className="icon-btn" title="Tartibda orqaga" style={{ width: 34, height: 34 }} onClick={() => move(i, 1)} disabled={i === items.length - 1}>
                        <Icon name="chevDown" style={{ width: 16, height: 16 }} />
                      </button>
                      <div
                        className={'switch ' + (it.isActive ? 'on' : '')}
                        title={it.isActive ? 'Faol' : "O'chiq"}
                        onClick={() => patch(i, { isActive: !it.isActive })}
                      />
                      <button className="icon-btn" title="O'chirish" style={{ width: 34, height: 34, color: 'var(--danger)' }} onClick={() => remove(i)}>
                        <Icon name="trash" style={{ width: 16, height: 16 }} />
                      </button>
                    </>
                  )}
                </div>
                <textarea
                  className="textarea"
                  style={{ minHeight: 190 }}
                  value={it.content}
                  disabled={!canEdit}
                  onChange={(e) => patch(i, { content: e.target.value })}
                  placeholder="Matn: aniq faktlar, narxlar, shartlar. Qanday yozsangiz — AI shunday aytadi."
                />
                {(it.updatedAt || it.sourceFile) && (
                  <div className="field-hint">
                    {it.sourceFile && <>Manba: {it.sourceFile}{it.updatedAt ? ' · ' : ''}</>}
                    {it.updatedAt && <>Oxirgi o'zgarish: {it.updatedAt}{it.updatedBy ? ` · ${it.updatedBy}` : ''}</>}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {!loading && canEdit && items.length > 0 && (
          <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
            <button className="btn btn-outline" onClick={add}><Icon name="plus" /> Bo'lak qo'shish</button>
            <button className="btn btn-primary" onClick={save} disabled={saving || !dirty}>
              <Icon name="check" /> {saving ? 'Saqlanmoqda…' : 'Saqlash'}
            </button>
            {dirty && <span className="field-hint" style={{ alignSelf: 'center' }}>Saqlanmagan o'zgarishlar bor</span>}
          </div>
        )}
      </div>
    </MarketingPage>
  )
}
