import { useEffect, useRef } from 'react'
import type { WavRecorder } from '@/lib/wavRecorder'

/**
 * Yozish vaqtidagi jonli to'lqin (waveform) — mikrofon balandligini chiziq-chiziq
 * (scrolling bars) ko'rsatadi. `recorder.getLevel()` (0..1 RMS) dan o'qiydi.
 */
export function RecWaveform({
  recorder,
  active,
}: {
  recorder: React.RefObject<WavRecorder | null>
  active: boolean
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const barsRef = useRef<number[]>([])
  const rafRef = useRef<number | null>(null)

  useEffect(() => {
    if (!active) return
    const canvas = canvasRef.current
    if (!canvas) return
    const dpr = window.devicePixelRatio || 1
    const cssW = canvas.clientWidth || 280
    const cssH = canvas.clientHeight || 56
    canvas.width = Math.round(cssW * dpr)
    canvas.height = Math.round(cssH * dpr)
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    ctx.scale(dpr, dpr)

    const BAR_W = 3
    const GAP = 2
    const maxBars = Math.floor(cssW / (BAR_W + GAP))
    barsRef.current = []
    const accent =
      getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#2563eb'

    const draw = () => {
      const lvl = recorder.current?.getLevel() ?? 0
      // RMS odatda kichik — ko'rinarli bo'lishi uchun kuchaytiramiz va chegaralaymiz.
      const h = Math.min(1, lvl * 3.2)
      const bars = barsRef.current
      bars.push(h)
      if (bars.length > maxBars) bars.shift()

      ctx.clearRect(0, 0, cssW, cssH)
      const mid = cssH / 2
      ctx.fillStyle = accent
      for (let i = 0; i < bars.length; i++) {
        const x = i * (BAR_W + GAP)
        const bh = Math.max(2, bars[i] * cssH)
        const y = mid - bh / 2
        if (ctx.roundRect) {
          ctx.beginPath()
          ctx.roundRect(x, y, BAR_W, bh, BAR_W / 2)
          ctx.fill()
        } else {
          ctx.fillRect(x, y, BAR_W, bh)
        }
      }
      rafRef.current = requestAnimationFrame(draw)
    }
    rafRef.current = requestAnimationFrame(draw)

    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current)
      rafRef.current = null
    }
  }, [active, recorder])

  return <canvas ref={canvasRef} style={{ width: '100%', maxWidth: 320, height: 56, display: 'block' }} />
}
