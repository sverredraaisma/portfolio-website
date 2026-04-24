<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
import { hashPasswordForTransit } from '~/composables/usePasswordHash'
import {
  startPasskeyEnrolment,
  isPasskeySupported
} from '~/composables/useWebAuthn'

definePageMeta({ middleware: 'auth' })

type PasskeyDto = { id: string; name: string; createdAt: string; lastUsedAt: string | null }
type PasskeyOptions = { optionsJson: string; sessionId: string }

type AccountExport = {
  id: string
  username: string
  email: string
  emailVerified: boolean
  isAdmin: boolean
  totpEnabled: boolean
  recoveryCodesRemaining: number
  createdAt: string
  posts: Array<{ id: string; title: string; slug: string; createdAt: string; updatedAt: string; published: boolean }>
  comments: Array<{ id: string; postId: string; body: string; createdAt: string }>
  refreshTokens: Array<{ id: string; createdAt: string; expiresAt: string; revokedAt: string | null }>
  auditEvents: Array<{ id: string; kind: string; detail: string | null; at: string }>
}

const AUDIT_KIND_LABELS: Record<string, string> = {
  'password.changed': 'Password changed',
  'password.reset': 'Password reset',
  'email.changed': 'Email changed',
  'totp.enabled': '2FA enabled',
  'totp.disabled': '2FA disabled',
  'totp.recoveryCodesRegenerated': 'Recovery codes regenerated',
  'sessions.revoked': 'All sessions revoked'
}
function auditLabel(kind: string) { return AUDIT_KIND_LABELS[kind] ?? kind }

type TotpEnrolment = { otpAuthUri: string; secretBase32: string }

const auth = useAuthStore()
const rpc = useRpc()
const router = useRouter()

const data = ref<AccountExport | null>(null)
const loading = ref(true)
const error = ref('')

const exporting = ref(false)
const deleting = ref(false)
const commentStrategy = ref<'anonymise' | 'delete'>('anonymise')
const confirmText = ref('')

// Change-password panel state
const currentPwd = ref('')
const newPwd = ref('')
const newPwdRepeat = ref('')
const changingPwd = ref(false)
const pwdMessage = ref('')

// Sign-out-everywhere
const revokingAll = ref(false)
const revokeMessage = ref('')

// Email-change panel state
const newEmail = ref('')
const requestingEmail = ref(false)
const emailMessage = ref('')

// TOTP panel state
const totpEnrolment = ref<TotpEnrolment | null>(null)
const totpQrDataUrl = ref('')
const totpCode = ref('')
const totpBusy = ref(false)
const totpMessage = ref('')

// Recovery-codes modal: codes are shown ONCE; the user must save them. We
// keep them in memory only — never persisted to localStorage or refetched.
const freshRecoveryCodes = ref<string[] | null>(null)
const regeneratingCodes = ref(false)
const recoveryError = ref('')

// Passkey panel state
const passkeys = ref<PasskeyDto[]>([])
const passkeyName = ref('')
const passkeyBusy = ref(false)
const passkeyMessage = ref('')
const passkeysSupported = ref(false)

async function loadPasskeys() {
  try {
    passkeys.value = await rpc.call<PasskeyDto[]>('auth.passkeyList')
  } catch {
    // best-effort; the panel renders an empty list and will recover on retry
  }
}

async function addPasskey() {
  passkeyMessage.value = ''
  if (!isPasskeySupported()) {
    passkeyMessage.value = 'Your browser does not support passkeys.'
    return
  }
  passkeyBusy.value = true
  try {
    const opts = await rpc.call<PasskeyOptions>('auth.passkeyRegisterStart')
    const attestationJson = await startPasskeyEnrolment(opts.optionsJson)
    const added = await rpc.call<PasskeyDto>('auth.passkeyRegisterFinish', {
      sessionId: opts.sessionId,
      attestationJson,
      name: passkeyName.value || undefined
    })
    passkeyName.value = ''
    passkeyMessage.value = `Added "${added.name}".`
    await loadPasskeys()
  } catch (e) {
    // Browser cancellations come through as DOMException 'NotAllowedError'.
    const msg = e instanceof Error ? e.message : String(e)
    passkeyMessage.value = msg.includes('NotAllowedError')
      ? 'Cancelled.'
      : msg
  } finally {
    passkeyBusy.value = false
  }
}

