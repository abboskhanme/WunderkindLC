import { useEffect, useState } from 'react'
import { Plus, Trash2, MapPin, School as SchoolIcon, Pencil, Check, X } from 'lucide-react'
import type { District, School } from '@/types'
import {
  getDistricts,
  createDistrict,
  updateDistrict,
  deleteDistrict,
  createSchool,
  updateSchool,
  deleteSchool,
} from '@/api/services/districts'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

export function DistrictsPage() {
  const [loading, setLoading] = useState(true)
  const [districts, setDistricts] = useState<District[]>([])
  const [adding, setAdding] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    getDistricts()
      .then(setDistricts)
      .finally(() => setLoading(false))
  }, [])

  const addDistrict = async () => {
    const name = adding.trim()
    if (!name || busy) return
    setBusy(true)
    try {
      const created = await createDistrict(name)
      setDistricts((prev) => [...prev, created])
      setAdding('')
    } catch (e) {
      alert(e instanceof Error ? e.message : "Tuman qo'shib bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <PageHeader
        title="Tuman va maktablar"
        sub="Tuman yarating va ichiga maktablarini qo'shing. O'quvchi ma'lumotini kiritishda tuman → maktab tanlanadi."
      />

      {/* Yangi tuman qo'shish */}
      <Card title="Yangi tuman" sub="Tuman nomini kiriting (masalan: Yunusobod)">
        <div className="flex items-center gap-2">
          <input
            value={adding}
            onChange={(e) => setAdding(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && addDistrict()}
            placeholder="Tuman nomi..."
            className={cn(control, 'flex-1')}
          />
          <Button onClick={addDistrict} disabled={busy || !adding.trim()}>
            <Plus className="h-4 w-4" /> Tuman qo'shish
          </Button>
        </div>
      </Card>

      {districts.length === 0 ? (
        <p className="mt-4 text-sm text-slate-400">Hali tuman yo'q — yuqorida qo'shing.</p>
      ) : (
        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          {districts.map((d) => (
            <DistrictCard key={d.id} district={d} onChange={setDistricts} />
          ))}
        </div>
      )}
    </div>
  )
}

function DistrictCard({
  district,
  onChange,
}: {
  district: District
  onChange: React.Dispatch<React.SetStateAction<District[]>>
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(district.name)
  const [addingSchool, setAddingSchool] = useState('')
  const [busy, setBusy] = useState(false)

  const saveName = async () => {
    const trimmed = name.trim()
    if (!trimmed || trimmed === district.name) {
      setEditing(false)
      setName(district.name)
      return
    }
    await updateDistrict(district.id, trimmed)
    onChange((prev) => prev.map((x) => (x.id === district.id ? { ...x, name: trimmed } : x)))
    setEditing(false)
  }

  const removeDistrict = async () => {
    if (!confirm(`"${district.name}" tumani va undagi barcha maktablar o'chiriladi. Davom etilsinmi?`)) return
    await deleteDistrict(district.id)
    onChange((prev) => prev.filter((x) => x.id !== district.id))
  }

  const addSchool = async () => {
    const sName = addingSchool.trim()
    if (!sName || busy) return
    setBusy(true)
    try {
      const created = await createSchool(district.id, sName)
      onChange((prev) =>
        prev.map((x) => (x.id === district.id ? { ...x, schools: [...x.schools, created] } : x)),
      )
      setAddingSchool('')
    } catch (e) {
      alert(e instanceof Error ? e.message : "Maktab qo'shib bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  const saveSchool = async (s: School, value: string) => {
    const trimmed = value.trim()
    if (!trimmed || trimmed === s.name) return
    await updateSchool(s.id, trimmed)
    onChange((prev) =>
      prev.map((x) =>
        x.id === district.id
          ? { ...x, schools: x.schools.map((y) => (y.id === s.id ? { ...y, name: trimmed } : y)) }
          : x,
      ),
    )
  }

  const removeSchool = async (s: School) => {
    await deleteSchool(s.id)
    onChange((prev) =>
      prev.map((x) =>
        x.id === district.id ? { ...x, schools: x.schools.filter((y) => y.id !== s.id) } : x,
      ),
    )
  }

  return (
    <Card
      title={
        editing ? (
          <span className="flex items-center gap-2">
            <input
              value={name}
              autoFocus
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') saveName()
                if (e.key === 'Escape') {
                  setEditing(false)
                  setName(district.name)
                }
              }}
              className={cn(control, 'flex-1')}
            />
            <button type="button" onClick={saveName} className="rounded-lg p-1.5 text-emerald-600 hover:bg-emerald-50">
              <Check className="h-4 w-4" />
            </button>
            <button
              type="button"
              onClick={() => {
                setEditing(false)
                setName(district.name)
              }}
              className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100"
            >
              <X className="h-4 w-4" />
            </button>
          </span>
        ) : (
          <span className="flex items-center gap-2">
            <MapPin className="h-4 w-4 text-brand-500" />
            {district.name}
            <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-semibold text-slate-500">
              {district.schools.length} maktab
            </span>
          </span>
        )
      }
      actions={
        !editing && (
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setEditing(true)}
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
              title="Tuman nomini tahrirlash"
            >
              <Pencil className="h-4 w-4" />
            </button>
            <button
              type="button"
              onClick={removeDistrict}
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
              title="Tumanni o'chirish"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        )
      }
    >
      <div className="space-y-2">
        {[...district.schools]
          .sort((a, b) => a.order - b.order)
          .map((s) => (
            <div key={s.id} className="flex items-center gap-2">
              <SchoolIcon className="h-4 w-4 shrink-0 text-slate-300" />
              <input
                defaultValue={s.name}
                onBlur={(e) => saveSchool(s, e.target.value)}
                className={cn(control, 'flex-1')}
              />
              <button
                type="button"
                onClick={() => removeSchool(s)}
                className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          ))}
        {district.schools.length === 0 && (
          <p className="text-xs text-slate-400">Maktab yo'q — quyida qo'shing.</p>
        )}

        <div className="flex items-center gap-2 pt-1">
          <input
            value={addingSchool}
            onChange={(e) => setAddingSchool(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && addSchool()}
            placeholder="Maktab raqami yoki nomi..."
            className={cn(control, 'flex-1')}
          />
          <Button variant="secondary" onClick={addSchool} disabled={busy || !addingSchool.trim()}>
            <Plus className="h-4 w-4" /> Qo'shish
          </Button>
        </div>
      </div>
    </Card>
  )
}
