import { useEffect, useState } from 'react'
import { Plus, Trash2, Save, Check, ChevronUp, ChevronDown, Lock } from 'lucide-react'
import {
  getLeadEntryForm, saveLeadEntryForm, leadEntryStateLabels,
  type LeadEntryForm, type LeadEntryState, type LeadEntryStandard,
} from '@/api/services/leadEntryForm'
import {
  fieldKindLabels, needsOptions, type LeadFormFieldKind,
} from '@/api/services/leadForms'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { formTabs } from '@/config/sectionTabs'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage } from '@/lib/utils'

/** Muharrirdagi qo'shimcha savol (`id` bo'lsa — mavjud savol, bo'lmasa yangi). */
interface FieldState {
  id?: string
  label: string
  kind: LeadFormFieldKind
  options: string[]
  placeholder: string
  required: boolean
}

/** Segmented control tugmalarining tartibi — "kamdan ko'pga" (so'ralmaydi → majburiy). */
const STATES: LeadEntryState[] = ['hidden', 'optional', 'required']

/** Tuman kaliti — maktab AYNAN shunga bog'liq (kaskad), shu sabab alohida nomlangan. */
const KEY_DISTRICT = 'districtId'
const KEY_SCHOOL = 'schoolId'

/**
 * «LID KIRITISH FORMASI» — Lidlar bo'limidagi «Yangi lid» oynasi qanday ko'rinishini sozlash.
 *
 * ⚠️ Sozlama FAQAT XODIM QO'LDA kiritadigan lidga tegishli: ommaviy lid formasi, daraja testi,
 * landing va Instagram'dan kelgan lidlar O'Z qoidalari bilan yaratiladi. Aks holda "Tug'ilgan
 * kun majburiy" degan sozlama tashqaridan kelayotgan lidlarni jimgina rad etib, markaz
 * mijozini yo'qotardi (serverdagi `LeadEntryRules` izohi bilan bir xil sabab).
 */
