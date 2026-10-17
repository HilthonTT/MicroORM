import Link from "next/link";
import {
  ArrowRight,
  Braces,
  Check,
  Combine,
  Database,
  FileCode2,
  GitMerge,
  History,
  Plug,
  ShieldCheck,
  Shuffle,
  Workflow,
  Zap,
} from "lucide-react";

import { CodeSample } from "@/components/code-sample";
import { CopyCommand } from "@/components/copy-command";
import { GithubIcon } from "@/components/github-icon";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { site } from "@/lib/site";

/** Soft amber rows over indigo columns, like the threads in the logo. */
function Threads() {
  return (
    <div
      aria-hidden
      className="pointer-events-none absolute inset-x-0 top-0 -z-10 h-[56rem] overflow-hidden [mask-image:linear-gradient(to_bottom,black_45%,transparent)]"
    >
      <div className="bg-grid absolute inset-0" />
      {[0, 1, 2].map((i) => (
        <div
          key={`row-${i}`}
          className="absolute left-1/2 h-24 w-[140%] -translate-x-1/2 rounded-[100%] bg-brand blur-3xl"
          style={{ top: `${8 + i * 9}rem`, opacity: 0.09 - i * 0.02 }}
        />
      ))}
      {[0, 1, 2, 3].map((i) => (
        <div
          key={`col-${i}`}
          className="absolute top-0 h-[44rem] w-40 rounded-[100%] bg-indigo blur-3xl"
          style={{ left: `${14 + i * 22}%`, opacity: 0.35 }}
        />
      ))}
    </div>
  );
}

function Section({
  id,
  eyebrow,
  title,
  intro,
  children,
}: {
  id?: string;
  eyebrow: string;
  title: React.ReactNode;
  intro?: string;
  children: React.ReactNode;
}) {
  return (
    <section id={id} className="mx-auto max-w-7xl scroll-mt-24 px-5 py-24">
      <p className="font-mono text-xs uppercase tracking-[0.25em] text-brand">
        {eyebrow}
      </p>
      <h2 className="mt-3 max-w-3xl text-3xl font-semibold tracking-tight text-white sm:text-4xl">
        {title}
      </h2>
      {intro && (
        <p className="mt-4 max-w-2xl text-lg leading-relaxed text-mist-300">
          {intro}
        </p>
      )}
      <div className="mt-12">{children}</div>
    </section>
  );
}

const hero = `
var db = new MicroDb(SqlDialect.PostgreSql, dataSource);

// Every {hole} becomes a parameter. Plain strings don't compile.
var recent = await db.QueryAsync<Order>(
    $"SELECT * FROM orders WHERE placed_at > {since} AND status IN {statuses}");

// Typed queries translated to SQL, never silently run in memory.
var vips = await db.Table<Customer>()
    .Where(c => c.Active && c.LifetimeSpend > 10_000)
    .OrderByDescending(c => c.LifetimeSpend)
    .Take(20)
    .ToListAsync();
`;

const tour = [
  {
    title: "CRUD without SQL",
    code: `
var books = db.Table<Book>();
await books.InsertAsync(book);      // book.Id is now set
book.Finished = true;
await books.UpdateAsync(book);
var found = await books.FindAsync(book.Id);
await books.UpsertAsync(book, on: b => b.Isbn);`,
  },
  {
    title: "Set-based writes",
    code: `
await db.Table<Customer>()
    .Where(c => c.LastSeen < cutoff)
    .ExecuteUpdateAsync(s => s.Set(c => c.Active, false));

await db.Table<Session>()
    .Where(s => s.ExpiresAt < now)
    .ExecuteDeleteAsync();`,
  },
  {
    title: "Projections and aggregates",
    code: `
var names = await db.Table<Customer>()
    .Where(c => c.Active)
    .Select(c => new { c.Id, c.Name })
    .ToListAsync();

var revenue = await db.Table<Order>()
    .Where(o => o.Paid)
    .SumAsync(o => o.Total);`,
  },
  {
    title: "Joins into objects",
    code: `
var rows = await db.QueryAsync<Order, Customer, (Order, Customer?)>(
    $"""
    SELECT o.id, o.total, c.id, c.name
    FROM orders o LEFT JOIN customers c ON c.id = o.customer_id
    """,
    (o, c) => (o, c));`,
  },
];

