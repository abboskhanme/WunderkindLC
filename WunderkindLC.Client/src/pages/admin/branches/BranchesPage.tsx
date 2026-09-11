import { useEffect, useState } from 'react'
import { MapContainer, TileLayer, Marker, Circle, useMapEvents, useMap } from 'react-leaflet'
import L from 'leaflet'
// Leaflet CSS shu yerda — global index.css'da EMAS (faqat xarita sahifalariga kerak)
import 'leaflet/dist/leaflet.css'
import { Plus, Pencil, Trash2, MapPin, LocateFixed } from 'lucide-react'
import type { Branch } from '@/types'
import {
  getBranches,
  createBranch,
  updateBranch,
  deleteBranch,
  type BranchPayload,
} from '@/api/services/branches'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'

// Leaflet marker ikoni Vite bilan to'g'ri yuklanmaydi — CDN'dan ko'rsatamiz (LocationPage'dagidek).
const markerIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
})

// Toshkent markazi — yangi filial uchun standart ko'rinish.
const DEFAULT_CENTER: [number, number] = [41.311081, 69.279737]

const empty: BranchPayload = { name: '', address: '', latitude: 0, longitude: 0, radiusMeters: 100 }

export function BranchesPage() {
  const [branches, setBranches] = useState<Branch[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Branch | null>(null)
  const [form, setForm] = useState<BranchPayload>(empty)
  // Joriy joyni aniqlash holati + xaritani markazlash uchun nishon nuqta
  const [locating, setLocating] = useState(false)
  const [flyTo, setFlyTo] = useState<[number, number] | null>(null)

  useEffect(() => {
    getBranches()
      .then(setBranches)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm(empty)
    setFlyTo(null)
    setFormOpen(true)
  }
  const openEdit = (b: Branch) => {
    setEditing(b)
    setForm({
      name: b.name,
      address: b.address,
      latitude: b.latitude,
      longitude: b.longitude,
      radiusMeters: b.radiusMeters,
    })
    setFlyTo(null)
    setFormOpen(true)
  }

  // Brauzer GPS'idan joriy joyni olib, formaga qo'yadi va xaritani o'sha yerga markazlaydi.
  const useCurrentLocation = () => {
    if (!('geolocation' in navigator)) {
      alert("Brauzer joylashuvni qo'llab-quvvatlamaydi.")
      return
    }
    setLocating(true)
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        const { latitude, longitude } = pos.coords
        setForm((f) => ({ ...f, latitude, longitude }))
        setFlyTo([latitude, longitude])
        setLocating(false)
      },
      (err) => {
        setLocating(false)
        alert(
          err.code === err.PERMISSION_DENIED
            ? "Joylashuvga ruxsat berilmadi. Brauzer sozlamalaridan ruxsat bering."
            : "Joriy joyni aniqlab bo'lmadi. Qaytadan urinib ko'ring.",
        )
      },
      { enableHighAccuracy: true, timeout: 10000 },
    )
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    if (editing) {
      updateBranch(editing.id, form).then((u) =>
        setBranches((p) => p.map((x) => (x.id === u.id ? u : x))),
      )
    } else {
      createBranch(form).then((c) => setBranches((p) => [...p, c]))
    }
    setFormOpen(false)
  }

  const handleDelete = (b: Branch) => {
    if (!confirm(`"${b.name}" filialini o'chirasizmi?`)) return
    deleteBranch(b.id).then(() => setBranches((p) => p.filter((x) => x.id !== b.id)))
  }

  const hasPoint = form.latitude !== 0 || form.longitude !== 0

  return (
    <div>
      <PageHeader
        title="Filiallar"
        sub="Filial nomi, manzil, joylashuv (xarita) va radius"
        actions={
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi filial
          </Button>
        }
      />

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : branches.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <MapPin className="h-6 w-6" />
            </div>
            <h4>Hali filial qo'shilmagan</h4>
            <p>"Yangi filial" tugmasi orqali qo'shing.</p>
          </div>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {branches.map((b) => (
            <Card key={b.id} className="flex flex-col gap-2">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="truncate font-semibold text-slate-800">{b.name}</p>
                  <p className="text-sm text-slate-500">{b.address || '—'}</p>
                </div>
                <div className="flex items-center gap-0.5">
                  <button
                    type="button"
                    title="Tahrirlash"
                    onClick={() => openEdit(b)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={() => handleDelete(b)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs text-slate-400">
                <span className="inline-flex items-center gap-1">
                  <MapPin className="h-3.5 w-3.5" />
                  <span className="font-mono">
                    {b.latitude.toFixed(5)}, {b.longitude.toFixed(5)}
                  </span>
                </span>
                <Badge tone="violet">
                  radius <span className="font-mono">{b.radiusMeters}</span> m
                </Badge>
              </div>
              <div className="h-40 overflow-hidden rounded-lg">
                <MapContainer
                  center={[b.latitude || DEFAULT_CENTER[0], b.longitude || DEFAULT_CENTER[1]]}
                  zoom={14}
                  scrollWheelZoom={false}
                  style={{ height: '100%', width: '100%' }}
                >
                  <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
                  {(b.latitude !== 0 || b.longitude !== 0) && (
                    <>
                      <Marker position={[b.latitude, b.longitude]} icon={markerIcon} />
                      <Circle center={[b.latitude, b.longitude]} radius={b.radiusMeters} />
                    </>
                  )}
                </MapContainer>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        size="lg"
        title={editing ? 'Filialni tahrirlash' : 'Yangi filial'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="branch-form">
              Saqlash
            </Button>
          </>
        }
      >
        <form id="branch-form" onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Filial nomi"
            required
            value={form.name}
            onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          />
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => setForm((f) => ({ ...f, address: e.target.value }))}
          />
          <Input
            label="Radius (metr)"
            type="number"
            min={10}
            step={10}
            value={form.radiusMeters}
            onChange={(e) => setForm((f) => ({ ...f, radiusMeters: Number(e.target.value) }))}
          />
          <div>
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
              <label className="block text-sm font-medium text-slate-600">
                Joylashuv — xaritani bosing yoki markerni suring
              </label>
              <Button
                type="button"
                variant="secondary"
                onClick={useCurrentLocation}
                disabled={locating}
              >
                <LocateFixed className="h-4 w-4" />
                {locating ? 'Aniqlanmoqda...' : 'Joriy joyni tanlash'}
              </Button>
            </div>
            <div className="h-72 overflow-hidden rounded-lg border border-slate-200">
              <MapContainer
                key={editing?.id ?? 'new'}
                center={hasPoint ? [form.latitude, form.longitude] : DEFAULT_CENTER}
                zoom={14}
                style={{ height: '100%', width: '100%' }}
              >
                <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
                <FlyTo target={flyTo} />
                <ClickPicker onPick={(lat, lng) => setForm((f) => ({ ...f, latitude: lat, longitude: lng }))} />
                {hasPoint && (
                  <>
                    <Marker
                      position={[form.latitude, form.longitude]}
                      icon={markerIcon}
                      draggable
                      eventHandlers={{
                        dragend: (e) => {
                          const m = e.target.getLatLng()
                          setForm((f) => ({ ...f, latitude: m.lat, longitude: m.lng }))
                        },
                      }}
                    />
                    <Circle center={[form.latitude, form.longitude]} radius={form.radiusMeters} />
                  </>
                )}
              </MapContainer>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              {hasPoint
                ? `Tanlangan: ${form.latitude.toFixed(5)}, ${form.longitude.toFixed(5)}`
                : 'Joylashuv tanlanmagan — xaritani bosing.'}
            </p>
          </div>
        </form>
      </Modal>
    </div>
  )
}

function ClickPicker({ onPick }: { onPick: (lat: number, lng: number) => void }) {
  useMapEvents({
    click(e) {
      onPick(e.latlng.lat, e.latlng.lng)
    },
  })
  return null
}

// Nishon nuqta o'zgarganda xaritani o'sha joyga markazlaydi (masalan "Joriy joyni tanlash" bosilganda).
function FlyTo({ target }: { target: [number, number] | null }) {
  const map = useMap()
  useEffect(() => {
    if (target) map.setView(target, 16)
  }, [target, map])
  return null
}
