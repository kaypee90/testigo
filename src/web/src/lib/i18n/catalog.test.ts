// The dashboard's language catalogs (src/lib/i18n). Run with `npm test` (Node's own test runner).
import { test } from "node:test";
import assert from "node:assert/strict";
import { es } from "./es.ts";
import { DEFAULT_LANG, LANGUAGES, formatAgo, formatDate, formatDateTime, parseLang, type Lang } from "./languages.ts";

type Tree = { [key: string]: unknown };

/** Every leaf of a catalog as "path → value". */
function leaves(node: unknown, path = ""): [string, unknown][] {
  if (node && typeof node === "object") {
    return Object.entries(node as Tree).flatMap(([k, v]) => leaves(v, path ? `${path}.${k}` : k));
  }
  return [[path, node]];
}

const codes = Object.keys(LANGUAGES) as Lang[];

test("Spanish is the default", () => {
  assert.equal(DEFAULT_LANG, "es");
  assert.ok(codes.includes("en"));
});

test("the §12 banner keeps its exact Spanish wording", () => {
  assert.equal(es.banner, "Prototipo con datos ficticios. No es un canal oficial de denuncia. No envíe información real.");
});

for (const code of codes) {
  test(`${code} has exactly the Spanish keys, with the same kinds of values`, () => {
    const source = new Map(leaves(es));
    const other = new Map(leaves(LANGUAGES[code]));
    assert.deepEqual([...other.keys()].sort(), [...source.keys()].sort());
    for (const [key, value] of source) {
      const translated = other.get(key);
      assert.equal(typeof translated, typeof value, key);
      if (typeof value === "function") assert.equal((translated as () => string).length, value.length, `${key}: arguments`);
    }
  });

  test(`${code} has no empty texts and every function returns text`, () => {
    for (const [key, value] of leaves(LANGUAGES[code])) {
      if (typeof value === "string") assert.notEqual(value.trim(), "", key);
      if (typeof value === "function") {
        const args = Array.from({ length: value.length }, (_, i) => (i % 2 === 0 ? 3 : "x"));
        const out = (value as (...a: unknown[]) => unknown)(...args);
        assert.equal(typeof out, "string", key);
        assert.notEqual((out as string).trim(), "", key);
      }
    }
  });

  test(`${code} names itself and has a usable locale`, () => {
    const t = LANGUAGES[code];
    assert.ok(t.language.name.length > 0);
    assert.doesNotThrow(() => new Intl.DateTimeFormat(t.language.locale));
  });
}

test("the language cookie accepts only known codes", () => {
  assert.equal(parseLang("en"), "en");
  assert.equal(parseLang("es"), "es");
  for (const bad of [undefined, null, "", "EN", "fr", "en-US", "__proto__", "constructor", "toString", "en; Path=/"]) {
    assert.equal(parseLang(bad), "es", String(bad));
  }
});

test("dates follow the language and show a dash when missing", () => {
  const iso = "2026-03-05T14:30:00Z";
  assert.equal(formatDate(null, "es-CL"), "—");
  assert.equal(formatDateTime(undefined, "en-GB"), "—");
  assert.equal(formatDate(iso, "en-GB"), new Date(iso).toLocaleDateString("en-GB"));
  assert.notEqual(formatDateTime(iso, "es-CL"), formatDateTime(iso, "en-GB"));
});

test("how long ago is worded in the language", () => {
  const now = Date.parse("2026-03-05T14:30:00Z");
  assert.equal(formatAgo(null, "en-GB", now), "—");
  assert.equal(formatAgo(new Date(now - 10_000).toISOString(), "en-GB", now), "now");
  assert.equal(formatAgo(new Date(now - 10_000).toISOString(), "es-CL", now), "ahora");
  assert.match(formatAgo(new Date(now - 5 * 60_000).toISOString(), "en-GB", now), /5 min.* ago/);
  assert.match(formatAgo(new Date(now - 5 * 60_000).toISOString(), "es-CL", now), /hace 5 min/);
  assert.match(formatAgo(new Date(now - 3 * 86_400_000).toISOString(), "en-GB", now), /3 days ago/);
  // A clock that's slightly ahead never shows a future time.
  assert.equal(formatAgo(new Date(now + 60_000).toISOString(), "en-GB", now), "now");
});
