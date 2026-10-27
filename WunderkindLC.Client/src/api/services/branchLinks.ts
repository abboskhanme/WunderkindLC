import { api } from '../client'

/**
 * Tepadagi FILIAL ALMASHTIRGICH (faqat superadmin — server HARD rol bilan tekshiradi). Har filial — alohida ilova va
 * alohida baza; bu ro'yxat faqat ular orasidagi HAVOLA (`current` — hozir ochiq filial).
 */
export interface BranchLink {
  name: string
  url: string
  current: boolean
}

export async function getBranchLinks(): Promise<BranchLink[]> {
  const { data } = await api.get<BranchLink[]>('/admin/branch-links')
  return data
}