const features = [
  {
    icon: ShieldCheck,
    title: "Injection-safe by type",
    text: "Query methods take Sql, an interpolated string handler. Values become parameters; concatenated strings don't compile.",
  },
  {
    icon: Braces,
    title: "Composable fragments",
    text: "Build WHERE clauses from pieces, expand collections to IN lists, and quote identifiers with Sql.Identifier.",
  },
  {
    icon: Workflow,
    title: "Honest translation",
    text: "A small, documented LINQ subset. Anything else throws and points you to .Where($\"...\") instead of running in memory.",
  },
  {
    icon: Combine,
    title: "One session model",
    text: "MicroDb opens a pooled connection per call; MicroTransaction has the same API on one connection.",
  },
  {
    icon: History,
    title: "Optimistic concurrency",
    text: "Mark a version column and updates or deletes of stale rows throw MicroConcurrencyException.",
  },
  {
    icon: GitMerge,
    title: "Atomic upserts",
    text: "Insert or update in one statement, matched on the key or on any unique columns, on every database.",
  },
  {
    icon: Shuffle,
    title: "Type handlers",
    text: "Value objects, strongly typed IDs, JSON columns and enums as text, in both directions.",
  },
  {
    icon: Zap,
    title: "Sync and async",
    text: "Every API has both forms, written once. Reads keep pace with Dapper in time and memory.",
  },
];

const comparison: [string, string, string, string, string][] = [
  ["Raw SQL, mapped to objects", "yes", "yes", "yes", "yes"],
  ["SQL injection blocked by the type system", "—", "—", "FromSql", "every API"],
  ["Typed LINQ-style queries", "—", "—", "yes", "a focused subset"],
  ["Unsupported query throws, no client eval", "n/a", "n/a", "yes", "yes"],
  ["CRUD without writing SQL", "—", "yes", "yes", "yes"],
  ["Composite keys and optimistic concurrency", "—", "—", "yes", "yes"],
  ["Atomic upsert", "—", "—", "—", "yes"],
  ["Multi-mapping joins, multiple result sets", "yes", "—", "—", "yes"],
  ["Stored procedures with outputs", "yes", "yes", "raw SQL", "yes"],
  ["Change tracking, migrations", "—", "—", "yes", "by design, no"],
  ["Dependencies", "none", "none", "many", "none"],
];

const databases = [
  { name: "SQLite", dialect: "SqlDialect.Sqlite", provider: "Microsoft.Data.Sqlite" },
  { name: "PostgreSQL", dialect: "SqlDialect.PostgreSql", provider: "Npgsql · PostgreSQL 17" },
  { name: "SQL Server", dialect: "SqlDialect.SqlServer", provider: "Microsoft.Data.SqlClient · 2022" },
  { name: "MySQL", dialect: "SqlDialect.MySql", provider: "MySqlConnector · MySQL 8.4" },
];

function Cell({ value, highlight }: { value: string; highlight?: boolean }) {
  if (value === "yes") {
    return <Check className={`mx-auto size-4 ${highlight ? "text-brand" : "text-mist-300"}`} />;
  }
  return <span className={highlight ? "font-medium text-brand-light" : "text-mist-400"}>{value}</span>;
}

