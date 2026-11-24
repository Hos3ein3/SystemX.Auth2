# Gateway

## What this is

`src/SystemX.Auth2.Gateway` is an **empty project** (`GatewayAssemblyMarker.cs` only). It is **not implemented in v1**.

## When to use it

You do not need it. The Server already works behind any load balancer ([deployment-and-scaling.md](deployment-and-scaling.md)), and your applications validate tokens themselves with `.UseApiAuthentication()` ([client-authorization.md](client-authorization.md)).

## What is planned

A YARP-based helper for gateway projects that:

1. routes `/connect/*` and the sign-in pages to many Server replicas, with health checks and forwarded headers;
2. validates the JWT once;
3. applies route metadata rules (`Auth2:Permission`, `Role`, `Anonymous`);
4. forwards the original token;
5. strips untrusted identity headers.

Until it exists, use your own reverse proxy and turn on `Auth2:Security:UseForwardedHeaders` ([security.md](security.md)).

## Related pages

- [roadmap.md](roadmap.md)
- [deployment-and-scaling.md](deployment-and-scaling.md)
