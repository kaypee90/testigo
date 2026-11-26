/** A small line-icon set (24px grid, 1.75 stroke), so the app needs no icon dependency. */
type IconProps = { className?: string };

function Svg({ className, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round"
      className={className ?? "h-4 w-4"} aria-hidden>
      {children}
    </svg>
  );
}

export const Icons = {
  /** Testigo's mark: a speech bubble (a testimony) holding an eye (a witness). docs/assets/testigo-logo.svg. */
  Logo: ({ className }: IconProps) => (
    <svg viewBox="0 0 32 32" className={className ?? "h-7 w-7"} aria-hidden>
      <rect width="32" height="32" rx="8" className="fill-brand-500" />
      <path d="M16 5.5c6 0 10.5 4 10.5 9s-4.5 9-10.5 9c-1.3 0-2.6-.2-3.8-.55L7 25.5l1.7-4.6C6.7 19.3 5.5 17 5.5 14.5c0-5 4.5-9 10.5-9Z" fill="white" />
      <path d="M9.6 14.5C11.2 11.8 13.4 10.4 16 10.4s4.8 1.4 6.4 4.1c-1.6 2.7-3.8 4.1-6.4 4.1s-4.8-1.4-6.4-4.1Z" className="fill-brand-500" />
      <circle cx="16" cy="14.5" r="2.3" fill="white" />
      <circle cx="16" cy="14.5" r="1.1" className="fill-brand-800" />
    </svg>
  ),
  Mail: (p: IconProps) => <Svg {...p}><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m3 7 9 6 9-6" /></Svg>,
  Settings: (p: IconProps) => <Svg {...p}><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" /></Svg>,
  Plus: (p: IconProps) => <Svg {...p}><path d="M12 5v14M5 12h14" /></Svg>,
  Copy: (p: IconProps) => <Svg {...p}><rect x="9" y="9" width="12" height="12" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></Svg>,
  Check: (p: IconProps) => <Svg {...p}><path d="m5 12 5 5L20 7" /></Svg>,
  X: (p: IconProps) => <Svg {...p}><path d="M18 6 6 18M6 6l12 12" /></Svg>,
  Eye: (p: IconProps) => <Svg {...p}><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z" /><circle cx="12" cy="12" r="3" /></Svg>,
  ChevronDown: (p: IconProps) => <Svg {...p}><path d="m6 9 6 6 6-6" /></Svg>,
  ChevronRight: (p: IconProps) => <Svg {...p}><path d="m9 6 6 6-6 6" /></Svg>,
  ChevronLeft: (p: IconProps) => <Svg {...p}><path d="m15 18-6-6 6-6" /></Svg>,
  Alert: (p: IconProps) => <Svg {...p}><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /><path d="M12 9v4M12 17h.01" /></Svg>,
  Sun: (p: IconProps) => <Svg {...p}><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></Svg>,
  Moon: (p: IconProps) => <Svg {...p}><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" /></Svg>,
  Monitor: (p: IconProps) => <Svg {...p}><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></Svg>,
  Menu: (p: IconProps) => <Svg {...p}><path d="M4 6h16M4 12h16M4 18h16" /></Svg>,
  LogOut: (p: IconProps) => <Svg {...p}><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" /></Svg>,
  Replay: (p: IconProps) => <Svg {...p}><path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5" /></Svg>,
  Shield: (p: IconProps) => <Svg {...p}><path d="M12 3 4 7v6c0 4 3.4 7.2 8 8 4.6-.8 8-4 8-8V7l-8-4Z" /></Svg>,
  File: (p: IconProps) => <Svg {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8l-5-5Z" /><path d="M14 3v5h5" /></Svg>,
  Lock: (p: IconProps) => <Svg {...p}><rect x="4.5" y="10.5" width="15" height="10" rx="2" /><path d="M8 10.5V7a4 4 0 0 1 8 0v3.5" /></Svg>,
  Trash: (p: IconProps) => <Svg {...p}><path d="M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V4h6v3" /></Svg>,
  /** A speech bubble with a line in it: sending feedback. */
  /** Chevrons folding toward a line: a long conversation collapsed into a summary. */
};
