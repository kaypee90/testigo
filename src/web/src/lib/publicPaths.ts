/** Pages anyone can open without signing in: sign-in and the link that confirms a new account's
 * email. Everything else in the staff dashboard is behind sign-in. */
const PUBLIC_PATHS = ["/login", "/verify-email"];

export function isPublicPath(pathname: string) {
  return PUBLIC_PATHS.includes(pathname);
}
