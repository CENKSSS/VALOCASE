# Onboarding QA — nickname rules, telemetry, registration

Manual pass for a physical Android device. Everything here is executable without
access to the Unity editor; the only tools needed are `adb` and, for the backend
sections, log/database access.

The build under test must be a **Development** APK for the `adb logcat` filters to
show client logs. The telemetry itself is **not** development-only — the funnel
events are sent from release builds too, and section 6 checks exactly that.

---

## 0. Setup

```bash
adb devices
```

Reset to a genuine first launch before each full pass. This clears the save, the
fan-notice flag, the installation id, and the guest token:

```bash
adb shell pm clear com.cenk.valocase
```

> Replace the package name if it differs — read it from
> `ProjectSettings/ProjectSettings.asset` (`applicationIdentifier`).

Clearing app data mints a **new installationId**, so each pass appears as a new
install in the funnel. That is intended: it is what makes the funnel rows for a
run attributable to that run.

### Log filters

Client, everything relevant to onboarding:

```bash
adb logcat -s Unity:V | grep -E "FanMadeNotice|FirstLaunchProfile|BackendAuth|OnboardingTelemetry|Backend\]"
```

Telemetry only:

```bash
adb logcat -s Unity:V | grep OnboardingTelemetry
```

Registration only:

```bash
adb logcat -s Unity:V | grep -E "BackendAuth|guest"
```

Crashes and native errors:

```bash
adb logcat *:E
```

### Backend log lines to expect

| When | Line |
|---|---|
| Every accepted event | `telemetry accepted: event=<name> appVersion=… platform=ANDROID installation=<8 hex>` |
| A retry that arrived twice | `telemetry duplicate: event=… ` |
| Registration reached the controller | `guest registration request received: bodyPresent=true displayNameLength=N` |
| Registration started | `guest registration started` |
| Registration succeeded | `guest registration created: accountId=…` |

The nickname text never appears in any log line, only its length. If you see the
name itself anywhere, that is a defect — report it.

### Database queries

```sql
-- Funnel for today, one row per day, distinct installations per step.
SELECT * FROM admin_onboarding_funnel ORDER BY day DESC LIMIT 3;

-- Why players were blocked at the nickname screen.
SELECT * FROM admin_onboarding_rejections ORDER BY day DESC;

-- One install's walk through the funnel, in order.
SELECT event_name, rejection_reason, network_error_category, http_status,
       client_timestamp_utc, received_at
FROM onboarding_events
ORDER BY received_at DESC
LIMIT 40;
```

To confirm nothing sensitive is stored, this must return only the declared
columns and no nickname anywhere:

```sql
SELECT * FROM onboarding_events ORDER BY received_at DESC LIMIT 5;
```

---

## 1. Happy path

| # | Step | Expected |
|---|---|---|
| 1.1 | `pm clear`, launch app | Fan-made notice appears. Log: `[FanMadeNotice] Popup shown` |
| 1.2 | Check DB | `app_launched` and `fan_notice_shown` rows exist for a new `installation_id` |
| 1.3 | Press OK | Notice closes, nickname screen appears |
| 1.4 | Check DB | `fan_notice_accepted`, then `nickname_screen_shown` |
| 1.5 | Type `Player123`, press CONFIRM | Button turns red once the name is valid. Screen shows SAVING… then closes to gameplay |
| 1.6 | Check DB | `nickname_confirm_clicked`, `registration_attempted`, `registration_succeeded` — in that order, no `registration_failed` |
| 1.7 | Check `accounts` | Exactly **one** new row, `display_name = 'Player123'`, never `AgentXXXX` |
| 1.8 | Check wallet | One wallet, 17500 VP |
| 1.9 | Open Settings | Name shows `Player123` |
| 1.10 | Force-quit and relaunch | No notice, no nickname screen, no second account |

---

## 2. Nickname validation

Type each value and press CONFIRM. Note the message shown, and whether a request
left the device (`registration_attempted` in the DB).

**No HTTP request is sent for any rejected name.** A rejection produces a
`nickname_rejected` row and nothing else.

| Input | Verdict | Message (TR device) | `rejection_reason` |
|---|---|---|---|
| `Player123` | accept | — | — |
| `player_name` | accept | — | — |
| `Çınar` | accept | — | — |
| `Yiğit` | accept | — | — |
| `José` | accept | — | — |
| `Łukasz` | accept | — | — |
| `Ελληνικά` | accept | — | — |
| `한국어` | accept | — | — |
| `محمد` | accept | — | — |
| `अर्जुन` | accept | — | — |
| `Ahmet Yılmaz` | reject | Kullanıcı adında boşluk kullanılamaz. | `WHITESPACE` |
| `John Smith` | reject | Kullanıcı adında boşluk kullanılamaz. | `WHITESPACE` |
| `Jean-Luc` | reject | …yalnızca harf, rakam ve alt çizgi (_)… | `INVALID_CHARACTER` |
| `O'Connor` | reject | …yalnızca harf, rakam ve alt çizgi (_)… | `INVALID_CHARACTER` |
| 😀😀😀 | reject | …yalnızca harf, rakam ve alt çizgi (_)… | `INVALID_CHARACTER` |
| (empty) | reject | Kullanıcı adı boş bırakılamaz. | `BLANK` |
| spaces only | reject | Kullanıcı adı boş bırakılamaz. | `BLANK` |
| ` Player ` | accept | — | stored as `Player`, trimmed |
| `ab` | reject | Kullanıcı adı en az 3 karakter olmalıdır. | `TOO_SHORT` |
| `abcdefghijklmno` (15) | accept | — | — |
| `abcdefghijklmnop` (16) | reject | Kullanıcı adı en fazla 15 karakter olabilir. | `TOO_LONG` |

