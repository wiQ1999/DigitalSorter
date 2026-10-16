let broker;
let gallery;
let activeMedia;
let full = false;
const secureRequests = new Map();

export function isNativeTv() {
  return !!globalThis.DigitalSorterNative?.postMessage;
}
export function secureStorage(operation, value) {
  if (!isNativeTv()) throw new Error("Brak natywnego magazynu poświadczeń TV.");
  return new Promise((resolve, reject) => {
    const id = crypto.randomUUID();
    const timer = setTimeout(() => {
      secureRequests.delete(id);
      reject(new Error("Magazyn TV nie odpowiada."));
    }, 10000);
    secureRequests.set(id, { resolve, reject, timer });
    globalThis.DigitalSorterNative.postMessage(
      JSON.stringify({ id, operation, value }),
    );
  });
}
globalThis.digitalSorterNativeComplete = (id, value, error) => {
  const request = secureRequests.get(id);
  if (!request) return;
  clearTimeout(request.timer);
  secureRequests.delete(id);
  if (error) request.reject(new Error("Nie można odczytać sesji TV."));
  else request.resolve(value);
};

export async function initialize(reference, tv, filesEndpoint) {
  broker = reference;
  document.body.classList.toggle("tv", tv);
  if (!("serviceWorker" in navigator))
    throw new Error(
      "Ta przeglądarka nie obsługuje bezpiecznego odtwarzania Drive.",
    );
  navigator.serviceWorker.addEventListener("message", async (event) => {
    if (event.data?.type !== "drive-token" || !event.ports[0]) return;
    if (
      event.source?.scriptURL !==
      new URL("/media-worker.js", location.origin).href
    )
      return;
    try {
      event.ports[0].postMessage({
        token: await broker.invokeMethodAsync(
          "GoogleAccessToken",
          !!event.data.force,
        ),
      });
    } catch {
      event.ports[0].postMessage({ error: true });
    }
  });
  await navigator.serviceWorker.register("/media-worker.js", { scope: "/" });
  await navigator.serviceWorker.ready;
  if (!navigator.serviceWorker.controller)
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(
        () =>
          reject(
            new Error("Nie można przygotować odtwarzacza. Odśwież stronę."),
          ),
        15000,
      );
      navigator.serviceWorker.addEventListener(
        "controllerchange",
        () => {
          clearTimeout(timeout);
          resolve();
        },
        { once: true },
      );
    });
  navigator.serviceWorker.controller.postMessage({
    type: "configure-source",
    filesEndpoint,
  });
  document.addEventListener("keydown", onKey);
  document.addEventListener("fullscreenchange", () => {
    if (!document.fullscreenElement && full) {
      full = false;
      gallery?.invokeMethodAsync("Key", "exit");
    }
  });
  globalThis.digitalSorterBack = () => {
    if (!full) return false;
    fullscreen(false);
    gallery?.invokeMethodAsync("Key", "exit");
    return true;
  };
}
export function bindGallery(reference) {
  gallery = reference;
}
export async function fullscreen(enabled) {
  full = enabled;
  if (enabled && !document.fullscreenElement) {
    try {
      await document.documentElement.requestFullscreen();
    } catch {
      /* CSS fullscreen is used by TV WebView. */
    }
  } else if (!enabled && document.fullscreenElement)
    await document.exitFullscreen();
}
function onKey(event) {
  if (event.target.matches("input,textarea,select")) return;
  if (event.key === "Escape" || event.key === "BrowserBack") {
    if (full) {
      event.preventDefault();
      fullscreen(false);
      gallery?.invokeMethodAsync("Key", "exit");
    }
    return;
  }
  if (
    event.key === "MediaPlayPause" ||
    (event.key === " " && event.target.closest(".media-stage"))
  ) {
    if (activeMedia instanceof HTMLVideoElement) {
      event.preventDefault();
      activeMedia.paused
        ? activeMedia.play().catch(() => {})
        : activeMedia.pause();
    }
    return;
  }
  const isPlayer =
    event.target === document.body || event.target.closest(".media-stage");
  if (isPlayer && ["ArrowLeft", "ArrowRight"].includes(event.key)) {
    event.preventDefault();
    gallery?.invokeMethodAsync(
      "Key",
      event.key === "ArrowRight" ? "next" : "previous",
    );
    return;
  }
  if (!document.body.classList.contains("tv") || !event.key.startsWith("Arrow"))
    return;
  const items = [
    ...document.querySelectorAll(
      'button:not(:disabled),a[href],input,[tabindex="0"]',
    ),
  ].filter(
    (el) =>
      el.getClientRects().length &&
      getComputedStyle(el).visibility !== "hidden",
  );
  const current = document.activeElement.getBoundingClientRect();
  const direction = {
    ArrowLeft: [-1, 0],
    ArrowRight: [1, 0],
    ArrowUp: [0, -1],
    ArrowDown: [0, 1],
  }[event.key];
  if (!direction) return;
  const candidates = items
    .filter((el) => el !== document.activeElement)
    .map((el) => {
      const rect = el.getBoundingClientRect();
      const dx = rect.x + rect.width / 2 - current.x - current.width / 2;
      const dy = rect.y + rect.height / 2 - current.y - current.height / 2;
      const forward = dx * direction[0] + dy * direction[1];
      const sideways = Math.abs(dx * direction[1] + dy * direction[0]);
      return { el, forward, score: forward + sideways * 3 };
    })
    .filter((x) => x.forward > 4)
    .sort((a, b) => a.score - b.score);
  if (candidates[0]) {
    event.preventDefault();
    candidates[0].el.focus();
    candidates[0].el.scrollIntoView({ block: "nearest", inline: "nearest" });
  }
}

