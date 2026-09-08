export function normalizeHttpRangeSourceUrl(input) {
  const parsed = new URL(input);
  const driveMatch = parsed.hostname === "drive.google.com"
    ? parsed.pathname.match(/^\/file\/d\/([^/]+)\/?/)
    : null;
  if (!driveMatch) return parsed;

  // Google Drive's ordinary share/download redirect can answer Range requests with a final 200.
  // Address the public byte-serving host directly so the Range header reaches the endpoint that
  // demonstrated exact 206/Content-Range semantics in research/HTTP_RANGE_TRANSPORT.md.
  const direct = new URL("https://drive.usercontent.google.com/download");
  direct.searchParams.set("export", "download");
  direct.searchParams.set("confirm", "t");
  direct.searchParams.set("id", driveMatch[1]);
  const resourceKey = parsed.searchParams.get("resourcekey");
  if (resourceKey) direct.searchParams.set("resourcekey", resourceKey);
  return direct;
}
