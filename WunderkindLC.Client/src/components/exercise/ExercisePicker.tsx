/**
 * "TOPSHIRIQ YARATISH" — topshiriq TURINI tanlash oynasi (katta modal karta): sarlavha +
 * kategoriya tablari, har kategoriya uchun tur kartalari, kartada esa o'sha turning mini
 * "foydalanuvchi ko'rinishi" previewi va "Yaratishni boshlash" tugmasi. Ranglar — CRM tokenlari.
 *
 * OXIRGI tab — "Boshqa": interaktiv mashq bo'lmagan ESKI turlar (video, matn, audio, PDF,
 * lug'at, oddiy test). Ular tanlansa oddiy (eski) topshiriq yaratiladi.
 */
import { Fragment, useEffect, useState } from 'react'
import type { CSSProperties } from 'react'
// Konstruktor CSS'i (`.dc-*`) shu yerda — main.tsx'da EMAS: faqat mashq ekranlari bilan yuklanadi
import '@/styles/exercise.css'
import type { LessonType } from '@/types'
import type { ExerciseKind } from './model'
import { CATEGORIES, LegacyPreview, MiniPreview, OTHER_CATEGORY, UI, display, sans, Icon } from './catalog'
import { ConstructorHeader } from './kit'

interface Props {
  /** Hozir tanlangan tur (mavjud mashq tahrirlanayotgan bo'lsa) — karta belgilanadi. */
  current?: ExerciseKind | null
  onPick: (kind: ExerciseKind) => void
  /** Berilsa — oxirgi "Boshqa" tabi ko'rinadi (eski topshiriq turlari). */
  onPickLegacy?: (type: LessonType) => void
  onClose: () => void
  /** Sarlavha ostidagi matn. */
  subtitle?: string
}

/** Karta ichidagi mini preview sarlavhasi. `badge` — faqat HOLAT uchun (masalan "Tanlangan");
 *  bo'sh bo'lsa umuman ko'rsatilmaydi (har kartada turgan "Tayyor" yorlig'i olib tashlandi). */
function CardHead({ badge }: { badge?: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 7, padding: '9px 11px', borderBottom: '1px solid #eceef2', background: '#fbfaf7' }}>
      <span style={{ flex: 'none', width: 6, height: 6, borderRadius: '50%', background: UI.accent, display: 'inline-block' }} />
      <span
        style={{
          flex: 1, minWidth: 0, fontSize: 10, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase',
          color: '#8582f0', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
        }}
      >
        Foydalanuvchi ko'rinishi
      </span>
      {badge && (
        <span
          style={{
            flex: 'none', whiteSpace: 'nowrap', fontSize: 9.5, fontWeight: 700, letterSpacing: '.04em', textTransform: 'uppercase',
            padding: '3px 8px', borderRadius: 20, color: UI.accent, background: UI.accentSoft,
          }}
        >
          {badge}
        </span>
      )}
    </div>
  )
}

/** Karta pastidagi ikon + nom + tavsif + harakat tugmasi. */
function CardBody({ icon, name, desc, cta }: { icon: string; name: string; desc: string; cta: string }) {
  return (
    <>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div style={{ flex: 'none', width: 42, height: 42, borderRadius: 11, display: 'flex', alignItems: 'center', justifyContent: 'center', background: UI.accent }}>
          <Icon name={icon} />
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <h3 style={{ margin: 0, fontWeight: 700, fontSize: 17.5, color: UI.ink, ...display }}>{name}</h3>
          <p style={{ margin: 0, fontSize: 13, lineHeight: 1.5, color: UI.muted }}>{desc}</p>
        </div>
      </div>
      <span
        className="dc-go"
        style={{
          alignSelf: 'flex-start', marginTop: 'auto', display: 'inline-flex', alignItems: 'center', gap: 8,
          fontSize: 14.5, fontWeight: 600, color: UI.accent, background: UI.accentSoft, padding: '11px 18px', borderRadius: 11, transition: 'all .15s',
        }}
      >
        {cta}
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 12h14M13 6l6 6-6 6" />
        </svg>
      </span>
    </>
  )
}

/** Karta ichidagi preview qutisi — kartaning bo'sh balandligini to'ldiradi. */
const previewBox: CSSProperties = {
  flex: 1,
  minHeight: 0,
  display: 'flex',
  flexDirection: 'column',
  borderRadius: 13,
  overflow: 'hidden',
  background: '#fff',
  border: '1px solid #eceef2',
  boxShadow: '0 2px 8px -4px rgba(40,30,80,.12)',
}

