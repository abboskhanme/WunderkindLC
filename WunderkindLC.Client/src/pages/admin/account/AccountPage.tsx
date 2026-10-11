import { PageHeader } from '@/components/ui/PageHeader'
import { MyMcpConnections } from '@/components/settings/MyMcpConnections'
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
        {/* O'z AI ulanishlari (read-only MCP) — faqat ulana oladigan yoki ulanishi borga ko'rinadi. */}
        <MyMcpConnections />
      </div>
    </div>
  )
}
