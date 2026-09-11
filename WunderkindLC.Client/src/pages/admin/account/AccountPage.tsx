import { PageHeader } from '@/components/ui/PageHeader'
import { AccountSettings } from './AccountSettings'

/** Tepadagi profil menyusidan ochiladigan akkaunt (login/parol) sozlamalari sahifasi. */
export function AccountPage() {
  return (
    <div>
      <PageHeader
        title="Akkaunt sozlamalari"
        sub="Tizimga kirish login (email) va parolingiz"
      />
      <div className="max-w-3xl">
        <AccountSettings />
      </div>
    </div>
  )
}
