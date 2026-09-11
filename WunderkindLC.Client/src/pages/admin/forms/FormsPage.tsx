import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  Plus, Pencil, Trash2, Link2, Copy, Check, Users, Eye, ExternalLink, ClipboardList, Files,
} from 'lucide-react'
import {
  getLeadForms, createLeadForm, deleteLeadForm, duplicateLeadForm,
  getLeadFormSources, emptySocials,
  type LeadFormListItem,
} from '@/api/services/leadForms'
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

/** Formaning ommaviy URL'i — ijtimoiy tarmoq profiliga AYNAN shu havola qo'yiladi. */
export function formUrl(slug: string) {
  return `${window.location.origin}/forma/${slug}`
}

/**
 * LID FORMALARI ro'yxati. Har bir kanal (Instagram, Facebook, Telegram, ...) uchun alohida forma —
 * o'z havolasi va o'z manbasi bilan, shu sabab lidning qaysi tarmoqdan kelgani aniq bo'ladi.
 */
export function FormsPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  const canTests = can('schedule.levelTests', 'view')
  const [forms, setForms] = useState<LeadFormListItem[]>([])
  const [sources, setSources] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [creating, setCreating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [newTitle, setNewTitle] = useState('')
  const [newSource, setNewSource] = useState('')
  const [newCourse, setNewCourse] = useState('')
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getLeadForms(), getLeadFormSources()])
      .then(([f, s]) => {
        setForms(f)
        setSources(s)
      })
      .finally(() => setLoading(false))
  }, [])

  const copy = async (slug: string) => {
    try {
      await navigator.clipboard.writeText(formUrl(slug))
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
      const created = await createLeadForm({
        title: newTitle.trim(),
        source: newSource,
        courseName: newCourse.trim(),
        courseOptions: [],
        intro: '',
        successText: '',
        buttonText: '',
        askAge: false,
        askCourse: false,
        askParentPhone: false,
        isActive: true,
        fields: [],
        socials: emptySocials,
      })
      setCreating(false)
      setNewTitle('')
      setNewSource('')
      setNewCourse('')
      navigate(`/admin/forms/${created.id}`)
    } catch (err) {
      alert(apiErrorMessage(err, "Forma yaratib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleDuplicate = async (f: LeadFormListItem) => {
    try {
      const copyForm = await duplicateLeadForm(f.id)
      navigate(`/admin/forms/${copyForm.id}`)
    } catch (err) {
      alert(apiErrorMessage(err, "Nusxa olib bo'lmadi"))
    }
  }

  const handleDelete = async (f: LeadFormListItem) => {
    if (
      !confirm(
        `"${f.title}" formasini o'chirasizmi?\n\n` +
          `${f.submissionCount} ta ariza tarixi ham o'chadi. Ular yaratgan LIDLAR CRM'da qoladi.`,
      )
    )
      return
    try {
      await deleteLeadForm(f.id)
      setForms((prev) => prev.filter((x) => x.id !== f.id))
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <CardTabs items={formTabs(true, canTests)} className="mb-5" />

      <PageHeader
        title="Lid formalari"
        sub="Har bir ijtimoiy tarmoq uchun alohida forma yarating — havola qayerga qo'yilganiga qarab lid manbasi o'zi aniqlanadi"
        actions={
          can('leads.forms', 'create') && (
            <Button onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" /> Yangi forma
            </Button>
          )
        }
      />

      {forms.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-12 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
              <ClipboardList className="h-7 w-7" />
            </div>
            <p className="max-w-md text-sm text-slate-500">
              Hali forma yo'q. Masalan «Instagram — bepul sinov darsi» formasini yarating va
              havolasini Instagram profilingizga qo'ying.
            </p>
            {can('leads.forms', 'create') && (
              <Button onClick={() => setCreating(true)}>
                <Plus className="h-4 w-4" /> Yangi forma
              </Button>
            )}
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {forms.map((f) => (
            <Card key={f.id} className="flex flex-col">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <h3 className="truncate text-base font-bold text-slate-800">
                    <Link to={`/admin/forms/${f.id}`} className="text-inherit no-underline hover:underline">
                      {f.title}
                    </Link>
                  </h3>
                  <p className="mt-0.5 truncate text-xs text-slate-400">
                    {f.courseName || 'Kurs tanlanmagan'}
                  </p>
                </div>
                <Badge tone={f.isActive ? 'green' : 'default'}>{f.isActive ? 'Faol' : "O'chiq"}</Badge>
              </div>

              {/* Manba — modulning asosiy ma'nosi, shuning uchun ko'zga tashlanib turadi */}
              <div className="mt-2">
                {f.source ? (
                  <Badge tone="blue">{f.source}</Badge>
                ) : (
                  <Badge tone="amber">Manba tanlanmagan</Badge>
                )}
              </div>

              <div className="mt-3 flex items-center gap-4 text-sm text-slate-500">
                <span className="inline-flex items-center gap-1.5" title="Forma necha marta ochilgan">
                  <Eye className="h-4 w-4 text-slate-400" /> {f.views}
                </span>
                <span className="inline-flex items-center gap-1.5" title="Tushgan arizalar">
                  <Users className="h-4 w-4 text-slate-400" /> {f.submissionCount} ariza
                </span>
                <span className="inline-flex items-center gap-1.5" title="Qo'shimcha savollar">
                  <ClipboardList className="h-4 w-4 text-slate-400" /> {f.fieldCount}
                </span>
              </div>

              {/* Ommaviy havola */}
              <div className="mt-3 flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5">
                <Link2 className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                <span className="flex-1 truncate font-mono text-xs text-slate-500">/forma/{f.slug}</span>
                <button
                  type="button"
                  onClick={() => copy(f.slug)}
                  title="Havolani nusxalash"
                  className="shrink-0 rounded p-1 text-slate-400 transition-colors hover:bg-white hover:text-brand-600"
                >
                  {copied === f.slug ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
                </button>
                <a
                  href={formUrl(f.slug)}
                  target="_blank"
                  rel="noreferrer"
                  title="Yangi oynada ochish"
                  className="shrink-0 rounded p-1 text-slate-400 transition-colors hover:bg-white hover:text-brand-600"
                >
                  <ExternalLink className="h-3.5 w-3.5" />
                </a>
              </div>

              <div className="mt-4 flex items-center gap-2 border-t border-slate-100 pt-3">
                <Button variant="secondary" className="flex-1" onClick={() => navigate(`/admin/forms/${f.id}`)}>
                  <Pencil className="h-4 w-4" /> Tahrirlash
                </Button>
                {can('leads.forms', 'create') && (
                  <button
                    type="button"
                    onClick={() => handleDuplicate(f)}
                    title="Nusxa olish (boshqa tarmoq uchun)"
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                  >
                    <Files className="h-4 w-4" />
                  </button>
                )}
                {can('leads.forms', 'delete') && (
                  <button
                    type="button"
                    onClick={() => handleDelete(f)}
                    title="O'chirish"
                    className={cn('rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600')}
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                )}
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Yangi forma — nom + manba + kurs */}
      <Modal
        open={creating}
        onClose={() => setCreating(false)}
        title="Yangi lid formasi"
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
            label="Forma nomi"
            required
            placeholder="Masalan: Instagram — bepul sinov darsi"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            autoFocus
          />
          <Select label="Manba (kanal)" value={newSource} onChange={(e) => setNewSource(e.target.value)}>
            <option value="">— Manba tanlanmagan —</option>
            {sources.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
          {/* Kurs — erkin matn (markazdagi kurslar katalogidan olinmaydi). */}
          <Input
            label="Kurs / taklif nomi (ixtiyoriy)"
            value={newCourse}
            onChange={(e) => setNewCourse(e.target.value)}
            placeholder="Masalan: Bepul sinov darsi"
          />
          <p className="text-xs text-slate-400">
            Manba ro'yxati «O'quv bo'limi → Sabablar» sahifasida boshqariladi. Yaratgandan so'ng
            qo'shimcha savollar va matnlarni sozlaysiz.
          </p>
        </div>
      </Modal>
    </div>
  )
}
