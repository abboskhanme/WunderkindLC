import { api } from '../client'

export interface Room {
  id: string
  name: string
  capacity: number
  building?: string
  location?: string
  isActive: boolean
  createdAt: string
}

export interface RoomUtilization {
  roomId: string
  roomName: string
  capacity: number
  currentStudents: number
  totalSlots?: number
  gap?: number
  groupCount?: number
  occupancyPercent: number
  activeGroupCount: number
  weeklyActiveHours: number
  weeklyUtilizationPercent: number
  efficiencyScore: number
  efficiencyStatus: string
  building?: string
  location?: string
  groupNames?: string[]
}

export interface CreateRoomPayload {
  name: string
  capacity: number
  building?: string
  location?: string
}

export async function getRooms(): Promise<Room[]> {
  const { data } = await api.get<Room[]>('/admin/rooms')
  return data
}

export async function createRoom(payload: CreateRoomPayload): Promise<Room> {
  const { data } = await api.post<Room>('/admin/rooms', payload)
  return data
}

export async function updateRoom(id: string, payload: CreateRoomPayload): Promise<Room> {
  const { data } = await api.put<Room>(`/admin/rooms/${id}`, payload)
  return data
}

export async function deleteRoom(id: string): Promise<void> {
  await api.delete(`/admin/rooms/${id}`)
}

export async function getRoomUtilizationDashboard(): Promise<RoomUtilization[]> {
  const { data } = await api.get<RoomUtilization[]>('/admin/rooms/utilization-dashboard')
  return data
}

export interface RoomGroupSlot {
  groupId: string
  groupName: string
  studentCount: number
  courseName?: string
}

export interface RoomCapacityMetric {
  roomId: string
  roomName: string
  capacity: number
  groupCount: number
  totalSlots: number
  actualStudents: number
  utilizationPercent: number
  gap: number
  status: string
  groups: RoomGroupSlot[]
}

export async function getRoomCapacity(roomId: string): Promise<RoomCapacityMetric> {
  const { data } = await api.get<RoomCapacityMetric>(`/admin/rooms/${roomId}/capacity`)
  return data
}

export interface RoomGroupDetail {
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
  studentCount: number
  studentCapacity: number
  utilizationPercent: number
  days: string
  timeSlot: string
}

export interface RoomDetailMetric {
  roomId: string
  roomName: string
  building: string
  location: string
  capacity: number
  groupCount: number
  totalSlots: number
  actualStudents: number
  occupancyPercent: number
  utilizationPercent: number
  weeklyUtilizationPercent: number
  weeklyActiveHours: number
  efficiencyScore: number
  efficiencyStatus: string
  gap: number
  groups: RoomGroupDetail[]
}

export async function getRoomDetail(roomId: string): Promise<RoomDetailMetric> {
  const { data } = await api.get<RoomDetailMetric>(`/admin/rooms/${roomId}/detail`)
  return data
}
