import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { GraduationCap, Loader2, RefreshCw, Share2, Printer, CheckCircle2, XCircle } from 'lucide-react'
import { api } from '@/api/client'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'
import {
  VerificationResult,
  type CertificateVerificationDto,
} from '@/components/public/VerificationResult'

type Phase = 'loading' | 'done' | 'error'

export function VerifyCertificatePage() {
  const { id = '' } = useParams<{ id: string }>()

  const [phase, setPhase] = useState<Phase>('loading')
  const [data, setData] = useState<CertificateVerificationDto | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '' })
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  useEffect(() => {
    if (!id) {
      setErrorMsg('Sertifikat ID ko\'rsatilmagan')
      setPhase('error')
      return
    }
    fetchVerification()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function fetchVerification() {
    setPhase('loading')
    setErrorMsg(null)
    try {
      const { data: result } = await api.get<CertificateVerificationDto>(
        `/public/certificates/${id}/verify`,
      )
      setData(result)
      setPhase('done')
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      if (status === 404) {
        setErrorMsg('Bu sertifikat topilmadi')
      } else if (status === 400) {
        setErrorMsg('Sertifikat ID noto\'g\'ri')
      } else {
        setErrorMsg('Serverga ulanishda xatolik yuz berdi')
      }
      setPhase('error')
    }
  }

  function handleCopy() {
    const url = window.location.href
    if (navigator.clipboard) {
      navigator.clipboard.writeText(url).then(() => {
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
      })
    }
  }

  function handlePrint() {
    window.print()
  }

  const isValid = data?.isValid === true

  return (
    <div className="verify-page min-h-screen bg-gradient-to-br from-slate-50 via-white to-brand-50/30 px-4 py-8 sm:py-14 print:bg-white print:py-0">
      <div className="mx-auto w-full max-w-2xl">

        {/* Brand header */}
        <div className="mb-6 flex items-center justify-between print:hidden">
          <div className="flex items-center gap-2.5">
            {brand.logoUrl ? (
              <img
                src={brand.logoUrl}
                alt="Logo"
                className="h-9 w-9 rounded-xl object-contain shadow"
              />
            ) : (
              <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow">
                <GraduationCap className="h-5 w-5" />
              </div>
            )}
            <span className="text-base font-bold tracking-tight text-slate-800">
              {brand.name || 'WunderkindLC'}
            </span>
          </div>

          {/* Action buttons — only shown when result is loaded */}
          {phase === 'done' && (
            <div className="flex items-center gap-2">
              <button
                onClick={handleCopy}
                title="Havolani nusxalash"
                className="flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:bg-slate-50"
              >
                {copied ? (
                  <>
                    <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" />
                    Nusxalandi
                  </>
                ) : (
                  <>
                    <Share2 className="h-3.5 w-3.5" />
                    Ulashish
                  </>
                )}
              </button>
              <button
                onClick={handlePrint}
                title="Chop etish"
                className="flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:bg-slate-50"
              >
                <Printer className="h-3.5 w-3.5" />
                Chop etish
              </button>
            </div>
          )}
        </div>

        {/* Page title */}
        <div className="mb-4 text-center print:mb-6">
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-400">
            Sertifikat tekshiruvi
          </p>
          <h1 className="mt-1 text-2xl font-bold text-slate-800">
            Haqiqiylikni tasdiqlash
          </h1>
        </div>

        {/* Main card */}
        <div className="overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-xl shadow-slate-200/60 print:border-slate-300 print:shadow-none">

          {/* Status strip — shown at top once loaded */}
          {phase === 'done' && data && (
            <div
              className={`flex items-center gap-3 px-6 py-3.5 print:hidden ${
                isValid ? 'bg-emerald-600' : 'bg-red-500'
              }`}
            >
              {isValid ? (
                <CheckCircle2 className="h-5 w-5 shrink-0 text-white" />
              ) : (
                <XCircle className="h-5 w-5 shrink-0 text-white" />
              )}
              <p className="text-sm font-semibold text-white">
                {isValid
                  ? 'Sertifikat haqiqiy va amal qiluvchi'
                  : 'Sertifikat yaroqsiz yoki topilmadi'}
              </p>
              {id && (
                <span className="ml-auto font-mono text-xs text-white/70">
                  ID: {id.slice(0, 8)}…
                </span>
              )}
            </div>
          )}

          {/* Content */}
          <div className="px-6 py-8 sm:px-8 print:px-4 print:py-6">

            {/* Loading */}
            {phase === 'loading' && (
              <div className="flex flex-col items-center gap-4 py-12">
                <div className="flex h-16 w-16 items-center justify-center rounded-full bg-slate-50">
                  <Loader2 className="h-7 w-7 animate-spin text-brand-500" />
                </div>
                <div className="text-center">
                  <p className="font-semibold text-slate-700">Tekshirilmoqda...</p>
                  <p className="mt-1 text-sm text-slate-400">Sertifikat ma&apos;lumotlari yuklanmoqda</p>
                </div>
              </div>
            )}

            {/* Error (network/not found) */}
            {phase === 'error' && (
              <div>
                <VerificationResult isValid={false} data={null} error={errorMsg} />
                <div className="mt-6 flex justify-center">
                  <button
                    onClick={fetchVerification}
                    className="flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-sm font-medium text-slate-600 shadow-sm transition-colors hover:bg-slate-50"
                  >
                    <RefreshCw className="h-4 w-4" />
                    Qayta urinish
                  </button>
                </div>
              </div>
            )}

            {/* Result */}
            {phase === 'done' && (
              <VerificationResult
                isValid={isValid}
                data={data}
                error={null}
              />
            )}
          </div>
        </div>

        {/* Footer */}
        <p className="mt-6 text-center text-xs text-slate-400 print:hidden">
          {brand.name || 'WunderkindLC'} · O&apos;quv markazi · Sertifikat tekshiruv tizimi
        </p>

        {/* Print footer */}
        <div className="hidden print:mt-8 print:block print:text-center print:text-xs print:text-slate-400">
          <p>
            {brand.name || 'WunderkindLC'} — {window.location.href}
          </p>
          <p className="mt-1">Chop etilgan: {new Date().toLocaleDateString('uz-UZ')}</p>
        </div>
      </div>

      {/* Print styles */}
      <style>{`
        @media print {
          body { margin: 0; }
          .verify-page { padding: 0; }
          @page { margin: 1.5cm; size: A4; }
        }
      `}</style>
    </div>
  )
}
