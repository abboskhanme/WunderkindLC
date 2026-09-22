"""
Logotipdan barcha ikonka o'lchamlarini tayyorlaydi.

⚠️ Fon CHETDAN flood-fill bilan olib tashlanadi (hamma oq pikselni emas): logotip ichida ham
och rangli joylar bor — ularni ham shaffof qilib yuborsak, tasvir teshik-teshik bo'lardi.

Chiqadigan fayllar:
  logo.png            — shaffof fonli asl (to'liq o'lcham), CenterMeta.LogoUrl uchun
  favicon-32.png      — brauzer yorlig'i
  favicon-192.png     — PWA
  favicon-512.png     — PWA
  apple-touch-icon.png— iOS (shaffoflikni qo'llab-quvvatlamaydi → OQ fon)
"""
import sys
from PIL import Image, ImageDraw

src, out_dir = sys.argv[1], sys.argv[2]

im = Image.open(src).convert('RGBA')
w, h = im.size

# ── 1. Chetdagi oq fonni shaffof qilamiz ──────────────────────────────────────
mark = (255, 0, 255, 255)  # vaqtinchalik belgi rangi (tasvirda uchramaydi)
flat = Image.new('RGB', im.size, (255, 255, 255))
flat.paste(im, mask=im.split()[3])
for corner in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
    ImageDraw.floodfill(flat, corner, mark[:3], thresh=32)

px_flat, px_out = flat.load(), im.load()
removed = 0
for y in range(h):
    for x in range(w):
        if px_flat[x, y] == mark[:3]:
            px_out[x, y] = (255, 255, 255, 0)
            removed += 1

# ── 2. Bo'sh chekkalarni qirqamiz (ikonkada logo kichik bo'lib qolmasin) ──────
im = im.crop(im.getbbox())

def square(img, size, bg=None, pad=0.06):
    """Kvadrat kanvasga markazlab joylaydi. `pad` — chekka bo'shlig'i (ulush)."""
    canvas = Image.new('RGBA', (size, size), bg or (255, 255, 255, 0))
    inner = int(size * (1 - 2 * pad))
    scaled = img.copy()
    scaled.thumbnail((inner, inner), Image.LANCZOS)
    canvas.paste(scaled, ((size - scaled.width) // 2, (size - scaled.height) // 2), scaled)
    return canvas

square(im, max(im.size)).save(f'{out_dir}/logo.png')
square(im, 32).save(f'{out_dir}/favicon-32.png')
square(im, 192).save(f'{out_dir}/favicon-192.png')
square(im, 512).save(f'{out_dir}/favicon-512.png')
# iOS shaffof fonni QORA qilib ko'rsatadi — shuning uchun oq fon beriladi.
square(im, 180, bg=(255, 255, 255, 255), pad=0.08).save(f'{out_dir}/apple-touch-icon.png')

print(f'shaffof qilingan piksel: {removed:,} | qirqilgandan keyin: {im.size}')
