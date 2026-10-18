// Google Drive browser adapter: stream directly to the client, without persistent caches.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) =>
  event.waitUntil(self.clients.claim()),
);
const sources = new Map();
const sizes = new Map();
class AuthorizationRequired extends Error {}
self.addEventListener("message", (event) => {
  if (event.data?.type === "forget-media" && event.source?.id) {
    sizes.delete(event.source.id);
    return;
  }
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
        : reject(new AuthorizationRequired("Authorization required"));
    };
    client.postMessage({ type: "drive-token", force }, [channel.port2]);
  });
}
async function stream(request, clientId, id) {
  try {
    let token = await tokenFor(clientId);
    const parameters = new URL(request.url).searchParams;
    const previewSize = parameters.get("preview");
    const thumbnail = parameters.has("thumbnail") || previewSize !== null;
    const source =
      sources.get(clientId) ?? "https://www.googleapis.com/drive/v3/files";
    const uri = mediaEndpoint(source, id, thumbnail, previewSize);
    async function get(url, ranged = true) {
      const headers = new Headers({ Authorization: `Bearer ${token}` });
      if (ranged && !thumbnail && request.headers.has("Range"))
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
      const target = thumbnailTarget(json.thumbnailLink, source, previewSize);
      // The API returns a short-lived signed thumbnail URL. Sending an OAuth
      // header to googleusercontent triggers a preflight it does not support.
      response = await fetch(target.href, {
        cache: "no-store",
        credentials: "omit",
        redirect: "follow",
      });
    }
    const headers = await mediaHeaders(
      response,
      request,
      thumbnail,
      clientId,
      id,
      source,
      get,
    );
    headers.set("Cache-Control", "no-store");
    headers.set("X-Content-Type-Options", "nosniff");
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  } catch (error) {
    return new Response(null, {
      status: error instanceof AuthorizationRequired ? 401 : 502,
      headers: { "Cache-Control": "no-store" },
    });
  }
}
function mediaEndpoint(source, id, thumbnail, previewSize) {
  const fields = previewSize !== null ? "thumbnailLink,size" : "thumbnailLink";
  const query = thumbnail ? "fields=" + fields : "alt=media";
  return `${source}/${encodeURIComponent(id)}?${query}&supportsAllDrives=true`;
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

async function restoreContentRange(
  headers,
  request,
  clientId,
  id,
  source,
  get,
) {
  // Drive's CORS exposes Content-Length, but not Content-Range. Reconstruct it
  // from the requested range, returned byte count and authoritative file size.
  let clientSizes = sizes.get(clientId);
  if (!clientSizes) {
    clientSizes = new Map();
    sizes.set(clientId, clientSizes);
  }
  let size = clientSizes.get(id);
  if (!size) {
    const metadata = await get(
      `${source}/${encodeURIComponent(id)}?fields=size&supportsAllDrives=true`,
      false,
    );
    if (!metadata.ok) throw new Error("Media metadata unavailable");
    size = Number((await metadata.json()).size);
    if (!Number.isSafeInteger(size) || size <= 0)
      throw new Error("Invalid media size");
    // Metadata only, bounded and scoped to the requesting browser client.
    if (clientSizes.size >= 64)
      clientSizes.delete(clientSizes.keys().next().value);
    clientSizes.set(id, size);
  }
  headers.set(
    "Content-Range",
    reconstructedRange(
      request.headers.get("Range"),
      Number(headers.get("Content-Length")),
      size,
    ),
  );
  headers.set("Accept-Ranges", "bytes");
}

function thumbnailTarget(link, source, previewSize) {
  const target = new URL(link);
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
    throw new Error("Invalid thumbnail URL");
  // Drive thumbnails are screen-sized on TV; original bytes remain available via alt=media.
  if (
    previewSize !== null &&
    target.hostname.endsWith(".googleusercontent.com")
  ) {
    const size = Math.max(256, Math.min(1920, Number(previewSize) || 1280));
    target.href = target.href.replace(/=s\d+[^/?]*$/, "=s" + size);
  }

  return target;
}

async function mediaHeaders(
  response,
  request,
  thumbnail,
  clientId,
  id,
  source,
  get,
) {
  const headers = new Headers();
  for (const key of [
    "Content-Type",
    "Content-Length",
    "Content-Range",
    "Accept-Ranges",
  ])
    if (response.headers.has(key)) headers.set(key, response.headers.get(key));
  if (!thumbnail && response.status === 206 && !headers.has("Content-Range"))
    await restoreContentRange(headers, request, clientId, id, source, get);
  return headers;
}

function requestedBounds(value, size) {
  const range = /^bytes=(\d*)-(\d*)$/.exec(value ?? "");
  if (!range || (!range[1] && !range[2])) throw new Error("Missing byte range");
  const start = range[1]
    ? Number(range[1])
    : Math.max(0, size - Number(range[2]));
  const limit = range[2] && range[1] ? Number(range[2]) : size - 1;
  return { start, limit };
}
function reconstructedRange(value, length, size) {
  const { start, limit } = requestedBounds(value, size);
  const end = start + length - 1;
  if (![start, end, length].every(Number.isSafeInteger))
    throw new Error("Invalid byte range");
  if (start < 0 || length <= 0 || end >= size || end > limit)
    throw new Error("Invalid byte range");
  return `bytes ${start}-${end}/${size}`;
}
