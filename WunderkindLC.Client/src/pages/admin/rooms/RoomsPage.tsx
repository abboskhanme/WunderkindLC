import { useEffect, useState } from 'react'
import { Building2, MapPin } from 'lucide-react'
import { IconPencil, IconSearch, IconTrash } from '@tabler/icons-react'
import type { Room } from '@/types'
import type { CreateRoomPayload } from '@/api/services/rooms'
import { getRooms, createRoom, updateRoom, deleteRoom } from '@/api/services/rooms'
import { Button } from '@/components/ui/Button'
import { CardTabs } from '@/components/ui/CardTabs'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { roomTabs } from '@/config/sectionTabs'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'

const emptyForm: CreateRoomPayload = {
  name: '',
  capacity: 20,
  building: '',
  location: '',
}

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Room | null>(null)
  const [form, setForm] = useState<CreateRoomPayload>(emptyForm)
  const [saving, setSaving] = useState(false)
  const [deleteConfirm, setDeleteConfirm] = useState<Room | null>(null)
  const [query, setQuery] = useState('')

  useEffect(() => {
    getRooms()
      .then(setRooms)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm(emptyForm)
    setFormOpen(true)
  }

  const openEdit = (room: Room) => {
    setEditing(room)
    setForm({
      name: room.name,
      capacity: room.capacity,
      building: room.building ?? '',
      location: room.location ?? '',
    })
    setFormOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    if (form.capacity <= 0) return
    setSaving(true)
    try {
      if (editing) {
        const updated = await updateRoom(editing.id, form)
        setRooms((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      } else {
        const created = await createRoom(form)
        setRooms((prev) => [...prev, created])
      }
      setFormOpen(false)
    } catch {
      alert("Xatolik yuz berdi. Qaytadan urinib ko'ring.")
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (room: Room) => {
    try {
      await deleteRoom(room.id)
      setRooms((prev) => prev.filter((r) => r.id !== room.id))
    } catch {
      alert("O'chirib bo'lmadi — ehtimol xonaga guruhlar biriktirilgan.")
    } finally {
      setDeleteConfirm(null)
    }
  }

  const update = <K extends keyof CreateRoomPayload>(key: K, value: CreateRoomPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  // Cardlar yuklanish paytida ham TURADI — aks holda sahifa ochilganda ular bir lahzaga
  // yo'qolib, foydalanuvchi ma'lumot kelguncha boshqa bo'limga o'ta olmasdi.
  if (loading)
    return (
      <div className="space-y-6">
        <CardTabs items={roomTabs} />
        <Loader />
      </div>
    )

  const activeRooms = rooms.filter((r) => r.isActive)
  // Qidiruv — nom, bino va joylashuv bo'yicha (edutizim ro'yxatidagi "Qidirish").
  // ⚠️ `useMemo` ISHLATILMAYDI: bu yer yuqoridagi `if (loading) return ...` dan KEYIN turadi,
  // ya'ni hook shartli chaqirilardi (React qoidasi buziladi — sahifa oq bo'lib qolardi).
  const q = query.trim().toLowerCase()
  const shown = q
    ? activeRooms.filter((r) =>
        [r.name, r.building, r.location].filter(Boolean).join(' ').toLowerCase().includes(q),
      )
    : activeRooms

  return (
    <div className="space-y-2.5">
      <CardTabs items={roomTabs} />

      <ListToolbar
        addLabel="Xona qo'shish"
        onAdd={openCreate}
        left={
          <div className="relative w-full max-w-[260px]">
            <IconSearch className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Qidirish"
              className="h-[34px] w-full rounded-lg border border-black/25 bg-white pl-8 pr-3 text-[13px] outline-none focus:border-brand-600"
            />
          </div>
        }
      />

      <div className="flex justify-end">
        <TotalPill total={shown.length} />
      </div>

      <DataTable
        rows={shown}
        rowKey={(r) => r.id}
        numbered
        columns={[
          { key: 'name', header: 'Sarlavha', render: (r) => <span className="font-medium">{r.name}</span> },
          { key: 'capacity', header: "O'quvchi sig'imi", align: 'right', render: (r) => r.capacity },
          {
            key: 'building',
            header: 'Bino / qavat',
            render: (r) =>
              r.building ? (
                <span className="inline-flex items-center gap-1.5 text-[#333]">
                  <Building2 className="h-4 w-4 text-[#9ca3af]" />
                  {r.building}
                </span>
              ) : (
                <span className="text-[#9ca3af]">—</span>
              ),
          },
          {
            key: 'location',
            header: 'Joylashuv',
            render: (r) =>
              r.location ? (
                <span className="inline-flex items-center gap-1.5 text-[#333]">
                  <MapPin className="h-4 w-4 text-[#9ca3af]" />
                  {r.location}
                </span>
              ) : (
                <span className="text-[#9ca3af]">—</span>
              ),
          },
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (r) => (
              <div className="flex justify-end gap-1.5">
                <TintedIconButton label="Tahrirlash" onClick={() => openEdit(r)}>
                  <IconPencil className="h-4 w-4" />
                </TintedIconButton>
                <TintedIconButton
                  label="O'chirish"
                  className="border-[#e34a29]/20 bg-[#e34a29]/10 text-[#e34a29] hover:bg-[#e34a29]/15"
                  onClick={() => setDeleteConfirm(r)}
                >
                  <IconTrash className="h-4 w-4" />
                </TintedIconButton>
              </div>
            ),
          },
        ]}
      />

      {/* Yaratish / tahrirlash modali */}
      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? 'Xonani tahrirlash' : 'Yangi xona'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="room-form" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <form id="room-form" onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Xona nomi *"
            required
            placeholder="Masalan: 301"
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
          />
          <Input
            label="Sig'im (o'quvchilar soni) *"
            type="number"
            min={1}
            max={1000}
            required
            value={form.capacity}
            onChange={(e) => update('capacity', Number(e.target.value))}
          />
          <Input
            label="Bino / qavat"
            placeholder="Masalan: A-blok, 3-qavat"
            value={form.building ?? ''}
            onChange={(e) => update('building', e.target.value)}
          />
          <Input
            label="Joylashuv / manzil"
            placeholder="Masalan: Shimoliy qanot"
            value={form.location ?? ''}
            onChange={(e) => update('location', e.target.value)}
          />
        </form>
      </Modal>

      {/* O'chirish tasdiqi */}
      <Modal
        open={deleteConfirm !== null}
        onClose={() => setDeleteConfirm(null)}
        title="Xonani o'chirish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleteConfirm(null)}>
              Bekor qilish
            </Button>
            <Button
              variant="danger"
              onClick={() => deleteConfirm && handleDelete(deleteConfirm)}
            >
              O'chirish
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          <strong>{deleteConfirm?.name}</strong> xonasini o'chirmoqchimisiz?
          Agar xonaga guruhlar biriktirilgan bo'lsa, o'chirib bo'lmaydi.
        </p>
      </Modal>
    </div>
  )
}

