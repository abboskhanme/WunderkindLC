// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { linkTelegramChat } from '../telegramLink'

function setInitData(initData: string | undefined) {
  ;(window as unknown as { Telegram?: unknown }).Telegram =
    initData === undefined ? undefined : { WebApp: { initData } }
}

describe('linkTelegramChat — Panel login links the bot chat', () => {
  afterEach(() => {
    setInitData(undefined)
    sessionStorage.clear()
  })

  it('does nothing outside Telegram (no or empty initData)', async () => {
    const post = vi.fn().mockResolvedValue({})
    setInitData(undefined)
    await linkTelegramChat('u1', post)
    setInitData('')
    await linkTelegramChat('u1', post)
    expect(post).not.toHaveBeenCalled()
  })

  it('posts the signed initData once per session and account', async () => {
    const post = vi.fn().mockResolvedValue({})
    setInitData('user=%7B%22id%22%3A1%7D&hash=abc')
    await Promise.all([linkTelegramChat('u1', post), linkTelegramChat('u1', post)])
    await linkTelegramChat('u1', post)
    expect(post).toHaveBeenCalledTimes(1)
    expect(post).toHaveBeenCalledWith({ initData: 'user=%7B%22id%22%3A1%7D&hash=abc' })
    await linkTelegramChat('u2', post) // another account on the same device links too
    expect(post).toHaveBeenCalledTimes(2)
  })

  it('swallows a failure and retries later', async () => {
    const post = vi.fn().mockRejectedValueOnce(new Error('400')).mockResolvedValue({})
    setInitData('x=1&hash=abc')
    await expect(linkTelegramChat('u1', post)).resolves.toBeUndefined()
    await linkTelegramChat('u1', post)
    expect(post).toHaveBeenCalledTimes(2)
  })
})