export function LeadEntryFormPage() {
  const { can } = usePerm()
  const canEdit = can('leads.forms', 'edit')
  const canTests = can('schedule.levelTests', 'view')

  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  /** Saqlangandan keyin qisqa vaqt ko'rinadigan belgi (nusxalash tugmasidagi naqsh). */
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const [standard, setStandard] = useState<LeadEntryStandard[]>([])
  const [fields, setFields] = useState<FieldState[]>([])

  const apply = (f: LeadEntryForm) => {
    setStandard(f.standard)
    setFields(
      f.fields.map((x) => ({
        id: x.id, label: x.label, kind: x.kind, options: x.options,
        placeholder: x.placeholder, required: x.required,
      })),
    )
  }

  useEffect(() => {
    getLeadEntryForm()
      .then(apply)
      .catch((err) => setError(apiErrorMessage(err, "Sozlamani yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // ---- Standart maydonlar ----
  const setState = (key: string, state: LeadEntryState) =>
    setStandard((ss) => ss.map((s) => (s.key === key ? { ...s, state } : s)))

  /** Tuman so'ralmasa maktab ham ko'rsatilmaydi — server ham AYNAN shunday normallashtiradi. */
  const districtHidden =
    standard.find((s) => s.key === KEY_DISTRICT)?.state === 'hidden'

  // ---- Qo'shimcha savollar (lid formasi muharriridagi bilan bir xil boshqaruv) ----
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
    if (saving || !canEdit) return
    setSaving(true)
    setError('')
    try {
      const updated = await saveLeadEntryForm({
        standard: standard.map((s) => ({ key: s.key, state: s.state })),
        fields: fields
          .map((f) => ({
            id: f.id,
            label: f.label.trim(),
            kind: f.kind,
            options: f.options.map((o) => o.trim()).filter((o) => o.length > 0),
            placeholder: f.placeholder.trim(),
            required: f.required,
          }))
          .filter((f) => f.label.length > 0),
      })
      // Server TOZALAGAN holatni qaytadan yozamiz: masalan tuman so'ralmasa maktab ham
      // yopiladi, variantsiz qolgan "ro'yxatdan tanlash" esa oddiy matnga tushadi.
      apply(updated)
      setSaved(true)
      setTimeout(() => setSaved(false), 1600)
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const saveButton = canEdit && (
    <div className="flex items-center gap-2">
      {saved && (
        <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-600">
          <Check className="h-3.5 w-3.5" /> Saqlandi
        </span>
      )}
      <Button onClick={handleSave} disabled={saving}>
        <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
      </Button>
    </div>
  )

  return (
    <div>
      <CardTabs items={formTabs(true, canTests)} className="mb-5" />

      <PageHeader
        title="Lid kiritish formasi"
        sub="Lidlar bo'limidagi «Yangi lid» oynasi shu yerda sozlanadi: qaysi maydon so'raladi, qaysisi majburiy"
        actions={saveButton}
      />

      {/* Modulning CHEGARASI — foydalanuvchi "hamma lidga tegadi" deb o'ylab qolmasin */}
      <p className="mb-4 rounded-xl border border-brand-100 bg-brand-50/50 px-3 py-2.5 text-sm text-slate-600">
        Bu sozlama FAQAT xodim qo'lda kiritadigan lidga tegishli. Ommaviy lid formasi, daraja
        testi va Instagram'dan keladigan lidlar o'z qoidalari bilan tushaveradi — ularga
        ta'sir qilmaydi.
      </p>

      {error && (
        <p className="mb-4 rounded-xl bg-red-50 px-3 py-2.5 text-sm text-red-700">{error}</p>
      )}

      <div className="space-y-4">
        <Card
          title="Standart maydonlar"
          sub="Har bir maydon «Yangi lid» oynasida qanday ko'rinishi: so'ralmaydi / ixtiyoriy / majburiy"
        >
          <div className="divide-y divide-slate-100">
            {standard.map((s) => {
              // Maktab tumandan quriladi (kaskad): tumansiz select doim bo'sh turardi
              const cascadeOff = s.key === KEY_SCHOOL && districtHidden
              return (
                <div key={s.key} className="py-2.5 first:pt-0 last:pb-0">
                  <div
                    className={cn(
                      'flex flex-wrap items-center justify-between gap-3',
                      cascadeOff && 'opacity-50',
                    )}
                  >
                    <span className="flex items-center gap-1.5 text-sm text-slate-700">
                      {s.label}
                      {s.locked && <Lock className="h-3.5 w-3.5 text-slate-300" />}
                      {s.locked && (
                        <span className="text-xs font-normal text-slate-400">doim majburiy</span>
                      )}
                    </span>
                    <div className="inline-flex rounded-lg bg-slate-100 p-0.5 text-xs">
                      {STATES.map((st) => (
                        <button
                          key={st}
                          onClick={() => setState(s.key, st)}
                          disabled={!canEdit || s.locked || cascadeOff}
                          className={cn(
                            'rounded-md px-2.5 py-1 font-medium transition-colors disabled:cursor-not-allowed',
                            s.state === st
                              ? 'bg-white text-slate-800 shadow-sm'
                              : 'text-slate-500 hover:text-slate-700',
                          )}
                        >
                          {leadEntryStateLabels[st]}
                        </button>
                      ))}
                    </div>
                  </div>
                  {cascadeOff && (
                    <p className="mt-1 text-[11px] text-amber-600">
                      Tuman so'ralmasa maktab ham ko'rsatilmaydi — maktablar ro'yxati tumandan
                      quriladi.
                    </p>
                  )}
                </div>
              )
            })}
          </div>
        </Card>

        <Card
          title={`Qo'shimcha savollar (${fields.length})`}
          sub="Javoblar lidga saqlanadi va lid oynasida ko'rinadi (standart maydonlardan keyin so'raladi)"
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
              Qo'shimcha savol yo'q — menejer faqat standart maydonlarni to'ldiradi.
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
                      placeholder="Savol matni (masalan: Qaysi vaqtda o'qimoqchi?)"
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

        {canEdit && <div className="flex justify-end">{saveButton}</div>}
      </div>
    </div>
  )
}
