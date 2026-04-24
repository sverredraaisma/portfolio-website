// Apply the saved (or system-preferred) theme as early as possible on the
// client so we don't get a light-flash before hydration finishes.
export default defineNuxtPlugin(() => {
  useTheme().init()
})
