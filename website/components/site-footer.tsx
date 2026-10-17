import Link from "next/link";

import { site } from "@/lib/site";
import { Logo } from "./logo";

export function SiteFooter() {
  return (
    <footer className="border-t border-white/5">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-5 py-10 text-sm text-mist-400 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-4">
          <Logo />
          <span>MIT licensed · built for .NET 10</span>
        </div>
        <div className="flex gap-6">
          <Link href="/docs/" className="hover:text-white">
            Docs
          </Link>
          <Link href="/docs/getting-started/" className="hover:text-white">
            Getting started
          </Link>
          <a href={site.repo} className="hover:text-white">
            GitHub
          </a>
          <a href={`${site.repo}/blob/main/CHANGELOG.md`} className="hover:text-white">
            Changelog
          </a>
        </div>
      </div>
    </footer>
  );
}
