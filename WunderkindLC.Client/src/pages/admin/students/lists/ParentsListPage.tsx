import { getStudents } from '@/api/services/students'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { StudentListBoard, balanceColumn, nameColumn } from './StudentListBoard'

/**
 * O'quvchilar → «Ota-ona» (edutizim `/students/parent`): har o'quvchining OTA-ONASI — ism,
 * telefon va balans.
 *
 * ⚠️ Bizdagi eski `/admin/parents` sahifasi boshqa narsa (ota-onaning ILOVA akkaunti: qurilma,
 * oxirgi kirish) — u "Future → Ilova" bo'limida qoldi.
 */
export function ParentsListPage() {
  return (
    <StudentListBoard
      load={() => getStudents()}
      columns={[
        nameColumn,
        {
          key: 'father',
          header: 'Otasining ismi',
          render: (s) => s.fatherFullName || <span className="text-[#9ca3af]">—</span>,
        },
        { key: 'fatherPhone', header: 'Telefon raqam', render: (s) => <PhoneChip phone={s.fatherPhone} /> },
        {
          key: 'mother',
          header: 'Onasining ismi',
          render: (s) => s.motherFullName || <span className="text-[#9ca3af]">—</span>,
        },
        { key: 'motherPhone', header: 'Telefon raqam', render: (s) => <PhoneChip phone={s.motherPhone} /> },
        {
          key: 'parent',
          header: 'Ota-ona (asosiy)',
          render: (s) => s.parentFullName || <span className="text-[#9ca3af]">—</span>,
        },
        { key: 'parentPhone', header: 'Telefon raqam', render: (s) => <PhoneChip phone={s.parentPhone} /> },
        balanceColumn,
      ]}
    />
  )
}
