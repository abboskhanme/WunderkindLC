import { useEffect, useMemo, useState } from 'react'
import { Plus, Pencil, Trash2, Settings2 } from 'lucide-react'
import {
  getSmsTemplates,
  createSmsTemplate,
  updateSmsTemplate,
  deleteSmsTemplate,
  type SmsTemplate,
} from '@/api/services/messages'
import {
  getAutoMessageRules,
  getMessageTokens,
  type AutoMessageRule,
} from '@/api/services/autoMessages'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

type LibraryItem =
  | { kind: 'manual'; id: string; name: string; text: string; raw: SmsTemplate }
  | { kind: 'auto'; id: string; name: string; text: string; enabled: boolean }

/**
 * "Xabar matnlari" kutubxonasi — FAQAT foydalanuvchi yaratgan matnlarni (qo'lda "Yangi matn"
 * andozalari + "Xabar yaratish" avto-xabar qoidalari matnlari) ko'rsatadi. Oldindan tayyor
 * (hardcode) andozalar KO'RSATILMAYDI — ro'yxat foydalanuvchi yaratmaguncha bo'sh bo'ladi.
 * Bosilganda matn composerga (`onPick`) yuklanadi. Qo'lda yaratilgan matnlarni shu yerda
 * qo'shish/tahrirlash/o'chirish mumkin; avto matnlarda "Sozlash" tugmasi Avto xabarlar
 * tabiga o'tkazadi (`onConfigureAuto`).
 */
export function MessageTemplateLibrary({
  onPick,
  onConfigureAuto,
  currentText,
}: {
  onPick: (text: string, name: string) => void
  onConfigureAuto: (ruleId: string) => void
  currentText?: string
}) {
  const [manual, setManual] = useState<SmsTemplate[]>([])
  const [autoRules, setAutoRules] = useState<AutoMessageRule[]>([])
  const [loading, setLoading] = useState(true)

  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<SmsTemplate | null>(null)
  const [name, setName] = useState('')
  const [text, setText] = useState('')
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState('')
  const [tokens, setTokens] = useState<TokenDef[]>([])

  useEffect(() => {
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group !== 'lead')))
      .catch(() => setTokens([]))
  }, [])

  const reload = () => {
    setLoading(true)
    Promise.all([
      getSmsTemplates().catch(() => []),
      getAutoMessageRules().catch(() => []),
    ])
      .then(([m, a]) => {
        setManual(m)
        setAutoRules(a)
      })
      .finally(() => setLoading(false))
  }

  useEffect(reload, [])

  const items: LibraryItem[] = useMemo(() => {
    const manualItems: LibraryItem[] = manual.map((t) => ({
      kind: 'manual',
      id: t.id,
      name: t.name,
      text: t.text,
      raw: t,
    }))
    const autoItems: LibraryItem[] = autoRules
      .filter((r) => r.template.trim() !== '')
      .map((r) => ({ kind: 'auto', id: r.id, name: r.name, text: r.template, enabled: r.enabled }))
    return [...manualItems, ...autoItems]
  }, [manual, autoRules])

  const openNew = () => {
    setEditing(null)
    setName('')
    setText(currentText ?? '')
    setErr('')
    setModalOpen(true)
  }

  const openEdit = (t: SmsTemplate) => {
    setEditing(t)
    setName(t.name)
    setText(t.text)
    setErr('')
    setModalOpen(true)
  }

  const save = async () => {
    if (!name.trim() || !text.trim() || saving) return
    setSaving(true)
    try {
      if (editing) {
        await updateSmsTemplate(editing.id, { name: name.trim(), text: text.trim() })
      } else {
        await createSmsTemplate({ name: name.trim(), text: text.trim() })
      }
      setModalOpen(false)
      reload()
    } catch (e) {
      setErr(errMsg(e))
    } finally {
      setSaving(false)
    }
  }

  const remove = async (t: SmsTemplate) => {
    if (!window.confirm(`"${t.name}" matnini o'chirasizmi?`)) return
    await deleteSmsTemplate(t.id)
    reload()
  }

  return (
    <Card
      title="Xabar matnlari"
      sub="Tayyor matnlar — bosilganda matn maydoniga yuklanadi"
      actions={
        <Button variant="secondary" onClick={openNew}>
          <Plus className="h-4 w-4" /> Yangi matn
        </Button>
      }
      bodyClassName="space-y-1.5"
    >
      {loading ? (
        <p className="py-6 text-center text-sm text-slate-400">Yuklanmoqda...</p>
      ) : items.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Hali tayyor matn yo'q</p>
      ) : (
        <div className="grid max-h-72 grid-cols-1 items-start gap-1.5 overflow-y-auto pr-1 md:grid-cols-2">
          {items.map((item) => (
            <div
              key={`${item.kind}-${item.id}`}
              className={cn(
                'flex items-center gap-2 rounded-lg border border-slate-100 px-3 py-2 transition-colors hover:border-slate-200',
                item.kind === 'auto' && !item.enabled && 'opacity-60',
              )}
            >
              <button
                type="button"
                onClick={() => onPick(item.text, item.name)}
                className="min-w-0 flex-1 text-left"
              >
                <div className="flex items-center gap-1.5">
                  <span className="truncate text-sm font-medium text-slate-700">{item.name}</span>
                  {item.kind === 'auto' ? (
                    <>
                      <Badge tone="violet">Avto</Badge>
                      {!item.enabled && (
                        <span className="text-[10px] font-medium text-slate-400">(o'chirilgan)</span>
                      )}
                    </>
                  ) : (
                    <Badge tone="default">Qo'lda</Badge>
                  )}
                </div>
                <p className="mt-0.5 line-clamp-1 text-xs text-slate-400">{item.text}</p>
              </button>
              <div className="flex shrink-0 items-center gap-1">
                {item.kind === 'manual' && (
                  <>
                    <button
                      type="button"
                      onClick={() => openEdit(item.raw)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                      title="Tahrirlash"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </button>
                    <button
                      type="button"
                      onClick={() => remove(item.raw)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      title="O'chirish"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </>
                )}
                {item.kind === 'auto' && (
                  <button
                    type="button"
                    onClick={() => onConfigureAuto(item.id)}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2 py-1 text-xs font-medium text-slate-500 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
                  >
                    <Settings2 className="h-3.5 w-3.5" /> Sozlash
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editing ? 'Matnni tahrirlash' : 'Yangi matn'}
        size="md"
        footer={
          <>
            <Button variant="secondary" onClick={() => setModalOpen(false)}>
              Bekor qilish
            </Button>
            <Button onClick={save} disabled={!name.trim() || !text.trim() || saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          {err && <p className="text-sm font-medium text-red-600">{err}</p>}
          <Input
            label="Nom"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="masalan: Qarzdorlik eslatmasi"
          />
          <MessageEditor
            label="Matn"
            value={text}
            onChange={setText}
            tokens={tokens}
            rows={5}
            placeholder="Matn (o'rinbosarlar bilan, masalan {fish}, {qarzdorlik})"
          />
        </div>
      </Modal>
    </Card>
  )
}

/** Xato xabarini axios javobidan ajratadi. */
function errMsg(e: unknown): string {
  return (
    (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    "Saqlab bo'lmadi"
  )
}
