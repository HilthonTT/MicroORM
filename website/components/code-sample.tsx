import { codeToHtml } from "shiki";

/** A C# sample highlighted at build time, styled like the docs' code blocks. */
export async function CodeSample({
  code,
  title,
  className = "",
}: {
  code: string;
  title?: string;
  className?: string;
}) {
  const html = await codeToHtml(code.trim(), { lang: "csharp", theme: "vesper" });
  return (
    <div className={`code-sample min-w-0 ${className}`}>
      {title && (
        <p className="mb-2 font-mono text-xs uppercase tracking-widest text-mist-400">
          {title}
        </p>
      )}
      <div dangerouslySetInnerHTML={{ __html: html }} />
    </div>
  );
}
