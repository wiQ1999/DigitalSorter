// Google Drive browser adapter: stream directly to the client, without persistent caches.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) =>
  event.waitUntil(self.clients.claim()),
);
const sources = new Map();
self.addEventListener("message", (event) => {
  if (event.data?.type !== "configure-source" || !event.source?.id) return;
  const uri = new URL(event.data.filesEndpoint);
  if (
    uri.href === "https://www.googleapis.com/drive/v3/files" ||
    (uri.origin === self.location.origin &&
      uri.pathname === "/test-drive/files")
  )
    sources.set(event.source.id, uri.href);
});
async function tokenFor(clientId, force = false) {
  const client = await self.clients.get(clientId);
  if (!client) throw new Error("Missing requesting client");
  const channel = new MessageChannel();
  return await new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => reject(new Error("Token unavailable")),
      20000,
    );
    channel.port1.onmessage = (event) => {
      clearTimeout(timer);
      channel.port1.close();
      event.data.token
        ? resolve(event.data.token)
        : reject(new Error("Authorization required"));
    };
    client.postMessage({ type: "drive-token", force }, [channel.port2]);
  });
}
async function stream(request, clientId, id) {
  try {
    let token = await tokenFor(clientId);
    const thumbnail = new URL(request.url).searchParams.has("thumbnail");
    const source =
      sources.get(clientId) ?? "https://www.googleapis.com/drive/v3/files";
    let uri = `${source}/${encodeURIComponent(id)}?${thumbnail ? "fields=thumbnailLink" : "alt=media"}&supportsAllDrives=true`;
    async function get(url) {
      const headers = new Headers({ Authorization: `Bearer ${token}` });
      if (!thumbnail && request.headers.has("Range"))
        headers.set("Range", request.headers.get("Range"));
      return fetch(url, {
        headers,
        cache: "no-store",
        credentials: "omit",
        redirect: "follow",
      });
    }
    let response = await get(uri);
    if (response.status === 401) {
      token = await tokenFor(clientId, true);
      response = await get(uri);
    }
    if (thumbnail && response.ok) {
      const json = await response.json();
      if (!json.thumbnailLink) return new Response(null, { status: 404 });
      const target = new URL(json.thumbnailLink);
      if (
        target.protocol !== "https:" ||
        !(
          target.hostname.endsWith(".googleusercontent.com") ||
          target.hostname === "www.googleapis.com" ||
          (source.startsWith(self.location.origin + "/test-drive/") &&
            target.origin === self.location.origin &&
            target.pathname.startsWith("/test-drive/"))
        )
      )
        return new Response(null, { status: 404 });
      response = await get(target.href);
    }
    const headers = new Headers();
    for (const key of [
      "Content-Type",
      "Content-Length",
      "Content-Range",
      "Accept-Ranges",
    ])
      if (response.headers.has(key))
        headers.set(key, response.headers.get(key));
    headers.set("Cache-Control", "no-store");
    headers.set("X-Content-Type-Options", "nosniff");
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  } catch {
    return new Response(null, {
      status: 401,
      headers: { "Cache-Control": "no-store" },
    });
  }
}
self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (
    url.origin !== self.location.origin ||
    !url.pathname.startsWith("/drive-content/")
  )
    return;
  const id = decodeURIComponent(url.pathname.slice("/drive-content/".length));
  if (event.request.method !== "GET" || !/^[\w-]{1,200}$/.test(id)) {
    event.respondWith(new Response(null, { status: 400 }));
    return;
  }
  event.respondWith(stream(event.request, event.clientId, id));
});
