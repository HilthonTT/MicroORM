import { Info, TriangleAlert } from "lucide-react";

export function Callout({ type = "info", children }: { type?: "info" | "warn"; children: React.ReactNode }) {
  const warn = type === "warn";
  const Icon = warn ? TriangleAlert : Info;
  return (
    <div
      className={`not-prose my-6 flex gap-3 rounded-xl border p-4 text-sm leading-relaxed ${
        warn ? "border-warn/30 bg-warn/5 text-mist-100" : "border-info/30 bg-info/5 text-mist-100"
      }`}
    >
      <Icon className={`mt-0.5 size-4 shrink-0 ${warn ? "text-warn" : "text-info"}`} />
      <div className="[&_code]:rounded [&_code]:bg-white/10 [&_code]:px-1 [&_code]:font-mono">{children}</div>
    </div>
  );
}
