import { useEffect, useMemo, useState } from 'react'
import {
  Plus, Pencil, Trash2, ClipboardList, ChevronRight, AlertTriangle, Loader2, Award,
} from 'lucide-react'
import type { GroupTest } from '@/types'
import { getGroupTests, createTest, updateTest, deleteTest } from '@/api/services/testResults'
import { getCertificateTemplates } from '@/api/services/testCertificates'
import type { TestCertificateTemplate } from '@/api/services/testCertificates'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, formatDate } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { localIsoDate } from '@/lib/localDate'

/**
 * Guruh testlari — YAGONA admin komponenti. Ikki joyda AYNAN bir xil ishlaydi:
 *  • "O'quv bo'limi → Testlar natijalari" → guruh sahifasi (`TestGroupPage`);
 *  • guruh (jurnal) sahifasidagi "Imtihonlar" tabi (`ClassDetailPage`).
 * Test faqat OFLAYN: nom, sana, maksimal ball — ballni o'qituvchi/admin qo'lda kiritadi.
 * (Onlayn rejim — bot orqali PDF, javob kaliti, test kodi — 2026-10 da olib tashlandi.)
 *
 * Ruxsat: yaratish/tahrir/o'chirish `classes` bo'limi bo'yicha (`usePerm`) — admin/superadmin
 * cheklovsiz, xodimga "Xodimlar va rollar" dan beriladi. Server ham AYNAN shu qoidada
 * (`TestResultsController` → `[AdminPerm("classes")]`), o'qituvchi ilovasi esa
 * `TeacherPortalController`ning `test-results/*` endpointlari orqali BIR XIL servisga
 * (`TestResultService`) boradi.
 */

const control =
  'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

const todayIso = () => localIsoDate(new Date())

