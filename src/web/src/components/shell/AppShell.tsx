"use client";

import { SITE_NAME } from "@/lib/site";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { signOut, switchOrganization } from "@/lib/api";
import { isPublicPath } from "@/lib/publicPaths";
import { useAuth } from "@/components/platform/AuthProvider";
import { Icons } from "@/components/ui/icons";
import { cx } from "@/components/ui";
import { useTheme, type ThemeChoice } from "./theme";

type NavItem = { href: string; label: string; icon: (p: { className?: string }) => React.ReactNode; match?: string[] };

/** The menu. A new staff page is added here (docs/extending.md). */
const NAV: { section: string; items: NavItem[] }[] = [
  {
    section: "Testigo",
    items: [{ href: "/casos", label: "Casos", icon: Icons.Shield }],
  },
];

const COLLAPSED_KEY = "testigo:navCollapsed";

function readCollapsed() {
  try {
    return typeof window !== "undefined" && localStorage.getItem(COLLAPSED_KEY) === "1";
  } catch {
    return false;
  }
}

/**
 * The frame around every signed-in page: a sidebar with the product's sections, the organization,
 * the account and the theme; the page fills the rest. Collapsible on desktop, a drawer on phones.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [mobileOpen, setMobileOpen] = useState(false);

  if (isPublicPath(pathname)) return <>{children}</>;

  const toggleCollapsed = () => {
    setCollapsed((c) => {
      try {
        localStorage.setItem(COLLAPSED_KEY, c ? "0" : "1");
      } catch {
        // Not remembered; fine.
      }
      return !c;
    });
  };

  const isActive = (item: NavItem) =>
    // Whole path segments: /casos-x isn't /casos.
    item.href === "/" ? pathname === "/" : [item.href, ...(item.match ?? [])].some((m) => pathname === m || pathname.startsWith(`${m}/`));

  // A round button on the sidebar's edge, beside the logo, so it takes no row of its own.
  const collapseToggle = (
    <button
      onClick={toggleCollapsed}
      className="absolute -right-3 top-3 z-10 hidden h-6 w-6 items-center justify-center rounded-full border border-zinc-200 bg-white text-zinc-400 shadow-sm hover:text-zinc-700 lg:flex dark:border-zinc-700 dark:bg-zinc-900 dark:hover:text-zinc-200"
      title={collapsed ? "Expandir el menú" : "Contraer el menú"}
      aria-label={collapsed ? "Expandir el menú" : "Contraer el menú"}
    >
      <Icons.ChevronRight className={cx("h-3.5 w-3.5 transition-transform", !collapsed && "rotate-180")} />
    </button>
  );

  const sidebar = (
    <nav className={cx("relative flex h-full flex-col border-r border-zinc-200 bg-zinc-50/80 dark:border-zinc-800/80 dark:bg-zinc-950", collapsed ? "w-16" : "w-60")}>
      <div className="flex h-12 items-center gap-2.5 px-4">
        <Icons.Logo className="h-7 w-7 shrink-0" />
        {!collapsed && (
          <div className="min-w-0">
            <div className="text-sm font-semibold tracking-tight text-zinc-900 dark:text-zinc-50">{SITE_NAME}</div>
            <div className="text-[10px] uppercase tracking-wider text-zinc-500">Prototipo · Fase 0</div>
          </div>
        )}
        {collapseToggle}
      </div>

      <div className="flex-1 space-y-4 overflow-y-auto overscroll-contain px-2 py-2">
        {NAV.map((group) => (
          <div key={group.section}>
            {!collapsed && <div className="px-2.5 pb-1.5 text-[10px] font-semibold uppercase tracking-wider text-zinc-400 dark:text-zinc-500">{group.section}</div>}
            <ul className="space-y-0.5">
              {group.items.map((item) => {
                const active = isActive(item);
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={() => setMobileOpen(false)}
                      title={collapsed ? item.label : undefined}
                      className={cx(
                        "group flex items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-sm transition-colors",
                        active
                          ? "bg-white font-medium text-zinc-900 shadow-sm ring-1 ring-zinc-200 dark:bg-zinc-900 dark:text-zinc-50 dark:ring-zinc-800"
                          : "text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-900/70 dark:hover:text-zinc-100",
                        collapsed && "justify-center",
                      )}
                    >
                      <item.icon className={cx("h-4 w-4 shrink-0", active ? "text-brand-500" : "text-zinc-400 group-hover:text-zinc-600 dark:group-hover:text-zinc-300")} />
                      {!collapsed && item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </div>

      <div className="space-y-1 border-t border-zinc-200 p-2 dark:border-zinc-800/80">
        <AccountCard collapsed={collapsed} onNavigate={() => setMobileOpen(false)} />
      </div>
    </nav>
  );

  // The frame is exactly the window and never scrolls itself: each column scrolls on its own.
  // `relative` on the frame and on <main> keeps absolutely positioned bits (screen-reader-only
  // labels, menus) inside them; otherwise one far down a long page made the whole window scroll.
  return (
    <div className="relative flex h-full min-h-0 flex-1 overflow-hidden print:h-auto print:overflow-visible">
      <div className="hidden h-full shrink-0 lg:block print:hidden">{sidebar}</div>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 flex lg:hidden">
          <div className="h-full">{sidebar}</div>
          <button className="flex-1 bg-black/40" aria-label="Cerrar el menú" onClick={() => setMobileOpen(false)} />
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-12 items-center gap-2 border-b border-zinc-200 px-3 lg:hidden print:hidden dark:border-zinc-800">
          <button onClick={() => setMobileOpen(true)} className="relative rounded-md p-1.5 text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-900" aria-label="Abrir el menú">
            <Icons.Menu className="h-5 w-5" />
          </button>
          <Icons.Logo className="h-6 w-6" />
          <span className="text-sm font-semibold">{SITE_NAME}</span>
        </div>
        <main className="relative min-h-0 flex-1 overflow-y-auto print:overflow-visible">{children}</main>
      </div>
    </div>
  );
}

function ThemeSwitch() {
  const [theme, setTheme] = useTheme();
  const options: [ThemeChoice, React.ReactNode, string][] = [
    ["light", <Icons.Sun key="l" className="h-3.5 w-3.5" />, "Claro"],
    ["dark", <Icons.Moon key="d" className="h-3.5 w-3.5" />, "Oscuro"],
    ["system", <Icons.Monitor key="s" className="h-3.5 w-3.5" />, "Como el sistema"],
  ];
  return (
    <div className="flex items-center justify-between rounded-md px-2 py-1">
      <span className="text-xs text-zinc-500">Tema</span>
      <div className="flex rounded-md bg-zinc-200/60 p-0.5 dark:bg-zinc-900">
        {options.map(([value, icon, label]) => (
          <button
            key={value}
            title={label}
            aria-label={label}
            aria-pressed={theme === value}
            onClick={() => setTheme(value)}
            className={cx(
              "rounded p-1 transition-colors",
              theme === value ? "bg-white text-zinc-900 shadow-sm dark:bg-zinc-700 dark:text-zinc-50" : "text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200",
            )}
          >
            {icon}
          </button>
        ))}
      </div>
    </div>
  );
}

/**
 * Who's signed in and which organization; its menu holds the organization switcher, settings,
 * platform admin, the theme and sign-out.
 */
