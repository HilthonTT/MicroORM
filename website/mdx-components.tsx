import type { MDXComponents } from "mdx/types";
import Link from "next/link";

import { Callout } from "@/components/mdx-parts";

/** Internal links go through next/link so they get the GitHub Pages base path. */
function Anchor({ href = "", ...props }: React.ComponentProps<"a">) {
  return href.startsWith("/") ? <Link href={href} {...props} /> : <a href={href} {...props} />;
}

export function useMDXComponents(components: MDXComponents): MDXComponents {
  return { ...components, a: Anchor, Callout };
}
