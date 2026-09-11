// `src/api/cache.ts` — SHAFFOF GET-KESH va uning MIQYOSLI (scoped) invalidatsiyasi.
//
// Nima qulflanadi:
//  1. miqyos hisoblash (`scopeOf`) va bo'lak-darajali moslik (`keyInScope`);
//  2. kesishma jadvali: to'lov o'quvchilar ro'yxatini ham tashlashi SHART, aks holda
//     kassir eskirgan BALANS ko'radi (modulning eng qimmat xatosi);
//  3. whitelist standarti: jadvalda yo'q miqyos → BUTUN kesh tozalanadi;
//  4. adapter darajasidagi haqiqiy xatti-harakat (kesh hit, dedup, invalidatsiya).

import axios, { type AxiosAdapter, type AxiosResponse } from 'axios'
import { beforeEach, describe, expect, it } from 'vitest'
import {
  apiCacheSize,
  cacheKeyOf,
  clearApiCache,
  installApiCache,
  invalidationScopesFor,
  keyInScope,
  scopeOf,
} from '../cache'

describe('scopeOf — mutatsiya qaysi resursga tegdi', () => {
  it('rol/namespace yo\'llarida IKKI bo\'lak', () => {
    expect(scopeOf('/admin/students/123/archive')).toBe('/admin/students')
    expect(scopeOf('/admin/students')).toBe('/admin/students')
    expect(scopeOf('/teacher/groups/7/contacts')).toBe('/teacher/groups')
    expect(scopeOf('/student/curriculum/progress')).toBe('/student/curriculum')
  })

  it('boshqa yo\'llarda BITTA bo\'lak', () => {
    expect(scopeOf('/cti/agents/5/dial')).toBe('/cti')
    expect(scopeOf('/school')).toBe('/school')
  })

  it('query/hash tashlanadi', () => {
    expect(scopeOf('/admin/finance/transactions?reasonId=1')).toBe('/admin/finance')
  })

  it('miqyosini aniqlab bo\'lmaydiganlar — null (chaqiruvchi butun keshni tozalaydi)', () => {
    expect(scopeOf(undefined)).toBeNull()
    expect(scopeOf('')).toBeNull()
    expect(scopeOf('/')).toBeNull()
    // Absolut URL: kesh kalitlari NISBIY yo'ldan quriladi, taqqoslab bo'lmaydi.
    expect(scopeOf('https://boshqa.host/admin/students')).toBeNull()
  })
})

describe('keyInScope — bo\'lak darajasida moslik', () => {
  it('o\'zi va ostidagilar mos keladi', () => {
    expect(keyInScope('/admin/students?{}', '/admin/students')).toBe(true)
    expect(keyInScope('/admin/students/12?{"a":1}', '/admin/students')).toBe(true)
  })

  it('nomi o\'xshash BOSHQA resurs mos KELMAYDI (oddiy startsWith xatosi)', () => {
    expect(keyInScope('/admin/student-attendance?{}', '/admin/students')).toBe(false)
    // ⚠️ Prefiks SEGMENT chegarasida tugashi SHART — naive `startsWith` AYNAN shu yerda
    // adashardi. Loyihada bunday juftlik hozir yo'q (`/admin/staff-tasks` — kunlik cheklist —
    // olib tashlandi), lekin `/admin/<miqyos>-...` nomli endpoint qo'shilishi bilanoq kerak.
    expect(keyInScope('/admin/staff-roles?{}', '/admin/staff')).toBe(false)
  })
})

describe('invalidationScopesFor — kesishma jadvali', () => {
  it('to\'lov O\'QUVCHILAR ro\'yxatini ham tashlaydi (balans eskirmasin)', () => {
    const scopes = invalidationScopesFor('/admin/finance/transactions')
    expect(scopes).toContain('/admin/finance')
    expect(scopes).toContain('/admin/students')
    expect(scopes).toContain('/admin/kassa')
    expect(scopes).toContain('/admin/classes')
  })

  it('kassa to\'lovi ham (boshqa eshik — o\'sha pul)', () => {
    expect(invalidationScopesFor('/admin/kassa/students/9/payments')).toContain('/admin/students')
  })

  it('a\'zolik o\'zgarishi IKKALA tomonni tashlaydi', () => {
    const scopes = invalidationScopesFor('/admin/classes/5/members')
    expect(scopes).toContain('/admin/students')
    expect(scopes).toContain('/admin/journal')
  })

  it('har mutatsiya tarix va dashboard\'ni tashlaydi', () => {
    const scopes = invalidationScopesFor('/admin/rooms/3')
    expect(scopes).toContain('/admin/audit')
    expect(scopes).toContain('/admin/dashboard')
  })

  it('global ta\'sirli va NOMA\'LUM yo\'llar — null (butun kesh)', () => {
    expect(invalidationScopesFor('/auth/login')).toBeNull()
    expect(invalidationScopesFor('/admin/settings/school')).toBeNull()
    expect(invalidationScopesFor('/admin/staff/7/permissions')).toBeNull()
    // Whitelist standarti: jadvalda yo'q — demak o'rganilmagan, xavfsiz tomonga o'tamiz.
    expect(invalidationScopesFor('/admin/yangi-modul/1')).toBeNull()
  })
})

