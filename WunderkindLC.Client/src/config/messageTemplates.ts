/**
 * E'lon (Telegram) va Push (Firebase) bo'limlari uchun umumiy tayyor xabar andozalari va
 * o'rinbosarlar. O'rinbosarlar yuborishda har bir o'quvchiga moslab to'ldiriladi (backend).
 */
export const messageTemplates: { label: string; text: string }[] = [
  {
    label: "Ota-onalar yig'ilishi",
    text: "Hurmatli ota-ona! {sinf} guruh ota-onalar yig'ilishi ___ kuni soat ___ da bo'lib o'tadi. Farzandingiz {fish}ning ishtiroki muhim. Iltimos, keling.",
  },
  {
    label: 'Darsga kelmayapti',
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) so'nggi kunlarda darslarga qatnashmayapti. Iltimos, sababini markazga ma'lum qiling.",
  },
  {
    label: "O'zlashtirish pasaymoqda",
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf})ning o'zlashtirishi pasaymoqda. Iltimos, qulay vaqtda markazga tashrif buyuring.",
  },
  {
    label: 'Qarzdorlik eslatmasi',
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) bo'yicha to'lov qarzi: {qarzdorlik}. Iltimos, to'lovni amalga oshiring.",
  },
]

/**
 * Matnga qo'yiladigan o'rinbosarlar — yuborishda har o'quvchiga (yoki lid/o'qituvchiga) moslab
 * to'ldiriladi. Backend: `MessageTokenizer`. O'qituvchi/lidga ba'zi tokenlar bo'sh qoladi.
 */
export const messageTokens: { token: string; label: string }[] = [
  // Ism-sharif
  { token: '{fish}', label: 'F.I.SH' },
  { token: '{ism}', label: 'Ism' },
  { token: '{familiya}', label: 'Familiya' },
  { token: '{sharif}', label: 'Otasining ismi' },
  // Guruh / moliya
  { token: '{guruh}', label: 'Guruh' },
  { token: '{sinf}', label: 'Sinf/guruh (= {guruh})' },
  { token: '{oqituvchi}', label: "Guruh o'qituvchisi F.I.SH" },
  { token: '{qarzdorlik}', label: 'Qarzdorlik' },
  { token: '{balans}', label: 'Balans' },
  // Aloqa
  { token: '{ota-ona}', label: 'Ota-ona' },
  { token: '{telefon}', label: 'Telefon (kontakt)' },
  { token: '{oquvchi_telefon}', label: "O'quvchi tel." },
  { token: '{ota}', label: 'Ota F.I.SH' },
  { token: '{ota_telefon}', label: 'Ota tel.' },
  { token: '{ona}', label: 'Ona F.I.SH' },
  { token: '{ona_telefon}', label: 'Ona tel.' },
  // O'quvchi ma'lumotlari
  { token: '{manzil}', label: 'Manzil' },
  { token: '{tugilgan}', label: "Tug'ilgan sana" },
  // Guruh dars jadvali (o'quvchining asosiy guruhidan)
  { token: '{dars_sana}', label: 'Dars sanasi (masalan 30.06.2026)' },
  { token: '{dars_vaqti}', label: 'Dars vaqti (masalan 11:20)' },
  { token: '{dars_kunlari}', label: 'Dars kunlari (masalan Du, Chor, Jum)' },
  // Markaz / sana
  { token: '{markaz}', label: 'Markaz nomi' },
  { token: '{sana}', label: 'Bugungi sana' },
  { token: '{oy}', label: 'Joriy oy' },
  { token: '{yil}', label: 'Yil' },
  // Avto-SMS hodisasiga xos (faqat mos hodisada to'ladi)
  { token: '{summa}', label: "To'lov summasi" },
  { token: '{natija}', label: 'Test natijasi' },
  { token: '{ball}', label: 'Test bali (masalan 8/10)' },
  { token: '{foiz}', label: 'Test foizi (masalan 80%)' },
  { token: '{daraja}', label: 'Test darajasi' },
  { token: '{link}', label: 'Daraja testi havolasi' },
]
