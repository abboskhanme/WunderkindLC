import { fileURLToPath, URL } from 'node:url'

import { defineConfig, type UserConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import fs from 'fs'
import path from 'path'
import child_process from 'child_process'
import { env } from 'process'

// https://vite.dev/config/
export default defineConfig(({ command }): UserConfig => {
    // Barcha rejimlar uchun umumiy sozlamalar.
    const config: UserConfig = {
        plugins: [react(), tailwindcss()],
        resolve: {
            alias: {
                '@': fileURLToPath(new URL('./src', import.meta.url)),
            },
        },
        build: {
            rollupOptions: {
                output: {
                    // React asoslarini alohida "react" chunk'iga ajratamiz — u kam o'zgaradi,
                    // shuning uchun brauzerda uzoq keshlanadi (app kodi o'zgarsa ham qayta yuklanmaydi).
                    // Vite 8 (rolldown) faqat funksiya shaklini qabul qiladi.
                    manualChunks: (id: string) =>
                        /node_modules\/(react|react-dom|react-router|react-router-dom|scheduler)\//.test(id)
                            ? 'react'
                            : undefined,
                },
            },
        },
    }

    // Quyidagilar FAQAT dev serverida kerak (HTTPS cert, proxy, host).
    // `vite build` (jumladan Docker) paytida `dotnet`/sertifikat YO'Q —
    // shuning uchun bu blokni ishga tushirmaymiz (aks holda build yiqiladi).
    if (command !== 'serve') return config

    // DOCKER (hot reload) rejimi: konteynerda `dotnet dev-certs` YO'Q, shuning uchun dev-server
    // oddiy HTTP'da ko'tariladi va fayllarni SO'ROV (polling) bilan kuzatadi — bind-mount'da
    // macOS/Windows fayl hodisalari konteynerga yetib kelmaydi. Bayroqlar docker-compose.dev.yml'da.
    // Bo'sh qoldirilsa (odatdagi lokal `npm run dev`) — hammasi avvalgidek: HTTPS + sertifikat.
    const useHttps = env.VITE_DEV_HTTPS !== 'false'
    const usePolling = env.VITE_DEV_POLL === 'true'

    // ASP.NET Core SPA proxy uchun HTTPS sertifikat sozlamasi.
    const baseFolder =
        env.APPDATA !== undefined && env.APPDATA !== ''
            ? `${env.APPDATA}/ASP.NET/https`
            : `${env.HOME}/.aspnet/https`

    const certificateName = 'wunderkindlc.client'
    const certFilePath = path.join(baseFolder, `${certificateName}.pem`)
    const keyFilePath = path.join(baseFolder, `${certificateName}.key`)

    if (useHttps) {
        if (!fs.existsSync(baseFolder)) {
            fs.mkdirSync(baseFolder, { recursive: true })
        }

        if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
            if (
                0 !==
                child_process.spawnSync(
                    'dotnet',
                    ['dev-certs', 'https', '--export-path', certFilePath, '--format', 'Pem', '--no-password'],
                    { stdio: 'inherit' },
                ).status
            ) {
                throw new Error('Could not create certificate.')
            }
        }
    }

    // Backend (WunderkindLC.Server) manzili — proxy uchun.
    const target = env.ASPNETCORE_HTTPS_PORT
        ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
        : env.ASPNETCORE_URLS
            ? env.ASPNETCORE_URLS.split(';')[0]
            : 'https://localhost:7288'

    return {
        ...config,
        server: {
            // LAN'dagi boshqa qurilmalar (telefon va h.k.) ham kira olishi uchun
            // barcha tarmoq interfeyslarida tinglaymiz (faqat localhost emas).
            host: true,
            // Multi-tenant subdomenlar (dev'da *.lvh.me / *.nip.io 127.0.0.1'ga ishora qiladi).
            // Vite noma'lum Host sarlavhalarini bloklaydi — bularni ruxsat etamiz.
            //
            // `.trycloudflare.com` — Cloudflare "quick tunnel": lokal dev-serverni tashqi
            // havola bilan sinash uchun (`cloudflared tunnel --url http://localhost:7701`).
            // Host tasodifiy generatsiya qilinadi, ya'ni uni oldindan yozib bo'lmaydi —
            // shuning uchun butun domen ruxsat etiladi. FAQAT dev-server uchun: prod'da
            // SPA statik fayl sifatida beriladi, bu blok umuman ishlamaydi.
            allowedHosts: ['.lvh.me', '.nip.io', '.localhost', '.trycloudflare.com'],
            // Frontend so'rovlarini ASP.NET backendiga yo'naltiramiz.
            proxy: {
                '^/api': {
                    target,
                    secure: false,
                },
                // SignalR guruh chati hub'i (WebSocket) — backendga yo'naltiramiz.
                '^/hubs': {
                    target,
                    secure: false,
                    ws: true,
                },
                // CTI (Local Call) agent WebSocket'i — `wss://host/ws?token=...`.
                // ⚠️ SignalR EMAS, xom WebSocket (`Program.cs` → `app.Map("/ws", ...)`).
                // Busiz Android agent dev-serverga ULANA OLMAYDI: `/ws` Vite'ning SPA
                // fallback'iga tushib, javob sifatida index.html qaytardi — ya'ni agent
                // panelda DOIM "oflayn" ko'rinar va click-to-call ishlamasdi.
                // Naqsh AYNAN `/ws` (yoki `/ws?...`) ni oladi — `/wsX` kabi yo'llar tegmaydi.
                '^/ws(\\?.*)?$': {
                    target,
                    secure: false,
                    ws: true,
                },
                // Yuklangan materiallar (fayllar) — backenddan.
                '^/uploads': {
                    target,
                    secure: false,
                },
                // Karyera Mini App'i (`/vakansiya`) va landing — React EMAS, backend `wwwroot`idan
                // beriladigan statik sahifalar. Dev-server portida ham ochilsin.
                '^/vakansiya': {
                    target,
                    secure: false,
                },
                '^/vendor/': {
                    target,
                    secure: false,
                },
                '^/landing': {
                    target,
                    secure: false,
                },
                '^/weatherforecast': {
                    target,
                    secure: false,
                },
            },
            port: parseInt(env.DEV_SERVER_PORT || '58472'),
            // Docker'da bind-mount fayl hodisalarini bermaydi — polling shart, aks holda
            // tahrir qilingan fayl brauzerga umuman yetib bormaydi.
            ...(usePolling ? { watch: { usePolling: true, interval: 300 } } : {}),
            ...(useHttps
                ? {
                      https: {
                          key: fs.readFileSync(keyFilePath),
                          cert: fs.readFileSync(certFilePath),
                      },
                  }
                : {}),
        },
    }
})
