import { useEffect, useMemo, useState } from 'react'
import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet'
import L from 'leaflet'
// Leaflet CSS shu yerda — global index.css'da EMAS (faqat xarita sahifalariga kerak)
import 'leaflet/dist/leaflet.css'
import { MapPin } from 'lucide-react'
import type { Group, StudentLocationRow } from '@/types'
import { getStudentLocations } from '@/api/services/locations'
import { getClasses } from '@/api/services/classes'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge } from '@/components/ui/Badge'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatDate, cn } from '@/lib/utils'

// Leaflet default marker ikon Vite/bundler bilan to'g'ri yuklanmaydi — qo'lda CDN ko'rsatamiz.
// (Aks holda pin ko'rinmaydi.)
const defaultIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41],
})

/**
 * Admin "Joylashuv" sahifasi — o'quvchilar mobil ilova orqali yuborgan uy joylashuvi xaritada.
 * Pin'lar bosilsa: F.I.SH, guruh, manzil, yangilangan vaqt. Guruhga ko'ra filtr.
 */
export function LocationPage() {
  const [rows, setRows] = useState<StudentLocationRow[]>([])
  const [classes, setClasses] = useState<Group[]>([])
  const [loading, setLoading] = useState(true)
  const [classFilter, setClassFilter] = useState('all')

  useEffect(() => {
    setLoading(true)
    Promise.all([getStudentLocations(), getClasses()])
      .then(([r, c]) => {
        setRows(r)
        setClasses(c)
      })
      .finally(() => setLoading(false))
  }, [])

  const filtered = useMemo(
    () => (classFilter === 'all' ? rows : rows.filter((r) => r.className === classFilter)),
    [rows, classFilter],
  )

  // Xarita markazi — birinchi pin yoki Toshkent (fallback).
  const center: [number, number] = filtered.length > 0
    ? [filtered[0].latitude, filtered[0].longitude]
    : [41.2995, 69.2401] // Toshkent

  // Guruhlar bo'yicha hisob — pastdagi statistika uchun.
  const byClass = useMemo(() => {
    const map = new Map<string, number>()
    rows.forEach((r) => map.set(r.className, (map.get(r.className) ?? 0) + 1))
    return [...map.entries()].sort((a, b) => b[1] - a[1])
  }, [rows])

  return (
    <div>
      <PageHeader
        title="Joylashuv"
        sub={
          <>
            O'quvchilar mobil ilova orqali yuborgan uy joylashuvi —{' '}
            <b className="font-mono text-slate-600">{filtered.length}</b> ta pin
          </>
        }
        actions={
          <Select
            value={classFilter}
            onChange={(e) => setClassFilter(e.target.value)}
            className="min-w-[160px]"
          >
            <option value="all">Barcha guruhlar</option>
            {classes.map((c) => (
              <option key={c.id} value={c.name}>
                {c.name}
              </option>
            ))}
          </Select>
        }
      />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <MapPin className="h-6 w-6" />
            </div>
            <h4>Joylashuv yo'q</h4>
            <p>Hozircha hech bir o'quvchi mobil ilova orqali joylashuv yubormagan.</p>
            <p className="text-xs">
              O'quvchilar ilovaga kirib "Joylashuvni saqlash" bosgach, bu yerda pin ko'rinadi.
            </p>
          </div>
        </Card>
      ) : (
        <div className="space-y-6">
          <Card tight className="overflow-hidden">
            <div style={{ height: 500 }}>
              <MapContainer
                center={center}
                zoom={12}
                style={{ height: '100%', width: '100%' }}
                key={`${classFilter}-${filtered.length}`}
              >
                <TileLayer
                  attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                />
                {filtered.map((r) => (
                  <Marker
                    key={r.studentId}
                    position={[r.latitude, r.longitude]}
                    icon={defaultIcon}
                  >
                    <Popup>
                      <div className="text-sm">
                        <p className="font-semibold text-slate-800">{r.fullName}</p>
                        <p className="text-slate-500">{r.className}</p>
                        {r.address && <p className="mt-1 text-xs text-slate-600">{r.address}</p>}
                        {r.updatedAt && (
                          <p className="mt-1 font-mono text-[11px] text-slate-400">
                            Yangilangan: {formatDate(r.updatedAt.slice(0, 10))}
                          </p>
                        )}
                        <a
                          href={`https://www.google.com/maps?q=${r.latitude},${r.longitude}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="mt-1 inline-block text-xs text-brand-600 hover:underline"
                        >
                          Google Maps'da ochish →
                        </a>
                      </div>
                    </Popup>
                  </Marker>
                ))}
              </MapContainer>
            </div>
          </Card>

          {/* Guruh bo'yicha hisob */}
          {byClass.length > 0 && (
            <Card title="Guruh bo'yicha joylashuv ulushi">
              <div className="flex flex-wrap gap-2">
                {byClass.map(([name, count]) => (
                  <button
                    key={name}
                    type="button"
                    onClick={() => setClassFilter(name)}
                    className={cn(
                      'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold transition-colors',
                      classFilter === name
                        ? 'border-transparent bg-brand-50 text-brand-700'
                        : 'border-slate-200 bg-white text-slate-600 hover:border-brand-200',
                    )}
                  >
                    {name} <span className="font-mono">· {count}</span>
                  </button>
                ))}
              </div>
            </Card>
          )}

          {/* Jadval */}
          <Card tight>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>F.I.SH</th>
                    <th>Guruh</th>
                    <th>Manzil</th>
                    <th>Koordinata</th>
                    <th>Yangilangan</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((r) => (
                    <tr key={r.studentId}>
                      <td className="font-medium text-slate-800">{r.fullName}</td>
                      <td>
                        <Badge tone="default">{r.className}</Badge>
                      </td>
                      <td className="max-w-[24rem] truncate text-slate-600" title={r.address ?? ''}>
                        {r.address || '—'}
                      </td>
                      <td className="font-mono text-xs text-slate-500">
                        {r.latitude.toFixed(5)}, {r.longitude.toFixed(5)}
                      </td>
                      <td className="font-mono text-xs text-slate-500">
                        {r.updatedAt ? formatDate(r.updatedAt.slice(0, 10)) : '—'}
                      </td>
                      <td className="text-right">
                        <a
                          href={`https://www.google.com/maps?q=${r.latitude},${r.longitude}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-xs font-semibold text-brand-600 hover:text-brand-700"
                        >
                          Xaritada →
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      )}
    </div>
  )
}
