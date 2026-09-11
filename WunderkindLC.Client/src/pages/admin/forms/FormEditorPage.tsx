import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Plus, Trash2, Copy, Check, ExternalLink, Link2, Save, ListChecks, Users,
  ChevronUp, ChevronDown, Eye,
} from 'lucide-react'
import {
  getLeadForm, updateLeadForm, getLeadFormSubmissions,
  getLeadFormSources, emptySocials,
  fieldKindLabels, needsOptions,
  type LeadFormDetail, type LeadFormSubmission, type LeadFormFieldInput, type LeadFormFieldKind,
  type LeadFormSocials,
} from '@/api/services/leadForms'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge } from '@/components/ui/Badge'
import { LeadStageChip } from '@/components/leads/LeadStageChip'
import { usePerm } from '@/lib/permissions'
import { cn, formatDate, formatMoney, apiErrorMessage } from '@/lib/utils'
import { formUrl } from './FormsPage'

/** Muharrirdagi maydon holati (`id` bo'lsa — mavjud maydon). */
interface FieldState {
  id?: string
  label: string
  kind: LeadFormFieldKind
  options: string[]
  placeholder: string
  required: boolean
}

/**
 * Bitta LID FORMASI muharriri: sozlamalar + qo'shimcha savollar + tushgan arizalar.
 * Ism va telefon HAR DOIM so'raladi (lidning eng kam ma'lumoti) — qolgani shu yerda sozlanadi.
 */
