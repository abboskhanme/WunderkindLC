import { useEffect, useState } from 'react'
import { FileText, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createMessagingTemplate,
  deleteMessagingTemplate,
  getMessagingTemplates,
  updateMessagingTemplate,
  type MessagingTemplate,
} from '@/api/services/messaging'
import { getMessageTokens } from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { TelegramPreview } from '@/components/messaging/TelegramPreview'
import { useMessagePreview } from '@/components/messaging/useTelegramDraft'
import { telegramPlain } from '@/lib/telegramHtml'
import { apiErrorMessage } from '@/lib/utils'

/**
 * «Shablonlar» (CRM upgrade P2.8) — qo'lda yuboriladigan tayyor matnlar (`messaging/templates`).
 * Kompozitorda va qarz eslatmasida «Shablondan» tanlanadi. Server matnni formatlash va o'zgaruvchilar
 * bo'yicha tekshiradi (xato — oynada, matn saqlanib qoladi).
 */
export function TemplatesTab({ canCreate, canEdit, canDelete }: { canCreate: boolean; canEdit: boolean; canDelete: boolean }) {
  const [items, setItems] = useState<MessagingTemplate[] | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [modal, setModal] = useState<MessagingTemplate | 'new' | null>(null)
  const [tokens, setTokens] = useState<TokenDef[]>([])

  useEffect(() => {
    let alive = true
    getMessagingTemplates()
      .then((t) => alive && setItems(t))
      .catch((e) => alive && setError(apiErrorMessage(e, "Shablonlarni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [tick])

  useEffect(() => {
    getMessageTokens().then((ts) => setTokens(ts.filter((t) => t.group !== 'lead'))).catch(() => setTokens([]))
  }, [])

  const remove = async (t: MessagingTemplate) => {
    if (!confirm(`"${t.name}" shabloni o'chirilsinmi?`)) return
    try {
      await deleteMessagingTemplate(t.id)
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "O'chirib bo'lmadi"))
    }
  }

  return (
    <Card
      title="Shablonlar"
      sub="Qo'lda yuboriladigan tayyor matnlar — «Xabar yuborish»da tanlanadi"
      actions={
        canCreate ? (
          <Button onClick={() => setModal('new')}>
            <Plus className="h-4 w-4" /> Yangi shablon
          </Button>
        ) : undefined
      }
    >
      {error && <p className="mb-3 text-sm font-medium text-red-600">{error}</p>}
      {items === null && !error ? (
        <Loader label="Yuklanmoqda..." />
      ) : (items ?? []).length === 0 ? (
        <div className="py-10 text-center">
          <FileText className="mx-auto mb-2 h-6 w-6 text-slate-300" />
          <p className="text-sm text-slate-400">Hali shablon yo'q</p>
        </div>
      ) : (
        <ul className="divide-y divide-slate-100">
          {(items ?? []).map((t) => (
            <li key={t.id} className="flex items-start gap-3 py-2.5">
              <div className="min-w-0 flex-1">
                <p className="font-medium text-slate-700">{t.name}</p>
                <p className="line-clamp-2 text-xs text-slate-500">{telegramPlain(t.text)}</p>
              </div>
              {canEdit && (
                <button
                  type="button"
                  onClick={() => setModal(t)}
                  className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                  title="Tahrirlash"
                >
                  <Pencil className="h-4 w-4" />
                </button>
              )}
              {canDelete && (
                <button
                  type="button"
                  onClick={() => remove(t)}
                  className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600"
                  title="O'chirish"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {modal && (
        <TemplateModal
          key={modal === 'new' ? 'new' : modal.id}
          item={modal === 'new' ? null : modal}
          tokens={tokens}
          onClose={() => setModal(null)}
          onSaved={() => {
            setModal(null)
            setTick((x) => x + 1)
          }}
        />
      )}
    </Card>
  )
}

function TemplateModal({
  item,
  tokens,
  onClose,
  onSaved,
}: {
  item: MessagingTemplate | null
  tokens: TokenDef[]
  onClose: () => void
  onSaved: () => void
}) {
  const [name, setName] = useState(item?.name ?? '')
  const [text, setText] = useState(item?.text ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const { preview, loading } = useMessagePreview({ template: text })

  const save = async () => {
    if (!name.trim() || !text.trim()) {
      setError('Nom va matn kerak')
      return
    }
    setSaving(true)
    setError('')
    try {
      if (item) await updateMessagingTemplate(item.id, { name: name.trim(), text })
      else await createMessagingTemplate({ name: name.trim(), text })
      onSaved()
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={item ? 'Shablonni tahrirlash' : 'Yangi shablon'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <div className="space-y-3">
          <Input label="Nom" value={name} onChange={(e) => setName(e.target.value)} />
          <MessageEditor
            formatting
            label="Matn"
            value={text}
            onChange={setText}
            tokens={tokens}
            rows={8}
            error={(preview?.errors ?? []).join('; ') || null}
          />
          {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        </div>
        <TelegramPreview html={preview?.html ?? ''} mode={preview?.mode ?? 'text'} loading={loading} />
      </div>
    </Modal>
  )
}
