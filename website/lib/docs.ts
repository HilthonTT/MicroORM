export type DocLink = { title: string; href: string };
export type DocSection = { title: string; links: DocLink[] };

/** Order of the docs sidebar and the previous/next links. */
export const docs: DocSection[] = [
  {
    title: "Getting started",
    links: [
      { title: "Introduction", href: "/docs/" },
      { title: "Getting started", href: "/docs/getting-started/" },
    ],
  },
  {
    title: "Guides",
    links: [
      { title: "Interpolated SQL", href: "/docs/sql/" },
      { title: "Mapping", href: "/docs/mapping/" },
      { title: "Querying", href: "/docs/querying/" },
      { title: "CRUD & transactions", href: "/docs/crud-and-transactions/" },
      { title: "Joins & result sets", href: "/docs/joins-and-result-sets/" },
      { title: "Type handlers", href: "/docs/type-handlers/" },
      { title: "Stored procedures", href: "/docs/stored-procedures/" },
    ],
  },
  {
    title: "Reference",
    links: [
      { title: "Databases", href: "/docs/databases/" },
      { title: "Benchmarks", href: "/docs/benchmarks/" },
      { title: "Architecture", href: "/docs/architecture/" },
    ],
  },
];

export const allDocs: DocLink[] = docs.flatMap((s) => s.links);
