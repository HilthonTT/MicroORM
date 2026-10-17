export const site = {
  name: "MicroORM",
  tagline: "SQL in, objects out",
  repo: "https://github.com/HilthonTT/MicroORM",
  install: "git clone https://github.com/HilthonTT/MicroORM.git",
};

/** Prefixes public assets with the deploy base path (GitHub Pages). */
export function asset(path: string): string {
  const base = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
  return `${base}${path.startsWith("/") ? path : `/${path}`}`;
}