async function removePasskey(p: PasskeyDto) {
  if (!confirm(`Remove "${p.name}"? You will lose this passkey on its device.`)) return
  passkeyBusy.value = true
  try {
    await rpc.call<void>('auth.passkeyDelete', { id: p.id })
    await loadPasskeys()
  } catch (e) {
    passkeyMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    passkeyBusy.value = false
  }
}

async function renamePasskey(p: PasskeyDto) {
  const next = prompt('New name:', p.name)
  if (!next || next.trim() === p.name) return
  try {
    await rpc.call<void>('auth.passkeyRename', { id: p.id, name: next.trim() })
    await loadPasskeys()
  } catch (e) {
    passkeyMessage.value = e instanceof Error ? e.message : String(e)
  }
}

onMounted(async () => {
  passkeysSupported.value = isPasskeySupported()
  await loadPasskeys()
})

async function loadExport() {
  loading.value = true
  error.value = ''
  try {
    data.value = await rpc.call<AccountExport>('account.export')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

onMounted(loadExport)

async function downloadExport() {
  exporting.value = true
  error.value = ''
  try {
    const fresh = await rpc.call<AccountExport>('account.export')
    const blob = new Blob([JSON.stringify(fresh, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `account-${fresh.username}-${new Date().toISOString().slice(0, 10)}.json`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    exporting.value = false
  }
}

const expectedConfirm = computed(() => `delete ${data.value?.username ?? ''}`)

async function changePassword() {
  pwdMessage.value = ''
  if (newPwd.value.length < 8) {
    pwdMessage.value = 'New password must be at least 8 characters.'
    return
  }
  if (newPwd.value !== newPwdRepeat.value) {
    pwdMessage.value = 'New passwords do not match.'
    return
  }
  changingPwd.value = true
  try {
    const currentClientHash = await hashPasswordForTransit(currentPwd.value)
    const newClientHash = await hashPasswordForTransit(newPwd.value)
    await rpc.call<void>('auth.changePassword', { currentClientHash, newClientHash })
    pwdMessage.value = 'Password updated. Other sessions have been signed out.'
    currentPwd.value = ''
    newPwd.value = ''
    newPwdRepeat.value = ''
  } catch (e) {
    pwdMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    changingPwd.value = false
  }
}

async function startTotp() {
  totpMessage.value = ''
  totpBusy.value = true
  try {
    const enrol = await rpc.call<TotpEnrolment>('auth.totpStart')
    totpEnrolment.value = enrol
    // Lazy-load qrcode so it's not in the initial bundle.
    const QR = await import('qrcode')
    totpQrDataUrl.value = await QR.toDataURL(enrol.otpAuthUri, { width: 220, margin: 1 })
  } catch (e) {
    totpMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    totpBusy.value = false
  }
}

async function confirmTotp() {
  if (!totpCode.value || totpCode.value.length < 6) {
    totpMessage.value = 'Enter the 6-digit code from your authenticator app.'
    return
  }
  totpBusy.value = true
  totpMessage.value = ''
  try {
    const res = await rpc.call<{ recoveryCodes: string[] }>('auth.totpConfirm', { code: totpCode.value })
    totpEnrolment.value = null
    totpQrDataUrl.value = ''
    totpCode.value = ''
    totpMessage.value = 'TOTP enabled. Save the recovery codes below — they are shown only once.'
    // Surface the freshly-issued recovery codes so the user can save them.
    freshRecoveryCodes.value = res.recoveryCodes
    await loadExport()
  } catch (e) {
    totpMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    totpBusy.value = false
  }
}

async function regenerateRecoveryCodes() {
  if (!confirm('Generate a new set? Any unused old codes stop working immediately.')) return
  regeneratingCodes.value = true
  recoveryError.value = ''
  try {
    const res = await rpc.call<{ codes: string[] }>('auth.totpRegenerateRecoveryCodes')
    freshRecoveryCodes.value = res.codes
    await loadExport()
  } catch (e) {
    recoveryError.value = e instanceof Error ? e.message : String(e)
  } finally {
    regeneratingCodes.value = false
  }
}

function downloadRecoveryCodes() {
  if (!freshRecoveryCodes.value) return
  const text = '# sverre.dev recovery codes\n# Each code can be used once instead of a TOTP code.\n\n'
    + freshRecoveryCodes.value.join('\n') + '\n'
  const blob = new Blob([text], { type: 'text/plain' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `recovery-codes-${new Date().toISOString().slice(0, 10)}.txt`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

function dismissRecoveryCodes() {
  freshRecoveryCodes.value = null
}

function cancelTotpEnrolment() {
  totpEnrolment.value = null
  totpQrDataUrl.value = ''
  totpCode.value = ''
  totpMessage.value = ''
}

async function disableTotp() {
  if (!totpCode.value || totpCode.value.length < 6) {
    totpMessage.value = 'Enter a current 6-digit code to confirm.'
    return
  }
  totpBusy.value = true
  totpMessage.value = ''
  try {
    await rpc.call<void>('auth.totpDisable', { code: totpCode.value })
    totpCode.value = ''
    totpMessage.value = 'TOTP disabled.'
    await loadExport()
  } catch (e) {
    totpMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    totpBusy.value = false
  }
}

async function requestEmailChange() {
  emailMessage.value = ''
  if (!newEmail.value || !newEmail.value.includes('@')) {
    emailMessage.value = 'A valid email address is required.'
    return
  }
  requestingEmail.value = true
  try {
    await rpc.call<void>('auth.requestEmailChange', { newEmail: newEmail.value })
    emailMessage.value = `Confirmation link sent to ${newEmail.value}. Click it to apply the change.`
    newEmail.value = ''
  } catch (e) {
    emailMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    requestingEmail.value = false
  }
}

async function revokeAllSessions() {
  revokeMessage.value = ''
  revokingAll.value = true
  try {
    await rpc.call<void>('auth.revokeAllSessions')
    // The current session's refresh token was just revoked too; the next 401
    // will fail to refresh and the rpc client will log us out.
    revokeMessage.value = 'All sessions revoked. You will be signed out shortly.'
  } catch (e) {
    revokeMessage.value = e instanceof Error ? e.message : String(e)
  } finally {
    revokingAll.value = false
  }
}

async function deleteAccount() {
  if (confirmText.value.trim() !== expectedConfirm.value) {
    error.value = `Type "${expectedConfirm.value}" to confirm.`
    return
  }
  deleting.value = true
  error.value = ''
  try {
    await rpc.call<void>('account.delete', { commentStrategy: commentStrategy.value })
    auth.logout()
    router.push('/?deleted=1')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    deleting.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10 space-y-8">
    <header>
      <h1 class="text-2xl text-cyan-400">$ account</h1>
      <p class="text-xs text-zinc-500 mt-1">
        Your data, your rights. See <NuxtLink to="/privacy" class="hover:text-cyan-400 underline">privacy policy</NuxtLink>.
      </p>
    </header>

    <p v-if="loading" class="text-zinc-500">loading...</p>
    <p v-if="error" class="text-red-400 text-sm">{{ error }}</p>

    <template v-if="data">
      <div class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-1 text-sm">
        <div><span class="text-zinc-500">username:</span> {{ data.username }}</div>
        <div><span class="text-zinc-500">email:</span> {{ data.email }}
          <span v-if="!data.emailVerified" class="text-yellow-500 text-xs">(unverified)</span>
        </div>
        <div><span class="text-zinc-500">role:</span> {{ data.isAdmin ? 'admin' : 'member' }}</div>
        <div><span class="text-zinc-500">joined:</span> {{ new Date(data.createdAt).toLocaleString() }}</div>
        <div><span class="text-zinc-500">posts:</span> {{ data.posts.length }}
          · <span class="text-zinc-500">comments:</span> {{ data.comments.length }}
          · <span class="text-zinc-500">sessions:</span> {{ data.refreshTokens.length }}
        </div>
      </div>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-3">
        <h2 class="text-lg text-cyan-400">$ passkeys</h2>
        <p class="text-xs text-zinc-500">
          Sign in with your device biometric or hardware key — no password, no codes.
          Each device you add can sign in on its own.
        </p>

        <ul v-if="passkeys.length" class="text-sm divide-y divide-zinc-200 dark:divide-zinc-800 border border-zinc-200 dark:border-zinc-800 rounded">
          <li v-for="p in passkeys" :key="p.id" class="flex items-center gap-3 px-3 py-2">
            <div class="flex-1 min-w-0">
              <div class="truncate">{{ p.name }}</div>
              <div class="text-xs text-zinc-500 truncate">
                added {{ new Date(p.createdAt).toLocaleDateString() }}
                <template v-if="p.lastUsedAt">· last used {{ new Date(p.lastUsedAt).toLocaleDateString() }}</template>
              </div>
            </div>
            <button @click="renamePasskey(p)" class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500">rename</button>
            <button @click="removePasskey(p)" class="text-xs px-2 py-1 rounded border border-red-300 dark:border-red-900 text-red-400 hover:border-red-500">✕</button>
          </li>
        </ul>
        <p v-else class="text-xs text-zinc-500 italic">No passkeys yet.</p>

        <div class="flex gap-2 items-center">
          <input
            v-model="passkeyName"
            placeholder="device label (e.g. iPhone, YubiKey)"
            class="flex-1 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm"
          />
          <button
            :disabled="passkeyBusy || !passkeysSupported"
            @click="addPasskey"
            class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 text-sm disabled:opacity-50"
          >{{ passkeyBusy ? '...' : 'add passkey' }}</button>
        </div>
        <p v-if="!passkeysSupported" class="text-xs text-yellow-500">Your browser does not support passkeys.</p>
        <p v-if="passkeyMessage" class="text-xs"
          :class="passkeyMessage.startsWith('Added') ? 'text-cyan-400' : 'text-red-400'"
        >{{ passkeyMessage }}</p>
      </section>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-3">
        <h2 class="text-lg text-cyan-400">$ two-factor (TOTP)</h2>
        <p class="text-xs text-zinc-500">
          Status:
          <span :class="data.totpEnabled ? 'text-cyan-400' : 'text-yellow-500'">
            {{ data.totpEnabled ? 'enabled' : 'disabled' }}
          </span>
        </p>

        <!-- Disabled, no enrolment in flight: enrol button -->
        <template v-if="!data.totpEnabled && !totpEnrolment">
          <button
            :disabled="totpBusy"
            @click="startTotp"
            class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 text-sm disabled:opacity-50"
          >{{ totpBusy ? '...' : 'enable 2FA' }}</button>
        </template>

        <!-- Enrolment in flight: QR + secret + confirmation code -->
        <template v-else-if="!data.totpEnabled && totpEnrolment">
          <p class="text-xs text-zinc-500">
            Scan the QR with an authenticator app (Authy, 1Password, Google Authenticator, …)
            or paste the secret manually. Then enter the 6-digit code to confirm.
          </p>
          <div class="flex flex-wrap gap-4 items-start">
            <img v-if="totpQrDataUrl" :src="totpQrDataUrl" alt="TOTP QR code" class="rounded bg-white p-1" />
            <div class="text-xs text-zinc-500 space-y-1 break-all">
              <div>secret:</div>
              <code class="block bg-zinc-100 dark:bg-zinc-900 rounded px-2 py-1 text-cyan-400">{{ totpEnrolment.secretBase32 }}</code>
            </div>
          </div>
          <div class="flex gap-2 items-center">
            <input
              v-model="totpCode"
              inputmode="numeric"
              maxlength="6"
              placeholder="123456"
              class="w-32 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm font-mono tracking-widest text-center"
            />
            <button
              :disabled="totpBusy"
              @click="confirmTotp"
              class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 text-sm disabled:opacity-50"
            >{{ totpBusy ? '...' : 'confirm' }}</button>
            <button
              @click="cancelTotpEnrolment"
              class="text-xs text-zinc-500 hover:text-zinc-300"
            >cancel</button>
          </div>
        </template>

        <!-- Already enabled: disable form + recovery codes management -->
        <template v-else>
          <div class="text-xs text-zinc-500">
            recovery codes left:
            <span :class="data.recoveryCodesRemaining < 3 ? 'text-yellow-500' : 'text-cyan-400'">
              {{ data.recoveryCodesRemaining }}
            </span>
            <button
              :disabled="regeneratingCodes"
              @click="regenerateRecoveryCodes"
              class="ml-2 underline hover:text-cyan-400 disabled:opacity-50"
            >regenerate</button>
            <span v-if="recoveryError" class="text-red-400 ml-2">{{ recoveryError }}</span>
          </div>

          <p class="text-xs text-zinc-500">
            To disable, enter a current 6-digit code from your authenticator app.
          </p>
          <div class="flex gap-2 items-center">
            <input
              v-model="totpCode"
              inputmode="numeric"
              maxlength="6"
              placeholder="123456"
              class="w-32 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm font-mono tracking-widest text-center"
            />
            <button
              :disabled="totpBusy"
              @click="disableTotp"
              class="bg-yellow-600 hover:bg-yellow-500 text-black font-bold rounded px-4 py-2 text-sm disabled:opacity-50"
            >{{ totpBusy ? '...' : 'disable 2FA' }}</button>
          </div>
        </template>

        <!-- One-time view of freshly-issued recovery codes -->
        <div v-if="freshRecoveryCodes" class="mt-3 border border-yellow-500 dark:border-yellow-700 rounded p-3 space-y-2">
          <p class="text-xs text-yellow-500 dark:text-yellow-400 font-bold">
            Save these recovery codes — they are shown only once.
          </p>
          <p class="text-xs text-zinc-500">
            Each code substitutes for a TOTP code at login (single-use). Store them somewhere safe.
          </p>
          <ul class="grid grid-cols-2 gap-x-6 gap-y-1 font-mono text-sm text-cyan-400 select-all">
            <li v-for="c in freshRecoveryCodes" :key="c">{{ c }}</li>
          </ul>
          <div class="flex gap-2 pt-2">
            <button
              @click="downloadRecoveryCodes"
              class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
            >download .txt</button>
            <button
              @click="dismissRecoveryCodes"
              class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
            >I have saved them</button>
          </div>
        </div>

        <p v-if="totpMessage" class="text-xs"
          :class="totpMessage.includes('enabled') || totpMessage.includes('disabled') ? 'text-cyan-400' : 'text-red-400'"
        >{{ totpMessage }}</p>
      </section>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-3">
        <h2 class="text-lg text-cyan-400">$ change email</h2>
        <p class="text-xs text-zinc-500">
          Current address: <span class="text-cyan-400">{{ data.email }}</span>.
          We'll send a confirmation link to the new address; the change applies only when you click it.
        </p>
        <div class="flex gap-2">
          <input
            v-model="newEmail"
            type="email"
            placeholder="new email"
            autocomplete="email"
            class="flex-1 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm"
          />
          <button
            :disabled="requestingEmail || !newEmail"
            @click="requestEmailChange"
            class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 text-sm disabled:opacity-50"
          >{{ requestingEmail ? '...' : 'send link' }}</button>
        </div>
        <p v-if="emailMessage" class="text-xs" :class="emailMessage.startsWith('Confirmation link sent') ? 'text-cyan-400' : 'text-red-400'">
          {{ emailMessage }}
        </p>
      </section>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-3">
        <h2 class="text-lg text-cyan-400">$ change password</h2>
        <div class="space-y-2 text-sm">
          <input
            v-model="currentPwd"
            type="password"
            placeholder="current password"
            autocomplete="current-password"
            class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
          />
          <input
            v-model="newPwd"
            type="password"
            placeholder="new password (min 8 chars)"
            autocomplete="new-password"
            class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
          />
          <input
            v-model="newPwdRepeat"
            type="password"
            placeholder="repeat new password"
            autocomplete="new-password"
            class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
          />
        </div>
        <button
          :disabled="changingPwd || !currentPwd || !newPwd"
          @click="changePassword"
          class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >{{ changingPwd ? '...' : 'change password' }}</button>
        <p v-if="pwdMessage" class="text-xs" :class="pwdMessage.startsWith('Password updated') ? 'text-cyan-400' : 'text-red-400'">
          {{ pwdMessage }}
        </p>
      </section>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-2">
        <h2 class="text-lg text-cyan-400">$ sessions</h2>
        <p class="text-xs text-zinc-500">
          You have <span class="text-cyan-400">{{ data.refreshTokens.filter(t => !t.revokedAt && new Date(t.expiresAt) > new Date()).length }}</span>
          active session(s). Sign out of every device — including this one — at once.
        </p>
        <button
          :disabled="revokingAll"
          @click="revokeAllSessions"
          class="bg-yellow-600 hover:bg-yellow-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >{{ revokingAll ? '...' : 'sign out everywhere' }}</button>
        <p v-if="revokeMessage" class="text-xs text-cyan-400">{{ revokeMessage }}</p>
      </section>

      <section class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-2">
        <h2 class="text-lg text-cyan-400">$ activity</h2>
        <p class="text-xs text-zinc-500">Recent sensitive actions on your account.</p>
        <ul v-if="data.auditEvents?.length" class="text-xs space-y-1 max-h-64 overflow-y-auto">
          <li v-for="e in data.auditEvents" :key="e.id" class="flex gap-2">
            <span class="text-zinc-500 w-40 shrink-0">{{ new Date(e.at).toLocaleString() }}</span>
            <span class="text-cyan-400">{{ auditLabel(e.kind) }}</span>
            <span v-if="e.detail" class="text-zinc-500">— {{ e.detail }}</span>
          </li>
        </ul>
        <p v-else class="text-xs text-zinc-500 italic">No activity yet.</p>
      </section>

      <section>
        <h2 class="text-lg text-cyan-400 mb-2">$ export</h2>
        <p class="text-xs text-zinc-500 mb-3">
          Download every piece of data tied to your account as a JSON file (AVG art. 15 / 20).
        </p>
        <button
          :disabled="exporting"
          @click="downloadExport"
          class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >{{ exporting ? '...' : 'download my data' }}</button>
      </section>

      <section class="border border-red-300 dark:border-red-900 rounded p-4 space-y-3">
        <h2 class="text-lg text-red-400">$ delete account</h2>
        <p class="text-xs text-zinc-500">
          Removes your profile, all posts you authored, every active session, and your comments
          (per the choice below). This cannot be undone (AVG art. 17).
        </p>

        <div class="space-y-2 text-sm">
          <label class="flex items-start gap-2">
            <input v-model="commentStrategy" type="radio" value="anonymise" class="mt-1" />
            <span>
              <span class="text-cyan-400">anonymise</span> my comments —
              keep the comment body and timestamp but break the link to my account.
              Comments will display as "anonymous".
            </span>
          </label>
          <label class="flex items-start gap-2">
            <input v-model="commentStrategy" type="radio" value="delete" class="mt-1" />
            <span>
              <span class="text-red-400">delete</span> all my comments along with the account.
            </span>
          </label>
        </div>

        <div class="space-y-2">
          <label class="text-xs text-zinc-500">
            Type <code class="bg-zinc-100 dark:bg-zinc-900 px-1 rounded">{{ expectedConfirm }}</code> to confirm:
          </label>
          <input
            v-model="confirmText"
            class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm"
            :placeholder="expectedConfirm"
          />
        </div>

        <button
          :disabled="deleting || confirmText.trim() !== expectedConfirm"
          @click="deleteAccount"
          class="bg-red-700 hover:bg-red-600 text-white rounded px-4 py-2 disabled:opacity-50"
        >{{ deleting ? '...' : 'delete my account permanently' }}</button>
      </section>
    </template>
  </section>
</template>