export function FormEditorPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { can } = usePerm()
  const canEdit = can('leads.forms', 'edit')

  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [detail, setDetail] = useState<LeadFormDetail | null>(null)
  const [sources, setSources] = useState<string[]>([])
  const [tab, setTab] = useState<'edit' | 'submissions'>('edit')
  const [copied, setCopied] = useState(false)
  const [subs, setSubs] = useState<LeadFormSubmission[] | null>(null)

  // Forma holati
  const [title, setTitle] = useState('')
  const [source, setSource] = useState('')
  const [courseName, setCourseName] = useState('')
  const [courseOptions, setCourseOptions] = useState<string[]>([])
  const [socials, setSocials] = useState<LeadFormSocials>(emptySocials)
  const [intro, setIntro] = useState('')
  const [successText, setSuccessText] = useState('')
  const [buttonText, setButtonText] = useState('')
  const [askAge, setAskAge] = useState(false)
  const [askCourse, setAskCourse] = useState(false)
  const [askParentPhone, setAskParentPhone] = useState(false)
  const [isActive, setIsActive] = useState(true)
  const [fields, setFields] = useState<FieldState[]>([])

  const apply = (d: LeadFormDetail) => {
    setDetail(d)
    setTitle(d.title)
    setSource(d.source)
    setCourseName(d.courseName)
    setCourseOptions(d.courseOptions)
    setSocials(d.socials ?? emptySocials)
    setIntro(d.intro)
    setSuccessText(d.successText)
    setButtonText(d.buttonText)
    setAskAge(d.askAge)
    setAskCourse(d.askCourse)
    setAskParentPhone(d.askParentPhone)
    setIsActive(d.isActive)
    setFields(
      d.fields.map((f) => ({
        id: f.id, label: f.label, kind: f.kind, options: f.options,
        placeholder: f.placeholder, required: f.required,
      })),
    )
  }

  useEffect(() => {
    Promise.all([getLeadForm(id), getLeadFormSources()])
      .then(([d, s]) => {
        apply(d)
        setSources(s)
      })
      .catch(() => setDetail(null))
      .finally(() => setLoading(false))
  }, [id])

  useEffect(() => {
    if (tab === 'submissions' && subs === null) getLeadFormSubmissions(id).then(setSubs).catch(() => setSubs([]))
  }, [tab, subs, id])

  const copy = async () => {
    if (!detail) return
    try {
      await navigator.clipboard.writeText(formUrl(detail.slug))
      setCopied(true)
      setTimeout(() => setCopied(false), 1600)
    } catch {
      /* jim */
    }
  }

  // ---- Kurs variantlari (formaning O'ZIDA yoziladi, markaz katalogidan EMAS) ----
  const setCourseOption = (i: number, val: string) =>
    setCourseOptions((cs) => cs.map((c, x) => (x === i ? val : c)))
  const addCourseOption = () => setCourseOptions((cs) => [...cs, ''])
  const removeCourseOption = (i: number) => setCourseOptions((cs) => cs.filter((_, x) => x !== i))
  /** Kurs so'ralsin deb belgilanganda ro'yxat bo'sh bo'lsa — darhol ikkita bo'sh qator beriladi. */
  const toggleAskCourse = (v: boolean) => {
    setAskCourse(v)
    if (v && courseOptions.length === 0) setCourseOptions(['', ''])
  }

  // ---- Qo'shimcha savollar ----
  const addField = () =>
    setFields((fs) => [...fs, { label: '', kind: 'text', options: [], placeholder: '', required: false }])
  const removeField = (i: number) => setFields((fs) => fs.filter((_, x) => x !== i))
  const patchField = (i: number, patch: Partial<FieldState>) =>
    setFields((fs) => fs.map((f, x) => (x === i ? { ...f, ...patch } : f)))
  const moveField = (i: number, dir: -1 | 1) =>
    setFields((fs) => {
      const j = i + dir
      if (j < 0 || j >= fs.length) return fs
      const next = [...fs]
      ;[next[i], next[j]] = [next[j], next[i]]
      return next
    })
  const setOption = (i: number, oi: number, val: string) =>
    setFields((fs) => fs.map((f, x) => (x === i ? { ...f, options: f.options.map((o, y) => (y === oi ? val : o)) } : f)))
  const addOption = (i: number) =>
    setFields((fs) => fs.map((f, x) => (x === i ? { ...f, options: [...f.options, ''] } : f)))
  const removeOption = (i: number, oi: number) =>
    setFields((fs) => fs.map((f, x) => (x === i ? { ...f, options: f.options.filter((_, y) => y !== oi) } : f)))

  /** Turi o'zgarganda variantlar mos holga keltiriladi (variantli turga o'tsa — ikkita bo'sh qator). */
  const changeKind = (i: number, kind: LeadFormFieldKind) =>
    patchField(i, {
      kind,
      options: needsOptions(kind) ? (fields[i].options.length > 0 ? fields[i].options : ['', '']) : [],
    })

  const handleSave = async () => {
    if (!detail || saving || !canEdit) return
    setSaving(true)
    try {
      const payloadFields: LeadFormFieldInput[] = fields
        .map((f) => ({
          id: f.id,
          label: f.label.trim(),
          kind: f.kind,
          options: f.options.map((o) => o.trim()).filter((o) => o.length > 0),
          placeholder: f.placeholder.trim(),
          required: f.required,
        }))
        .filter((f) => f.label.length > 0)
      const updated = await updateLeadForm(id, {
        title: title.trim(),
        source,
        courseName: courseName.trim(),
        courseOptions: courseOptions.map((c) => c.trim()).filter((c) => c.length > 0),
        socials,
        intro: intro.trim(),
        successText: successText.trim(),
        buttonText: buttonText.trim(),
        askAge,
        askCourse,
        askParentPhone,
        isActive,
        fields: payloadFields,
      })
      // Server tozalagan/tartiblagan holatni qaytadan yuklaymiz (masalan variantsiz qolgan
      // "ro'yxatdan tanlash" maydoni oddiy matnga tushirilgan bo'lishi mumkin).
      apply(updated)
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!detail)
    return (
      <Card>
        <p className="py-10 text-center text-slate-400">Forma topilmadi.</p>
      </Card>
    )

  return (
    <div>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <button
              onClick={() => navigate('/admin/forms')}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
              title="Orqaga"
            >
              <ArrowLeft className="h-5 w-5" />
            </button>
            {detail.title || 'Lid formasi'}
          </span>
        }
        sub={
          <span className="flex flex-wrap items-center gap-2">
            {detail.source ? <Badge tone="blue">{detail.source}</Badge> : <Badge tone="amber">Manba tanlanmagan</Badge>}
            <span>{detail.courseName ? `Kurs: ${detail.courseName}` : 'Kurs tanlanmagan'}</span>
            <span className="inline-flex items-center gap-1 text-slate-400">
              <Eye className="h-3.5 w-3.5" /> {detail.views} marta ochilgan
            </span>
          </span>
        }
        actions={
          tab === 'edit' && canEdit ? (
            <Button onClick={handleSave} disabled={saving}>
              <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          ) : undefined
        }
      />

      {/* Ommaviy havola — AYNAN shu ijtimoiy tarmoq profiliga qo'yiladi */}
      <div className="mb-4 flex flex-wrap items-center gap-2 rounded-xl border border-brand-100 bg-brand-50/50 px-3 py-2.5">
        <Link2 className="h-4 w-4 shrink-0 text-brand-500" />
        <span className="flex-1 truncate font-mono text-sm text-slate-600">{formUrl(detail.slug)}</span>
        <button
          onClick={copy}
          className="inline-flex items-center gap-1.5 rounded-lg bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:text-brand-600"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
          {copied ? 'Nusxalandi' : 'Nusxalash'}
        </button>
        <a
          href={formUrl(detail.slug)}
          target="_blank"
          rel="noreferrer"
          className="inline-flex items-center gap-1.5 rounded-lg bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:text-brand-600"
        >
          <ExternalLink className="h-3.5 w-3.5" /> Ochish
        </a>
      </div>

      {/* Tablar */}
      <div className="mb-4 flex gap-1 rounded-lg bg-slate-100 p-1 text-sm">
        {([
          { key: 'edit' as const, label: 'Tahrirlash', icon: ListChecks },
          { key: 'submissions' as const, label: 'Arizalar', icon: Users },
        ]).map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={cn(
              'flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 font-medium transition-colors',
              tab === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            <t.icon className="h-4 w-4" /> {t.label}
          </button>
        ))}
      </div>

      {tab === 'edit' ? (
        <div className="space-y-4">
          <Card title="Asosiy ma'lumot">
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Forma nomi" value={title} onChange={(e) => setTitle(e.target.value)} disabled={!canEdit} />
              <Select label="Manba (kanal)" value={source} onChange={(e) => setSource(e.target.value)} disabled={!canEdit}>
                <option value="">— Manba tanlanmagan —</option>
                {/* Formadagi eski manba ro'yxatdan o'chirilgan bo'lsa ham variant sifatida qoladi */}
                {(sources.includes(source) || !source ? sources : [source, ...sources]).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </Select>
              {/* Kurs — ERKIN MATN: reklamada ko'pincha markazdagi rasmiy kurs nomi emas,
                  taklifning O'ZI yoziladi ("Bepul sinov darsi", "Yozgi IELTS intensiv"). */}
              <Input
                label="Kurs / taklif nomi"
                value={courseName}
                onChange={(e) => setCourseName(e.target.value)}
                placeholder="Masalan: Ingliz tili — boshlang'ich"
                disabled={!canEdit}
              />
              <Input
                label="Tugma matni"
                value={buttonText}
                onChange={(e) => setButtonText(e.target.value)}
                placeholder="Yuborish"
                disabled={!canEdit}
              />
            </div>
            <Textarea
              label="Kirish matni (forma tepasida ko'rinadi)"
              className="mt-3"
              rows={2}
              value={intro}
              onChange={(e) => setIntro(e.target.value)}
              placeholder="Masalan: Bepul sinov darsiga yozilish uchun ma'lumotlaringizni qoldiring."
              disabled={!canEdit}
            />
            <Textarea
              label="Rahmat matni (yuborilgandan keyin)"
              className="mt-3"
              rows={2}
              value={successText}
              onChange={(e) => setSuccessText(e.target.value)}
              placeholder="Rahmat! Arizangiz qabul qilindi — tez orada siz bilan bog'lanamiz."
              disabled={!canEdit}
            />
            <label className="mt-3 flex cursor-pointer items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                disabled={!canEdit}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
              />
              Faol (ommaviy havola orqali ochiladi)
            </label>
            {!source && (
              <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
                Manba tanlanmagan — bu formadan kelgan lidlarda manba bo'sh qoladi va kanal
                statistikasida ko'rinmaydi.
              </p>
            )}
          </Card>

          <Card
            title="Standart maydonlar"
            sub="Ism va telefon HAR DOIM so'raladi — lidning eng kam ma'lumoti"
          >
            <div className="space-y-2 text-sm">
              <div className="flex items-center gap-2 text-slate-500">
                <Check className="h-4 w-4 text-emerald-500" /> Ism-familiya (majburiy)
              </div>
              <div className="flex items-center gap-2 text-slate-500">
                <Check className="h-4 w-4 text-emerald-500" /> Telefon raqam (majburiy)
              </div>
              <Toggle
                checked={askAge}
                onChange={setAskAge}
                disabled={!canEdit}
                label="Yoshi so'ralsin"
                hint="Lid izohiga yoziladi"
              />
              <Toggle
                checked={askCourse}
                onChange={toggleAskCourse}
                disabled={!canEdit}
                label="Kursni mijozning o'zi tanlasin"
                hint="Quyida yozilgan variantlardan; tanlangani lidning «qiziqqan kursi» bo'ladi"
              />
              {askCourse && (
                <div className="ml-6 space-y-1.5 rounded-xl border border-slate-200 p-3">
                  <p className="text-xs text-slate-500">
                    Kurs variantlari — SHU formaning o'ziniki. Markazdagi kurslar ro'yxatidan
                    olinmaydi: reklamada ko'pincha boshqacha nom ("Bepul sinov darsi") yoziladi.
                  </p>
                  {courseOptions.map((c, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <span className="h-4 w-4 shrink-0 rounded-full border-2 border-slate-300" />
                      <input
                        value={c}
                        onChange={(e) => setCourseOption(i, e.target.value)}
                        placeholder={`Kurs ${i + 1}`}
                        disabled={!canEdit}
                        className="flex-1 rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
                      />
                      {canEdit && (
                        <button
                          onClick={() => removeCourseOption(i)}
                          title="Variantni o'chirish"
                          className="rounded p-1 text-slate-300 transition-colors hover:text-red-500"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      )}
                    </div>
                  ))}
                  {canEdit && (
                    <button
                      onClick={addCourseOption}
                      className="inline-flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-700"
                    >
                      <Plus className="h-3.5 w-3.5" /> Kurs qo'shish
                    </button>
                  )}
                  {courseOptions.filter((c) => c.trim()).length === 0 && (
                    <p className="text-[11px] text-amber-600">
                      Variant yozilmasa — bu savol formada umuman ko'rsatilmaydi.
                    </p>
                  )}
                </div>
              )}
              <Toggle
                checked={askParentPhone}
                onChange={setAskParentPhone}
                disabled={!canEdit}
                label="Ota-onaning telefoni so'ralsin"
                hint="Lidda «otasining telefoni» maydoniga tushadi — lidlar qidiruvi uni ham qamraydi"
              />
            </div>
          </Card>

          <Card
            title="Ijtimoiy tarmoqlar"
            sub="Ariza yuborilgandan keyin «Rahmat!» ekranida ikonka bo'lib chiqadi — mijoz menejer qo'ng'iroq qilgunicha kanalga obuna bo'lib qolsin"
          >
            <div className="grid gap-3 sm:grid-cols-2">
              {([
                { key: 'instagram' as const, label: 'Instagram', ph: 'instagram.com/wunderkind' },
                { key: 'telegram' as const, label: 'Telegram', ph: 't.me/wunderkind' },
                { key: 'facebook' as const, label: 'Facebook', ph: 'facebook.com/wunderkind' },
                { key: 'youtube' as const, label: 'YouTube', ph: 'youtube.com/@wunderkind' },
                { key: 'website' as const, label: 'Sayt', ph: 'wunderkind.uz' },
              ]).map((s) => (
                <Input
                  key={s.key}
                  label={s.label}
                  value={socials[s.key]}
                  onChange={(e) => setSocials((v) => ({ ...v, [s.key]: e.target.value }))}
                  placeholder={s.ph}
                  disabled={!canEdit}
                />
              ))}
            </div>
            <p className="mt-2 text-xs text-slate-400">
              Bo'sh qoldirilgan havola ko'rsatilmaydi. `https://` yozish shart emas — o'zi qo'shiladi.
            </p>
          </Card>

          <Card
            title={`Qo'shimcha savollar (${fields.length})`}
            sub="Javoblar lid izohiga va arizalar ro'yxatiga tushadi (baholanmaydi)"
            actions={
              canEdit && (
                <Button variant="secondary" onClick={addField}>
                  <Plus className="h-4 w-4" /> Savol
                </Button>
              )
            }
          >
            {fields.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">
                Qo'shimcha savol yo'q — forma faqat ism va telefon so'raydi (eng yuqori konversiya).
              </p>
            ) : (
              <div className="space-y-4">
                {fields.map((f, i) => (
                  <div key={f.id ?? i} className="rounded-xl border border-slate-200 p-3">
                    <div className="flex items-start gap-2">
                      <span className="mt-2 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-500">
                        {i + 1}
                      </span>
                      <input
                        value={f.label}
                        onChange={(e) => patchField(i, { label: e.target.value })}
                        placeholder="Savol matni (masalan: Qaysi vaqtda o'qimoqchisiz?)"
                        disabled={!canEdit}
                        className="flex-1 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 disabled:bg-slate-50"
                      />
                      {canEdit && (
                        <div className="flex shrink-0 items-center">
                          <button
                            onClick={() => moveField(i, -1)}
                            disabled={i === 0}
                            title="Yuqoriga"
                            className="rounded p-1 text-slate-300 transition-colors hover:text-slate-600 disabled:opacity-30"
                          >
                            <ChevronUp className="h-4 w-4" />
                          </button>
                          <button
                            onClick={() => moveField(i, 1)}
                            disabled={i === fields.length - 1}
                            title="Pastga"
                            className="rounded p-1 text-slate-300 transition-colors hover:text-slate-600 disabled:opacity-30"
                          >
                            <ChevronDown className="h-4 w-4" />
                          </button>
                          <button
                            onClick={() => removeField(i)}
                            title="O'chirish"
                            className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      )}
                    </div>

                    <div className="mt-2 flex flex-wrap items-center gap-3 pl-8">
                      <select
                        value={f.kind}
                        onChange={(e) => changeKind(i, e.target.value as LeadFormFieldKind)}
                        disabled={!canEdit}
                        className="rounded-lg border border-slate-200 px-2 py-1 text-xs text-slate-600 outline-none focus:border-brand-400 disabled:bg-slate-50"
                      >
                        {(Object.keys(fieldKindLabels) as LeadFormFieldKind[]).map((k) => (
                          <option key={k} value={k}>
                            {fieldKindLabels[k]}
                          </option>
                        ))}
                      </select>
                      <label className="flex cursor-pointer items-center gap-1.5 text-xs text-slate-500">
                        <input
                          type="checkbox"
                          checked={f.required}
                          onChange={(e) => patchField(i, { required: e.target.checked })}
                          disabled={!canEdit}
                          className="h-3.5 w-3.5 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                        />
                        Majburiy
                      </label>
                      {!needsOptions(f.kind) && (
                        <input
                          value={f.placeholder}
                          onChange={(e) => patchField(i, { placeholder: e.target.value })}
                          placeholder="Maydon ichidagi yordamchi matn (ixtiyoriy)"
                          disabled={!canEdit}
                          className="min-w-[180px] flex-1 rounded-lg border border-slate-200 px-2.5 py-1 text-xs text-slate-600 outline-none focus:border-brand-400 disabled:bg-slate-50"
                        />
                      )}
                    </div>

                    {needsOptions(f.kind) && (
                      <div className="mt-2 space-y-1.5 pl-8">
                        {f.options.map((opt, oi) => (
                          <div key={oi} className="flex items-center gap-2">
                            <span
                              className={cn(
                                'h-4 w-4 shrink-0 border-2 border-slate-300',
                                f.kind === 'checkbox' ? 'rounded' : 'rounded-full',
                              )}
                            />
                            <input
                              value={opt}
                              onChange={(e) => setOption(i, oi, e.target.value)}
                              placeholder={`Variant ${oi + 1}`}
                              disabled={!canEdit}
                              className="flex-1 rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
                            />
                            {canEdit && f.options.length > 1 && (
                              <button
                                onClick={() => removeOption(i, oi)}
                                title="Variantni o'chirish"
                                className="rounded p-1 text-slate-300 transition-colors hover:text-red-500"
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            )}
                          </div>
                        ))}
                        {canEdit && (
                          <button
                            onClick={() => addOption(i)}
                            className="inline-flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-700"
                          >
                            <Plus className="h-3.5 w-3.5" /> Variant qo'shish
                          </button>
                        )}
                        {f.options.filter((o) => o.trim()).length === 0 && (
                          <p className="text-[11px] text-amber-600">
                            Variant kiritilmasa — bu savol saqlashda oddiy matnga aylanadi.
                          </p>
                        )}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </Card>

          {canEdit && (
            <div className="flex justify-end">
              <Button onClick={handleSave} disabled={saving}>
                <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
              </Button>
            </div>
          )}
        </div>
      ) : (
        <SubmissionsTable
          subs={subs}
          onOpenLead={(leadId) => navigate(`/admin/leads?lead=${leadId}`)}
        />
      )}
    </div>
  )
}

/** Sozlamalar uchun kichik "ptichka + izoh" qatori. */
function Toggle({
  checked, onChange, label, hint, disabled,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
  hint?: string
  disabled?: boolean
}) {
  return (
    <label className="flex cursor-pointer items-start gap-2">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        disabled={disabled}
        className="mt-0.5 h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
      />
      <span>
        <span className="text-slate-600">{label}</span>
        {hint && <span className="block text-xs text-slate-400">{hint}</span>}
      </span>
    </label>
  )
}

/** Formaga tushgan arizalar — lidning HOZIRGI holati bilan (o'quvchi bo'ldimi, faolmi). */
export function SubmissionsTable({
  subs, onOpenLead, showForm = false,
}: {
  subs: LeadFormSubmission[] | null
  /** Lidlar bo'limida AYNAN shu lidni ochadi (kanbandagi kartani izlab yurish shart emas). */
  onOpenLead: (leadId: string) => void
  showForm?: boolean
}) {
  if (subs === null)
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  if (subs.length === 0)
    return (
      <Card>
        <p className="py-8 text-center text-sm text-slate-400">Hali ariza tushmagan.</p>
      </Card>
    )

  return (
    <Card title="Arizalar" sub="Har bir ariza CRM Lidlar bo'limida lid bo'lib turadi">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-3 py-2">F.I.SH</th>
              <th className="px-3 py-2">Telefon</th>
              {showForm && <th className="px-3 py-2">Forma</th>}
              <th className="px-3 py-2">Kurs</th>
              <th className="px-3 py-2">Belgi</th>
              <th className="px-3 py-2">Bosqich</th>
              <th className="px-3 py-2 text-right">To'lov</th>
              <th className="px-3 py-2 text-center">Holat</th>
              <th className="px-3 py-2">Sana</th>
              <th className="px-3 py-2 text-right">Lid</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-50">
            {subs.map((s) => (
              <tr key={s.id} className={cn('hover:bg-slate-50/60', s.leadDeleted && 'bg-red-50/40')}>
                <td className={cn('px-3 py-2 font-medium text-slate-700', s.leadDeleted && 'text-red-600 line-through')}>
                  {s.fullName}
                  {s.leadDeleted && <span className="ml-2 text-[11px] font-normal text-red-500">(lid o'chirilgan)</span>}
                  {!s.isNewLead && (
                    <span className="ml-2 text-[11px] font-normal text-slate-400" title="Bu telefon CRM'da allaqachon bor edi — ariza mavjud lidga biriktirildi">
                      takroriy
                    </span>
                  )}
                  {(s.age > 0 || s.parentPhone || s.answers.length > 0) && (
                    <div className="mt-1 space-y-0.5">
                      {s.age > 0 && <div className="text-[11px] font-normal text-slate-400">Yoshi: {s.age}</div>}
                      {s.parentPhone && (
                        <div className="text-[11px] font-normal text-slate-400">Ota-ona: {s.parentPhone}</div>
                      )}
                      {s.answers.map((a, k) => (
                        <div key={k} className="text-[11px] font-normal text-slate-400">
                          <span className="text-slate-500">{a.question}:</span>{' '}
                          {a.answers.length ? a.answers.join(', ') : '—'}
                        </div>
                      ))}
                    </div>
                  )}
                </td>
                <td className="px-3 py-2 font-mono text-slate-500">{s.phone}</td>
                {showForm && <td className="px-3 py-2 text-slate-600">{s.formTitle}</td>}
                <td className="px-3 py-2 text-slate-600">{s.courseName || <span className="text-slate-300">—</span>}</td>
                <td className="px-3 py-2">
                  {s.ref ? (
                    <span className="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-[11px] text-slate-600">{s.ref}</span>
                  ) : (
                    <span className="text-slate-300">—</span>
                  )}
                </td>
                {/* Lid voronkaning qayerida turibdi (kanban ustuni) */}
                <td className="px-3 py-2">
                  <LeadStageChip title={s.stageTitle} color={s.stageColor} />
                </td>
                {/* SOTUV: pul keldimi — "o'quvchi bo'ldi" hali pul degani emas */}
                <td className="px-3 py-2 text-right">
                  {s.paid ? (
                    <span className="whitespace-nowrap font-mono text-xs font-semibold text-emerald-600">
                      {formatMoney(s.paidTotal)}
                      {s.firstPaidAt && (
                        <span className="block font-sans text-[10px] font-normal text-slate-400">
                          {formatDate(s.firstPaidAt)}
                        </span>
                      )}
                    </span>
                  ) : (
                    <span className="text-slate-300">—</span>
                  )}
                </td>
                <td className="px-3 py-2 text-center">
                  {s.active ? (
                    <Badge tone="green">Aktiv o'quvchi</Badge>
                  ) : s.studentId ? (
                    <Badge tone="blue">O'quvchi</Badge>
                  ) : (
                    <Badge>Lid</Badge>
                  )}
                </td>
                <td className="px-3 py-2 text-slate-500">{formatDate(s.createdAt)}</td>
                <td className="px-3 py-2 text-right">
                  {/* Lid o'chirilgan bo'lsa ochadigan narsa yo'q — tugma ham ko'rsatilmaydi */}
                  {s.leadDeleted || !s.leadId ? (
                    <span className="text-slate-300">—</span>
                  ) : (
                    <button
                      onClick={() => onOpenLead(s.leadId)}
                      title="Lidlar bo'limida shu lidni ochish"
                      className="text-xs font-medium text-brand-600 hover:text-brand-700"
                    >
                      Lidni ochish →
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )
}
