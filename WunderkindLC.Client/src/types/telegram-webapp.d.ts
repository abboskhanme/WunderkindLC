export {}

declare global {
  /** Telegram Mini App theme (https://core.telegram.org/bots/webapps#themeparams). Every key is optional. */
  interface TelegramThemeParams {
    bg_color?: string
    text_color?: string
    hint_color?: string
    link_color?: string
    button_color?: string
    button_text_color?: string
    secondary_bg_color?: string
    header_bg_color?: string
    bottom_bar_bg_color?: string
    accent_text_color?: string
    section_bg_color?: string
    section_header_text_color?: string
    section_separator_color?: string
    subtitle_text_color?: string
    destructive_text_color?: string
  }

  interface TelegramBackButton {
    isVisible: boolean
    show(): void
    hide(): void
    onClick(cb: () => void): void
    offClick(cb: () => void): void
  }

  interface TelegramWebApp {
    ready(): void
    expand(): void
    /** Signed launch data — EMPTY when the page is opened outside Telegram (SDK still loads). */
    initData?: string
    platform?: string
    version?: string
    colorScheme?: 'light' | 'dark'
    themeParams?: TelegramThemeParams
    isVersionAtLeast?(version: string): boolean
    setHeaderColor?(color: string): void
    setBackgroundColor?(color: string): void
    setBottomBarColor?(color: string): void
    disableVerticalSwipes?(): void
    onEvent?(event: string, handler: () => void): void
    offEvent?(event: string, handler: () => void): void
    BackButton?: TelegramBackButton
    HapticFeedback?: {
      selectionChanged(): void
      impactOccurred(style: 'light' | 'medium' | 'heavy' | 'rigid' | 'soft'): void
    }
  }

  interface Window {
    /** Telegram bot Menu Button orqali Web App sifatida ochilganda mavjud (oddiy brauzerda yo'q). */
    Telegram?: {
      WebApp?: TelegramWebApp
    }
  }
}
