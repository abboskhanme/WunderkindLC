import { useEffect, useRef, useState } from 'react'
import {
  ArrowLeft,
  ClipboardList,
  Loader2,
  Pencil,
  Plus,
  Trash2,
  Award,
  Download,
  Send,
} from 'lucide-react'
import type { GroupTest, TestResultDetail } from '@/types'
import {
  getTeacherGroupTests,
  getTeacherTestDetail,
  createTeacherTest,
  updateTeacherTest,
  deleteTeacherTest,
  saveTeacherTestScoresBatch,
  sendTeacherTestResults,
} from '@/api/services/teacher'
import {
  getTeacherCertificateTemplates,
  startTeacherTestCertificates,
  getCertificateJob,
  downloadCertificate,
  downloadAllCertificates,
} from '@/api/services/testCertificates'
import type { CertificateJob, TestCertificateTemplate } from '@/api/services/testCertificates'
import { cn, formatDate, apiErrorMessage } from '@/lib/utils'
import { localIsoDate } from '@/lib/localDate'
import { NumberInput } from '@/components/ui/NumberInput'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { currentMonth } from '@/lib/month'
import { fmtScore, testKindLabel } from '@/lib/ratingPeriods'
import { formatSentAt, MAX_QUESTIONS, MIN_QUESTIONS, TEST_KINDS, type TestKind } from '@/lib/testScoreGrid'
import { TestScoreGrid, type TestScoreGridHandle } from '@/pages/admin/tests/TestScoreGrid'
import { SendResultsPanel } from '@/pages/admin/tests/SendResultsPanel'
import { TestMonthFilter } from '@/pages/admin/tests/TestMonthFilter'

/**
 * Bitta guruh testlari — o'qituvchi ilovasidagi YAGONA komponent. Ikki joyda AYNAN bir xil ishlaydi:
 *  • pastki navigatsiyadagi "Test" bo'limi (`TeacherTestsPage` — guruh tanlangandan keyin);
 *  • guruh (jurnal) sahifasidagi "Imtihonlar" tabi (`TeacherGroupDetailPage`).
 * Test faqat NATIJA (CRM U4a): nom, sana, turi (oylik/oraliq), «Savollar soni» — har o'quvchiga YECHGANI
 * (Excel'dek jadval — admin bilan BIR XIL `TestScoreGrid`), foiz o'zi hisoblanadi; «Ota-onalarga yuborish».
 * Admin tarafdagi ekvivalenti — `admin/tests/GroupTestsPanel`; ikkalasi ham serverda BIR XIL
 * `TestResultService`ga boradi.
 */

const todayIso = () => localIsoDate(new Date())

/** Sertifikat fon ishi holatini shuncha vaqtda bir so'raymiz (bo'lak ~7 soniyada tayyor bo'ladi). */
const CERT_POLL_MS = 2500
/** Ketma-ket shuncha xatodan keyin so'rashni to'xtatamiz (tarmoq uzilsa abadiy urinmasin). */
const CERT_POLL_MAX_FAILS = 5

const field =
  'h-10 w-full rounded-lg border border-line bg-white px-3 text-[14px] text-ink focus:border-teal-500 focus:outline-none'
const label = 'mb-1 block text-[12px] font-semibold text-mute'


/**
 * O'qituvchi — test yaratish/tahrirlash. Admin ("O'quv bo'limi → Testlar natijalari") bilan BIR XIL:
 * «Test nomi», «Sana», «Turi» (standart yo'q), «Savollar soni» (eski testda «Maksimal ball») + ixtiyoriy sertifikat.
 */
