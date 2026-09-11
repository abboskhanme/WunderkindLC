/* ============================================================
   WAV diktafon — mikrofondan PCM olib, 16kHz mono WAV blobiga aylantiradi.
   Azure Speech (Pronunciation Assessment) WAV PCM 16kHz mono kutadi, shuning
   uchun server tomonda konvertatsiya kerak emas (WebView'da ham ishlaydi).
   ============================================================ */

export interface WavRecorder {
  /** Yozishni to'xtatadi va WAV blobini qaytaradi. */
  stop: () => Promise<Blob>
  /** Bekor qiladi (blob qaytarmaydi), resurslarni tozalaydi. */
  cancel: () => void
  /** Joriy ovoz balandligi (0..1, RMS) — waveform chizish uchun. */
  getLevel: () => number
}

/** Mikrofonni so'rab, yozishni boshlaydi. Ruxsat berilmasa throw qiladi. */
export async function startWavRecording(): Promise<WavRecorder> {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
  })
  const AC: typeof AudioContext =
    window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
  const ctx = new AC()
  // MUHIM: getUserMedia await'idan keyin yaratilgan AudioContext ko'pincha 'suspended'
  // holatda bo'ladi (Chrome/WebView autoplay siyosati) — resume qilmasak onaudioprocess
  // umuman ishlamaydi va WAV bo'sh chiqadi.
  if (ctx.state === 'suspended') {
    try {
      await ctx.resume()
    } catch {
      /* resume bo'lmasa ham davom etamiz */
    }
  }
  const source = ctx.createMediaStreamSource(stream)
  const processor = ctx.createScriptProcessor(4096, 1, 1)
  const analyser = ctx.createAnalyser()
  analyser.fftSize = 1024
  const levelBuf = new Uint8Array(analyser.fftSize)
  const buffers: Float32Array[] = []
  let length = 0

  processor.onaudioprocess = (e) => {
    const ch = e.inputBuffer.getChannelData(0)
    buffers.push(new Float32Array(ch))
    length += ch.length
  }
  source.connect(analyser)
  source.connect(processor)
  processor.connect(ctx.destination)
  const inputRate = ctx.sampleRate

  const cleanup = () => {
    try {
      processor.disconnect()
      source.disconnect()
      stream.getTracks().forEach((t) => t.stop())
      void ctx.close()
    } catch {
      /* allaqachon yopilgan */
    }
  }

  return {
    stop: async () => {
      const merged = new Float32Array(length)
      let off = 0
      for (const b of buffers) {
        merged.set(b, off)
        off += b.length
      }
      cleanup()
      const down = downsample(merged, inputRate, 16000)
      return encodeWav(down, 16000)
    },
    cancel: cleanup,
    getLevel: () => {
      analyser.getByteTimeDomainData(levelBuf)
      let sum = 0
      for (let i = 0; i < levelBuf.length; i++) {
        const v = (levelBuf[i] - 128) / 128
        sum += v * v
      }
      return Math.sqrt(sum / levelBuf.length) // 0..1 RMS
    },
  }
}

/** Float32 PCM'ni outRate (16kHz) ga tushiradi (oddiy o'rtalash). */
function downsample(buf: Float32Array, inRate: number, outRate: number): Float32Array {
  if (outRate >= inRate) return buf
  const ratio = inRate / outRate
  const newLen = Math.round(buf.length / ratio)
  const result = new Float32Array(newLen)
  let pos = 0
  for (let idx = 0; idx < newLen; idx++) {
    const next = Math.round((idx + 1) * ratio)
    let sum = 0
    let count = 0
    for (let i = pos; i < next && i < buf.length; i++) {
      sum += buf[i]
      count++
    }
    result[idx] = count ? sum / count : 0
    pos = next
  }
  return result
}

/** 16-bit PCM mono WAV blobini quradi. */
function encodeWav(samples: Float32Array, rate: number): Blob {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  const writeStr = (offset: number, s: string) => {
    for (let i = 0; i < s.length; i++) view.setUint8(offset + i, s.charCodeAt(i))
  }
  writeStr(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeStr(8, 'WAVE')
  writeStr(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true) // PCM
  view.setUint16(22, 1, true) // mono
  view.setUint32(24, rate, true)
  view.setUint32(28, rate * 2, true) // byte rate
  view.setUint16(32, 2, true) // block align
  view.setUint16(34, 16, true) // bits per sample
  writeStr(36, 'data')
  view.setUint32(40, samples.length * 2, true)
  let off = 44
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]))
    view.setInt16(off, s < 0 ? s * 0x8000 : s * 0x7fff, true)
    off += 2
  }
  return new Blob([view], { type: 'audio/wav' })
}