Switch the device language to English and re-check three of the rejections: the
messages must be the English set, and the `rejection_reason` codes unchanged.

**Cross-check against the server.** For each accepted name above, confirm
`accounts.display_name` equals exactly what was typed (after trimming). For each
rejected name, confirm no account row was created.

---

## 3. Network and failure handling

Reset with `pm clear` before each row.

| # | Setup | Action | Expected |
|---|---|---|---|
| 3.1 | Airplane mode ON | Type valid name, CONFIRM | Turkish offline message under the field. Screen stays open, CONFIRM usable again. **No account created.** |
| 3.2 | Still offline | Check DB | `nickname_confirm_clicked` and `registration_attempted` may be missing — they are queued on the device and sent when connectivity returns |
| 3.3 | Turn airplane mode OFF, wait ~30 s | Check DB | Queued events arrive, including `registration_failed` with `network_error_category = 'offline'` and `http_status` NULL |
| 3.4 | Still on the same screen | Press CONFIRM again | Registration succeeds. Exactly **one** account row |
| 3.5 | Throttle the connection so the request exceeds 15 s | CONFIRM | Timeout message. `registration_failed`, category `timeout` |
| 3.6 | Point the build at a host that does not resolve | CONFIRM | `registration_failed`, category `dns` or `transport` |
| 3.7 | Force a 500 from the backend | CONFIRM | Server-error message. `registration_failed`, category `http_error`, `http_status = 500` |
| 3.8 | Force a 400 (e.g. temporarily tighten the server rule) | CONFIRM | `registration_failed`, category `http_error`, `http_status = 400`. No account |
| 3.9 | Kill the app mid-request (`adb shell am force-stop …`) | Relaunch | Nickname screen shown again. Either zero or one account — never two. If one was created server-side without the client storing the token, a second account is possible; see the note in the report |

### Double-tap CONFIRM

| # | Action | Expected |
|---|---|---|
| 3.10 | Tap CONFIRM twice as fast as possible | Exactly one `registration_attempted` and one account row. The second tap is swallowed by the saving guard |
| 3.11 | Tap CONFIRM ~10 times rapidly with an invalid name | One `nickname_rejected` per tap, all with the same reason. Backend rate limit is 60/min per installation — beyond that expect 429s, which the client drops silently and which must never surface to the player |

---

## 4. Telemetry hygiene

| # | Check | Expected |
|---|---|---|
| 4.1 | Capture traffic to `/api/v1/telemetry/onboarding` (proxy or server log) | Body contains only: `installationId`, `eventId`, `eventName`, `clientTimestampUtc`, `appVersion`, `platform`, `rejectionReason`, `networkErrorCategory`, `httpStatus` |
| 4.2 | Same capture | **No** nickname, guest token, auth header, email, or advertising id. The request carries no `X-Guest-Token` at all |
| 4.3 | `SELECT DISTINCT installation_id FROM onboarding_events` for one device | One id across every event of the run |
| 4.4 | Compare to `player_sessions` for the account created in section 1 | Same installation id reported by the session analytics |
| 4.5 | Relaunch the app several times without clearing data | `installation_id` stays the same |
| 4.6 | Telemetry endpoint disabled server-side (404) | Registration still completes normally. Client stops retrying |
| 4.7 | Telemetry endpoint returning 500 | Registration still completes. Client retries up to 4 times then drops |

---

## 5. Missing UI references

| # | Setup | Expected |
|---|---|---|
| 5.1 | Launch into a scene with no Canvas | Log: `[FanMadeNotice] No Canvas found — notice skipped this session.` **No** `fan_notice_shown` row — the event means the player saw it |
| 5.2 | Same | Player reaches gameplay with no account. This is a known gap; see the report's Part 4 |

---

## 6. Release build behaviour

Build a **release** (non-development) APK and repeat sections 1 and 2 at a
reduced scope.

| # | Check | Expected |
|---|---|---|
| 6.1 | `adb logcat -s Unity:V \| grep OnboardingTelemetry` | Quiet. The per-event debug lines are development-only |
| 6.2 | DB funnel for the release run | All nine events present. Telemetry is **not** compiled out |
| 6.3 | `grep BACKEND_DEBUG`, `grep BattleLobbyDiag`, `grep EarnVp` | Silent in release |
| 6.4 | Registration | Behaves identically to the development build |

---

## 7. Regression sweep

| # | Area | Check |
|---|---|---|
| 7.1 | Settings → rename | `Çınar` now accepted (it was refused by the old ASCII rule). 16 characters refused |
| 7.2 | Settings → rename | Name change persists after relaunch and appears in battle lobbies |
| 7.3 | Existing install upgraded from an older build | No nickname screen, no second account, existing name preserved |
| 7.4 | Session analytics | Session start/heartbeat still reported after the installation-id refactor |