function isHeif(name, mime) {
  return /\.(heic|heif)$/i.test(name) || /^image\/(heic|heif)$/i.test(mime);
}

export function fallbackThumbnail(image, id, name, mime, video) {
  const controller = new AbortController();
  const source = "/drive-content/" + encodeURIComponent(id);
  let disposed = false,
    objectUrl,
    preview;
  function releaseVideo() {
    if (!preview) return;
    preview.pause();
    preview.removeAttribute("src");
    preview.load();
    preview = undefined;
  }
  function show(blob) {
    if (disposed || !blob) return;
    objectUrl = URL.createObjectURL(blob);
    image.src = objectUrl;
  }
  async function render() {
    if (isHeif(name, mime)) {
      const response = await fetch(source, { signal: controller.signal });
      if (!response.ok) return;
      const { heicTo } = await import("../vendor/heic-to.js");
      if (!disposed)
        show(
          await heicTo({
            blob: await response.blob(),
            type: "image/jpeg",
            quality: 0.7,
          }),
        );
    } else if (!video) {
      image.src = source;
    } else {
      preview = document.createElement("video");
      const media = preview;
      media.preload = "metadata";
      media.muted = true;
      media.playsInline = true;
      let captured = false;
      const capture = () => {
        if (disposed || captured || media.readyState < 2) return;
        captured = true;
        const canvas = document.createElement("canvas");
        canvas.width = 256;
        canvas.height = 160;
        const scale = Math.max(256 / media.videoWidth, 160 / media.videoHeight);
        canvas
          .getContext("2d")
          .drawImage(
            media,
            (256 - media.videoWidth * scale) / 2,
            (160 - media.videoHeight * scale) / 2,
            media.videoWidth * scale,
            media.videoHeight * scale,
          );
        canvas.toBlob(show, "image/jpeg", 0.7);
        releaseVideo();
      };
      media.addEventListener("loadeddata", capture);
      media.addEventListener("seeked", capture);
      media.addEventListener(
        "loadedmetadata",
        () => {
          if (
            !disposed &&
            Number.isFinite(media.duration) &&
            media.duration > 0
          )
            media.currentTime = Math.min(0.1, media.duration / 2);
        },
        { once: true },
      );
      media.addEventListener("error", releaseVideo, { once: true });
      media.src = source;
    }
  }
  render().catch(() => {});
  return {
    dispose() {
      disposed = true;
      controller.abort();
      releaseVideo();
      image.removeAttribute("src");
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    },
  };
}

export async function mountMedia(stage, id, name, mime, video, reference) {
  const surface = stage.querySelector(".media-surface");
  const media = document.createElement(video ? "video" : "img");
  const canvas = document.createElement("canvas");
  canvas.width = 48;
  canvas.height = 32;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  let disposed = false,
    timer,
    objectUrl;
  media.setAttribute("aria-label", name);
  if (video) {
    media.controls = true;
    media.playsInline = true;
    media.preload = "metadata";
  } else {
    media.alt = name;
    media.decoding = "async";
  }
  activeMedia = media;
  surface.append(media);
  media.addEventListener("error", () => {
    if (!disposed)
      reference
        .invokeMethodAsync(
          "MediaError",
          "Plik jest niedostępny, uszkodzony lub jego kodek nie jest obsługiwany przez to urządzenie.",
        )
        .catch(() => {});
  });
  const sample = () => {
    if (
      disposed ||
      full ||
      !(video ? media.readyState >= 2 : media.complete && media.naturalWidth)
    )
      return;
    try {
      context.drawImage(media, 0, 0, 48, 32);
      const points = {
        top: [20, 0, 8, 3],
        right: [45, 12, 3, 8],
        bottom: [20, 29, 8, 3],
        left: [0, 12, 3, 8],
      };
      for (const [side, point] of Object.entries(points)) {
        const pixels = context.getImageData(...point).data;
        let r = 0,
          g = 0,
          b = 0,
          count = 0;
        for (let i = 0; i < pixels.length; i += 4) {
          if (!pixels[i + 3]) continue;
          r += pixels[i];
          g += pixels[i + 1];
          b += pixels[i + 2];
          count++;
        }
        if (count)
          stage.style.setProperty(
            `--ambient-${side}`,
            `rgb(${Math.round(r / count)} ${Math.round(g / count)} ${Math.round(b / count)})`,
          );
      }
    } catch {
      /* Unreadable frames leave a neutral background; media remains playable. */
    }
  };
  media.addEventListener(video ? "loadeddata" : "load", sample);
  if (video) timer = setInterval(sample, 350);
  const url = "/drive-content/" + encodeURIComponent(id);
  if (isHeif(name, mime)) {
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error("Unavailable");
      const { heicTo } = await import("../vendor/heic-to.js");
      const converted = await heicTo({
        blob: await response.blob(),
        type: "image/jpeg",
        quality: 0.92,
      });
      if (!disposed) {
        objectUrl = URL.createObjectURL(converted);
        media.src = objectUrl;
      }
    } catch {
      if (!disposed)
        await reference.invokeMethodAsync(
          "MediaError",
          "Nie udało się odczytać zdjęcia HEIC/HEIF. Sprawdź dostęp do pliku.",
        );
    }
  } else media.src = url;
  return {
    dispose() {
      disposed = true;
      clearInterval(timer);
      if (video) media.pause();
      media.removeAttribute("src");
      if (video) media.load();
      media.remove();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      if (activeMedia === media) activeMedia = null;
    },
  };
}
export function clearMedia() {
  if (activeMedia instanceof HTMLVideoElement) activeMedia.pause();
  activeMedia?.removeAttribute("src");
}
