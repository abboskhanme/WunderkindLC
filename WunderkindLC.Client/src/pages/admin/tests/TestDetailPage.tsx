import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Trophy, Check, Loader2, Bot, Clock, Send, FileText, Eye, EyeOff,
  Users, Globe, KeyRound, Copy, Award, Download,
} from 'lucide-react'
import type { TestResultDetail } from '@/types'
import { getTestDetail, setTestScore } from '@/api/services/testResults'
import {
  startTestCertificates,
  getCertificateJob,
  downloadCertificate,
  downloadAllCertificates,
} from '@/api/services/testCertificates'
import type { CertificateJob } from '@/api/services/testCertificates'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, formatDate } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

const MEDALS = ['🥇', '🥈', '🥉']

/** Sertifikat fon ishi holatini shuncha vaqtda bir so'raymiz (bo'lak ~7 soniyada tayyor bo'ladi). */
const CERT_POLL_MS = 2500
/** Ketma-ket shuncha xatodan keyin so'rashni to'xtatamiz (tarmoq uzilsa abadiy urinmasin). */
const CERT_POLL_MAX_FAILS = 5

/** Ball foizi (butun songa yaxlitlangan). */
const percentOf = (score: number, max: number) => (max > 0 ? Math.round((score / max) * 100) : 0)

/**
 * Test tafsiloti — guruhning faol o'quvchilari ballari (ball bo'yicha kamayish tartibida).
 * Har o'quvchiga ball kiritiladi; kiritilganda ro'yxat qayta saralanadi (tepadan pastga).
 */
