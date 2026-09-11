import { api } from '../client'
import type { District, School } from '@/types'

/** Barcha tumanlar — har biri ichidagi maktablari bilan. */
export async function getDistricts(): Promise<District[]> {
  const { data } = await api.get<District[]>('/admin/districts')
  return data
}

export async function createDistrict(name: string): Promise<District> {
  const { data } = await api.post<District>('/admin/districts', { name })
  return data
}

export async function updateDistrict(id: string, name: string): Promise<void> {
  await api.put(`/admin/districts/${id}`, { name })
}

export async function deleteDistrict(id: string): Promise<void> {
  await api.delete(`/admin/districts/${id}`)
}

export async function createSchool(districtId: string, name: string): Promise<School> {
  const { data } = await api.post<School>(`/admin/districts/${districtId}/schools`, { name })
  return data
}

export async function updateSchool(id: string, name: string): Promise<void> {
  await api.put(`/admin/schools/${id}`, { name })
}

export async function deleteSchool(id: string): Promise<void> {
  await api.delete(`/admin/schools/${id}`)
}
