let broker;
let gallery;
let activeMedia;
let full = false;
let nativeTv = false;
let activeDialog;
const prefetched = new Map();
const contentUrl = (id) => "/drive-content/" + encodeURIComponent(id);
const photoUrl = (id) =>
  full && !nativeTv
    ? contentUrl(id)
    : contentUrl(id) + "?preview=" + (nativeTv ? 1280 : 1920);

function releaseImage(image) {
  image.removeAttribute("src");
  image.remove();
}
export function prefetchMedia(upcoming, currentId) {
  const candidate = upcoming.find((item) => !isHeif(item.name, item.mimeType));
  for (const [id, image] of prefetched) {
    if (id !== candidate?.id && id !== currentId) {
      releaseImage(image);
      prefetched.delete(id);
    }
  }
  if (!candidate || prefetched.has(candidate.id)) return;
  const image = new Image();
  image.decoding = "async";
  image.fetchPriority = "low";
  prefetched.set(candidate.id, image);
  const start = () => {
    if (prefetched.get(candidate.id) === image)
      image.src = photoUrl(candidate.id);
  };
  if (activeMedia instanceof HTMLImageElement && !activeMedia.complete)
    activeMedia.addEventListener("load", start, { once: true });
  else start();
}
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

export async function initialize(reference, tv, filesEndpoint, assetVersion) {
  broker = reference;
  nativeTv = tv;
  if (assetVersion)
    await Promise.all(
      [...document.querySelectorAll('link[rel="stylesheet"]')].map((link) =>
        refreshStylesheet(link, assetVersion),
      ),
    );
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
    if (activeDialog) {
      activeDialog.cancel();
      return true;
    }
    if (!full) return false;
    fullscreen(false);
    gallery?.invokeMethodAsync("Key", "exit");
    return true;
  };
}
export function bindGallery(reference) {
  gallery = reference;
}
export function unbindGallery() {
  gallery = null;
}
export function showReadProgress() {
  document
    .querySelector(".metadata-progress")
    ?.scrollIntoView({ block: "nearest", inline: "nearest" });
}
export function observeThumbnails(strip, reference) {
  let previous = -1;
  let disposed = false;
  const observer = new ResizeObserver(([entry]) => {
    const style = getComputedStyle(strip);
    const minimum = parseFloat(style.getPropertyValue("--thumbnail-min-width"));
    const gap = parseFloat(style.columnGap) || 0;
    const width = entry.contentRect.width;
    const capacity =
      width > 0 ? Math.max(1, Math.floor((width + gap) / (minimum + gap))) : 0;
    if (capacity === previous) return;
    previous = capacity;
    reference
      .invokeMethodAsync("SetPreviewCapacity", capacity)
      .catch((error) => {
        if (!disposed) console.error(error);
      });
  });
  observer.observe(strip);
  return {
    dispose() {
      disposed = true;
      observer.disconnect();
    },
  };
}
export async function fullscreen(enabled) {
  full = enabled;
  if (
    enabled &&
    !nativeTv &&
    activeMedia instanceof HTMLImageElement &&
    activeMedia.dataset.original
  ) {
    activeMedia.dataset.fullResolution = "true";
    activeMedia.src = activeMedia.dataset.original;
  }
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
  if (activeDialog) {
    dialogKey(event);
    return;
  }
  if (event.target.matches("input,textarea,select")) return;
  if (["Escape", "BrowserBack"].includes(event.key)) {
    if (full) {
      event.preventDefault();
      fullscreen(false);
      gallery?.invokeMethodAsync("Key", "exit");
    }
    return;
  }
  if (isPlaybackKey(event)) {
    toggleVideo(event);
    return;
  }
  if (navigateMedia(event)) return;
  if (document.body.classList.contains("tv") && event.key.startsWith("Arrow"))
    moveFocus(event);
}
function isPlaybackKey(event) {
  return (
    event.key === "MediaPlayPause" ||
    (event.key === " " && event.target.closest(".media-stage"))
  );
}

