# Feature: fix "queue count from clients not showing" + make it self-verifiable

## The bug
On the central monitor server's **Calls → Queue** view, only the local server's
per-pool queue line shows. Remote agents (e.g. `ConsoleSample`) never surface a
queue line — the server receives **no queue metrics** from clients. (Calls do
flow from clients when call-forwarding is on; queue metrics specifically do not.)

## Goal
1. Make the server **also** show live per-pool queue/exec metrics from every
   connected agent (grouped by configuration), updating as the agent does work.
2. Make this path **verifiable by the agent (me) without a human running the
   apps** — via an automated integration test, and optionally MCP tooling — so we
   can confirm the fix and prevent regressions.

## Done when
- An integration test reproduces the flow with a real Tharga.Communication
  client+server and asserts an agent starts sending queue metrics once the server
  has a live subscriber (and the server ingests them).
- Running the sample server + console and opening Calls → Queue shows the
  console's queue line.
