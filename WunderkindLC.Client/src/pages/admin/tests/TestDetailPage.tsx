import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Loader2, Award, Download } from 'lucide-react'
import type { TestResultDetail } from '@/types'
import { getTestDetail, saveTestScoresBatch, sendTestResults } from '@/api/services/testResults'
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
import { testKindLabel } from '@/lib/ratingPeriods'
import { TestScoreGrid, type TestScoreGridHandle } from './TestScoreGrid'
import { SendResultsPanel } from './SendResultsPanel'

/** Sertifikat fon ishi holatini shuncha vaqtda bir so'raymiz (bo'lak ~7 soniyada tayyor bo'ladi). */
const CERT_POLL_MS = 2500
/** Ketma-ket shuncha xatodan keyin so'rashni to'xtatamiz (tarmoq uzilsa abadiy urinmasin). */
const CERT_POLL_MAX_FAILS = 5

/**
 * Test tafsiloti — Excel'dek natijalar jadvali (`TestScoreGrid`, CRM U4a): har o'quvchiga YECHGAN savollar soni,
 * foiz jonli, o'rin — oxirgi saqlashdan; «Ota-onalarga yuborish» (`SendResultsPanel`) va sertifikatlar.
 */
export function TestDetailPage() {
  const { groupId = '', testId = '' } = useParams()
  const navigate = useNavigate()
  const { can } = usePerm()
  const editable = can('classes.testResults', 'edit')
  const canSend = can('classes.testResults', 'create')
  const grid = useRef<TestScoreGridHandle>(null)

  const [detail, setDetail] = useState<TestResultDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
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

  /**
   * Hali saqlanmagan natijalarni saqlab BO'LGUNCHA kutamiz (jadval avtosaqlashi 800 ms kechikadi).
   * Aks holda sertifikat/yuborish server natija yozilishidan OLDIN ro'yxatni o'qib, oxirgi kiritilgan
   * o'quvchi sertifikatsiz yoki ESKI natija bilan qolardi.
   */
  const flushDrafts = async () => {
    await grid.current?.flush()
  }

  /** «Yuborilgan» vaqtini yangilash — tafsilotni qayta o'qiymiz (jadval qiymatlari tegilmaydi). */
  const refreshSent = () => {
    getTestDetail(testId)
      .then((d) => setDetail((prev) => (prev && prev.id === d.id ? { ...prev, resultsSentAt: d.resultsSentAt } : prev)))
      .catch(() => { /* ikkilamchi: vaqt keyingi ochilishda ko'rinadi */ })
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
        sub={[
          formatDate(detail.date),
          testKindLabel(detail.kind ?? ''),
          (detail.questionCount ?? 0) > 0 ? `${detail.questionCount} ta savol` : `Maksimal ball: ${detail.maxScore}`,
          `Kiritilgan: ${scored}/${detail.rows.length}`,
        ].filter(Boolean).join(' · ')}
      />

      {error && <Card className="mb-3 py-2.5 text-center text-sm text-red-500">{error}</Card>}

      {detail.rows.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          Guruhda faol o'quvchi yo'q.
        </Card>
      ) : (
        <TestScoreGrid
          key={detail.id}
          ref={grid}
          detail={detail}
          editable={editable}
          save={(rows) => saveTestScoresBatch(testId, rows)}
          onDetail={setDetail}
        />
      )}

      {canSend && detail.rows.length > 0 && (
        <SendResultsPanel
          className="mt-3"
          resultsSentAt={detail.resultsSentAt}
          beforeSend={flushDrafts}
          send={(resend) => sendTestResults(testId, resend)}
          onSent={refreshSent}
        />
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
    </div>
  )
}
