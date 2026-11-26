import { cookies } from "next/headers";
import { LANG_COOKIE, messagesFor, parseLang, type Lang } from "./languages.ts";

/** The language this request asked for (its cookie), for server components and metadata. */
export async function requestLang(): Promise<Lang> {
  return parseLang((await cookies()).get(LANG_COOKIE)?.value);
}

export async function requestMessages() {
  return messagesFor(await requestLang());
}