/* ─────────── Adapter darajasidagi haqiqiy xatti-harakat ─────────── */

function makeClient() {
  const calls: string[] = []
  const base: AxiosAdapter = async (config) => {
    calls.push(`${(config.method ?? 'get').toUpperCase()} ${config.url}`)
    return {
      data: { url: config.url, n: calls.length },
      status: 200,
      statusText: 'OK',
      headers: {},
      config,
    } as AxiosResponse
  }
  const instance = axios.create({ adapter: base })
  installApiCache(instance)
  return { instance, calls }
}

describe('kesh adapteri', () => {
  beforeEach(() => clearApiCache())

  it('ikkinchi GET tarmoqqa CHIQMAYDI', async () => {
    const { instance, calls } = makeClient()
    await instance.get('/admin/students')
    await instance.get('/admin/students')
    expect(calls).toHaveLength(1)
    expect(apiCacheSize()).toBe(1)
  })

  it('parallel bir xil GET — bitta so\'rov (dedup)', async () => {
    const { instance, calls } = makeClient()
    await Promise.all([instance.get('/admin/classes'), instance.get('/admin/classes')])
    expect(calls).toHaveLength(1)
  })

  it('to\'lov o\'quvchilar keshini tashlaydi, BOG\'LIQ BO\'LMAGANINI saqlaydi', async () => {
    const { instance, calls } = makeClient()
    await instance.get('/admin/students')
    await instance.get('/admin/curriculum')
    await instance.post('/admin/finance/transactions', {})
    calls.length = 0
    await instance.get('/admin/students') // eskirdi → tarmoq
    await instance.get('/admin/curriculum') // tegilmagan → keshdan
    expect(calls).toEqual(['GET /admin/students'])
  })

  it('miqyoslab bo\'lmaydigan mutatsiya BUTUN keshni tozalaydi', async () => {
    const { instance } = makeClient()
    await instance.get('/admin/students')
    await instance.get('/admin/curriculum')
    expect(apiCacheSize()).toBe(2)
    await instance.put('/admin/settings/school', {})
    expect(apiCacheSize()).toBe(0)
  })

  it('XATO bilan tugagan mutatsiya keshni tozalamaydi (hech narsa o\'zgarmagan)', async () => {
    const calls: string[] = []
    const base: AxiosAdapter = async (config) => {
      calls.push(String(config.url))
      if ((config.method ?? 'get').toLowerCase() === 'post') throw new Error('500')
      return { data: {}, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse
    }
    const instance = axios.create({ adapter: base })
    installApiCache(instance)
    await instance.get('/admin/students')
    await expect(instance.post('/admin/students', {})).rejects.toThrow()
    expect(apiCacheSize()).toBe(1)
  })

  it('so\'rov ketayotganda kelgan MOS mutatsiya javobni keshga yozdirmaydi', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((r) => {
      release = r
    })
    const base: AxiosAdapter = async (config) => {
      if ((config.method ?? 'get').toLowerCase() === 'get') await gate
      return { data: {}, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse
    }
    const instance = axios.create({ adapter: base })
    installApiCache(instance)

    const pending = instance.get('/admin/students')
    await instance.post('/admin/finance/transactions', {}) // balans o'zgardi
    release()
    await pending
    // Javob mutatsiyadan OLDIN so'ralgan — eskirgan, keshga tushmasligi kerak.
    expect(apiCacheSize()).toBe(0)
  })

  it('boshqa miqyosdagi mutatsiya uchayotgan GET\'ga TEGMAYDI', async () => {
    let release: () => void = () => {}
    const gate = new Promise<void>((r) => {
      release = r
    })
    const base: AxiosAdapter = async (config) => {
      if ((config.method ?? 'get').toLowerCase() === 'get') await gate
      return { data: {}, status: 200, statusText: 'OK', headers: {}, config } as AxiosResponse
    }
    const instance = axios.create({ adapter: base })
    installApiCache(instance)

    const pending = instance.get('/admin/curriculum')
    await instance.post('/admin/finance/transactions', {})
    release()
    await pending
    expect(apiCacheSize()).toBe(1)
  })

  it('`cache: false` va `X-No-Cache` — kesh chetlab o\'tiladi', async () => {
    const { instance, calls } = makeClient()
    await instance.get('/admin/students', { cache: false })
    await instance.get('/admin/students', { headers: { 'X-No-Cache': '1' } })
    expect(apiCacheSize()).toBe(0)
    expect(calls).toHaveLength(2)
  })

  it('`/auth/` keshlanmaydi', async () => {
    const { instance, calls } = makeClient()
    await instance.get('/auth/me')
    await instance.get('/auth/me')
    expect(calls).toHaveLength(2)
    expect(apiCacheSize()).toBe(0)
  })

  it('cacheKeyOf — params tartibi kalitni o\'zgartirmaydi', () => {
    const key = (params: Record<string, unknown>) =>
      cacheKeyOf({ url: '/admin/students', params } as never)
    expect(key({ a: 1, b: 2 })).toBe(key({ b: 2, a: 1 }))
  })
})
