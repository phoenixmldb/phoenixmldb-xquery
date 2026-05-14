using System;
using PhoenixmlDb.XQuery.LanguageServer;
using StreamJsonRpc;

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

// HeaderDelimitedMessageHandler implements the LSP base-protocol "Content-Length: N\r\n\r\n..."
// framing. SystemTextJsonFormatter handles the camelCase JSON-RPC payloads we need.
var formatter = new SystemTextJsonFormatter();
var handler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);
var server = new XQueryLanguageServer();
var rpc = new JsonRpc(handler, server);
server.Rpc = rpc;
rpc.StartListening();
await rpc.Completion;
