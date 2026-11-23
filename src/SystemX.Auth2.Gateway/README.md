# SystemX.Auth2.Gateway

**Status: not implemented in v1.**

Planned: a YARP-based helper for gateway projects that

1. routes `/connect/*` and auth pages to many Server replicas, with health checks and forwarded headers;
2. validates the JWT once;
3. applies route metadata rules (`Auth2:Permission`, `Role`, `Anonymous`);
4. forwards the original token;
5. strips untrusted identity headers.

Clients do not need it. The Server already works behind any load balancer.
