import { useEffect, useState } from 'react'
import type { WikiSettings } from '@/api/services/wiki'
import { getWikiSettings, saveWikiSettings } from '@/api/services/wiki'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { apiErrorMessage } from '@/lib/utils'

interface WikiSettingsModalProps {
  onClose: () => void
  onSaved: () => void
}

/**
 * «Kim nimani ko'radi» — faqat rahbariyat. Menejerlar o'ziga biriktirilgan xodimlarning eslatmalarini
 * ko'radimi va qaysi LAVOZIMDAGILARNIKINI (hech biri belgilanmasa — barchasi). Kim kimga biriktirilgani
 * «Topshiriqlar → Xodimlar» da sozlanadi — bu yerda emas (bitta joy).
 */
export function WikiSettingsModal({ onClose, onSaved }: WikiSettingsModalProps) {
  const [settings, setSettings] = useState<WikiSettings | null>(null)
  const [enabled, setEnabled] = useState(true)
  const [positions, setPositions] = useState<Set<string>>(new Set())
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let alive = true
    getWikiSettings()
      .then((s) => {
        if (!alive) return
        setSettings(s)
        setEnabled(s.managersSeeTeamNotes)
        setPositions(new Set(s.managerVisiblePositionIds))
      })
      .catch((err) => {
        if (alive) setError(apiErrorMessage(err, "Sozlamani yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [])

  const togglePosition = (id: string) =>
    setPositions((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const submit = async () => {
    setBusy(true)
    setError('')
    try {
      await saveWikiSettings({ managersSeeTeamNotes: enabled, managerVisiblePositionIds: [...positions] })
      onSaved()
    } catch (err) {
      // Xato bo'lsa oyna YOPILMAYDI — tanlov saqlanib qoladi (error-visibility.md §2).
      setError(apiErrorMessage(err, "Sozlamani saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Eslatmalarni kim ko'radi"
      size="sm"
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void submit()} disabled={busy || !settings}>Saqlash</Button>
        </div>
      }
    >
      {!settings && !error && <Loader className="h-32" />}
      {settings && (
        <div className="space-y-4 text-[13.5px] text-slate-700">
          <ul className="space-y-1 rounded-lg bg-slate-50 p-3 text-[12.5px] text-slate-600">
            <li>• <b>Har xodim</b> — faqat o'z eslatmalarini ko'radi va yozadi (hamkasbinikini — yo'q).</li>
            <li>• <b>Rahbariyat</b> (direktor, administrator) — hammaning eslatmalarini o'qiydi.</li>
            <li>• <b>Menejer</b> — quyidagi sozlama bo'yicha, faqat o'ziga biriktirilganlarni.</li>
          </ul>

          <label className="flex cursor-pointer items-start gap-2.5">
            <input type="checkbox" className="mt-0.5 h-4 w-4" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
            <span>
              <span className="font-medium">Menejerlar o'ziga biriktirilgan xodimlarning eslatmalarini ko'radi</span>
              <span className="block text-[12px] text-slate-500">Kim kimga biriktirilgani — «Topshiriqlar → Xodimlar» bo'limida.</span>
            </span>
          </label>

          {enabled && (
            <div>
              <div className="mb-1.5 font-medium">Qaysi lavozimdagilarnikini</div>
              {settings.positions.length === 0 ? (
                <p className="text-[12.5px] text-slate-500">
                  Lavozimlar hali kiritilmagan («Topshiriqlar → Xodimlar → Lavozimlar») — menejer barcha bo'ysunuvchilarnikini ko'radi.
                </p>
              ) : (
                <div className="grid grid-cols-2 gap-1.5">
                  {settings.positions.map((p) => (
                    <label key={p.id} className="flex cursor-pointer items-center gap-2 rounded-md border border-slate-200 px-2 py-1.5 hover:bg-slate-50">
                      <input type="checkbox" className="h-4 w-4" checked={positions.has(p.id)} onChange={() => togglePosition(p.id)} />
                      <span className="truncate">{p.name}</span>
                    </label>
                  ))}
                </div>
              )}
              {settings.positions.length > 0 && (
                <p className="mt-1.5 text-[12px] text-slate-500">Hech biri belgilanmasa — barcha lavozimlar.</p>
              )}
            </div>
          )}
        </div>
      )}
      {error && <p className="mt-3 text-[13px] text-[#d32f2f]">{error}</p>}
    </Modal>
  )
}
