import { useEffect, useRef, useState } from 'react'
import { Mic, PenLine, RotateCcw, Sparkles, Square } from 'lucide-react'
import type { AiCheck, AiCheckListItem, AiCheckStatus } from '@/types'
import {
  getAiCheckStatus,
  getAiCheckHistory,
  getAiCheckItem,
  submitWriting,
  submitSpeaking,
} from '@/api/services/studentAiCheck'
import { startWavRecording, type WavRecorder } from '@/lib/wavRecorder'
import { RecWaveform } from '@/pages/student/RecWaveform'
import { fmtDate } from '@/pages/student/lib'
import { AiCheckResultView } from '@/pages/student/AiCheckResultView'
import { EmptyState, IconBox, PageHeader, StatusChip } from '@/pages/student/ui'

function errMsg(e: unknown): string {
  const ax = e as { response?: { data?: { message?: string } }; message?: string }
  return ax?.response?.data?.message ?? ax?.message ?? 'Xatolik yuz berdi'
}

export function StudentAiCheckScreen() {
  const [status, setStatus] = useState<AiCheckStatus | null>(null)
  const [history, setHistory] = useState<AiCheckListItem[]>([])
  const [tab, setTab] = useState<'writing' | 'speaking'>('writing')
  const [result, setResult] = useState<AiCheck | null>(null)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  // Writing
  const [wPrompt, setWPrompt] = useState('')
  const [wText, setWText] = useState('')
  const [wTaskType, setWTaskType] = useState<'' | 'ielts_task1' | 'ielts_task2'>('')

  // Speaking
  const [sPrompt, setSPrompt] = useState('')
  const [recState, setRecState] = useState<'idle' | 'recording' | 'recorded'>('idle')
  const [blob, setBlob] = useState<Blob | null>(null)
  const recorderRef = useRef<WavRecorder | null>(null)

  const reload = () => {
    getAiCheckStatus().then(setStatus).catch(() => {})
    getAiCheckHistory().then(setHistory).catch(() => {})
  }
  useEffect(reload, [])

  const openItem = async (id: string) => {
    setErr(null)
    try {
      const rec = await getAiCheckItem(id)
      setResult(rec)
    } catch (e) {
      setErr(errMsg(e))
    }
  }

  const doWriting = async () => {
    if (wText.trim().length < 10) { setErr('Matn juda qisqa (kamida 10 belgi).'); return }
    setErr(null); setBusy(true)
    try {
      const rec = await submitWriting(wText.trim(), wPrompt.trim() || undefined, wTaskType || undefined)
      setResult(rec); setWText(''); reload()
    } catch (e) {
      setErr(errMsg(e))
    } finally {
      setBusy(false)
    }
  }

  const startRec = async () => {
    setErr(null)
    try {
      recorderRef.current = await startWavRecording()
      setRecState('recording'); setBlob(null)
    } catch {
      setErr('Mikrofonga ruxsat berilmadi.')
    }
  }
  const stopRec = async () => {
    if (!recorderRef.current) return
    const b = await recorderRef.current.stop()
    recorderRef.current = null
    setBlob(b); setRecState('recorded')
  }
  const doSpeaking = async () => {
    if (!blob) return
    setErr(null); setBusy(true)
    try {
      // Erkin nutq: o'quvchi mavzu bo'yicha ingliz tilida gapiradi. Azure nutqni matnga o'girib,
      // har so'z talaffuzini baholaydi; Gemini esa to'liq tahlil qiladi. Reference matn shart emas.
      const rec = await submitSpeaking(blob, sPrompt.trim() || undefined)
      setResult(rec); setBlob(null); setRecState('idle'); reload()
    } catch (e) {
      setErr(errMsg(e))
    } finally {
      setBusy(false)
    }
  }

  // ---- Natija ko'rinishi ----
  if (result) {
    return (
      <div className="screen">
        <PageHeader title="AI tekshiruv natijasi" onBack={() => setResult(null)} />
        <div className="scroll">
          <div className="st-page" style={{ paddingTop: 8 }}>
            <AiCheckResultView rec={result} />
          </div>
        </div>
      </div>
    )
  }

  const notReady = status && !status.geminiReady
  const azureMissing = tab === 'speaking' && status && !status.azureReady
  // Markaz bo'limni ilovada ochmagan. Holat kelmagan bo'lsa (so'rov muvaffaqiyatsiz)
  // YOPIQ deb ko'rsatmaymiz.
  const closed = !!status && !status.enabled

  return (
    <div className="screen">
      <PageHeader title="AI tekshiruv" back="/student/profile" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {/* Bo'lim yopiq — tekshiruv yuborib bo'lmaydi (eski natijalar pastda qoladi) */}
          {closed && (
            <div className="card flush" style={{ marginBottom: 6 }}>
              <EmptyState
                icon={Sparkles}
                tone="violet"
                title="Bu bo'lim hali ochilmagan"
                sub="Markaz AI tekshiruvni yoqqach, yozma ishlar va talaffuzni shu yerda tekshirib, batafsil tahlil olish mumkin bo'ladi."
              />
            </div>
          )}

          {/* Holat banner */}
          {!closed && status && (
            <div className="card" style={{ marginBottom: 12 }}>
              {status.blocked ? (
                <div style={{ color: 'var(--red)', fontWeight: 700, fontSize: 14.5 }}>
                  AI tekshiruv bu akkaunt uchun cheklangan. Markaz administratoriga murojaat qiling.
                </div>
              ) : status.premium ? (
                <div className="row sp">
                  <span style={{ fontWeight: 700 }}>Premium</span>
                  <StatusChip tone="violet">cheksiz</StatusChip>
                </div>
              ) : (
                <div className="row sp">
                  <span style={{ fontSize: 14.5, fontWeight: 600 }}>Bugungi limit</span>
                  <span className="st-num" style={{ fontSize: 16 }}>
                    {status.usedToday} / {status.limit}
                  </span>
                </div>
              )}
            </div>
          )}

          {!closed && notReady && (
            <div className="st-banner info" style={{ marginBottom: 12 }}>
              AI tekshiruv hali sozlanmagan (admin Gemini kalitini kiritishi kerak).
            </div>
          )}

          {/* Tekshiruv yuborish qismi — faqat bo'lim OCHIQ bo'lganda. */}
          {!closed && (
            <>
            {/* Tab */}
            <div className="seg" style={{ marginBottom: 12 }}>
              <button className={tab === 'writing' ? 'on' : ''} onClick={() => { setTab('writing'); setErr(null) }}>
                <PenLine size={17} /> Writing
              </button>
              <button className={tab === 'speaking' ? 'on' : ''} onClick={() => { setTab('speaking'); setErr(null) }}>
                <Mic size={17} /> Speaking
              </button>
            </div>

            {err && (
              <div role="alert" className="st-banner" style={{ marginBottom: 12 }}>{err}</div>
            )}

            {/* Writing */}
            {tab === 'writing' && (
              <div className="card" style={{ marginBottom: 16 }}>
                {/* Baholash turi: umumiy yoki IELTS Task 1/2 */}
                <div className="seg" style={{ marginBottom: 10 }}>
                  <button className={wTaskType === '' ? 'on' : ''} onClick={() => setWTaskType('')}>Umumiy</button>
                  <button className={wTaskType === 'ielts_task1' ? 'on' : ''} onClick={() => setWTaskType('ielts_task1')}>IELTS Task 1</button>
                  <button className={wTaskType === 'ielts_task2' ? 'on' : ''} onClick={() => setWTaskType('ielts_task2')}>IELTS Task 2</button>
                </div>
                {wTaskType !== '' && (
                  <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
                    {wTaskType === 'ielts_task1'
                      ? 'IELTS Academic Task 1 — grafik/jadval/diagramma tavsifi (≥150 so’z). Band 0-9 baholanadi.'
                      : 'IELTS Task 2 — esse (≥250 so’z). Band 0-9 baholanadi.'}
                  </div>
                )}
                <div className="field" style={{ marginBottom: 10 }}>
                  <input
                    placeholder={wTaskType !== '' ? 'Savol / topshiriq matni (ixtiyoriy)' : 'Mavzu (ixtiyoriy)'}
                    value={wPrompt}
                    onChange={(e) => setWPrompt(e.target.value)}
                  />
                </div>
                <textarea
                  className="ta"
                  placeholder="Matningizni ingliz tilida yozing..."
                  value={wText}
                  onChange={(e) => setWText(e.target.value)}
                  rows={8}
                  style={{ marginBottom: 10 }}
                />
                <button
                  className="btn btn-primary"
                  disabled={busy || !!notReady || (status?.blocked ?? false)}
                  onClick={doWriting}
                >
                  {busy ? 'Tekshirilmoqda...' : 'AI tekshirish'}
                </button>
              </div>
            )}

            {/* Speaking */}
            {tab === 'speaking' && (
              <div className="card" style={{ marginBottom: 16 }}>
                {azureMissing && (
                  <div className="st-banner info" style={{ marginBottom: 10 }}>
                    Speaking baholash hali sozlanmagan (admin Azure kalitini kiritishi kerak).
                  </div>
                )}
                <div className="field" style={{ marginBottom: 6 }}>
                  <input placeholder="Mavzu (ixtiyoriy) — nima haqida gapirasiz" value={sPrompt} onChange={(e) => setSPrompt(e.target.value)} />
                </div>
                <div className="muted" style={{ fontSize: 12, marginBottom: 12 }}>
                  Ingliz tilida erkin gapiring. Azure nutqni matnga o'giradi va har so'z talaffuzini baholaydi
                  (yashil/qizil), AI esa to'liq tahlil qiladi. Aniqroq bo'lishi uchun balandroq va tiniq gapiring.
                </div>
                <div className="center" style={{ flexDirection: 'column', gap: 10, padding: '8px 0' }}>
                  {recState === 'idle' && (
                    <button className="btn btn-primary" disabled={!!azureMissing || (status?.blocked ?? false)} onClick={startRec}>
                      <Mic size={18} /> Yozishni boshlash
                    </button>
                  )}
                  {recState === 'recording' && (
                    <>
                      <RecWaveform recorder={recorderRef} active={recState === 'recording'} />
                      <button className="btn btn-danger" onClick={stopRec}>
                        <Square size={16} /> To'xtatish (yozilmoqda...)
                      </button>
                    </>
                  )}
                  {recState === 'recorded' && blob && (
                    <>
                      <audio controls src={URL.createObjectURL(blob)} style={{ width: '100%' }} />
                      <div className="row gap8" style={{ width: '100%' }}>
                        <button className="btn btn-ghost" style={{ flex: 1 }} disabled={busy} onClick={() => { setBlob(null); setRecState('idle') }}>
                          <RotateCcw size={16} /> Qayta yozish
                        </button>
                        <button className="btn btn-primary" style={{ flex: 1 }} disabled={busy} onClick={doSpeaking}>
                          {busy ? 'Tekshirilmoqda...' : 'AI tekshirish'}
                        </button>
                      </div>
                    </>
                  )}
                </div>
              </div>
            )}
            </>
          )}


          {/* Tarix */}
          <div className="st-sh"><div className="st-sh-title">Tarix</div></div>
          {history.length === 0 ? (
            <div className="card flush">
              <EmptyState compact icon={Sparkles} title="Hali tekshiruv yo'q" sub="Tekshirilgan ishlar shu yerda saqlanadi." />
            </div>
          ) : (
            <div className="card flush">
              {history.map((h) => (
                <button key={h.id} type="button" className="st-li press" onClick={() => openItem(h.id)}>
                  <IconBox icon={h.type === 'speaking' ? Mic : PenLine} tone="violet" />
                  <div className="st-li-main">
                    <div className="st-li-title">{h.type === 'speaking' ? 'Speaking' : 'Writing'}</div>
                    <div className="st-li-sub">{h.prompt || fmtDate(h.createdAt, true)}</div>
                  </div>
                  <span className="st-num" style={{ fontSize: 18 }}>{Math.round(h.score)}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
