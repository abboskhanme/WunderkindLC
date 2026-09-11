import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'

/**
 * ALOHIDA Vitest konfiguratsiyasi (`vite.config.ts` EMAS).
 *
 * Sabab: `vite.config.ts` `command === 'serve'` rejimida `dotnet dev-certs` ni chaqiradi va
 * HTTPS sertifikat fayllarini o'qiydi. Vitest konfiguratsiyani `serve` rejimida yuklaydi, shuning
 * uchun uni qayta ishlatsak — Docker/CI'da (`dotnet` yo'q) "Could not create certificate" bilan
 * yiqilardi. Vitest `vitest.config.ts` ni `vite.config.ts` dan USTUN ko'radi, shuning uchun
 * dev-server sozlamalari testlarga umuman aralashmaydi.
 */
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    globals: true,
    // Sof funksiyalar sinovdan o'tadi — DOM kerak emas. Kerak bo'lgan bir nechta joyda
    // (masalan `exportToCsv`) brauzer API'lari testning o'zida stub qilinadi.
    environment: 'node',
    include: ['src/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      include: [
        'src/lib/**',
        'src/config/**',
        'src/pages/admin/vacancies/careerLabels.ts',
      ],
    },
  },
})
