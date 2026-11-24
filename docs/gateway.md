# Gateway

**Status: not implemented in v1.** The project `SystemX.Auth2.Gateway` exists but is empty.

## How to use

There is nothing to run. If you need a reverse proxy in front of your APIs, use a normal one (YARP, nginx, Envoy)
and validate the access token in each API with the client package ([client setup](client-setup.md)).

## How to implement / extend

A gateway would validate tokens once and forward the user claims to the APIs. It is on the [roadmap](roadmap.md).
Until then, do not build on the empty project.
