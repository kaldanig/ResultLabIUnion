---
type: llm
focus: trace
weight: 1
---

The gRPC call is wrapped in Result.TryAsync with a mapper typed as RpcException that calls ex.ToError() from ResultUnion.Grpc
