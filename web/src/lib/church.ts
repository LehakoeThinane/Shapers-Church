// Facts about the church used across the website. Change them here, not in the pages.

export const church = {
  name: 'Shapers Church',
  tagline: 'Building productive people for the kingdom of God',
  address: '8 Mellis Road, Rivonia, Sandton, Johannesburg',
  shortAddress: '8 Mellis Road, Rivonia',
  mapsUrl: 'https://www.google.com/maps/search/?api=1&query=8+Mellis+Road%2C+Rivonia%2C+Sandton',
  email: 'info@shaperschurch.com',
  phones: ['084 954 8906', '072 141 9977'],
  sunday: { day: 'Sundays', time: '10:00' },
  midweek: { day: 'Wednesdays', time: '19:00', where: 'on Zoom', zoomId: '256 343 3552' },
  founded: 2011,
  // Accounts without an address yet are left null and stay hidden until one is added.
  social: [
    { label: 'Facebook', href: 'https://www.facebook.com/ShapersChurch/' },
    { label: 'Instagram', href: null },
    { label: 'YouTube', href: 'https://www.youtube.com/channel/UCZf66xLSk4RyXbMzI_lVf-g' },
    { label: 'X', href: 'https://x.com/shaperschurch' },
  ] as { label: string; href: string | null }[],
  /** The church's WhatsApp Business number (e.g. '082 123 4567'), for a "Chat with us" link. */
  whatsapp: null as string | null,
  giving: {
    bank: 'Standard Bank',
    accountName: 'Shapers Church',
    accountNumber: '061 471 976',
    branchCode: '051001',
    swift: 'SBZAZAJJ',
    cardUrl: 'https://pay.yoco.com/shapers-church',
  },
} as const;

const international = (phone: string) => `27${phone.replace(/\s/g, '').replace(/^0/, '')}`;

export const telHref = (phone: string) => `tel:+${international(phone)}`;

export const whatsappHref = (phone: string) => `https://wa.me/${international(phone)}`;

/** The four steps of the Shapers Growth Track, in the church's own words. */
export const growthTrack = [
  { step: 'Purpose', text: 'Discovering your purpose in God and being born again.' },
  { step: 'Pursuit', text: 'Living His word and pursuing His heart.' },
  { step: 'Partner', text: 'Being anchored, serving and giving in church.' },
  { step: 'Produce', text: 'Activating your gifts and talents to be productive in the marketplace.' },
] as const;