/** Guruh testlari ro'yxati + yaratish/tahrirlash/o'chirish. Test bosilsa — `onOpenTest`. */
export function GroupTestsPanel({
  groupId,
  onOpenTest,
}: {
  groupId: string
  onOpenTest: (testId: string) => void
}) {
  const { can } = usePerm()
  const [tests, setTests] = useState<GroupTest[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<GroupTest | null>(null)
  const [deleting, setDeleting] = useState<GroupTest | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    getGroupTests(groupId)
      .then((t) => active && setTests(t))
      .catch((e) => active && setError(apiErrorMessage(e, "Yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [groupId])

  const handleSaved = (t: GroupTest) => {
    setTests((prev) => {
      const exists = prev.some((x) => x.id === t.id)
      const next = exists ? prev.map((x) => (x.id === t.id ? { ...x, ...t } : x)) : [t, ...prev]
      return next.sort((a, b) => b.date.localeCompare(a.date))
    })
    setFormOpen(false)
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    try {
      await deleteTest(deleting.id)
      setTests((prev) => prev.filter((x) => x.id !== deleting.id))
      setDeleting(null)
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeleteBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-3">
      {can('classes.testResults', 'create') && (
        <div className="flex justify-end">
          <Button onClick={() => { setEditing(null); setFormOpen(true) }}>
            <Plus className="h-4 w-4" /> Yangi test
          </Button>
        </div>
      )}

      {error && <Card className="py-3 text-center text-sm text-red-500">{error}</Card>}

      {tests.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          Hali test yaratilmagan. "Yangi test" tugmasini bosing.
        </Card>
      ) : (
        <div className="space-y-2.5">
          {tests.map((t) => {
            return (
              <div
                key={t.id}
                className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 transition-all hover:border-brand-300"
              >
                <button
                  type="button"
                  onClick={() => onOpenTest(t.id)}
                  className="flex min-w-0 flex-1 items-center gap-3 text-left"
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <ClipboardList className="h-5 w-5" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 truncate font-semibold text-slate-800">
                      {t.name}
                      {t.certificateEnabled && (
                        <span
                          className="inline-flex shrink-0 items-center gap-1 rounded-md bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700"
                          title="Test natijasi bo'yicha sertifikat beriladi"
                        >
                          <Award className="h-3 w-3" />
                          SERTIFIKAT
                          {t.certificateCount > 0 && ` · ${t.certificateCount}`}
                        </span>
                      )}
                    </p>
                    <p className="text-xs text-slate-400">
                      {formatDate(t.date)} · Maks: {t.maxScore} ball
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-slate-500">
                      <span>
                        Baholangan:{' '}
                        <span className="font-medium text-slate-700">
                          {t.scoredCount}/{t.studentCount}
                        </span>
                      </span>
                      {t.avgScore != null && (
                        <span>
                          O'rtacha: <span className="font-medium text-slate-700">{t.avgScore}</span>
                        </span>
                      )}
                    </div>
                  </div>
                </button>
                <div className="flex shrink-0 items-center gap-1">
                  {can('classes.testResults', 'edit') && (
                    <button
                      type="button"
                      onClick={() => { setEditing(t); setFormOpen(true) }}
                      className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-50 hover:text-slate-600"
                      title="Tahrirlash"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                  )}
                  {can('classes.testResults', 'delete') && (
                    <button
                      type="button"
                      onClick={() => setDeleting(t)}
                      className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-500"
                      title="O'chirish"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                  <ChevronRight className="h-5 w-5 text-slate-300" />
                </div>
              </div>
            )
          })}
        </div>
      )}

      {formOpen && (
        <TestFormModal
          key={editing?.id ?? 'new'}
          groupId={groupId}
          editing={editing}
          onClose={() => setFormOpen(false)}
          onSaved={handleSaved}
        />
      )}

      <Modal
        open={!!deleting}
        onClose={() => !deleteBusy && setDeleting(null)}
        title="Testni o'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)} disabled={deleteBusy}>
              Bekor
            </Button>
            <Button variant="danger" onClick={confirmDelete} disabled={deleteBusy}>
              {deleteBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : "O'chirish"}
            </Button>
          </>
        }
      >
        <div className="flex items-start gap-3">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
          <p className="text-sm text-slate-600">
            <b>{deleting?.name}</b> testi va unga kiritilgan barcha ballar o'chiriladi. Bu amalni
            qaytarib bo'lmaydi.
          </p>
        </div>
      </Modal>
    </div>
  )
}

/** Test yaratish/tahrirlash modali — nom, sana, maksimal ball (+ ixtiyoriy sertifikat). */
function TestFormModal({
  groupId,
  editing,
  onClose,
  onSaved,
}: {
  groupId: string
  editing: GroupTest | null
  onClose: () => void
  onSaved: (t: GroupTest) => void
}) {
  const [name, setName] = useState(editing?.name ?? '')
  const [date, setDate] = useState(editing?.date ?? todayIso())
  const [maxScore, setMaxScore] = useState<string>(editing ? String(editing.maxScore) : '')

  // --- sertifikat ---
  const [certEnabled, setCertEnabled] = useState(editing?.certificateEnabled ?? false)
  const [certTemplateId, setCertTemplateId] = useState(editing?.certificateTemplateId ?? '')
  const [templates, setTemplates] = useState<TestCertificateTemplate[]>([])
  const [templatesLoaded, setTemplatesLoaded] = useState(false)

  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  // Shablonlar ro'yxati modal ochilganda bir marta yuklanadi.
  useEffect(() => {
    let active = true
    getCertificateTemplates(true)
      .then((list) => active && setTemplates(list))
      .catch(() => active && setTemplates([]))
      .finally(() => active && setTemplatesLoaded(true))
    return () => {
      active = false
    }
  }, [])

  const max = useMemo(() => Number(maxScore), [maxScore])
  const valid = name.trim().length > 0 && !!date && Number.isFinite(max) && max > 0

  const submit = async () => {
    if (!valid) {
      setErr('Nom, sana va 0 dan katta maksimal ball kiriting')
      return
    }
    setBusy(true)
    setErr('')
    const certificateTemplateId = certEnabled ? certTemplateId || null : null
    try {
      if (editing) {
        await updateTest(editing.id, {
          name: name.trim(), date, maxScore: max,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
        onSaved({
          ...editing, name: name.trim(), date, maxScore: max,
          certificateEnabled: certEnabled, certificateTemplateId: certificateTemplateId ?? '',
        })
      } else {
        const created = await createTest({
          groupId, name: name.trim(), date, maxScore: max,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
        onSaved(created)
      }
    } catch (e) {
      setErr(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={editing ? 'Testni tahrirlash' : 'Yangi test'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor
          </Button>
          <Button onClick={submit} disabled={busy || !valid}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <label className="mb-1 block text-xs font-medium text-slate-500">Test nomi</label>
          <input
            className={control}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: Unit 3 test"
            autoFocus
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-500">Sana</label>
            <input type="date" className={control} value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-500">Maksimal ball</label>
            <NumberInput
              as="input"
              className={control}
              value={maxScore === '' ? null : Number(maxScore)}
              onChange={(v) => setMaxScore(v === null ? '' : String(v))}
              placeholder="100"
            />
          </div>
        </div>

        {/* SERTIFIKAT */}
        <div className="rounded-xl border border-slate-200 bg-slate-50/60 p-3">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={certEnabled}
              onChange={(e) => setCertEnabled(e.target.checked)}
              className="mt-0.5 h-4 w-4 shrink-0 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
            />
            <span className="min-w-0">
              <span className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">
                <Award className="h-4 w-4 text-emerald-600" />
                Test natijasi bo'yicha sertifikat berilsin
              </span>
              <span className="block text-[11px] leading-snug text-slate-400">
                Ball kiritilgan har bir o'quvchiga bittadan sertifikat yaratiladi.
              </span>
            </span>
          </label>

          {certEnabled && (
            <div className="mt-3">
              {templatesLoaded && templates.length === 0 ? (
                <p className="rounded-lg bg-amber-50 px-3 py-2 text-[12px] text-amber-700">
                  Avval «Testlar natijalari → Sertifikat shablonlari» bo'limida Word shablon yuklang.
                </p>
              ) : (
                <>
                  <label className="mb-1 block text-xs font-medium text-slate-500">
                    Sertifikat shabloni
                  </label>
                  <select
                    className={control}
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

        {err && <p className="text-sm text-red-500">{err}</p>}
      </div>
    </Modal>
  )
}
