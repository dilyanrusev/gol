// The SignalR browser bundle (wwwroot/lib/signalr/signalr.min.js) is loaded with a plain <script>
// tag and exposes a global. This makes it visible to the TypeScript modules without a bundler.
declare const signalR: typeof import("@microsoft/signalr");
