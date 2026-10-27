import { useEffect, useState } from 'react'
import { CalendarX, Columns3, PhoneCall, RotateCcw, Wallet } from 'lucide-react'
import {
  getContactAutoSettings, getContactStages, restoreContactStage, saveContactAutoSettings,
  type ContactAutoSettings, type ContactStage,
} from '@/api/services/contactStages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { NumberInput } from '@/components/ui/NumberInput'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import { SettingToggle } from './SettingToggle'

/**
 * «Sozlamalar → Service» — «Bog'lanish kerak» (servis) taxtasining avtomatikasi (egasining talabi, 2026-10-27):
 * kelmaganlar (necha dars ketma-ket sababsiz) va qarzdorlar (oyning qaysi sanasidan) avtomatik navbatga tushadi.
 * Qarz to'langanda talab o'zi yopiladi — bu sozlamaga bog'liq emas. Pastda — o'chirilgan tizim ustunlarini qaytarish.
 * Server qoidalari: `.claude/rules/contacts.md` §9, §9.5.
 */
export function ContactServiceSettings() {
  const { can } = usePerm()
  const canEdit = can('contacts', 'edit')
  const [form, setForm] = useState<ContactAutoSettings | null>(null)
  const [lessons, setLessons] = useState<number | null>(null)
  const [day, setDay] = useState<number | null>(null)
  const [hidden, setHidden] = useState<ContactStage[]>([])
  const [loadError, setLoadError] = useState('')
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    Promise.all([getContactAutoSettings(), getContactStages(true)])
      .then(([s, stages]) => {
        setForm(s)
        setLessons(s.absenceLessons)
        setDay(s.debtDay)
        setHidden(stages.filter((x) => x.isHidden))
      })
      .catch((e) => setLoadError(apiErrorMessage(e, "Sozlamalarni yuklab bo'lmadi")))
  }, [])

  if (loadError) return <Card><p className="text-sm text-[#d32f2f]">{loadError}</p></Card>
  if (!form) return <Loader label="Yuklanmoqda..." />

  const patch = (p: Partial<ContactAutoSettings>) => {
    setForm({ ...form, ...p })
    setSaved(false)
  }

  const save = async () => {
    if (lessons === null || day === null) {
      setError("Darslar soni va sanani kiriting")
      return
    }
    setBusy(true)
    setError('')
    try {
      const s = await saveContactAutoSettings({ ...form, absenceLessons: lessons, debtDay: day })
      setForm(s)
      setSaved(true)
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const restore = async (stage: ContactStage) => {
    try {
      await restoreContactStage(stage.id)
      setHidden((h) => h.filter((x) => x.id !== stage.id))
    } catch (e) {
      alert(apiErrorMessage(e, "Ustunni qaytarib bo'lmadi"))
    }
  }

  return (
    <div className="space-y-3">
      <Card title={<span className="flex items-center gap-2"><PhoneCall className="h-4 w-4 text-brand-600" /> Service — avtomatik qo'shish</span>}>
        <p className="mb-4 text-sm text-slate-500">
        «Bog'lanish kerak» taxtasiga o'quvchilar avtomatik tushadi. Qarzdorning talabi qarzi to'liq to'langanda o'zi yopiladi.
        Talab «Hal bo'ldi» qilinsa yoki o'quvchi arxivlansa, karta taxtadan chiqadi.
        </p>
        <div className="max-w-xl space-y-4">
          <div className="space-y-2">
            <SettingToggle
              icon={CalendarX}
              title="Darsga kelmaganlarni avtomatik qo'shish"
              desc="O'quvchi bitta guruhda ketma-ket sababsiz dars qoldirsa — «Darsga kelmadi» talabi ochiladi"
              checked={form.absenceEnabled}
              onChange={(v) => canEdit && patch({ absenceEnabled: v })}
            />
            {form.absenceEnabled && (
              <label className="flex items-center gap-2 pl-10 text-sm text-slate-600">
                Ketma-ket
                <NumberInput value={lessons} onChange={(v) => { setLessons(v); setSaved(false) }}
                  disabled={!canEdit} className="w-16 text-center" aria-label="Necha dars" />
                ta dars sababsiz qoldirsa (1–10)
              </label>
            )}
          </div>

          <div className="space-y-2">
            <SettingToggle
              icon={Wallet}
              title="Qarzdorlarni avtomatik qo'shish"
              desc="To'lov muddatigacha to'lamagan o'quvchi — «To'lov qarzdorligi» talabi (summa kurslar bo'yicha)"
              checked={form.debtEnabled}
              onChange={(v) => canEdit && patch({ debtEnabled: v })}
            />
            {form.debtEnabled && (
              <label className="flex items-center gap-2 pl-10 text-sm text-slate-600">
                Har oyning
                <NumberInput value={day} onChange={(v) => { setDay(v); setSaved(false) }}
                  disabled={!canEdit} className="w-16 text-center" aria-label="Oyning sanasi" />
                -sanasidan boshlab (1–28)
              </label>
            )}
          </div>

          {error && <p className="text-sm text-[#d32f2f]">{error}</p>}
          {canEdit && (
            <div className="flex items-center gap-3">
              <Button onClick={() => void save()} disabled={busy}>{busy ? 'Saqlanmoqda...' : 'Saqlash'}</Button>
              {saved && <span className="text-sm text-emerald-600">Saqlandi</span>}
            </div>
          )}
        </div>
      </Card>

      {hidden.length > 0 && (
        <Card title={<span className="flex items-center gap-2"><Columns3 className="h-4 w-4 text-brand-600" /> O'chirilgan ustunlar</span>}>
          <p className="mb-3 text-sm text-slate-500">Taxtadan o'chirilgan asosiy ustunlar — kerak bo'lsa qaytaring.</p>
          <ul className="max-w-xl divide-y divide-slate-100">
            {hidden.map((s) => (
              <li key={s.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                <span className="text-slate-700">{s.title} <span className="text-slate-400">· {s.baseStatusLabel}</span></span>
                {canEdit && (
                  <Button variant="secondary" onClick={() => void restore(s)}>
                    <RotateCcw className="h-4 w-4" /> Qaytarish
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}
