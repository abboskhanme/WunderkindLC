export {}

declare global {
  interface Window {
    /** Telegram bot Menu Button orqali Web App sifatida ochilganda mavjud (oddiy brauzerda yo'q). */
    Telegram?: {
      WebApp?: {
        ready(): void
        expand(): void
      }
    }
  }
}
