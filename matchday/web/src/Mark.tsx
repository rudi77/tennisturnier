export function Mark() {
  return (
    <a className="mark" href="/" aria-label="MATCHDAY">
      <svg viewBox="0 0 32 32" width="26" height="26" aria-hidden="true">
        <circle cx="16" cy="16" r="14" fill="var(--ball)" />
        <path d="M6 8c8 2 12 8 12 16M26 8c-8 2-12 8-12 16" stroke="var(--ball-seam)" strokeWidth="2.5" fill="none" />
      </svg>
      <span>MATCHDAY</span>
    </a>
  )
}