function AccountCard({ collapsed, onNavigate }: { collapsed: boolean; onNavigate: () => void }) {
  const { me } = useAuth();
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // Close on a click outside or Escape.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (!menuRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey); };
  }, [open]);
  const [busy, setBusy] = useState(false);
  if (!me) return null;

  const orgs = me.organizations ?? [];
  const current = orgs.find((o) => o.tenant_id === me.tenant_id);
  const name = me.user?.email || "Usuario local";
  const initials = name.split(/[\s@.]+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "A";

  async function switchTo(tenantId: string) {
    setBusy(true);
    try {
      await switchOrganization(tenantId);
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- a full reload drops everything loaded for the previous organization
      window.location.assign("/casos");
    } finally {
      setBusy(false);
    }
  }

  async function logout() {
    setBusy(true);
    try {
      await signOut();
    } finally {
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- a full reload drops everything loaded for the previous organization
      window.location.assign("/login");
    }
  }

  const go = () => { setOpen(false); onNavigate(); };
  const at = (href: string) => pathname === href || pathname.startsWith(`${href}/`);
  const inMenu = at("/settings") || at("/admin");
  const itemClass = (active: boolean) => cx("flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-zinc-100 dark:hover:bg-zinc-800",
    active && "font-medium text-zinc-900 dark:text-zinc-50");

  return (
    <div className="relative" ref={menuRef}>
      <button
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        title={collapsed ? "Cuenta, ajustes y tema" : undefined}
        className={cx("flex w-full items-center gap-2.5 rounded-lg p-2 text-left hover:bg-zinc-100 dark:hover:bg-zinc-900", collapsed && "justify-center",
          inMenu && "bg-white ring-1 ring-zinc-200 dark:bg-zinc-900 dark:ring-zinc-800")}
      >
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand-400 to-brand-700 text-[11px] font-semibold text-white">
          {initials}
        </span>
        {!collapsed && (
          <span className="min-w-0 flex-1">
            <span className="block truncate text-xs font-medium text-zinc-800 dark:text-zinc-200">{current?.name ?? "Organización"}</span>
            <span className="block truncate text-[11px] text-zinc-500">{name} · {me.role}</span>
          </span>
        )}
        {!collapsed && <Icons.ChevronDown className="h-3.5 w-3.5 text-zinc-400" />}
      </button>

      {open && (
        <div className="absolute bottom-full left-0 z-50 mb-1 w-60 rounded-xl border border-zinc-200 bg-white p-1.5 shadow-xl dark:border-zinc-800 dark:bg-zinc-900">
          {orgs.length > 1 && (
            <>
              <div className="px-2 pb-1 pt-1.5 text-[10px] font-semibold uppercase tracking-wider text-zinc-400">Organizaciones</div>
              {orgs.map((o) => (
                <button key={o.tenant_id} disabled={busy || o.tenant_id === me.tenant_id} onClick={() => switchTo(o.tenant_id)}
                  className="flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm hover:bg-zinc-100 disabled:cursor-default dark:hover:bg-zinc-800">
                  <span className="truncate">{o.name}</span>
                  {o.tenant_id === me.tenant_id && <Icons.Check className="h-3.5 w-3.5 text-brand-500" />}
                </button>
              ))}
              <div className="my-1 border-t border-zinc-200 dark:border-zinc-800" />
            </>
          )}
          <Link href="/settings" onClick={go} className={itemClass(at("/settings"))}>
            <Icons.Settings className="h-3.5 w-3.5 text-zinc-400" /> Ajustes
          </Link>
          {me.user?.platform_admin && me.server.auth_mode === "accounts" && (
            <Link href="/admin" onClick={go} className={itemClass(at("/admin"))}>
              <Icons.Shield className="h-3.5 w-3.5 text-zinc-400" /> Administración
            </Link>
          )}
          <ThemeSwitch />
          {me.via === "session" && (
            <button onClick={logout} disabled={busy} className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm text-rose-600 hover:bg-zinc-100 dark:text-rose-400 dark:hover:bg-zinc-800">
              <Icons.LogOut className="h-3.5 w-3.5" /> Cerrar sesión
            </button>
          )}
        </div>
      )}
    </div>
  );
}
