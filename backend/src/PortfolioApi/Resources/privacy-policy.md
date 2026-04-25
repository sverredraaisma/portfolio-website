# draaisma.dev — Privacy policy

Last-Updated: 2026-04-24

This document describes how this website ("the site") handles your personal
data, in line with the Algemene Verordening Gegevensbescherming (AVG) — the
Dutch implementation of the EU General Data Protection Regulation (GDPR). If
anything below is unclear, contact the data controller using the address at
the bottom.

This Markdown file is the canonical version of the policy and is what the
Falcon-512 signature published alongside it attests to. The page at
`/privacy` renders this exact document — there is no second source. Save the
signed snapshot from the bottom of `/privacy` if you want a portable proof
of what the site committed to on a given date.

## 1. Who is the data controller

Sverre Draaisma is the natural person responsible for this site and acts
as data controller within the meaning of art. 4(7) AVG. Contact:
[sverre@draaisma.dev](mailto:sverre@draaisma.dev).

## 2. What data is collected, and why

- **Account profile.** Username, email address, account creation timestamp,
  email-verification timestamp, and a per-user random salt. Required to
  operate the account (art. 6(1)(b) AVG — performance of a contract).

- **Password material.** Your password is hashed in the browser with
  SHA-256 before transit; the server then re-hashes that value with
  Argon2id and a per-user salt and stores only the result. The plaintext
  password never leaves your device.

- **Sessions.** Refresh tokens are stored as SHA-256 hashes (so leaked
  database backups cannot be used as session tokens). Each row records
  when it was created, when it expires, and when it was revoked.

- **Posts and comments.** Content you submit is stored verbatim and shown
  publicly under your username (or as "anonymous" if you have anonymised
  it).

- **IP address.** Held only in volatile memory by the rate-limiter to
  throttle abusive request patterns. Not logged or persisted (art.
  6(1)(f) AVG — legitimate interest in keeping the service available).

- **Email delivery.** Verification and password-reset emails are sent
  through an SMTP server. The address you registered with is the only
  personal datum shared with the SMTP provider, and only for the duration
  of delivery.

- **Shared location (opt-in).** If you choose to share your location from
  your account page, the latitude and longitude you submit are stored
  alongside your username and shown on the public [/map](/map). The
  public list rounds the coordinates to ~110 m precision so an exact
  home address cannot be inferred. You can clear it any time, and it
  disappears automatically when you delete your account.

  When you share by typing a place name (e.g. "Amsterdam"), the lookup
  goes through the public OpenStreetMap geocoder
  ([Nominatim](https://nominatim.openstreetmap.org/)); the request is
  made server-side so your IP address is not shared with them.

## 3. What is *not* collected

- No analytics, no tracking pixels, no third-party advertising trackers.
- No cookies. The site uses `localStorage` only to remember your session
  and theme preference; both are cleared when you log out or use your
  browser's site-data controls.
- No data sold or shared with marketing partners. Ever.

## 4. Retention

Account data is kept for as long as the account exists. Refresh tokens
expire after a bounded lifetime (currently a small number of days) and
are then unusable; the rows themselves remain until you delete the
account or the cleanup job removes them. Comments and posts persist
until you delete them, anonymise them, or delete the account.

## 5. Your rights under the AVG

You can exercise the following rights from the [account page](/account)
(or by emailing the controller):

- **Access (art. 15) and portability (art. 20).** "Download my data"
  returns everything tied to your account in a machine-readable JSON
  file.
- **Rectification (art. 16).** Correct your username/email by contacting
  the controller; corrections are applied within a reasonable time.
- **Erasure (art. 17).** "Delete my account" removes the profile, all
  posts you authored, every active session, and (per your choice) either
  anonymises or hard-deletes your comments. The action is irreversible.
- **Restriction (art. 18) and objection (art. 21).** Email the
  controller — the site processes minimal data, but legitimate-interest
  processing (rate limiting) can be objected to.
- **Complaint.** If you believe your rights have not been respected,
  you can file a complaint with the Dutch supervisory authority,
  [Autoriteit Persoonsgegevens](https://autoriteitpersoonsgegevens.nl/).

## 6. Security

- Passwords are double-hashed (SHA-256 in the browser, Argon2id with a
  per-user salt on the server).
- Refresh tokens are stored as SHA-256 digests; raw tokens never sit in
  the database.
- Authenticated sessions can be revoked at any time by logging out
  (which deletes the active refresh token) or by changing your password
  (which revokes every active refresh token).
- The site holds a long-lived Falcon-512 (post-quantum) signing keypair
  used to sign first-party statements, including this document. Anyone
  can verify those signatures from the
  [verify page](/verify-statement); no personal data is involved.

## 7. Changes to this policy

Material changes will be reflected in the `Last-Updated` line at the top
of this document and republished with a fresh signature. If a change
affects your rights, the controller will reach out via the email address
on file before the change takes effect.
