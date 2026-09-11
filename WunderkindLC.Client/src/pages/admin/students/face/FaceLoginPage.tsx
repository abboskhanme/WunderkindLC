import { useCallback, useEffect, useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import { ScanFace, Settings, Smartphone } from 'lucide-react'
import { getFacePendingCount } from '@/api/services/face'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { FaceChecksTab } from './FaceChecksTab'
import { FaceDevicesTab } from './FaceDevicesTab'
import { FaceSettingsTab } from './FaceSettingsTab'

type Tab = 'checks' | 'devices' | 'settings'

/**
 * YUZ BILAN KIRISH — bitta sahifa, 3 tab:
 *  • Urinishlar — o'quvchi ilovasidan kelgan selfi tekshiruvlari; kutilayotganini admin
 *    tasdiqlaydi (tasdiqlangan selfi ETALON bo'ladi) yoki rad etadi;
 *  • Qurilmalar — bir marta tasdiqlangan telefonlar (ularda selfi qayta so'ralmaydi) va
 *    ularni bekor qilish + o'quvchining yuz etalonini tozalash;
 *  • Sozlamalar — modulni yoqish, o'xshashlik chegarasi, model versiyasi, saqlanadigan
 *    selfilar soni (maxfiylik).
 *
 * Yozish amallari `students:edit` bilan darvozalangan; sahifaning O'ZI `students` ruxsati
 * bilan (marshrutda `RequirePerm`), chunki javobda selfi manzillari qaytadi.
 */
export function FaceLoginPage() {
  const { can } = usePerm()
  const canEdit = can('students.face', 'edit')
  const [tab, setTab] = useState<Tab>('checks')
  const [pending, setPending] = useState({ count: 0, atLimit: false })

  const refreshPending = useCallback(() => {
    getFacePendingCount()
      .then(setPending)
      .catch(() => setPending({ count: 0, atLimit: false }))
  }, [])

  useEffect(refreshPending, [refreshPending])

  return (
    <div>
      <PageHeader
        title="Yuz bilan kirish"
        sub="O'quvchi ilovaga yangi qurilmadan kirganda selfi tekshiruvi"
        actions={
          <div className="tabs">
            <TabButton active={tab === 'checks'} onClick={() => setTab('checks')} icon={ScanFace}>
              <span className="inline-flex items-center gap-1.5">
                Urinishlar
                {pending.count > 0 && (
                  <span className="rounded-full bg-red-500 px-1.5 py-px text-[11px] font-bold text-white">
                    {/* Server bir so'rovda 500 ta beradi — undan ko'p bo'lsa aniq son noma'lum. */}
                    {pending.atLimit ? `${pending.count}+` : pending.count}
                  </span>
                )}
              </span>
            </TabButton>
            <TabButton active={tab === 'devices'} onClick={() => setTab('devices')} icon={Smartphone}>
              Qurilmalar
            </TabButton>
            <TabButton active={tab === 'settings'} onClick={() => setTab('settings')} icon={Settings}>
              Sozlamalar
            </TabButton>
          </div>
        }
      />

      {tab === 'checks' ? (
        <FaceChecksTab canDecide={canEdit} onDecided={refreshPending} />
      ) : tab === 'devices' ? (
        <FaceDevicesTab canEdit={canEdit} />
      ) : (
        <FaceSettingsTab canEdit={canEdit} />
      )}
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: LucideIcon
  children: React.ReactNode
}

function TabButton({ active, onClick, icon: Icon, children }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('tab inline-flex items-center gap-1.5', active && 'active')}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
