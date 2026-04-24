import { create as webAuthnCreate, get as webAuthnGet } from '@github/webauthn-json'

// The browser's WebAuthn API works with binary BufferSources for challenge /
// id / userHandle. Our backend serialises those as base64url strings; the
// @github/webauthn-json helper swaps the encoding in both directions so we
// don't have to hand-roll the conversion.
//
// Each helper returns a JSON-serialisable object suitable for transit back
// to the RPC handler.

export type RawOptions = { optionsJson: string; sessionId: string }

export async function startPasskeyEnrolment(options: RawOptions['optionsJson']) {
  const parsed = JSON.parse(options)
  const credential = await webAuthnCreate({ publicKey: parsed })
  return JSON.stringify(credential)
}

export async function startPasskeyAssertion(options: RawOptions['optionsJson']) {
  const parsed = JSON.parse(options)
  const assertion = await webAuthnGet({ publicKey: parsed })
  return JSON.stringify(assertion)
}

export function isPasskeySupported(): boolean {
  return typeof window !== 'undefined'
    && typeof window.PublicKeyCredential !== 'undefined'
    && typeof window.navigator?.credentials?.create === 'function'
}
