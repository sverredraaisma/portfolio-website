<script setup lang="ts">
import 'leaflet/dist/leaflet.css'

type Pin = {
  username: string
  isAdmin: boolean
  latitude: number
  longitude: number
  label?: string | null
  precisionDecimals?: number
}

// Match the tier names shown on /account so visitors who set their own
// precision recognise the language. Older callers that don't pass
// precisionDecimals get a blank precision line in the popup.
const PRECISION_LABEL: Record<number, string> = {
  5: 'exact (~1 m)',
  4: 'building (~11 m)',
  3: 'block (~110 m)',
  2: 'neighbourhood (~1 km)',
  1: 'city (~11 km)',
  0: 'region (~110 km)'
}

const props = defineProps<{
  pins: Pin[]
  // Optional fallback centre for when there are no pins yet (defaults to a
  // mid-Europe view — recognisable for most visitors and not arbitrary).
  fallbackCenter?: [number, number]
}>()

const mapEl = ref<HTMLElement | null>(null)
let map: any = null
let markers: any[] = []

// Leaflet ships its own marker icons but the default-import paths break
// with bundlers (the resolver doesn't see them as JS). The simplest fix
// for a small site is to draw our own marker via divIcon + a CSS class.

async function ensureMap() {
  if (map || !mapEl.value || typeof window === 'undefined') return
  const L = (await import('leaflet')).default
  map = L.map(mapEl.value, { worldCopyJump: true })
    .setView(props.fallbackCenter ?? [50, 10], 4)

  // OSM tiles. Required attribution per OSM's usage policy.
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
  }).addTo(map)

  drawMarkers(L)
}

async function drawMarkers(L?: any) {
  if (!map) return
  const Lib = L ?? (await import('leaflet')).default
  // Wipe + redraw on every change. The pin set is small enough that
  // delta-tracking would be more code than it's worth.
  for (const m of markers) m.remove()
  markers = []

  for (const pin of props.pins) {
    const icon = Lib.divIcon({
      className: 'pin',
      // The marker is the cyan dot; the username floats above it as a
      // small chip so a viewer can read who's where without clicking.
      html: `
        <div class="pin-stack">
          <span class="pin-name${pin.isAdmin ? ' pin-name-admin' : ''}">${escapeHtml(pin.username)}</span>
          <span class="pin-dot${pin.isAdmin ? ' pin-dot-admin' : ''}"></span>
        </div>
      `,
      iconSize: [16, 24],
      iconAnchor: [8, 24]
    })
    const marker = Lib.marker([pin.latitude, pin.longitude], { icon }).addTo(map)
    // Build a popup whenever there's anything beyond the username worth
    // showing — a label, a precision tier, or both.
    const lines: string[] = []
    if (pin.label) lines.push(escapeHtml(pin.label))
    const precisionText = pin.precisionDecimals !== undefined ? PRECISION_LABEL[pin.precisionDecimals] : undefined
    if (precisionText) lines.push(`<span class="pin-popup-precision">precision: ${escapeHtml(precisionText)}</span>`)
    if (lines.length > 0) {
      marker.bindPopup(`<strong>${escapeHtml(pin.username)}</strong><br>${lines.join('<br>')}`)
    }
    markers.push(marker)
  }

  // Auto-fit to the markers when there's at least one. Single-pin → keep
  // the map at a sensible zoom level rather than zooming all the way in.
  if (props.pins.length === 1) {
    map.setView([props.pins[0].latitude, props.pins[0].longitude], 8)
  } else if (props.pins.length > 1) {
    const bounds = Lib.latLngBounds(props.pins.map(p => [p.latitude, p.longitude]))
    map.fitBounds(bounds, { padding: [40, 40], maxZoom: 12 })
  }
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c]!))
}

onMounted(ensureMap)
watch(() => props.pins, () => drawMarkers(), { deep: true })
onBeforeUnmount(() => {
  if (map) { map.remove(); map = null }
  markers = []
})
</script>

<template>
  <div
    ref="mapEl"
    class="w-full h-[60vh] min-h-[300px] rounded border border-zinc-300 dark:border-zinc-800 overflow-hidden"
  />
</template>

<style>
/* Marker stack: chip on top, dot below — anchored at the dot's bottom
   centre so :hover is intuitive. Loaded globally (no scoped) so the
   inline divIcon HTML can find the rules. */
.pin .pin-stack {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  pointer-events: none;
}
.pin .pin-name {
  font-family: ui-monospace, "JetBrains Mono", monospace;
  font-size: 10px;
  line-height: 1;
  padding: 2px 4px;
  border-radius: 3px;
  background: rgba(6, 182, 212, 0.92);  /* cyan-500 */
  color: #000;
  white-space: nowrap;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
  pointer-events: auto;
}
.pin .pin-name-admin {
  background: rgba(248, 113, 113, 0.92); /* red-400 */
  color: #fff;
}
.pin .pin-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: rgb(6, 182, 212);
  border: 2px solid #fff;
  box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.5);
}
.pin .pin-dot-admin {
  background: rgb(248, 113, 113);
}

/* Secondary line in popups — visually distinct from the user-supplied label. */
.pin-popup-precision {
  display: inline-block;
  font-size: 11px;
  color: #6b7280;
}

/* Tone down Leaflet's default attribution box in dark mode. */
.dark .leaflet-container .leaflet-control-attribution {
  background: rgba(0, 0, 0, 0.6);
  color: #d4d4d8;
}
.dark .leaflet-container .leaflet-control-attribution a {
  color: rgb(34, 211, 238);
}
</style>
