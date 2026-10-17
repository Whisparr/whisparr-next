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

## 4. Check search

```bash
curl -s -o /dev/null -w '%{http_code}' 'https://api.whisparr.com/v5/site/search?q=brazzers'
```

Expected: `200`, with the same body as the `/v4` equivalent.

## 5. Check cache headers

```bash
curl -sI 'https://api.whisparr.com/v5/site/<a known studio guid>/scenes/full?limit=2'
```

Expected: `cache-control: public,s-maxage=86400,max-age=0`.

## 6. Unblock the app

Once steps 4 and 5 pass, the app change can start: switch the app's Skyhook
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
- a body with a non-empty `pending` list (the IDs still being fetched)

Clients are expected to wait `Retry-After` seconds and repeat the same request.
Once everything is hydrated the same URL answers `200` with `pending: []` and
becomes cacheable. A `202` is never cached, so Cloudflare does not serve a
stale pending page.