function TeacherTestFormModal({
  groupId,
  editing,
  onClose,
  onSaved,
}: {
  groupId: string
  editing: GroupTest | null
  onClose: () => void
  onSaved: () => void
}) {
  const legacy = !!editing && (editing.questionCount ?? 0) === 0
  const [name, setName] = useState(editing?.name ?? '')
  const [date, setDate] = useState(editing ? editing.date.slice(0, 10) : todayIso())
  const [kind, setKind] = useState<TestKind | ''>(
    editing?.kind === 'monthly' || editing?.kind === 'interim' ? editing.kind : '',
  )
  const [questions, setQuestions] = useState<number | null>(editing && !legacy ? editing.questionCount ?? null : null)
  const [maxScore, setMaxScore] = useState<number | null>(legacy ? editing!.maxScore : null)

  // --- sertifikat ---
  const [certEnabled, setCertEnabled] = useState(editing?.certificateEnabled ?? false)
  const [certTemplateId, setCertTemplateId] = useState(editing?.certificateTemplateId ?? '')
  const [templates, setTemplates] = useState<TestCertificateTemplate[]>([])
  const [templatesLoaded, setTemplatesLoaded] = useState(false)

  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  // Shablonlar ro'yxati modal ochilganda bir marta yuklanadi (o'qituvchi faqat tanlaydi).
  useEffect(() => {
    let active = true
    getTeacherCertificateTemplates()
      .then((res) => active && setTemplates(res.templates))
      .catch(() => active && setTemplates([]))
      .finally(() => active && setTemplatesLoaded(true))
    return () => {
      active = false
    }
  }, [])

  const validationError = (): string | null => {
    if (!name.trim()) return 'Test nomini kiriting'
    if (!date) return 'Sanani tanlang'
    if (!kind) return 'Test turini tanlang'
    if (legacy) return maxScore != null && maxScore > 0 ? null : "Maksimal ball 0 dan katta bo'lsin"
    if (questions == null || questions < MIN_QUESTIONS || questions > MAX_QUESTIONS)
      return "Savollar soni 1 dan 200 gacha bo'lsin"
    return null
  }

  const submit = async () => {
    const v = validationError()
    if (v) {
      setErr(v)
      return
    }
    setBusy(true)
    setErr('')
    const certificateTemplateId = certEnabled ? certTemplateId || null : null
    try {
      if (editing) {
        await updateTeacherTest(editing.id, {
          name: name.trim(), date, kind,
          questionCount: legacy ? null : questions,
          maxScore: legacy ? maxScore ?? 0 : 0,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
      } else {
        await createTeacherTest({
          groupId, name: name.trim(), date, kind, questionCount: questions,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
      }
      onSaved()
    } catch (e) {
      setErr(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      size="lg"
      title={editing ? 'Testni tahrirlash' : 'Yangi test'}
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-lg border border-line bg-white px-3.5 py-2 text-[13px] font-semibold text-mute disabled:opacity-50"
          >
            Bekor qilish
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={busy}
            className="rounded-lg bg-teal-600 px-3.5 py-2 text-[13px] font-semibold text-white disabled:opacity-50"
          >
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <span className={label}>Test nomi</span>
          <input
            className={field}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: Unit 5"
          />
        </div>

        <div>
          <span className={label}>Turi</span>
          <div className="flex gap-2" role="radiogroup" aria-label="Test turi">
            {TEST_KINDS.map((k) => (
              <label
                key={k.kind}
                className={cn(
                  'flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-lg border px-3 py-2 text-[14px] font-semibold',
                  kind === k.kind ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-line bg-white text-mute',
                )}
              >
                <input
                  type="radio"
                  name="teacher-test-kind"
                  className="h-4 w-4"
                  checked={kind === k.kind}
                  onChange={() => setKind(k.kind)}
                />
                {k.label}
              </label>
            ))}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <span className={label}>Sana</span>
            <input type="date" className={field} value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div>
            {legacy ? (
              <>
                <span className={label}>Maksimal ball</span>
                <NumberInput as="input" className={field} value={maxScore} onChange={setMaxScore} placeholder="100" />
              </>
            ) : (
              <>
                <span className={label}>Savollar soni</span>
                <NumberInput as="input" className={field} value={questions} onChange={setQuestions} placeholder="30" />
              </>
            )}
          </div>
        </div>

        {/* SERTIFIKAT */}
        <div className="rounded-xl border border-line bg-panel2 p-3">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={certEnabled}
              onChange={(e) => setCertEnabled(e.target.checked)}
              className="mt-0.5 h-4 w-4 shrink-0 rounded border-line text-teal-600 focus:ring-teal-500"
            />
            <span className="min-w-0">
              <span className="flex items-center gap-1.5 text-[13px] font-bold text-ink">
                <Award className="h-4 w-4 text-emerald-600" />
                Test natijasi bo'yicha sertifikat berilsin
              </span>
              <span className="block text-[11px] leading-snug text-faint">
                Ball kiritilgan har bir o'quvchiga bittadan sertifikat yaratiladi.
              </span>
            </span>
          </label>

          {certEnabled && (
            <div className="mt-3">
              {templatesLoaded && templates.length === 0 ? (
                <p className="rounded-lg bg-amber-50 px-3 py-2 text-[12px] font-semibold text-amber-700">
                  Sertifikat shabloni hali yuklanmagan — administratorga murojaat qiling.
                </p>
              ) : (
                <>
                  <span className={label}>Sertifikat shabloni</span>
                  <select
                    className={field}
                    value={certTemplateId}
                    onChange={(e) => setCertTemplateId(e.target.value)}
                    disabled={!templatesLoaded}
                  >
                    <option value="">— standart shablon —</option>
                    {templates.map((tpl) => (
                      <option key={tpl.id} value={tpl.id}>
                        {tpl.name}
                        {tpl.isDefault ? ' (standart)' : ''}
                      </option>
                    ))}
                  </select>
                </>
              )}
            </div>
          )}
        </div>

        {err && (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-[13px] font-semibold text-rose-600">{err}</p>
        )}
      </div>
    </Modal>
  )
}
/**
 * Guruh testlari paneli — ro'yxat (oy/tur filtri), test tafsiloti (Excel'dek jadval) va yaratish/tahrirlash/o'chirish.
 * Tashqi padding'ni CHAQIRUVCHI beradi (sahifada `px-4 pt-3 pb-6`, jurnal tabida — hech narsa).
 */
export function TeacherGroupTestsPanel({
  groupId,
  title,
  subtitle,
  onBack,
}: {
  groupId: string
  /** Ro'yxat rejimidagi sarlavha — guruh nomi yoki "Imtihonlar (testlar)". */
  title: string
  /** Sarlavha ostidagi kichik matn. */
  subtitle?: string
  /** Berilsa — ro'yxat rejimida chapda "orqaga" tugmasi ko'rinadi (guruhlar ro'yxatiga). */
  onBack?: () => void
}) {
  const thisMonth = currentMonth()
  const [month, setMonth] = useState(thisMonth)
  const [kind, setKind] = useState('')
  const [reload, setReload] = useState(0)
  const listKey = `${groupId}|${month}|${kind}|${reload}`
  const [list, setList] = useState<{ key: string; tests: GroupTest[]; error: string | null } | null>(null)

  const [detail, setDetail] = useState<TestResultDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailError, setDetailError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editingTest, setEditingTest] = useState<GroupTest | null>(null)

  const [deleteTarget, setDeleteTarget] = useState<GroupTest | null>(null)
  const [deleting, setDeleting] = useState(false)

  // --- sertifikat ---
  // Generatsiya FONDA, 5 tadan bo'lib bajariladi (admin sahifasi bilan bir xil oqim):
  // `certJob` — holat va shu daqiqada tayyor sertifikatlar; ish davomida ro'yxat to'lib boradi.
  const [certJob, setCertJob] = useState<CertificateJob | null>(null)
  const [certBusy, setCertBusy] = useState(false)
  const [certError, setCertError] = useState('')
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const jobRunning = certJob?.running === true
  const certTestId = detail?.id ?? ''
  /** HOZIR ochiq test — uchib ketgan so'rovlar javobini filtrlash uchun (pastdagi izohga qarang). */
  const openTestId = useRef('')
  /** Ketma-ket muvaffaqiyatsiz holat so'rovlari soni. */
  const pollFails = useRef(0)
  /** Jadval — sertifikat/yuborishdan oldin saqlanmagan natijalarni saqlash uchun. */
  const grid = useRef<TestScoreGridHandle>(null)

  useEffect(() => {
    let active = true
    const k = `${groupId}|${month}|${kind}|${reload}`
    getTeacherGroupTests(groupId, { month, kind })
      .then((tests) => active && setList({ key: k, tests, error: null }))
      .catch((err) => active && setList({ key: k, tests: [], error: apiErrorMessage(err, "Testlarni yuklab bo'lmadi") }))
    return () => {
      active = false
    }
  }, [groupId, month, kind, reload])

  const testsLoading = list?.key !== listKey
  const tests = list?.tests ?? []
  const loadTests = () => setReload((r) => r + 1)

  /** Ish ketayotganda holatni so'rab turamiz — tayyor bo'lganlar ro'yxatga qo'shilib boradi. */
  useEffect(() => {
    if (!jobRunning || !certTestId) return
    pollFails.current = 0
    const timer = setInterval(() => {
      getCertificateJob(certTestId, true)
        .then((j) => {
          // `clearInterval` faqat taymerni to'xtatadi — ALLAQACHON uchib ketgan so'rov baribir
          // qaytadi. Tekshirmasak, eski testning javobi endi ochilgan testga yozilib, unda
          // BOSHQA o'quvchilarning sertifikatlari ko'rinib qolardi.
          if (openTestId.current !== certTestId) return
          pollFails.current = 0
          setCertJob(j)
        })
        .catch(() => {
          // Ketma-ket bir necha xatodan keyin TO'XTAYMIZ (tarmoq uzilishi, 403/404) — aks holda
          // UI abadiy "Yaratilmoqda..." holatida so'rov yuboraverardi.
          if (openTestId.current !== certTestId) return
          pollFails.current += 1
          if (pollFails.current >= CERT_POLL_MAX_FAILS) {
            setCertJob((j) => (j ? { ...j, running: false } : j))
            setCertError("Holat aniqlanmadi — sertifikatlar fonda yaratilayotgan bo'lishi mumkin. Qaytadan oching.")
          }
        })
    }, CERT_POLL_MS)
    return () => clearInterval(timer)
  }, [jobRunning, certTestId])

  const openDetail = (t: GroupTest) => {
    openTestId.current = t.id
    setDetailLoading(true)
    setDetailError(null)
    setDetail(null)
    setCertJob(null)
    setCertError('')
    getTeacherTestDetail(t.id)
      .then((d) => {
        if (openTestId.current !== t.id) return   // boshqa testga o'tib ketilgan
        setDetail(d)
        // Boshqa oynada boshlangan generatsiya davom etayotgan bo'lishi mumkin — shuni ilib olamiz.
        if (d.certificateEnabled) {
          getCertificateJob(t.id, true)
            .then((j) => { if (j.running && openTestId.current === t.id) setCertJob(j) })
            .catch(() => { /* holat olinmasa ham tafsilot ishlayveradi */ })
        }
      })
      .catch((err) => {
        if (openTestId.current === t.id) setDetailError(apiErrorMessage(err, "Test tafsilotini yuklab bo'lmadi"))
      })
      .finally(() => setDetailLoading(false))
  }

  const backToTests = async () => {
    await grid.current?.flush().catch(() => { /* xato jadvalning o'zida ko'rsatilgan */ })
    openTestId.current = ''
    setDetail(null)
    setDetailError(null)
    setCertJob(null)
    setCertError('')
    loadTests()
  }

  const closeForm = () => {
    setFormOpen(false)
    setEditingTest(null)
  }

  const onFormSaved = () => {
    setFormOpen(false)
    setEditingTest(null)
    loadTests()
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await deleteTeacherTest(deleteTarget.id)
      setDeleteTarget(null)
      loadTests()
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    } finally {
      setDeleting(false)
    }
  }

  /** Jadvaldagi saqlanmagan natijalar — sertifikat yoki yuborishdan OLDIN saqlanadi (tartib muhim). */
  const flushScores = async () => {
    await grid.current?.flush()
  }

  /** «Yuborilgan» vaqtini yangilash (jadval qiymatlari tegilmaydi). */
  const refreshSent = () => {
    const id = openTestId.current
    if (!id) return
    getTeacherTestDetail(id)
      .then((d) => setDetail((prev) => (prev && prev.id === d.id ? { ...prev, resultsSentAt: d.resultsSentAt } : prev)))
      .catch(() => { /* ikkilamchi: vaqt keyingi ochilishda ko'rinadi */ })
  }

  /** «Saqlash va sertifikat yaratish» — ball kiritilgan har o'quvchiga (qayta bosilsa yangilanadi).
   *  So'rov ishni FONDA boshlaydi va darhol qaytadi; qolganini holat so'rovi kuzatadi. */
  const generateCertificates = async () => {
    if (!detail) return
    setCertBusy(true)
    setCertError('')
    try {
      await flushScores()   // avval natijalar, keyin sertifikat — tartib muhim
      setCertJob(await startTeacherTestCertificates(detail.id))
    } catch (err) {
      setCertError(apiErrorMessage(err, "Sertifikat yaratib bo'lmadi"))
    } finally {
      setCertBusy(false)
    }
  }

  const downloadOne = async (certificateId: string) => {
    setDownloadingId(certificateId)
    setCertError('')
    try {
      await downloadCertificate(certificateId, true)
    } catch (err) {
      setCertError(apiErrorMessage(err, "Yuklab bo'lmadi"))
    } finally {
      setDownloadingId(null)
    }
  }

  const downloadZip = async () => {
    if (!detail) return
    setDownloadingId('all')
    setCertError('')
    try {
      await downloadAllCertificates(detail.id, true)
    } catch (err) {
      setCertError(apiErrorMessage(err, "Yuklab bo'lmadi"))
    } finally {
      setDownloadingId(null)
    }
  }

  // ---------------- Test tafsiloti (natija kiritish) ----------------
  if (detail || detailLoading || detailError) {
    // SERTIFIKAT — testda yoqilgan bo'lsa yaratish tugmasi va ro'yxat ko'rinadi.
    const certEnabled = detail?.certificateEnabled === true
    // Ish boshlangan bo'lsa ro'yxat FON ISHIDAN keladi (u ish davomida to'lib boradi).
    const certificates = certJob ? certJob.items : detail?.certificates ?? []
    const certDone = certJob && !certJob.running && certJob.done > 0
    const certPercent = certJob && certJob.total > 0
      ? Math.round((certJob.done / certJob.total) * 100)
      : 0
    const questions = detail?.questionCount ?? 0
    return (
      <div>
        <div className="mb-4 flex items-center gap-2.5">
          <button
            type="button"
            onClick={backToTests}
            className="tap-scale flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-mute shadow-[var(--shadow-card)]"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
          <div className="min-w-0 flex-1">
            <p className="truncate text-[17px] font-extrabold text-ink">{detail?.name ?? 'Test'}</p>
            {detail && (
              <p className="text-[12px] text-mute">
                {[
                  formatDate(detail.date),
                  testKindLabel(detail.kind ?? ''),
                  questions > 0 ? `${questions} ta savol` : `Maks: ${detail.maxScore} ball`,
                ].filter(Boolean).join(' · ')}
              </p>
            )}
          </div>
        </div>

        {detailLoading ? (
          <div className="rounded-xl border border-line bg-white p-6 shadow-[var(--shadow-card)]">
            <Loader label="Yuklanmoqda..." />
          </div>
        ) : detailError ? (
          <div className="rounded-xl border border-line bg-white p-6 text-center text-[13px] font-semibold text-rose-600 shadow-[var(--shadow-card)]">
            {detailError}
          </div>
        ) : detail && detail.rows.length === 0 ? (
          <div className="rounded-xl border border-line bg-white px-5 py-8 text-center text-[13px] text-faint shadow-[var(--shadow-card)]">
            Bu guruhda faol o'quvchi yo'q.
          </div>
        ) : (
          detail && (
            <>
              <TestScoreGrid
                key={detail.id}
                ref={grid}
                detail={detail}
                editable
                save={(rows) => saveTeacherTestScoresBatch(detail.id, rows)}
                onDetail={setDetail}
              />
              <SendResultsPanel
                className="mt-3"
                resultsSentAt={detail.resultsSentAt}
                beforeSend={flushScores}
                send={(resend) => sendTeacherTestResults(detail.id, resend)}
                onSent={refreshSent}
              />
            </>
          )
        )}

        {/* SERTIFIKAT xabarlari + yaratish tugmasi (pastda yopishib turadi) */}
        {detail && certEnabled && (
          <>
            {/* Ish ketmoqda — progress. Sertifikatlar 5 tadan tayyor bo'lib pastdagi ro'yxatga
                qo'shilib boradi: tugashini kutmasdan yuklab olsa ham bo'ladi. */}
            {jobRunning && (
              <div className="mt-3 rounded-xl bg-teal-50 px-3 py-2.5">
                <p className="flex items-center justify-center gap-1.5 text-[13px] font-semibold text-teal-700">
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Yaratilmoqda... {certJob?.done ?? 0} / {certJob?.total ?? 0}
                </p>
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-teal-100">
                  <div
                    className="h-full rounded-full bg-teal-600 transition-all duration-500"
                    style={{ width: `${certPercent}%` }}
                  />
                </div>
                <p className="mt-1.5 text-center text-[11px] text-teal-700/80">
                  Tayyor bo'lganlarini hoziroq yuklab olsangiz bo'ladi.
                </p>
              </div>
            )}
            {certDone && (
              <p className="mt-3 rounded-xl bg-emerald-50 px-3 py-2 text-center text-[13px] font-semibold text-emerald-700">
                {certJob?.done} ta sertifikat yaratildi
              </p>
            )}
            {certJob?.warning && (
              <p className="mt-2 rounded-xl bg-amber-50 px-3 py-2 text-center text-[12px] font-semibold text-amber-700">
                {certJob.warning}
              </p>
            )}
            {(certError || certJob?.error) && (
              <p className="mt-2 rounded-xl bg-rose-50 px-3 py-2 text-center text-[13px] font-semibold text-rose-600">
                {certError || certJob?.error}
              </p>
            )}
            <div className="sticky bottom-0 z-10 mt-3 rounded-xl border border-line bg-white/95 p-3 shadow-[var(--shadow-card)] backdrop-blur">
              <button
                type="button"
                onClick={generateCertificates}
                disabled={certBusy || jobRunning}
                className="tap-scale flex w-full items-center justify-center gap-1.5 rounded-xl bg-teal-600 px-3 py-2.5 text-[14px] font-bold text-white disabled:opacity-50"
              >
                {certBusy || jobRunning ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Award className="h-4 w-4" />
                )}
                {jobRunning
                  ? `Yaratilmoqda... ${certJob?.done ?? 0}/${certJob?.total ?? 0}`
                  : 'Saqlash va sertifikat yaratish'}
              </button>
              <p className="mt-1.5 text-center text-[11px] text-faint">
                Sertifikat FAQAT natija kiritilgan o'quvchilarga yaratiladi; qayta bosilsa mavjudlari
                yangilanadi.
              </p>
            </div>
          </>
        )}

        {/* SERTIFIKATLAR — yaratilganlari (yuklab olish: PDF yoki Word) */}
        {/* `certEnabled` SHART: sertifikat yoqilmagan testda bu bo'lim umuman chiqmasligi kerak. */}
        {detail && certEnabled && certificates.length > 0 && (
          <div className="mt-4 overflow-hidden rounded-xl border border-line bg-white shadow-[var(--shadow-card)]">
            <div className="flex items-center gap-2 border-b border-line bg-panel2 px-4 py-2">
              <p className="flex min-w-0 flex-1 items-center gap-1.5 text-[12px] font-bold text-mute">
                <Award className="h-3.5 w-3.5 text-emerald-600" /> Sertifikatlar
                <span className="font-normal text-faint">({certificates.length})</span>
              </p>
              {/* ZIP faqat HAMMASI tayyor bo'lgach — yarim to'plam "hammasi" bo'lib chiqmasin. */}
              <button
                type="button"
                onClick={downloadZip}
                disabled={downloadingId === 'all' || jobRunning}
                className="flex shrink-0 items-center gap-1.5 rounded-lg border border-line bg-white px-2.5 py-1.5 text-[12px] font-semibold text-mute disabled:opacity-50"
              >
                {downloadingId === 'all' ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                ) : (
                  <Download className="h-3.5 w-3.5" />
                )}
                Hammasini yuklab olish (ZIP)
              </button>
            </div>
            {certificates.map((c, i) => (
              <div
                key={c.id}
                className={cn(
                  'flex items-center gap-3 px-4 py-3',
                  i < certificates.length - 1 && 'border-b border-line',
                )}
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[14px] font-semibold text-ink">
                    {c.studentName}
                    {c.status === 'docx' && (
                      <span
                        className="ml-1.5 rounded bg-amber-50 px-1.5 py-0.5 text-[10px] font-bold text-amber-700"
                        title="Serverda PDF yaratilmadi — faqat Word fayl mavjud"
                      >
                        faqat Word
                      </span>
                    )}
                  </p>
                  <p className="truncate text-[11px] text-faint">
                    <span className="font-mono">{c.number}</span> · {c.score}/{c.maxScore} ·{' '}
                    {c.percent}% · {formatDate(c.issuedAt)}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => downloadOne(c.id)}
                  disabled={downloadingId === c.id}
                  className="flex shrink-0 items-center gap-1.5 rounded-lg border border-line bg-white px-2.5 py-1.5 text-[12px] font-semibold text-mute disabled:opacity-50"
                >
                  {downloadingId === c.id ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Download className="h-3.5 w-3.5" />
                  )}
                  Yuklab olish
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    )
  }

  // ---------------- Guruh testlari ro'yxati ----------------
  return (
    <div>
      <div className="mb-3 flex items-center gap-2.5">
        {onBack && (
          <button
            type="button"
            onClick={onBack}
            className="tap-scale flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-mute shadow-[var(--shadow-card)]"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <div className="min-w-0 flex-1">
          <p className="truncate text-[17px] font-extrabold text-ink">{title}</p>
          {subtitle && <p className="text-[12px] text-mute">{subtitle}</p>}
        </div>
        <button
          type="button"
          onClick={() => {
            setEditingTest(null)
            setFormOpen(true)
          }}
          className="tap-scale flex shrink-0 items-center gap-1 rounded-xl bg-teal-600 px-3 py-2 text-[13px] font-semibold text-white"
        >
          <Plus className="h-4 w-4" /> Yangi test
        </button>
      </div>

      <div className="mb-3">
        <TestMonthFilter month={month} kind={kind} onMonth={setMonth} onKind={setKind} currentMonth={thisMonth} />
      </div>

      {testsLoading && !list ? (
        <div className="rounded-xl border border-line bg-white p-6 shadow-[var(--shadow-card)]">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : list?.error ? (
        <div className="rounded-xl border border-line bg-white p-6 text-center text-[13px] font-semibold text-rose-600 shadow-[var(--shadow-card)]">
          {list.error}
        </div>
      ) : tests.length === 0 ? (
        <div className="rounded-xl border border-line bg-white px-5 py-8 text-center shadow-[var(--shadow-card)]">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-tealsoft text-teal-600">
            <ClipboardList className="h-6 w-6" />
          </div>
          <h4 className="text-[15px] font-bold text-ink">{month ? "Bu oyda test yo'q" : "Hali test yo'q"}</h4>
          <p className="mt-1 text-[13px] text-mute">"Yangi test" tugmasi orqali yarating.</p>
        </div>
      ) : (
        <div className={cn('space-y-3 transition-opacity', testsLoading && 'opacity-60')}>
          {tests.map((t) => {
            const questions = t.questionCount ?? 0
            return (
              <div
                key={t.id}
                className="tap-scale rounded-xl border border-line bg-white p-4 shadow-[var(--shadow-card)]"
              >
                <div className="flex items-start gap-3">
                  <button
                    type="button"
                    onClick={() => openDetail(t)}
                    className="flex min-w-0 flex-1 items-start gap-3 text-left"
                  >
                    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-600">
                      <ClipboardList className="h-5 w-5" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="flex flex-wrap items-center gap-1.5 text-[15px] font-bold text-ink">
                        <span className="truncate">{t.name}</span>
                        {testKindLabel(t.kind ?? '') && (
                          <span className="shrink-0 rounded-md bg-indigo-50 px-1.5 py-0.5 text-[10px] font-bold text-indigo-700">
                            {testKindLabel(t.kind ?? '')}
                          </span>
                        )}
                        {t.certificateEnabled && (
                          <span
                            className="inline-flex shrink-0 items-center gap-1 rounded-md bg-emerald-50 px-1.5 py-0.5 text-[10px] font-bold text-emerald-700"
                            title="Test natijasi bo'yicha sertifikat beriladi"
                          >
                            <Award className="h-3 w-3" />
                            SERTIFIKAT
                            {t.certificateCount > 0 && ` · ${t.certificateCount}`}
                          </span>
                        )}
                      </p>
                      <p className="text-[12px] text-mute">
                        {formatDate(t.date)} · {questions > 0 ? `${questions} ta savol` : `Maks: ${t.maxScore} ball`}
                      </p>
                    </div>
                  </button>
                  <div className="flex shrink-0 items-center gap-0.5">
                    <button
                      type="button"
                      onClick={() => {
                        setEditingTest(t)
                        setFormOpen(true)
                      }}
                      className="rounded-lg p-1.5 text-faint transition-colors hover:bg-panel3 hover:text-ink"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => setDeleteTarget(t)}
                      className="rounded-lg p-1.5 text-faint transition-colors hover:bg-red-50 hover:text-red-500"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => openDetail(t)}
                  className="mt-3 flex w-full flex-wrap items-center gap-x-4 gap-y-1 rounded-xl border border-line bg-panel2 px-3 py-2 text-left text-[12px] text-mute"
                >
                  <span>
                    <span className="font-mono font-bold text-ink">{t.scoredCount}</span>/
                    <span className="font-mono">{t.studentCount}</span> kiritilgan
                  </span>
                  {t.avgPercent != null && (
                    <span>
                      O'rtacha: <span className="font-mono font-bold text-ink">{fmtScore(t.avgPercent)}%</span>
                    </span>
                  )}
                  {t.resultsSentAt && (
                    <span className="inline-flex items-center gap-1 text-emerald-700">
                      <Send className="h-3 w-3" /> {formatSentAt(t.resultsSentAt)}
                    </span>
                  )}
                </button>
              </div>
            )
          })}
        </div>
      )}

      {formOpen && (
        <TeacherTestFormModal
          key={editingTest?.id ?? 'new'}
          groupId={groupId}
          editing={editingTest}
          onClose={closeForm}
          onSaved={onFormSaved}
        />
      )}

      <Modal
        open={!!deleteTarget}
        onClose={() => !deleting && setDeleteTarget(null)}
        size="sm"
        title="Testni o'chirish"
        footer={
          <>
            <button
              type="button"
              onClick={() => setDeleteTarget(null)}
              disabled={deleting}
              className="rounded-lg border border-line bg-white px-3.5 py-2 text-[13px] font-semibold text-mute disabled:opacity-50"
            >
              Bekor qilish
            </button>
            <button
              type="button"
              onClick={confirmDelete}
              disabled={deleting}
              className="rounded-lg bg-red-600 px-3.5 py-2 text-[13px] font-semibold text-white disabled:opacity-50"
            >
              {deleting ? "O'chirilmoqda..." : "O'chirish"}
            </button>
          </>
        }
      >
        <p className="text-[13px] text-mute">
          <span className="font-semibold text-ink">{deleteTarget?.name}</span> testini va uning barcha
          natijalarini o'chirasizmi? Bu amalni qaytarib bo'lmaydi.
        </p>
      </Modal>
    </div>
  )
}
