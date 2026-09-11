import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Plus, Pencil, Trash2, Link2, Copy, Check, Users, ListChecks, ExternalLink, GraduationCap } from 'lucide-react'
import type { LevelTestListItem, Subject } from '@/types'
import { getLevelTests, createLevelTest, deleteLevelTest } from '@/api/services/levelTests'
import { getSubjects } from '@/api/services/subjects'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge } from '@/components/ui/Badge'
import { CardTabs } from '@/components/ui/CardTabs'
import { formTabs } from '@/config/sectionTabs'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage } from '@/lib/utils'

/** Test ommaviy URL'i. */
function testUrl(slug: string) {
  return `${window.location.origin}/test/${slug}`
}

export function LevelTestsPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  // "Formalar" bo'limining ikkinchi turi — lid formalari `leads` ruxsatida.
  const canForms = can('leads.forms', 'view')
  const [tests, setTests] = useState<LevelTestListItem[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [creating, setCreating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [newTitle, setNewTitle] = useState('')
  const [newCourse, setNewCourse] = useState('')
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getLevelTests(), getSubjects()])
      .then(([t, s]) => {
        setTests(t)
        setSubjects(s)
      })
      .finally(() => setLoading(false))
  }, [])

  const copy = async (slug: string) => {
    try {
      await navigator.clipboard.writeText(testUrl(slug))
      setCopied(slug)
      setTimeout(() => setCopied((c) => (c === slug ? null : c)), 1600)
    } catch {
      /* clipboard yopiq bo'lsa — jim */
    }
  }

  const handleCreate = async () => {
    if (!newTitle.trim() || busy) return
    setBusy(true)
    try {
      // Default daraja diapazonlari — keyin editorda o'zgartirsa bo'ladi.
      const created = await createLevelTest({
        title: newTitle.trim(),
        courseId: newCourse,
        intro: '',
        isActive: true,
        questions: [],
        bands: [
          { label: "Boshlang'ich", minPercent: 0 },
          { label: "O'rta", minPercent: 40 },
          { label: 'Yuqori', minPercent: 75 },
        ],
      })
      setCreating(false)
      setNewTitle('')
      setNewCourse('')
      navigate(`/admin/level-tests/${created.id}`)
    } catch (err) {
      alert(apiErrorMessage(err, "Test yaratib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleDelete = async (t: LevelTestListItem) => {
    if (!confirm(`"${t.title}" testini o'chirasizmi? Natijalar ham o'chadi (lidlar qoladi).`)) return
    try {
      await deleteLevelTest(t.id)
      setTests((prev) => prev.filter((x) => x.id !== t.id))
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <CardTabs items={formTabs(canForms, true)} className="mb-5" />

      <PageHeader
        title="Daraja testlari"
        sub="Kurs uchun test yarating — ommaviy havola orqali topshirilsa, CRM'da yangi lid bo'lib tushadi"
        actions={
          // «Statistika» tugmasi OLIB TASHLANDI — u endi bo'lim cardi («Test statistikasi»),
          // ya'ni sahifa tepasida ikkita bir xil yo'l turmasin.
          <Button onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" /> Yangi test
          </Button>
        }
      />

      {tests.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-12 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
              <GraduationCap className="h-7 w-7" />
            </div>
            <p className="text-sm text-slate-500">Hali test yo'q. Birinchi daraja testini yarating.</p>
            <Button onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" /> Yangi test
            </Button>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {tests.map((t) => (
            <Card key={t.id} className="flex flex-col">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <h3 className="truncate text-base font-bold text-slate-800">
                    <Link to={`/admin/level-tests/${t.id}`} className="text-inherit no-underline hover:underline">
                      {t.title}
                    </Link>
                  </h3>
                  <p className="mt-0.5 truncate text-xs text-slate-400">
                    {t.courseName || 'Kurs tanlanmagan'}
                  </p>
                </div>
                <Badge tone={t.isActive ? 'green' : 'default'}>{t.isActive ? 'Faol' : "O'chiq"}</Badge>
              </div>

              <div className="mt-3 flex items-center gap-4 text-sm text-slate-500">
                <span className="inline-flex items-center gap-1.5">
                  <ListChecks className="h-4 w-4 text-slate-400" /> {t.questionCount} savol
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <Users className="h-4 w-4 text-slate-400" /> {t.submissionCount} topshirgan
                </span>
              </div>

              {/* Ommaviy havola */}
              <div className="mt-3 flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5">
                <Link2 className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                <span className="flex-1 truncate font-mono text-xs text-slate-500">/test/{t.slug}</span>
                <button
                  type="button"
                  onClick={() => copy(t.slug)}
                  title="Havolani nusxalash"
                  className="shrink-0 rounded p-1 text-slate-400 transition-colors hover:bg-white hover:text-brand-600"
                >
                  {copied === t.slug ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
                </button>
                <a
                  href={testUrl(t.slug)}
                  target="_blank"
                  rel="noreferrer"
                  title="Yangi oynada ochish"
                  className="shrink-0 rounded p-1 text-slate-400 transition-colors hover:bg-white hover:text-brand-600"
                >
                  <ExternalLink className="h-3.5 w-3.5" />
                </a>
              </div>

              <div className="mt-4 flex items-center gap-2 border-t border-slate-100 pt-3">
                <Button variant="secondary" className="flex-1" onClick={() => navigate(`/admin/level-tests/${t.id}`)}>
                  <Pencil className="h-4 w-4" /> Tahrirlash
                </Button>
                <button
                  type="button"
                  onClick={() => handleDelete(t)}
                  title="O'chirish"
                  className={cn(
                    'rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600',
                  )}
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Yangi test — nom + kurs */}
      <Modal
        open={creating}
        onClose={() => setCreating(false)}
        title="Yangi daraja testi"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setCreating(false)}>
              Bekor
            </Button>
            <Button onClick={handleCreate} disabled={busy || !newTitle.trim()}>
              Yaratish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <Input
            label="Test nomi"
            required
            placeholder="Masalan: Ingliz tili daraja testi"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            autoFocus
          />
          <Select label="Kurs" value={newCourse} onChange={(e) => setNewCourse(e.target.value)}>
            <option value="">— Kurs tanlanmagan —</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
          <p className="text-xs text-slate-400">
            Yaratgandan so'ng savollar va daraja diapazonlarini qo'shasiz.
          </p>
        </div>
      </Modal>
    </div>
  )
}