export function TestDetailPage() {
  const { groupId = '', testId = '' } = useParams()
  const navigate = useNavigate()
  const { can } = usePerm()
  const editable = can('classes.testResults', 'edit')

  const [detail, setDetail] = useState<TestResultDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  // Har o'quvchi input qiymati (matn) — saqlanmaguncha lokal
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [savingId, setSavingId] = useState<string | null>(null)
  const [savedId, setSavedId] = useState<string | null>(null)
  // Onlayn test: javob kalitini ko'rsatish (yopiq holatda — tasodifan ko'rinib qolmasin)
  const [showKey, setShowKey] = useState(false)
  // --- sertifikat ---
  // Generatsiya FONDA, 5 tadan bo'lib bajariladi: `certJob` — holat (nechtadan nechtasi tayyor)
  // va shu daqiqada tayyor sertifikatlar. Ish ketayotganda ham ro'yxat to'lib boradi.
  const [certJob, setCertJob] = useState<CertificateJob | null>(null)
  const [certBusy, setCertBusy] = useState(false)
  const [certError, setCertError] = useState('')
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const jobRunning = certJob?.running === true
  /** Ketma-ket muvaffaqiyatsiz holat so'rovlari soni (render'ga ta'sir qilmaydi — shuning uchun ref). */
  const pollFails = useRef(0)

  const syncDraft = (d: TestResultDetail) =>
    setDraft(Object.fromEntries(d.rows.map((r) => [r.studentId, r.score == null ? '' : String(r.score)])))

  // Manzildagi testId almashsa komponent QAYTA YARATILMAYDI (marshrut bir xil), shuning uchun
  // sertifikat holatini o'zimiz tozalaymiz — aks holda yangi testda ESKI testning sertifikatlari
  // va "N ta yaratildi" xabari turib qolardi. Tozalash RENDER paytida bajariladi (React'ning
  // "prop o'zgarganda holatni moslash" usuli) — effekt ichida qilinsa ortiqcha render bo'lardi.
  const [shownTestId, setShownTestId] = useState(testId)
  if (shownTestId !== testId) {
    setShownTestId(testId)
    setLoading(true)
    setDetail(null)
    setCertJob(null)
    setCertError('')
    setError('')
  }

  useEffect(() => {
    let active = true      // testId almashsa eski so'rovning javobi yozilmasin
    getTestDetail(testId)
      .then((d) => {
        if (!active) return
        setDetail(d)
        syncDraft(d)
        // Sahifa boshqa oynada boshlangan generatsiya PAYTIDA ochilgan bo'lishi mumkin —
        // shuni ilib olamiz, aks holda progress ko'rinmay qolardi.
        if (d.certificateEnabled) {
          getCertificateJob(testId)
            .then((j) => { if (active && j.running) setCertJob(j) })
            .catch(() => { /* holat olinmasa ham sahifa ishlayveradi */ })
        }
      })
      .catch((e) => { if (active) setError(apiErrorMessage(e, 'Yuklab bo\'lmadi')) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [testId])

  /** Ish ketayotganda holatni so'rab turamiz — tayyor bo'lganlar ro'yxatga qo'shilib boradi. */
  useEffect(() => {
    if (!jobRunning) return
    pollFails.current = 0
    const timer = setInterval(() => {
      getCertificateJob(testId)
        .then((j) => {
          pollFails.current = 0
          setCertJob(j)
        })
        .catch(() => {
          // Ketma-ket bir necha xatodan keyin TO'XTAYMIZ. Aks holda tarmoq uzilsa yoki test
          // o'chirilsa (404/403) UI abadiy "Yaratilmoqda..." holatida so'rov yuboraverardi.
          pollFails.current += 1
          if (pollFails.current >= CERT_POLL_MAX_FAILS) {
            setCertJob((j) => (j ? { ...j, running: false } : j))
            setCertError("Holat aniqlanmadi — sertifikatlar fonda yaratilayotgan bo'lishi mumkin. Sahifani yangilang.")
          }
        })
    }, CERT_POLL_MS)
    return () => clearInterval(timer)
  }, [jobRunning, testId])

  const save = async (studentId: string) => {
    if (!detail) return
    const raw = (draft[studentId] ?? '').trim()
    const current = detail.rows.find((r) => r.studentId === studentId)?.score ?? null
    const next = raw === '' ? null : Number(raw)
    if (next !== null && (!Number.isFinite(next) || next < 0)) {
      setError('Ball manfiy bo\'lmasligi kerak')
      return
    }
    if (next === current) return // o'zgarmagan
    setSavingId(studentId)
    setError('')
    try {
      const updated = await setTestScore(testId, studentId, next)
      setDetail(updated)
      syncDraft(updated)
      setSavedId(studentId)
      setTimeout(() => setSavedId((s) => (s === studentId ? null : s)), 1200)
    } catch (e) {
      setError(apiErrorMessage(e, 'Saqlab bo\'lmadi'))
    } finally {
      setSavingId(null)
    }
  }

  /**
   * Hali saqlanmagan ballarni saqlab BO'LGUNCHA kutamiz.
   *
   * Tugma "Saqlash va sertifikat yaratish" deyiladi, lekin ball aslida katakdan chiqilganda
   * (`onBlur`) ALOHIDA so'rov bilan saqlanadi. Tugma bosilganda `mousedown` blur'ni, `click` esa
   * generatsiyani qo'zg'aydi — ikkalasi bir vaqtda ketib, server ball yozilishidan OLDIN ro'yxatni
   * o'qishi mumkin edi: oxirgi kiritilgan o'quvchi sertifikatsiz yoki ESKI ball bilan qolardi.
   */
  const flushDrafts = async () => {
    if (!detail) return
    const pending = detail.rows.filter((r) => {
      const raw = (draft[r.studentId] ?? '').trim()
      const next = raw === '' ? null : Number(raw)
      if (next !== null && (!Number.isFinite(next) || next < 0)) return false
      return next !== (r.score ?? null)
    })
    if (pending.length === 0) return
    for (const r of pending) {
      const raw = (draft[r.studentId] ?? '').trim()
      await setTestScore(testId, r.studentId, raw === '' ? null : Number(raw))
    }
    const fresh = await getTestDetail(testId)
    setDetail(fresh)
    syncDraft(fresh)
  }

  /** «Saqlash va sertifikat yaratish» — ball kiritilgan har o'quvchiga sertifikat (qayta bosilsa yangilanadi).
   *  So'rov ishni FONDA boshlaydi va darhol qaytadi; qolganini holat so'rovi kuzatadi. */
  const generate = async () => {
    setCertBusy(true)
    setCertError('')
    try {
      await flushDrafts()   // avval ballar, keyin sertifikat — tartib muhim
      setCertJob(await startTestCertificates(testId))
    } catch (e) {
      setCertError(apiErrorMessage(e, "Sertifikat yaratib bo'lmadi"))
    } finally {
      setCertBusy(false)
    }
  }

  const downloadOne = async (certificateId: string) => {
    setDownloadingId(certificateId)
    setCertError('')
    try {
      await downloadCertificate(certificateId, false)
    } catch (e) {
      setCertError(apiErrorMessage(e, "Yuklab bo'lmadi"))
    } finally {
      setDownloadingId(null)
    }
  }

  const downloadZip = async () => {
    setDownloadingId('all')
    setCertError('')
    try {
      await downloadAllCertificates(testId, false)
    } catch (e) {
      setCertError(apiErrorMessage(e, "Yuklab bo'lmadi"))
    } finally {
      setDownloadingId(null)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!detail)
    return <Card className="py-12 text-center text-slate-400">{error || 'Test topilmadi'}</Card>

  const scored = detail.rows.filter((r) => r.score != null).length
  const isOnline = detail.online?.mode === 'online'
  const submitted = detail.rows.filter((r) => r.source === 'bot').length
  // MARKAZDAN TASHQARI ishtirokchilar (test kodi bilan kirganlar) — alohida ro'yxat.
  const external = detail.externalRows ?? []
  // Ish boshlangan bo'lsa ro'yxat FON ISHIDAN keladi (u ish davomida to'lib boradi),
  // aks holda test tafsiloti bilan kelgan ro'yxatdan.
  const certificates = certJob ? certJob.items : detail.certificates ?? []
  const certEnabled = detail.certificateEnabled === true
  // Ish tugadi va biror sertifikat chiqdi — yashil xabar.
  const certDone = certJob && !certJob.running && certJob.done > 0

  return (
    <div>
      <button
        type="button"
        onClick={() => navigate(`/admin/test-results/${groupId}`)}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> {detail.groupName || 'Guruh'} testlari
      </button>

      <PageHeader
        title={detail.name}
        sub={`${formatDate(detail.date)} · Maksimal ball: ${detail.maxScore} · Baholangan: ${scored}/${detail.rows.length}`}
      />

      {isOnline && (
        <Card className="mb-3">
          <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
            <span className="inline-flex items-center gap-1.5 rounded-md bg-violet-50 px-2 py-1 text-xs font-semibold text-violet-600">
              <Bot className="h-3.5 w-3.5" /> ONLAYN TEST
            </span>
            <span className="text-slate-600">
              <span className="text-slate-400">Savollar:</span>{' '}
              <b>{detail.online.questionCount}</b> ta (A–
              {String.fromCharCode(64 + detail.online.optionCount)})
            </span>
            <span className="inline-flex items-center gap-1.5 text-slate-600">
              <Clock className="h-4 w-4 text-slate-400" />
              {detail.online.startAt.slice(11, 16)} – {detail.online.endAt.slice(11, 16)}
            </span>
            <span className="inline-flex items-center gap-1.5 text-slate-600">
              <Send className="h-4 w-4 text-slate-400" />
              Botdan yuborgan: <b>{submitted}</b>
            </span>
            {detail.online.pdfUrl && (
              <a
                href={detail.online.pdfUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 font-medium text-brand-600 hover:underline"
              >
                <FileText className="h-4 w-4" /> Savollar (PDF)
              </a>
            )}
            <button
              type="button"
              onClick={() => setShowKey((v) => !v)}
              className="inline-flex items-center gap-1.5 font-medium text-slate-500 hover:text-slate-700"
            >
              {showKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              Javob kaliti
            </button>
            {!!detail.online.code && (
              <button
                type="button"
                onClick={() => void navigator.clipboard?.writeText(detail.online.code)}
                className="inline-flex items-center gap-1.5 rounded-md bg-slate-100 px-2 py-1 font-mono text-xs font-semibold tracking-wider text-slate-700 hover:bg-slate-200"
                title="Test kodini nusxalash — markazda o'qimaydigan odam shu kod bilan ishlaydi"
              >
                <KeyRound className="h-3.5 w-3.5" /> {detail.online.code}
                <Copy className="h-3 w-3 text-slate-400" />
              </button>
            )}
            {!detail.online.groupOpen && (
              <span
                className="rounded-md bg-amber-50 px-2 py-1 text-xs font-semibold text-amber-700"
                title="Guruhga e'lon qilinmagan — faqat test kodi bilan ishlanadi"
              >
                FAQAT KOD
              </span>
            )}
          </div>
          {showKey && (
            <pre className="mt-3 overflow-x-auto rounded-lg bg-slate-50 p-3 text-xs leading-relaxed text-slate-700">
              {detail.online.answerKey
                .split('')
                .map((c, i) => `${i + 1}.${c}`)
                .join('   ')}
            </pre>
          )}
        </Card>
      )}

      {error && <Card className="mb-3 py-2.5 text-center text-sm text-red-500">{error}</Card>}

      {/* MARKAZDAGILAR — guruh a'zolari + test kodi bilan qo'shilgan markaz o'quvchilari */}
      {external.length > 0 && (
        <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold text-slate-700">
          <Users className="h-4 w-4 text-slate-400" /> Markazdagilar
          <span className="text-xs font-normal text-slate-400">({detail.rows.length})</span>
        </h2>
      )}

      {detail.rows.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          Guruhda faol o'quvchi yo'q.
        </Card>
      ) : (
        <Card tight className="overflow-hidden">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                <th className="w-16 px-4 py-3 font-medium">O'rin</th>
                <th className="px-4 py-3 font-medium">O'quvchi</th>
                {isOnline && <th className="px-4 py-3 font-medium">Javoblari</th>}
                <th className="w-40 px-4 py-3 text-right font-medium">Ball</th>
              </tr>
            </thead>
            <tbody>
              {detail.rows.map((r) => {
                const isTop = r.rank >= 1 && r.rank <= 3
                return (
                  <tr key={r.studentId} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                    <td className="px-4 py-2.5">
                      {r.rank === 0 ? (
                        <span className="text-slate-300">—</span>
                      ) : isTop ? (
                        <span className="text-lg leading-none">{MEDALS[r.rank - 1]}</span>
                      ) : (
                        <span className="font-semibold text-slate-500">{r.rank}</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5">
                      <span className={isTop ? 'font-semibold text-slate-800' : 'font-medium text-slate-700'}>
                        {r.fullName}
                      </span>
                      {r.rank === 1 && (
                        <Trophy className="ml-1.5 inline h-3.5 w-3.5 text-amber-400" />
                      )}
                      {r.member === false && (
                        <span
                          className="ml-1.5 rounded bg-sky-50 px-1.5 py-0.5 text-[10px] font-semibold text-sky-700"
                          title="Boshqa guruh o'quvchisi — test kodi bilan qo'shilgan"
                        >
                          BOSHQA GURUH
                        </span>
                      )}
                    </td>
                    {isOnline && (
                      <td className="px-4 py-2.5">
                        {r.source === 'bot' ? (
                          <div className="flex items-center gap-2">
                            <span
                              className="max-w-[220px] truncate font-mono text-xs text-slate-500"
                              title={r.answers}
                            >
                              {r.answers}
                            </span>
                            <span className="shrink-0 text-[11px] text-slate-400">
                              {r.submittedAt.slice(11, 16)}
                            </span>
                          </div>
                        ) : (
                          <span className="text-xs text-slate-300">— topshirmagan</span>
                        )}
                      </td>
                    )}
                    <td className="px-4 py-2.5">
                      <div className="flex items-center justify-end gap-2">
                        {savingId === r.studentId && (
                          <Loader2 className="h-4 w-4 animate-spin text-brand-400" />
                        )}
                        {savedId === r.studentId && <Check className="h-4 w-4 text-emerald-500" />}
                        <div className="flex items-center gap-1">
                          <input
                            type="number"
                            min={0}
                            max={detail.maxScore}
                            disabled={!editable || savingId === r.studentId}
                            value={draft[r.studentId] ?? ''}
                            onChange={(e) =>
                              setDraft((d) => ({ ...d, [r.studentId]: e.target.value }))
                            }
                            onBlur={() => editable && save(r.studentId)}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
                            }}
                            placeholder="—"
                            className="w-20 rounded-lg border border-slate-200 px-2.5 py-1.5 text-right text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 disabled:bg-slate-50 disabled:text-slate-400"
                          />
                          {/* Jami ball — "85 / 100 · 85%" */}
                          <span className="whitespace-nowrap text-xs text-slate-400">
                            {r.score == null
                              ? `/ ${detail.maxScore}`
                              : `${r.score} / ${detail.maxScore} · ${percentOf(r.score, detail.maxScore)}%`}
                          </span>
                        </div>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </Card>
      )}

      {editable && (
        <p className="mt-3 text-center text-xs text-slate-400">
          Ballni kiritib bosing yoki katakdan chiqing — natija avtomatik saqlanadi va ro'yxat qayta saralanadi.
        </p>
      )}

      {/* SERTIFIKAT natijalari — xabarlar va yaratish tugmasi (pastda yopishib turadi) */}
      {certEnabled && (
        <>
          {/* Ish ketmoqda — progress chizig'i. Sertifikatlar 5 tadan tayyor bo'lib, pastdagi
              ro'yxatga qo'shilib boradi: kutmasdan yuklab olsa ham bo'ladi. */}
          {jobRunning && (
            <div className="mt-3 rounded-lg bg-brand-50 px-3 py-2.5">
              <p className="flex items-center justify-center gap-2 text-sm font-medium text-brand-700">
                <Loader2 className="h-4 w-4 animate-spin" />
                Sertifikatlar yaratilmoqda... {certJob?.done ?? 0} / {certJob?.total ?? 0}
              </p>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-brand-100">
                <div
                  className="h-full rounded-full bg-brand-500 transition-all duration-500"
                  style={{
                    width: `${certJob && certJob.total > 0 ? Math.round((certJob.done / certJob.total) * 100) : 0}%`,
                  }}
                />
              </div>
              <p className="mt-1.5 text-center text-xs text-brand-600/80">
                Tayyor bo'lganlarini pastdan hoziroq yuklab olsangiz bo'ladi.
              </p>
            </div>
          )}
          {certDone && (
            <p className="mt-3 rounded-lg bg-emerald-50 px-3 py-2 text-center text-sm font-medium text-emerald-700">
              {certJob?.done} ta sertifikat yaratildi
            </p>
          )}
          {certJob?.warning && (
            <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-center text-sm text-amber-700">
              {certJob.warning}
            </p>
          )}
          {(certError || certJob?.error) && (
            <p className="mt-2 rounded-lg bg-red-50 px-3 py-2 text-center text-sm text-red-600">
              {certError || certJob?.error}
            </p>
          )}
          {editable && (
            <div className="sticky bottom-0 z-10 mt-3 flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white/95 px-4 py-3 shadow-lg backdrop-blur">
              <p className="text-xs text-slate-500">
                Sertifikat FAQAT ball kiritilgan o'quvchilarga yaratiladi. Qayta bosilsa mavjudlari
                yangilanadi — nusxa chiqmaydi.
              </p>
              <Button onClick={generate} disabled={certBusy || jobRunning} className="shrink-0">
                {certBusy || jobRunning ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Award className="h-4 w-4" />
                )}
                {jobRunning
                  ? `Yaratilmoqda... ${certJob?.done ?? 0}/${certJob?.total ?? 0}`
                  : 'Saqlash va sertifikat yaratish'}
              </Button>
            </div>
          )}
        </>
      )}

      {/* SERTIFIKATLAR — yaratilganlari (yuklab olish: PDF yoki Word) */}
      {certificates.length > 0 && (
        <div className="mt-6">
          <div className="mb-2 flex items-center justify-between gap-3">
            <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
              <Award className="h-4 w-4 text-emerald-600" /> Sertifikatlar
              <span className="text-xs font-normal text-slate-400">({certificates.length})</span>
            </h2>
            {/* ZIP faqat HAMMASI tayyor bo'lgach — yarim to'plamni "hammasi" deb yuklab bermaymiz. */}
            <Button
              variant="secondary"
              onClick={downloadZip}
              disabled={downloadingId === 'all' || jobRunning}
              title={jobRunning ? 'Hamma sertifikat tayyor bo\'lgach faollashadi' : undefined}
            >
              {downloadingId === 'all' ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Download className="h-4 w-4" />
              )}
              Hammasini yuklab olish (ZIP)
            </Button>
          </div>
          <Card tight className="overflow-hidden">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">O'quvchi</th>
                  <th className="px-4 py-3 font-medium">Raqami</th>
                  <th className="px-4 py-3 font-medium">Ball</th>
                  <th className="px-4 py-3 font-medium">Berilgan sana</th>
                  <th className="w-44 px-4 py-3 text-right font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {certificates.map((c) => (
                  <tr key={c.id} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                    <td className="px-4 py-2.5 font-medium text-slate-700">{c.studentName}</td>
                    <td className="px-4 py-2.5">
                      <span className="font-mono text-xs text-slate-500">{c.number}</span>
                      {c.status === 'docx' && (
                        <span
                          className="ml-1.5 rounded bg-amber-50 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700"
                          title="Serverda PDF yaratilmadi — faqat Word fayl mavjud"
                        >
                          faqat Word
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-slate-600">
                      {c.score} / {c.maxScore} · {c.percent}%
                    </td>
                    <td className="px-4 py-2.5 text-slate-500">{formatDate(c.issuedAt)}</td>
                    <td className="px-4 py-2.5 text-right">
                      <button
                        type="button"
                        onClick={() => downloadOne(c.id)}
                        disabled={downloadingId === c.id}
                        className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:text-brand-600 disabled:opacity-50"
                      >
                        {downloadingId === c.id ? (
                          <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                          <Download className="h-3.5 w-3.5" />
                        )}
                        Yuklab olish
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </div>
      )}

      {/* MARKAZDAN TASHQARI — test kodi bilan kirgan, markazda o'qimaydigan ishtirokchilar.
          Ular Student emas: ball qo'lda tahrirlanmaydi (faqat botdan kelgan natija). */}
      {external.length > 0 && (
        <div className="mt-6">
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold text-slate-700">
            <Globe className="h-4 w-4 text-teal-500" /> Markazdan tashqari
            <span className="text-xs font-normal text-slate-400">({external.length})</span>
          </h2>
          <Card tight className="overflow-hidden">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                  <th className="w-16 px-4 py-3 font-medium">O'rin</th>
                  <th className="px-4 py-3 font-medium">Ishtirokchi</th>
                  <th className="px-4 py-3 font-medium">Javoblari</th>
                  <th className="w-32 px-4 py-3 text-right font-medium">Ball</th>
                </tr>
              </thead>
              <tbody>
                {external.map((r) => {
                  const isTop = r.rank >= 1 && r.rank <= 3
                  return (
                    <tr key={r.id} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                      <td className="px-4 py-2.5">
                        {isTop ? (
                          <span className="text-lg leading-none">{MEDALS[r.rank - 1]}</span>
                        ) : (
                          <span className="font-semibold text-slate-500">{r.rank}</span>
                        )}
                      </td>
                      <td className="px-4 py-2.5">
                        <span className={isTop ? 'font-semibold text-slate-800' : 'font-medium text-slate-700'}>
                          {r.fullName}
                        </span>
                        {!!r.phone && (
                          <span className="ml-2 text-xs text-slate-400">{r.phone}</span>
                        )}
                      </td>
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-2">
                          <span className="max-w-[220px] truncate font-mono text-xs text-slate-500" title={r.answers}>
                            {r.answers}
                          </span>
                          <span className="shrink-0 text-[11px] text-slate-400">
                            {r.submittedAt.slice(11, 16)}
                          </span>
                        </div>
                      </td>
                      <td className="px-4 py-2.5 text-right">
                        <span className="font-semibold text-slate-800">{r.score}</span>
                        <span className="ml-1 text-xs text-slate-400">/ {detail.maxScore}</span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </Card>
          <p className="mt-2 text-center text-xs text-slate-400">
            Bular markazda o'qimaydigan ishtirokchilar — botda test kodi va F.I.Sh bilan kirishgan.
            Ballari faqat botdan keladi, qo'lda tahrirlanmaydi.
          </p>
        </div>
      )}
    </div>
  )
}
