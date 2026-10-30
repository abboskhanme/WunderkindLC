import { useState } from 'react'

function read(key: string): Set<string> {
  // localStorage — faqat qulaylik (qaysi papka ochiq). Yopiq bo'lsa yoki xato bersa — bo'sh.
  try {
    const v: unknown = JSON.parse(localStorage.getItem(key) ?? '[]')
    return new Set(Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [])
  } catch {
    return new Set()
  }
}

function write(key: string, set: Set<string>) {
  try {
    localStorage.setItem(key, JSON.stringify([...set]))
  } catch {
    // brauzer xotirasi yopiq (private rejim) — eslab qolinmaydi, xolos
  }
}

/** Brauzerda eslab qolinadigan id'lar to'plami (daraxtda ochiq papkalar, yig'ilgan bo'limlar). */
export function useStoredSet(key: string) {
  const [set, setSet] = useState<Set<string>>(() => read(key))
  const update = (fn: (next: Set<string>) => void) =>
    setSet((prev) => {
      const next = new Set(prev)
      fn(next)
      write(key, next)
      return next
    })
  return {
    set,
    toggle: (id: string) => update((s) => (s.has(id) ? s.delete(id) : s.add(id))),
    add: (id: string) => update((s) => s.add(id)),
    remove: (id: string) => update((s) => s.delete(id)),
  }
}
