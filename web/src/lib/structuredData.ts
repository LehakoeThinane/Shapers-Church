import type { Schemas } from './api';
import { church } from './church';

/**
 * schema.org data for search engines (JSON-LD): what lets Google show the church's address, the service and
 * upcoming events in search results and on Maps. Only facts the site already states; nothing is guessed.
 */
type Ld = Record<string, unknown>;

const international = (phone: string) => `+27${phone.replace(/\s/g, '').replace(/^0/, '')}`;

export function churchLd(site: URL): Ld {
  return {
    '@context': 'https://schema.org',
    '@type': 'Church',
    '@id': new URL('/#church', site).href,
    name: church.name,
    description: `${church.name} in Rivonia, Johannesburg: ${church.tagline.charAt(0).toLowerCase()}${church.tagline.slice(1)}.`,
    url: site.href,
    logo: new URL('/apple-touch-icon.png', site).href,
    image: new URL('/images/og-shapers.jpg', site).href,
    email: church.email,
    telephone: international(church.phones[0]),
    foundingDate: String(church.founded),
    address: {
      '@type': 'PostalAddress',
      streetAddress: '8 Mellis Road',
      addressLocality: 'Rivonia',
      addressRegion: 'Gauteng',
      addressCountry: 'ZA',
    },
    hasMap: church.mapsUrl,
    sameAs: church.social.map((s) => s.href).filter(Boolean),
  };
}

export function eventLd(e: Schemas['EventDto'], site: URL): Ld {
  const url = new URL(`/events/${e.slug}`, site).href;
  return {
    '@context': 'https://schema.org',
    '@type': 'Event',
    name: e.title,
    description: e.summary ?? undefined,
    startDate: e.startsAt,
    endDate: e.endsAt,
    eventStatus: 'https://schema.org/EventScheduled',
    eventAttendanceMode: 'https://schema.org/OfflineEventAttendanceMode',
    image: e.imageUrl ? [e.imageUrl] : undefined,
    url,
    location: {
      '@type': 'Place',
      name: e.location?.name ?? church.name,
      address: e.location?.address ?? church.address,
    },
    organizer: { '@type': 'Organization', name: church.name, url: site.href },
    // Church events are free; booking, when there is one, happens on the event page.
    offers: e.registrationRequired
      ? {
          '@type': 'Offer',
          price: 0,
          priceCurrency: 'ZAR',
          url,
          availability: e.registrationOpen ? 'https://schema.org/InStock' : 'https://schema.org/SoldOut',
        }
      : undefined,
  };
}

export function articleLd(p: Schemas['PostSummaryDto'], path: string, site: URL): Ld {
  return {
    '@context': 'https://schema.org',
    '@type': 'BlogPosting',
    headline: p.title,
    description: p.summary ?? undefined,
    datePublished: p.publishedAt,
    image: p.coverImageUrl ? [p.coverImageUrl] : undefined,
    url: new URL(path, site).href,
    author: p.author ? { '@type': 'Person', name: p.author } : { '@type': 'Organization', name: church.name },
    publisher: { '@type': 'Organization', name: church.name, logo: { '@type': 'ImageObject', url: new URL('/apple-touch-icon.png', site).href } },
  };
}