export function openDialog(element, reference) {
  const previous = document.activeElement;
  const cancel = () =>
    reference.invokeMethodAsync("CancelDisconnect").catch(() => {});
  const onCancel = (event) => {
    event.preventDefault();
    cancel();
  };
  activeDialog = { element, cancel };
  element.addEventListener("cancel", onCancel);
  element.showModal();
  element.querySelector("button").focus();
  return {
    dispose() {
      element.removeEventListener("cancel", onCancel);
      element.close();
      if (activeDialog?.element === element) activeDialog = null;
      if (previous.isConnected) previous.focus();
    },
  };
}
function dialogKey(event) {
  if (["Escape", "BrowserBack"].includes(event.key)) {
    event.preventDefault();
    activeDialog.cancel();
  } else if (event.key.startsWith("Arrow")) {
    event.preventDefault();
    moveFocus(event, activeDialog.element);
  }
}
function navigateMedia(event) {
  const isPlayer =
    event.target === document.body || event.target.closest(".media-stage");
  if (isPlayer && ["ArrowLeft", "ArrowRight"].includes(event.key)) {
    event.preventDefault();
    gallery?.invokeMethodAsync(
      "Key",
      event.key === "ArrowRight" ? "next" : "previous",
    );
    return true;
  }
  return false;
}

