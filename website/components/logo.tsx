/** The woven mark from assets/logo.svg: light warp threads crossed by amber rows. */
export function LogoMark({ className = "size-7" }: { className?: string }) {
  return (
    <svg viewBox="0 0 256 256" className={className} aria-hidden>
      <defs>
        <linearGradient id="microorm-tile" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#312E81" />
          <stop offset="1" stopColor="#1E1B4B" />
        </linearGradient>
        <linearGradient id="microorm-rows" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="#FBBF24" />
          <stop offset="1" stopColor="#F59E0B" />
        </linearGradient>
      </defs>
      <rect width="256" height="256" rx="56" fill="url(#microorm-tile)" />
      <g fill="#E0E7FF">
        <rect x="57" y="44" width="22" height="168" rx="11" />
        <rect x="97" y="44" width="22" height="168" rx="11" />
        <rect x="137" y="44" width="22" height="168" rx="11" />
        <rect x="177" y="44" width="22" height="168" rx="11" />
      </g>
      <g fill="url(#microorm-rows)" stroke="#1E1B4B" strokeWidth="5" paintOrder="stroke">
        <rect x="40" y="77" width="176" height="22" rx="11" />
        <rect x="40" y="117" width="176" height="22" rx="11" />
        <rect x="40" y="157" width="176" height="22" rx="11" />
      </g>
      <g fill="#E0E7FF" stroke="#1E1B4B" strokeWidth="5" paintOrder="stroke">
        <rect x="97" y="72" width="22" height="32" />
        <rect x="177" y="72" width="22" height="32" />
        <rect x="57" y="112" width="22" height="32" />
        <rect x="137" y="112" width="22" height="32" />
        <rect x="97" y="152" width="22" height="32" />
        <rect x="177" y="152" width="22" height="32" />
      </g>
    </svg>
  );
}

export function Logo() {
  return (
    <span className="flex items-center gap-2.5">
      <LogoMark />
      <span className="font-mono text-lg font-semibold tracking-tight">
        MicroORM
      </span>
    </span>
  );
}
