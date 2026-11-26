import Link from "next/link";
import { EmptyState, PageHeader } from "@/components/ui";
import { Icons } from "@/components/ui/icons";

/** Why the cases aren't shown, and what to do about it. */
export function AccessNotice({ problem }: { problem: string | null }) {
  const [title, description, action] =
    problem === "two_factor_required"
      ? ["Activa la verificación en dos pasos", "Los casos contienen datos sensibles: solo se abren con verificación en dos pasos (NFR-SEC-8).", <Link key="a" href="/settings?tab=account" className="text-sm font-medium text-brand-600 hover:underline">Ir a Seguridad</Link>]
      : problem === "not_a_person"
        ? ["Solo con una sesión personal", "Los casos no se abren con claves de API ni cuentas de servicio.", null]
        : ["Necesitas un rol de casos", "Pide a la administración de tu organización el rol de Análisis o Revisión.", null];
  return (
    <div>
      <PageHeader title="Casos" />
      <div className="p-6">
        <EmptyState icon={<Icons.Lock className="h-5 w-5" />} title={title as string} description={description as string} action={action} />
      </div>
    </div>
  );
}
