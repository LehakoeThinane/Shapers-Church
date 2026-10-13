/** "R 1 250,00", the way South Africans write rands. */
export const rands = (cents: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(cents / 100);