export default function Home() {
  return (
    <>
      <SiteHeader />
      <main className="relative isolate">
        <Threads />

        {/* Hero */}
        <section className="mx-auto max-w-7xl px-5 pt-20 pb-12 text-center sm:pt-28">
          <Link
            href="/docs/"
            className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.03] px-4 py-1.5 text-xs text-mist-300 transition hover:border-white/25 hover:text-white"
          >
            <span className="size-1.5 rounded-full bg-ok" />
            .NET 10 · SQLite, PostgreSQL, SQL Server, MySQL
            <ArrowRight className="size-3" />
          </Link>
          <h1 className="mx-auto mt-8 max-w-4xl text-5xl font-semibold tracking-tight text-white sm:text-7xl">
            SQL in, <span className="text-woven">objects out.</span>
          </h1>
          <p className="mx-auto mt-6 max-w-2xl text-lg leading-relaxed text-mist-300 sm:text-xl">
            MicroORM is a small, provider-agnostic ORM for .NET. Write SQL where
            SQL is the best tool, typed queries where they&apos;re clearer, and
            every value you pass is a parameter.
          </p>
          <div className="mt-10 flex flex-col items-center gap-4">
            <CopyCommand command={site.install} />
            <div className="flex flex-wrap justify-center gap-3">
              <Link
                href="/docs/getting-started/"
                className="inline-flex items-center gap-2 rounded-xl bg-white px-5 py-2.5 text-sm font-semibold text-ink-900 transition hover:bg-mist-100"
              >
                Get started <ArrowRight className="size-4" />
              </Link>
              <a
                href={site.repo}
                className="inline-flex items-center gap-2 rounded-xl border border-white/10 px-5 py-2.5 text-sm font-semibold text-white transition hover:border-white/30"
              >
                <GithubIcon /> Star on GitHub
              </a>
            </div>
          </div>
        </section>

        <div className="relative mx-auto max-w-4xl px-5">
          <div
            aria-hidden
            className="absolute inset-x-16 top-10 -z-10 h-[80%] rounded-full bg-gradient-to-r from-brand/20 via-indigo/40 to-brand/15 blur-3xl"
          />
          <div className="rounded-2xl border border-white/10 bg-ink-850/80 p-2 shadow-2xl shadow-black/50">
            <div className="flex items-center gap-1.5 px-3 py-2">
              <span className="size-2.5 rounded-full bg-white/15" />
              <span className="size-2.5 rounded-full bg-white/15" />
              <span className="size-2.5 rounded-full bg-white/15" />
              <span className="ml-3 font-mono text-xs text-mist-400">Program.cs</span>
            </div>
            <CodeSample code={hero} />
          </div>
        </div>

        {/* Tour */}
        <Section
          id="tour"
          eyebrow="A quick tour"
          title={
            <>
              Less ceremony than a full ORM,{" "}
              <span className="text-woven">more help than raw ADO.NET</span>.
            </>
          }
          intro="Map rows to plain classes by convention, query them with lambdas or interpolated SQL, and write them back with one call. No context, no change tracker, no migrations."
        >
          <div className="grid gap-5 lg:grid-cols-2">
            {tour.map((t) => (
              <div
                key={t.title}
                className="rounded-3xl border border-white/[0.07] bg-ink-850 p-5"
              >
                <CodeSample code={t.code} title={t.title} />
              </div>
            ))}
          </div>
        </Section>

        {/* Features */}
        <Section
          id="features"
          eyebrow="Features"
          title={
            <>
              Everything you reach for daily,{" "}
              <span className="text-woven">nothing you don&apos;t</span>.
            </>
          }
        >
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {features.map(({ icon: Icon, title, text }) => (
              <div
                key={title}
                className="rounded-2xl border border-white/[0.07] bg-ink-850 p-6"
              >
                <span className="inline-flex rounded-xl bg-white/5 p-2.5 text-brand-light ring-1 ring-white/10">
                  <Icon className="size-5" />
                </span>
                <h3 className="mt-4 font-semibold text-white">{title}</h3>
                <p className="mt-1.5 text-sm leading-relaxed text-mist-300">{text}</p>
              </div>
            ))}
          </div>
        </Section>

        {/* Comparison */}
        <Section
          id="compare"
          eyebrow="Compared"
          title="Between a micro-ORM and a full ORM."
          intro="MicroORM takes ideas from the libraries it learned from and adds a few of its own. It shares no code with any of them."
        >
          <div className="overflow-x-auto rounded-2xl border border-white/[0.07]">
            <table className="w-full min-w-[40rem] text-sm">
              <thead className="bg-ink-850 text-left">
                <tr>
                  <th className="px-5 py-3 font-medium text-mist-300" />
                  {["Dapper", "Massive", "EF Core", "MicroORM"].map((h) => (
                    <th
                      key={h}
                      className={`px-5 py-3 text-center font-medium ${h === "MicroORM" ? "text-brand-light" : "text-mist-300"}`}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {comparison.map(([label, ...values]) => (
                  <tr key={label} className="border-t border-white/5">
                    <td className="px-5 py-3 text-mist-100">{label}</td>
                    {values.map((v, i) => (
                      <td key={i} className="px-5 py-3 text-center">
                        <Cell value={v} highlight={i === 3} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Section>

        {/* Databases */}
        <Section
          id="databases"
          eyebrow="Databases"
          title={
            <>
              Bring your provider.{" "}
              <span className="text-woven">MicroORM brings the dialect.</span>
            </>
          }
          intro="The library references no database driver. Pick an ADO.NET provider, pass a SqlDialect, and the integration suite runs against every database in CI."
        >
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {databases.map((d) => (
              <div
                key={d.name}
                className="rounded-2xl border border-white/[0.07] bg-ink-850 p-6"
              >
                <Database className="size-5 text-brand-light" />
                <h3 className="mt-4 font-semibold text-white">{d.name}</h3>
                <p className="mt-2 font-mono text-xs text-brand-light">{d.dialect}</p>
                <p className="mt-1 text-sm text-mist-400">{d.provider}</p>
              </div>
            ))}
          </div>
        </Section>

        {/* Call to action */}
        <section className="mx-auto max-w-7xl px-5 pb-28">
          <div className="relative overflow-hidden rounded-3xl border border-white/10 bg-ink-850 px-8 py-16 text-center">
            <div
              aria-hidden
              className="absolute inset-0 -z-10 bg-gradient-to-br from-indigo/50 via-transparent to-brand-deep/40"
            />
            <div className="mx-auto flex max-w-xl flex-col items-center">
              <div className="flex gap-3 text-brand-light">
                <FileCode2 className="size-6" />
                <Plug className="size-6" />
              </div>
              <h2 className="mt-5 text-3xl font-semibold tracking-tight text-white">
                From zero to first query in five minutes.
              </h2>
              <p className="mt-3 text-mist-300">
                Reference the project, pick a provider, and map your first table.
              </p>
              <Link
                href="/docs/getting-started/"
                className="mt-8 inline-flex items-center gap-2 rounded-xl bg-white px-5 py-2.5 text-sm font-semibold text-ink-900 transition hover:bg-mist-100"
              >
                Read the guide <ArrowRight className="size-4" />
              </Link>
            </div>
          </div>
        </section>
      </main>
      <SiteFooter />
    </>
  );
}
