import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, Building2, MapPin } from 'lucide-react'
import type { Room } from '@/types'
import type { CreateRoomPayload } from '@/api/services/rooms'
import { getRooms, createRoom, updateRoom, deleteRoom } from '@/api/services/rooms'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { roomTabs } from '@/config/sectionTabs'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

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

  return (
    <div className="space-y-6">
      <CardTabs items={roomTabs} />
      <PageHeader
        title="Xonalar"
        sub={`Jami ${activeRooms.length} ta faol xona`}
        actions={
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" />
            Yangi xona
          </Button>
        }
      />

      {activeRooms.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <Building2 className="h-12 w-12 text-slate-300" />
            <p className="text-sm text-slate-500">Hali xona qo'shilmagan</p>
            <Button onClick={openCreate} className="text-xs">
              <Plus className="h-3.5 w-3.5" />
              Birinchi xonani qo'shish
            </Button>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {activeRooms.map((room) => (
            <RoomCard
              key={room.id}
              room={room}
              onEdit={() => openEdit(room)}
              onDelete={() => setDeleteConfirm(room)}
            />
          ))}
        </div>
      )}

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

interface RoomCardProps {
  room: Room
  onEdit: () => void
  onDelete: () => void
}

function RoomCard({ room, onEdit, onDelete }: RoomCardProps) {
  const capacityColor =
    room.capacity >= 30
      ? 'text-emerald-600 bg-emerald-50'
      : room.capacity >= 15
        ? 'text-amber-600 bg-amber-50'
        : 'text-slate-600 bg-slate-100'

  return (
    <div className="entity-card group relative flex flex-col gap-3 p-5">
      <div className="flex items-start justify-between gap-2">
        <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
          <Building2 className="h-5 w-5" />
        </div>
        <div className="flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
          <button
            onClick={onEdit}
            className="flex h-7 w-7 items-center justify-center rounded-lg border border-slate-200 text-slate-400 hover:bg-slate-50 hover:text-slate-700"
          >
            <Pencil className="h-3.5 w-3.5" />
          </button>
          <button
            onClick={onDelete}
            className="flex h-7 w-7 items-center justify-center rounded-lg border border-red-100 text-red-400 hover:bg-red-50 hover:text-red-600"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <div>
        <h3 className="text-base font-semibold text-slate-800">{room.name}</h3>
        {room.building && (
          <p className="mt-0.5 flex items-center gap-1 text-xs text-slate-400">
            <Building2 className="h-3 w-3" />
            {room.building}
          </p>
        )}
        {room.location && (
          <p className="mt-0.5 flex items-center gap-1 text-xs text-slate-400">
            <MapPin className="h-3 w-3" />
            {room.location}
          </p>
        )}
      </div>

      <div className="mt-auto flex items-center justify-between border-t border-slate-100 pt-3">
        <span className="text-xs text-slate-500">Sig'im</span>
        <span className={cn('rounded-full px-2.5 py-0.5 text-sm font-semibold', capacityColor)}>
          {room.capacity} kishi
        </span>
      </div>
    </div>
  )
}
