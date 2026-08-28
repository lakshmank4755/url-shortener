# Scenario 3 — Ambiguous Requirement: "Harden This Before We Launch"

**Days 2-3 of the assignment window.** The stated requirement, verbatim, as
this scenario is framed: *"We're worried about this being abused once it's
public — can you harden it before launch?"* No further specification.

## 1. Requirement understanding — normalizing the ambiguity

"Abuse" and "harden" are not engineering requirements by themselves. Before
writing any code, the possible interpretations were enumerated and each was
explicitly accepted, deferred, or rejected with a reason:

| Interpretation | Decision | Reasoning |
|---|---|---|
| Someone spams the create endpoint to generate thousands of links (spam/phishing infrastructure) | **In scope** | This is the most direct reading of "abused" for a URL shortener specifically — it's the textbook abuse case for this class of service. |
| Someone uses this service as an open redirector/SSRF proxy to probe internal infrastructure (cloud metadata endpoints, internal admin panels) | **In scope** | A URL shortener that accepts any URL and will redirect to it is a known SSRF vector if the target isn't validated; directly relevant given this is a URL-accepting service by definition. |
| Someone submits a URL scheme that isn't a real "link" at all (`javascript:`, `data:`) hoping a client-side context treats the redirect unsafely | **In scope** | Same root cause as the SSRF concern — insufficient validation of what counts as an acceptable target — cheap to close alongside it. |
| Someone brute-forces/enumerates short codes to discover other users' private links | **Partially in scope** | Addressed structurally by the greenfield decision to use random, non-sequential codes (docs/02) rather than an additional control here; noted explicitly so it isn't silently assumed unaddressed. |
| Someone DDoSes the service at the network/infrastructure level | **Out of scope, documented as such** | That's an infrastructure/CDN/WAF concern (e.g. rate limiting at a load balancer or Cloudflare), not something an application-layer change to this codebase meaningfully addresses. Called out explicitly rather than silently ignored, so a reviewer doesn't assume it was missed by oversight. |
| Someone uploads malicious *content* (this service doesn't accept file uploads) | **Not applicable** | No such surface exists in this service. |

Normalized engineering problem: **add rate limiting on the endpoint that
creates new links, and validate that submitted long-URLs cannot target
unsafe schemes or private/internal network addresses.**

## 2. Task decomposition

1. Rate limit `POST /api/urls`, partitioned per client so one abusive caller
   can't exhaust the limit for everyone else.
2. Define concrete, testable rules for "safe URL": scheme allowlist,
   private/loopback/link-local network-target blocklist, own-domain
   redirect-loop guard, a pluggable blocklist seam for real threat intel.
3. Decide and document the enumeration/information-disclosure trade-off:
   does a request for an *expired* link reveal more than a request for a
   *nonexistent* one? (see §4)
4. Wire both into the create path; confirm the redirect path is unaffected
   in the happy case (hardening must not add cost to legitimate use).

## 3. AI-assisted execution (traceability)

| # | Task given to AI | What AI produced | Engineer review outcome |
|---|---|---|---|
| 1 | "Add rate limiting to the link-creation endpoint using ASP.NET Core's built-in limiter, partitioned by client IP, with a sane default limit." | `AddRateLimiter` config in `Program.cs`, fixed-window policy, 20/min/IP, `[EnableRateLimiting("create")]` on the controller action | **Accepted the mechanism; the limit itself (20/min) was flagged as a placeholder** — a real number needs actual traffic data this prototype doesn't have. Documented explicitly as a tunable, not asserted as "correct" without evidence. |
| 2 | "Design and implement a URL safety validator: reject unsafe schemes and SSRF-style private-network targets." | `IUrlSafetyValidator` / `UrlSafetyValidator` with explicit scheme allowlist and IPv4 private-range + IPv6 link/site-local checks | **Reviewed rule-by-rule, not accepted wholesale.** Specifically verified the private-IPv4 ranges against RFC 1918 (10/8, 172.16/12, 192.168/16) plus 127/8 and 169.254/16 (link-local **and** the AWS/GCP/Azure metadata address, which lives in that same range) — this last one was called out explicitly because it's the single highest-value SSRF target for a service like this. |
| 2 | (follow-up) "What about a hostname that isn't an IP literal — could DNS rebinding get around this check?" | AI's answer, and the code's inline comment acknowledging it | **Correctly limited in scope, not silently glossed over.** The validator can only inspect what's in the request; a hostname that resolves to a private IP *after* this check (DNS rebinding) isn't caught here. Documented as a known limitation requiring an egress-proxy-level control in production (docs/06) rather than claiming this check is a complete SSRF defense. |
| 3 | "Should an expired link and a nonexistent link return the same status code, to avoid leaking which is which?" | AI laid out both options with trade-offs | **Engineer decision, not AI's**: chose to keep them distinct (404 vs 410) for usability — a person whose link expired benefits from knowing that, and the information disclosed (a given short code *once* existed) is low sensitivity for this application. Recorded here specifically because this is exactly the kind of judgment call the assignment asks the engineer, not the AI, to own. |
| 4 | "Confirm the redirect (read) path performs no additional validation work — safety checks belong only at creation time." | Verified `RedirectController` unchanged by this scenario | **Confirmed by inspection and by the smoke-test transcript in docs/06** — redirect latency for the happy path is unaffected by this scenario's changes. |

## 4. Validation and risk control

| Risk | Mitigation | Residual risk (accepted, documented) |
|---|---|---|
| Spam link creation | Per-IP rate limit, 20/min | Limit is a placeholder without real traffic data; a determined attacker with many IPs isn't stopped by this alone (would need CAPTCHA/account-based limits — out of scope). |
| SSRF via private/loopback targets | IP-literal check against RFC1918 + loopback + link-local ranges | DNS-rebinding (hostname resolves to a private IP after this check passes) is **not** covered — flagged, not hidden. |
| Malicious non-http(s) schemes | Scheme allowlist (http/https only) | None significant — this is a complete, static check. |
| Short-code enumeration | Random CSPRNG codes (structural, from docs/02) | Not a new control from this scenario; recorded here so it isn't assumed unaddressed. |
| Redirect-loop via shortening our own domain | Own-hostname blocklist, configurable | Requires the configured hostname list to be kept in sync with actual deployment hostnames — an operational, not code, risk. |

Manual validation against the live service (see docs/06 for full
transcript): confirmed `javascript:` payloads rejected (400), confirmed
`http://169.254.169.254/...` rejected (400), confirmed a burst of 25 rapid
creates produced exactly 20×`201` then 5×`429`, and confirmed legitimate
`https://` URLs were unaffected.

## 5. Output

- `src/UrlShortener.Infrastructure/Validation/UrlSafetyValidator.cs`,
  `UrlSafetyOptions.cs`
- Rate limiter configuration in `src/UrlShortener.Api/Program.cs`
- `[EnableRateLimiting("create")]` on `UrlsController.Create`