/** Mini maket — qutining o'rtasida turadi. */
const previewInner: CSSProperties = { flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center' }

const cardStyle = (on: boolean) => ({
  display: 'flex' as const,
  flexDirection: 'column' as const,
  gap: 16,
  borderRadius: 16,
  padding: 18,
  position: 'relative' as const,
  overflow: 'hidden' as const,
  minHeight: 'clamp(430px, 50vh, 530px)',
  textAlign: 'left' as const,
  background: '#fff',
  border: `1.5px solid ${on ? UI.accent : UI.line}`,
  cursor: 'pointer',
  ...sans,
})

export function ExercisePicker({ current, onPick, onPickLegacy, onClose, subtitle = 'Topshiriqlar' }: Props) {
  const tabs = onPickLegacy ? [...CATEGORIES, OTHER_CATEGORY] : CATEGORIES
  const [catId, setCatId] = useState(() => {
    if (!current) return CATEGORIES[0].id
    return CATEGORIES.find((c) => c.types.some((t) => t.kind === current))?.id ?? CATEGORIES[0].id
  })
  const active = tabs.find((c) => c.id === catId) ?? tabs[0]
  const isOther = active.id === OTHER_CATEGORY.id
  const cols = Math.min(4, Math.max(1, active.types.length))

  // Esc — yopish (CRM modallari bilan bir xil xulq).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    // KATTA MODAL KARTA — ekranga sig'adi, ichki qismi aylanadi. z-index 40: CRM modallari (z-50,
    // nom so'rash / bir nechta nom) shu ekranning USTIDA ochiladi.
    <div
      className="dc-root"
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, zIndex: 40, background: 'rgba(23,22,31,.55)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 'clamp(10px,3vh,28px)',
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: 'min(1420px,96vw)', minHeight: 'min(790px,92vh)', maxHeight: '92vh', background: UI.page, borderRadius: 20, overflow: 'hidden',
          display: 'flex', flexDirection: 'column', boxShadow: '0 40px 90px -30px rgba(23,22,31,.7)', ...sans,
        }}
      >
        <ConstructorHeader subtitle={subtitle} accent={UI.accent} onCancel={onClose} hideSave />

        {/* Kategoriya tablari — CRM uslubidagi "pill"lar, orasi keng; oxirgisi "Boshqa" */}
        <div
          style={{
            background: UI.bar, borderBottom: `1px solid ${UI.line}`, padding: '10px 20px',
            display: 'flex', alignItems: 'center', gap: 8, overflowX: 'auto', flex: 'none',
          }}
        >
          {tabs.map((c) => {
            const on = c.id === active.id
            const other = c.id === OTHER_CATEGORY.id
            return (
              <Fragment key={c.id}>
                {/* "Boshqa" — alohida guruh: oldidan nozik ajratuvchi */}
                {other && <span style={{ flex: 'none', width: 1, height: 22, background: UI.line, margin: '0 4px' }} />}
                <button
                  type="button"
                  onClick={() => setCatId(c.id)}
                  className="dc-tab"
                  style={{
                    display: 'inline-flex', alignItems: 'center', gap: 8, whiteSpace: 'nowrap', ...sans, fontWeight: 600, fontSize: 13.5,
                    padding: '9px 16px', borderRadius: 10, cursor: 'pointer', flex: 'none',
                    border: `1px solid ${on ? '#cfd3ff' : 'transparent'}`,
                    background: on ? UI.accentSoft : 'transparent',
                    color: on ? UI.accent : '#4a4d56',
                  }}
                >
                  {c.label}
                </button>
              </Fragment>
            )
          })}
        </div>

        <main style={{ flex: 1, minHeight: 0, overflowY: 'auto', width: '100%', padding: '28px 32px 34px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginBottom: 22 }}>
            <span style={{ fontSize: 12.5, fontWeight: 600, letterSpacing: '.06em', textTransform: 'uppercase', color: '#8582f0' }}>
              {isOther ? 'Oddiy kontent' : 'Yangi topshiriq'}
            </span>
            <h1 style={{ margin: 0, fontWeight: 700, fontSize: 27, letterSpacing: '-.02em', color: UI.ink, ...display }}>{active.title}</h1>
            <p style={{ margin: 0, fontSize: 14.5, color: UI.muted, maxWidth: 680 }}>{active.desc}</p>
          </div>

          <div
            style={{
              display: 'grid', gap: 20, alignItems: 'stretch',
              gridTemplateColumns: `repeat(${cols},minmax(0,1fr))`,
              maxWidth: cols * 320 + (cols - 1) * 20,
            }}
          >
            {isOther
              ? OTHER_CATEGORY.types.map((t) => (
                  <button key={t.lesson} type="button" onClick={() => onPickLegacy?.(t.lesson)} className="dc-tcard" style={cardStyle(false)}>
                    <div style={previewBox}>
                      <CardHead />
                      <div style={previewInner}>
                        <LegacyPreview type={t.lesson} />
                      </div>
                    </div>
                    <CardBody icon={t.icon} name={t.name} desc={t.desc} cta="Yaratishni boshlash" />
                  </button>
                ))
              : (active as (typeof CATEGORIES)[number]).types.map((t) => {
                  const chosen = current === t.kind
                  return (
                    <button key={t.kind} type="button" onClick={() => onPick(t.kind)} className="dc-tcard" style={cardStyle(chosen)}>
                      <div style={previewBox}>
                        <CardHead badge={chosen ? 'Tanlangan' : undefined} />
                        <div style={previewInner}>
                          <MiniPreview preview={t.preview} kind={t.kind} />
                        </div>
                      </div>
                      <CardBody icon={t.icon} name={t.name} desc={t.desc} cta={chosen ? 'Davom etish' : 'Yaratishni boshlash'} />
                    </button>
                  )
                })}
          </div>
        </main>
      </div>
    </div>
  )
}
