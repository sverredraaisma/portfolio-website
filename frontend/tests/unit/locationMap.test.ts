import { describe, expect, it, vi } from 'vitest'

// Leaflet pulls in DOM APIs (matchMedia, ResizeObserver) and the OSM tile
// layer makes a network request on init. Both blow up under happy-dom.
// Stub the lazy import so we can exercise the component's prop handling
// without booting the actual Leaflet runtime.
const mapStub = {
  setView: vi.fn().mockReturnThis(),
  fitBounds: vi.fn(),
  remove: vi.fn()
}
const tileStub = { addTo: vi.fn().mockReturnValue(mapStub) }
const markerStub = { addTo: vi.fn().mockReturnValue({ bindPopup: vi.fn(), remove: vi.fn() }) }
const Lstub = {
  default: {
    map: vi.fn().mockReturnValue(mapStub),
    tileLayer: vi.fn().mockReturnValue(tileStub),
    marker: vi.fn().mockReturnValue(markerStub),
    divIcon: vi.fn().mockReturnValue({}),
    latLngBounds: vi.fn().mockReturnValue({})
  }
}
vi.mock('leaflet', () => Lstub)
vi.mock('leaflet/dist/leaflet.css', () => ({}))

const { mount, flushPromises } = await import('@vue/test-utils')
const LocationMap = (await import('~/components/LocationMap.vue')).default

describe('<LocationMap>', () => {
  it('initialises a Leaflet map with the OSM tile layer', async () => {
    mount(LocationMap, { props: { pins: [] }, attachTo: document.body })

    await flushPromises()

    expect(Lstub.default.map).toHaveBeenCalledOnce()
    expect(Lstub.default.tileLayer).toHaveBeenCalledOnce()
    const tileUrl = Lstub.default.tileLayer.mock.calls[0][0]
    expect(tileUrl).toContain('openstreetmap.org')
  })

  it('draws one marker per pin', async () => {
    Lstub.default.marker.mockClear()
    mount(LocationMap, {
      props: { pins: [
        { username: 'a', isAdmin: false, latitude: 1, longitude: 2 },
        { username: 'b', isAdmin: true,  latitude: 3, longitude: 4 }
      ] },
      attachTo: document.body
    })
    await flushPromises()

    expect(Lstub.default.marker).toHaveBeenCalledTimes(2)
    expect(Lstub.default.marker.mock.calls[0][0]).toEqual([1, 2])
    expect(Lstub.default.marker.mock.calls[1][0]).toEqual([3, 4])
  })

  it('does not crash with no pins (empty fallback view)', async () => {
    Lstub.default.marker.mockClear()
    mount(LocationMap, { props: { pins: [] }, attachTo: document.body })
    await flushPromises()

    expect(Lstub.default.marker).not.toHaveBeenCalled()
  })

  it('escapes HTML in usernames so a hostile name cannot inject markup', async () => {
    Lstub.default.divIcon.mockClear()
    mount(LocationMap, {
      props: { pins: [
        { username: '<img src=x>', isAdmin: false, latitude: 0, longitude: 0 }
      ] },
      attachTo: document.body
    })
    await flushPromises()

    const html = Lstub.default.divIcon.mock.calls[0][0].html as string
    expect(html).not.toContain('<img')
    expect(html).toContain('&lt;img')
  })
})