function moveFocus(event, root = document) {
  const items = [
    ...root.querySelectorAll(
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
  const source = contentUrl(id);
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
  if (nativeTv && (video || isHeif(name, mime))) {
    image.removeAttribute("src");
    image.alt = "Podgląd niedostępny";
  } else render().catch(() => {});
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

export function mountMedia(stage, id, name, mime, video, reference) {
  const surface = stage.querySelector(".media-surface");
  const { media, prepared } = createMedia(id, name, video);
  let disposed = false,
    objectUrl;
  activeMedia = media;
  surface.append(media);
  let originalFallback = false;
  media.addEventListener("error", () => {
    if (disposed) return;
    if (
      !video &&
      !originalFallback &&
      media.dataset.fullResolution !== "true"
    ) {
      originalFallback = true;
      if (isHeif(name, mime)) void decodeHeif();
      else media.src = contentUrl(id);
      return;
    }
    describeFailure(media, video, id, controller.signal).then((message) => {
      if (!disposed)
        reference.invokeMethodAsync("MediaError", message).catch(() => {});
    });
  });
  const ready = () => {
    if (!disposed) reference.invokeMethodAsync("MediaReady").catch(() => {});
  };
  media.addEventListener(video ? "loadeddata" : "load", ready, { once: true });
  const ambient = observeAmbient(stage, media, video, () => disposed);
  const url = contentUrl(id);
  const controller = new AbortController();
  async function decodeHeif() {
    try {
      const response = await fetch(url, { signal: controller.signal });
      if (!response.ok) throw new Error("Unavailable");
      const { heicTo } = await import("../vendor/heic-to.js");
      const blob = await response.blob();
      if (disposed) return;
      const converted = await heicTo({
        blob,
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
  }
  originalFallback = setMediaSource(
    media,
    prepared,
    id,
    name,
    mime,
    video,
    decodeHeif,
  );
  if (isReadyPhoto(media)) {
    ready();
    ambient.sample();
  }
  return {
    dispose() {
      disposed = true;
      ambient.dispose();
      controller.abort();
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
  for (const image of prefetched.values()) releaseImage(image);
  prefetched.clear();
  navigator.serviceWorker.controller?.postMessage({ type: "forget-media" });
  if (activeMedia instanceof HTMLVideoElement) activeMedia.pause();
  activeMedia?.removeAttribute("src");
}

async function describeFailure(media, video, id, signal) {
  try {
    const response = await fetch(contentUrl(id), {
      headers: { Range: "bytes=0-0" },
      signal,
    });
    await response.body?.cancel();
    const failures = {
      401: "Połącz ponownie konto Google, aby odtworzyć materiał.",
      403: "Brak uprawnień do odczytu tego pliku na Google Drive.",
      404: "Ten plik nie jest już dostępny na Google Drive.",
      429: "Limit Google Drive został przekroczony. Spróbuj ponownie później.",
    };
    if (!response.ok)
      return (
        failures[response.status] ||
        "Nie udało się pobrać materiału. Sprawdź połączenie i spróbuj ponownie."
      );
  } catch {
    return "Pobieranie materiału zostało przerwane. Sprawdź połączenie i spróbuj ponownie.";
  }
  if (!video)
    return "Nie udało się odczytać zdjęcia. Format może być nieobsługiwany lub plik uszkodzony.";
  const messages = {
    2: "Pobieranie filmu zostało przerwane. Sprawdź połączenie i spróbuj ponownie.",
    3: "Nie udało się zdekodować filmu. Plik może być uszkodzony lub używać kodeka nieobsługiwanego przez to urządzenie.",
    4: "Ten format lub kodek wideo nie jest obsługiwany przez odtwarzacz. Spróbuj wersji MP4 z wideo H.264 i dźwiękiem AAC.",
  };
  return messages[media.error?.code] || "Nie udało się otworzyć filmu.";
}

function refreshStylesheet(link, version) {
  const url = new URL(link.href);
  if (url.searchParams.get("v") === version) return Promise.resolve();
  url.searchParams.set("v", version);
  return new Promise((resolve, reject) => {
    const fresh = link.cloneNode(false);
    let timer, check;
    const ready = () => {
      clearTimeout(timer);
      clearInterval(check);
      link.remove();
      resolve();
    };
    fresh.onload = ready;
    fresh.onerror = () => {
      clearTimeout(timer);
      clearInterval(check);
      reject(new Error("Nie można wczytać stylów aplikacji."));
    };
    fresh.href = url.href;
    // A new link reliably emits load in older Android WebViews; replacing href on
    // an already loaded stylesheet can leave its load event undelivered.
    link.after(fresh);
    check = setInterval(() => {
      if (fresh.sheet?.cssRules.length) ready();
    }, 50);
    timer = setTimeout(() => (fresh.sheet ? ready() : fresh.onerror()), 10000);
  });
}

function toggleVideo(event) {
  if (!(activeMedia instanceof HTMLVideoElement)) return;
  event.preventDefault();
  if (activeMedia.paused) activeMedia.play().catch(() => {});
  else activeMedia.pause();
}

function createMedia(id, name, video) {
  const prepared = !video && prefetched.get(id);
  prefetched.delete(id);
  const media = prepared || document.createElement(video ? "video" : "img");
  media.setAttribute("aria-label", name);
  if (video) {
    media.controls = true;
    media.playsInline = true;
    media.preload = "metadata";
  } else {
    media.alt = name;
    media.decoding = "async";
    if (!nativeTv && !isHeif(name, "")) media.dataset.original = contentUrl(id);
    if (full && !nativeTv) media.dataset.fullResolution = "true";
  }
  return { media, prepared };
}
function isReadyPhoto(media) {
  return (
    media instanceof HTMLImageElement &&
    media.complete &&
    media.naturalWidth > 0
  );
}
function setMediaSource(media, prepared, id, name, mime, video, decodeHeif) {
  if (isHeif(name, mime) && !nativeTv) void decodeHeif();
  else if (!prepared || !media.getAttribute("src") || (full && !nativeTv))
    media.src = video ? contentUrl(id) : photoUrl(id);
  else if (media.complete && !media.naturalWidth) {
    media.src = contentUrl(id);
    return nativeTv;
  }
  return false;
}
function observeAmbient(stage, media, video, isDisposed) {
  if (nativeTv) return { sample() {}, dispose() {} };
  const canvas = document.createElement("canvas");
  canvas.width = 48;
  canvas.height = 32;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  const sample = () => {
    if (
      isDisposed() ||
      nativeTv ||
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
  const timer = video ? setInterval(sample, 350) : undefined;
  return { sample, dispose: () => clearInterval(timer) };
}
