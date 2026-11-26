import { redirect } from "next/navigation";

/** The dashboard's home is the case queue. */
export default function Home() {
  redirect("/casos");
}
