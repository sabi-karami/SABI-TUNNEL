// SABI TUNNEL feed mirror (Cloudflare Worker "sabi-tunnel-mirror" on sabitunnel.sahebkarami.ir).
// The apps read their default server list and new builds from /app/*; when the panel host itself is hard
// to reach, this copy on Cloudflare still answers. Read-only: only GET/HEAD of /app/* are passed through.
const ORIGIN = "https://ttpanel-oh.sahebkarami.ir";

export default {
  async fetch(req) {
    const url = new URL(req.url);
    if (req.method !== "GET" && req.method !== "HEAD") {
      return new Response("method not allowed", { status: 405, headers: { allow: "GET, HEAD" } });
    }
    if (url.pathname === "/" || url.pathname === "/app/v1/ping") {
      return Response.json({ ok: true, brand: "SABI TUNNEL", mirror: true });
    }
    if (!url.pathname.startsWith("/app/")) return new Response("not found", { status: 404 });
    const feed = url.pathname === "/app/v1/feed";
    const dl = url.pathname.startsWith("/app/dl/");
    const ttl = feed ? 30 : dl ? 86400 : 60;
    let r;
    try {
      r = await fetch(ORIGIN + url.pathname + url.search, {
        headers: { "user-agent": "sabi-tunnel-mirror" },
        cf: { cacheEverything: true, cacheTtlByStatus: { "200-299": ttl, "404": 15, "500-599": 0 } },
      });
    } catch (e) {
      return new Response("origin unreachable", { status: 502 });
    }
    const h = new Headers(r.headers);
    h.delete("set-cookie");
    h.set("cache-control", dl && r.ok ? "public, max-age=86400" : "no-store");
    h.set("x-sabi-mirror", "1");
    return new Response(req.method === "HEAD" ? null : r.body, { status: r.status, headers: h });
  },
};
