# Skyhook /v5 deploy runbook

Skyhook is served under `https://api.whisparr.com/v5` as well as `/v4`. A proxy
strips the prefix before the request reaches the container, so the controllers
stay unversioned. Cloudflare purges cover both prefixes (two `Cloudflare:Sites`
entries sharing one zone).

Do these in order. Stop at the first failing check.

## 1. Add the /v5 proxy rule

Add a `/v5` strip-prefix rule next to the existing `/v4` rule, wherever the
`/v4` prefix is stripped today.

> TODO(plz12345): name the proxy and its config location here. It is not
> recorded in this repo or in Eros-Metadata.

## 2. Deploy the image

Build and deploy the Skyhook image from the `v5-bulk` branch of
Eros-Metadata (after the PR into `v4` is merged, from `v4`).

## 3. Recreate the container

```bash
docker compose up -d
```

Use `up -d`, not `restart`: `restart` keeps the old environment and the new
configuration is not picked up. Confirm with `docker exec <container> printenv`
if in doubt.

## 4. Extend the Cloudflare rules to /v5

In the `api.whisparr.com` zone, find every rule scoped to `/v4/*` and extend it
to `/v5/*` (add the path to the rule's expression, or duplicate the rule):

- cache rules: cache-everything / edge-TTL (eligible for cache, respect origin
  `cache-control`)
- WAF custom rules and managed-rule exceptions
- rate-limiting rules

Without the cache rule, `/v5` pages are never cached at the edge and every
request reaches the origin.

## 5. Rate-limit the hydrating endpoints

`performer/bulk` and `scenes/full` are anonymous and queue upstream fetches.
The origin caps its hydration queue and answers `503` when it is full, but
that only bounds the damage. Add a Cloudflare rate-limiting rule, counted per
client IP, matching:

```
(http.request.method eq "POST" and ends_with(http.request.uri.path, "/performer/bulk"))
or (http.request.method eq "GET" and ends_with(http.request.uri.path, "/scenes/full"))
```

A client polling one pending page every `Retry-After` (5s) makes 12 requests a
minute, so keep the threshold well above that.

> TODO(plz12345): pick the threshold and block duration from the zone's
> analytics for the `/v4` traffic.

## 6. Check search

```bash
curl -s -o /dev/null -w '%{http_code}' 'https://api.whisparr.com/v5/site/search?q=brazzers'
```

Expected: `200`, with the same body as the `/v4` equivalent.

## 7. Check cache headers

Use a GET (`curl -s -D - -o /dev/null`), not `curl -I`, so the request is the
one clients send. Pick a studio with more than two scenes, ideally one nobody
has asked for yet:

```bash
url='https://api.whisparr.com/v5/site/<a known studio guid>/scenes/full?limit=2'
curl -s -D - -o /dev/null "$url" | grep -iE '^(HTTP|cache-control|retry-after|cf-cache-status)'
```

While the scenes hydrate, expect `202`, `cache-control: no-store`,
`retry-after: 5` and `cf-cache-status: BYPASS` or `DYNAMIC` (never `HIT`).

Repeat until it answers `200`:

```bash
until [ "$(curl -s -o /dev/null -w '%{http_code}' "$url")" = 200 ]; do sleep 5; done
curl -s -D - -o /dev/null "$url" | grep -iE '^(HTTP|cache-control|cf-cache-status)'
```

Expected on the `200`: `cache-control: public,s-maxage=86400,max-age=0`. Run
the second `curl` once more: it must show `cf-cache-status: HIT`. A `MISS` on
every repeat means the step 4 cache rule does not cover `/v5`.

## 8. Unblock the app

Once steps 6 and 7 pass, the app change can start: switch the app's Skyhook
base URL to `https://api.whisparr.com/v5/{route}`.

## Cloudflare purge quota

A second purge site doubles Cloudflare purge calls (one request per 30 ids per
site), so check the zone's purge quota.

## What clients see: 202 while hydrating

`scenes/full` and `performer/bulk` hydrate missing items from upstream on
demand. While that happens they answer:

- `202 Accepted`
- `Retry-After: 5`
- `cache-control: no-store`
- a body with a non-empty `Pending` list (the IDs still being fetched)

Clients are expected to wait `Retry-After` seconds and repeat the same request.
Once everything is hydrated the same URL answers `200` with `Pending: []` and
becomes cacheable. A `202` is never cached, so Cloudflare does not serve a
stale pending page.

If the hydration queue is full (20,000 queued or in-flight IDs per kind) they
answer `503 Service Unavailable` with `Retry-After: 30` and
`cache-control: no-store` instead; clients wait and repeat, as for a `202`.
`performer/bulk` answers `400` to a body of more than 1000 IDs.
